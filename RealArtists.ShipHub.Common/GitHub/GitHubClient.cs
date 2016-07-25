﻿namespace RealArtists.ShipHub.Common.GitHub {
  using System;
  using System.Collections;
  using System.Collections.Generic;
  using System.Linq;
  using System.Net;
  using System.Net.Http;
  using System.Net.Http.Formatting;
  using System.Net.Http.Headers;
  using System.Threading;
  using System.Threading.Tasks;
  using Models;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Converters;
  using Newtonsoft.Json.Linq;
  using Newtonsoft.Json.Serialization;

  public class GitHubClient : IDisposable {
#if DEBUG
    public const bool UseFiddler = true;
#else
    public const bool UseFiddler = false;
#endif

    public const int RateLimitReserve = 1250; // Try to keep at least this many requests as rate limit buffer 
    public const int ConcurrencyLimit = 16;
    public const int MaxRetries = 2;

    static readonly Uri _ApiRoot = new Uri("https://api.github.com/");
    static readonly Uri _OauthTokenRedemption = new Uri("https://github.com/login/oauth/access_token");
    static readonly MediaTypeFormatter[] _MediaTypeFormatters;

    public static readonly JsonSerializerSettings JsonSettings;
    public static readonly MediaTypeHeaderValue JsonMediaType = new MediaTypeHeaderValue("application/json");
    public static readonly JsonMediaTypeFormatter JsonMediaTypeFormatter;

    static GitHubClient() {
      JsonSettings = new JsonSerializerSettings() {
        ContractResolver = new DefaultContractResolver() {
          NamingStrategy = new SnakeCaseNamingStrategy(),
        },
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Include,
      };
      JsonSettings.Converters.Add(new StringEnumConverter() {
        AllowIntegerValues = false,
        CamelCaseText = true,
      });
      JsonMediaTypeFormatter = new JsonMediaTypeFormatter() {
        SerializerSettings = JsonSettings,
      };
      _MediaTypeFormatters = new[] { JsonMediaTypeFormatter };

      if (UseFiddler) {
        ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => { return true; };
      }
    }

    private HttpClient _httpClient;

    public IGitHubCredentials DefaultCredentials { get; set; }
    public GitHubRateLimit LatestRateLimit { get; private set; }

    public GitHubClient(string productName, string productVersion, IGitHubCredentials credentials = null, GitHubRateLimit rateLimit = null) {
      DefaultCredentials = credentials;
      LatestRateLimit = rateLimit;

      // TODO: Only one handler for all clients?
      var handler = new HttpClientHandler() {
        AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip,
        AllowAutoRedirect = false,
        MaxRequestContentBufferSize = 4 * 1024 * 1024,
        UseCookies = false,
        UseDefaultCredentials = false,
        UseProxy = UseFiddler,
        Proxy = UseFiddler ? new WebProxy("127.0.0.1", 8888) : null,
      };
      _httpClient = new HttpClient(handler, true);

      var headers = _httpClient.DefaultRequestHeaders;
      headers.AcceptEncoding.Clear();
      headers.AcceptEncoding.ParseAdd("gzip");
      headers.AcceptEncoding.ParseAdd("deflate");

      headers.Accept.Clear();
      headers.Accept.ParseAdd("application/vnd.github.v3+json");

      headers.AcceptCharset.Clear();
      headers.AcceptCharset.ParseAdd("utf-8");

      headers.UserAgent.Clear();
      headers.UserAgent.Add(new ProductInfoHeaderValue(productName, productVersion));

      headers.Add("Time-Zone", "Etc/UTC");
    }

    public async Task<GitHubResponse<CreatedAccessToken>> CreateAccessToken(string clientId, string clientSecret, string code, string state) {
      var request = new GitHubRequest<object>(HttpMethod.Post, "", new {
        ClientId = clientId,
        ClientSecret = clientSecret,
        Code = code,
        State = state,
      });

      var httpRequest = new HttpRequestMessage(request.Method, _OauthTokenRedemption) {
        Content = request.CreateBodyContent(),
      };
      httpRequest.Headers.Accept.Clear();
      httpRequest.Headers.Accept.ParseAdd("application/json");

      var response = await _httpClient.SendAsync(httpRequest);

      var result = new GitHubResponse<CreatedAccessToken>() {
        Date = response.Headers.Date.Value,
        Status = response.StatusCode,
      };

      if (response.IsSuccessStatusCode) {
        var temp = await response.Content.ReadAsAsync<JToken>(_MediaTypeFormatters);
        if (temp["error"] != null) {
          result.Error = JsonRoundTrip<GitHubError>(temp);
        } else {
          result.Result = JsonRoundTrip<CreatedAccessToken>(temp);
        }
      } else {
        result.Error = await response.Content.ReadAsAsync<GitHubError>(_MediaTypeFormatters);
      }

      return result;
    }

    public Task<GitHubResponse<Models.Authorization>> CheckAccessToken(string clientId, string accessToken) {
      var request = new GitHubRequest(HttpMethod.Get, $"/applications/{clientId}/tokens/{accessToken}");
      return MakeRequest<Models.Authorization>(request);
    }

    public Task<GitHubResponse<Account>> User(IGitHubRequestOptions opts = null) {
      var request = new GitHubRequest(HttpMethod.Get, "user", opts?.CacheOptions);
      return MakeRequest<Account>(request, opts?.Credentials);
    }

    public async Task<GitHubResponse<IEnumerable<Repository>>> Repositories(IGitHubRequestOptions opts = null) {
      var request = new GitHubRequest(HttpMethod.Get, "user/repos", opts?.CacheOptions);
      var result = await MakeRequest<IEnumerable<Repository>>(request, opts?.Credentials);
      if (result.IsError || result.Pagination == null) {
        return result;
      } else {
        return await EnumerateParallel(result, opts?.Credentials);
      }
    }

    public async Task<GitHubResponse<IEnumerable<TimelineEvent>>> Timeline(string repoFullName, int issueNumber, IGitHubRequestOptions opts = null) {
      var request = new GitHubRequest(HttpMethod.Get, $"repos/{repoFullName}/issues/{issueNumber}/timeline", opts?.CacheOptions);

      // Timeline support (application/vnd.github.mockingbird-preview+json)
      // https://developer.github.com/changes/2016-05-23-timeline-preview-api/
      request.AcceptHeaderOverride = "application/vnd.github.mockingbird-preview+json";

      var result = await MakeRequest<IEnumerable<TimelineEvent>>(request, opts?.Credentials);
      if (result.IsError || result.Pagination == null) {
        return result;
      } else {
        return await EnumerateParallel(result, opts?.Credentials);
      }
    }

    public async Task<GitHubResponse<IEnumerable<Issue>>> Issues(string repoFullName, DateTimeOffset? since = null, IGitHubRequestOptions opts = null) {
      var request = new GitHubRequest(HttpMethod.Get, $"repos/{repoFullName}/issues", opts?.CacheOptions);
      if (since != null) {
        request.AddParameter("since", since);
      }
      request.AddParameter("state", "all");
      request.AddParameter("sort", "updated");

      // Reactions (application/vnd.github.squirrel-girl-preview+json)
      // https://developer.github.com/changes/2016-05-12-reactions-api-preview/
      request.AcceptHeaderOverride = "application/vnd.github.squirrel-girl-preview+json";

      var result = await MakeRequest<IEnumerable<Issue>>(request, opts?.Credentials);
      if (result.IsError || result.Pagination == null) {
        return result;
      } else {
        return await EnumerateParallel(result, opts?.Credentials);
      }
    }

    public async Task<GitHubResponse<IEnumerable<Comment>>> Comments(string repoFullName, int issueNumber, DateTimeOffset? since = null, IGitHubRequestOptions opts = null) {
      var request = new GitHubRequest(HttpMethod.Get, $"/repos/{repoFullName}/issues/{issueNumber}/comments", opts?.CacheOptions);
      if (since != null) {
        request.AddParameter("since", since);
      }

      // Reactions (application/vnd.github.squirrel-girl-preview+json)
      // https://developer.github.com/changes/2016-05-12-reactions-api-preview/
      request.AcceptHeaderOverride = "application/vnd.github.squirrel-girl-preview+json";

      var result = await MakeRequest<IEnumerable<Comment>>(request, opts?.Credentials);
      if (result.IsError || result.Pagination == null) {
        return result;
      } else {
        return await EnumerateParallel(result, opts?.Credentials);
      }
    }

    public async Task<GitHubResponse<IEnumerable<Comment>>> Comments(string repoFullName, DateTimeOffset? since = null, IGitHubRequestOptions opts = null) {
      var request = new GitHubRequest(HttpMethod.Get, $"/repos/{repoFullName}/issues/comments", opts?.CacheOptions);
      if (since != null) {
        request.AddParameter("since", since);
      }
      request.AddParameter("sort", "updated");
      request.AddParameter("direction", "asc");

      // Reactions (application/vnd.github.squirrel-girl-preview+json)
      // https://developer.github.com/changes/2016-05-12-reactions-api-preview/
      request.AcceptHeaderOverride = "application/vnd.github.squirrel-girl-preview+json";

      var result = await MakeRequest<IEnumerable<Comment>>(request, opts?.Credentials);
      if (result.IsError || result.Pagination == null) {
        return result;
      } else {
        return await EnumerateParallel(result, opts?.Credentials);
      }
    }

    public async Task<GitHubResponse<IEnumerable<IssueEvent>>> Events(string repoFullName, IGitHubRequestOptions opts = null) {
      var request = new GitHubRequest(HttpMethod.Get, $"/repos/{repoFullName}/issues/events", opts?.CacheOptions);
      request.AddParameter("sort", "updated");
      request.AddParameter("direction", "asc");
      var result = await MakeRequest<IEnumerable<IssueEvent>>(request, opts?.Credentials);
      if (result.IsError || result.Pagination == null) {
        return result;
      } else {
        return await EnumerateParallel(result, opts?.Credentials);
      }
    }

    public async Task<GitHubResponse<IEnumerable<Label>>> Labels(string repoFullName, IGitHubRequestOptions opts = null) {
      var request = new GitHubRequest(HttpMethod.Get, $"repos/{repoFullName}/labels", opts?.CacheOptions);
      var result = await MakeRequest<IEnumerable<Label>>(request, opts?.Credentials);
      if (result.IsError || result.Pagination == null) {
        return result;
      } else {
        return await EnumerateParallel(result, opts?.Credentials);
      }
    }

    public async Task<GitHubResponse<IEnumerable<Milestone>>> Milestones(string repoFullName, IGitHubRequestOptions opts = null) {
      var request = new GitHubRequest(HttpMethod.Get, $"repos/{repoFullName}/milestones", opts?.CacheOptions);
      request.AddParameter("state", "all");

      var result = await MakeRequest<IEnumerable<Milestone>>(request, opts?.Credentials);
      if (result.IsError || result.Pagination == null) {
        return result;
      } else {
        return await EnumerateParallel(result, opts?.Credentials);
      }
    }

    public async Task<GitHubResponse<IEnumerable<Account>>> Organizations(IGitHubRequestOptions opts = null) {
      var request = new GitHubRequest(HttpMethod.Get, "user/orgs", opts?.CacheOptions);
      var result = await MakeRequest<IEnumerable<Account>>(request, opts?.Credentials);

      if (result.IsError) {
        return result;
      }

      if (result.Pagination != null) {
        result = await EnumerateParallel(result, opts?.Credentials);
      }

      // Seriously GitHub?
      foreach (var org in result.Result) {
        org.Type = GitHubAccountType.Organization;
      }

      return result;
    }

    public async Task<GitHubResponse<IEnumerable<Account>>> OrganizationMembers(string orgLogin, IGitHubRequestOptions opts = null) {
      // defaults: filter=all, role=all
      var request = new GitHubRequest(HttpMethod.Get, $"/orgs/{orgLogin}/members", opts?.CacheOptions);
      var result = await MakeRequest<IEnumerable<Account>>(request, opts?.Credentials);
      if (result.IsError || result.Pagination == null) {
        return result;
      } else {
        return await EnumerateParallel(result, opts?.Credentials);
      }
    }

    public async Task<GitHubResponse<IEnumerable<Account>>> Assignable(string repoFullName, IGitHubRequestOptions opts = null) {
      var request = new GitHubRequest(HttpMethod.Get, $"repos/{repoFullName}/assignees", opts?.CacheOptions);
      var result = await MakeRequest<IEnumerable<Account>>(request, opts?.Credentials);
      if (result.IsError || result.Pagination == null) {
        return result;
      } else {
        return await EnumerateParallel(result, opts?.Credentials);
      }
    }

    public async Task<GitHubResponse<bool>> IsAssignable(string repoFullName, string login, IGitHubRequestOptions opts = null) {
      var request = new GitHubRequest(HttpMethod.Get, $"/repos/{repoFullName}/assignees/{login}", opts?.CacheOptions);
      var response = await MakeRequest<bool>(request, opts?.Credentials);
      response.IsError = false;
      switch (response.Status) {
        case HttpStatusCode.NotFound:
          response.Result = false;
          break;
        case HttpStatusCode.NoContent:
          response.Result = true;
          break;
        default:
          response.IsError = true;
          break;
      }
      return response;
    }

    public async Task<GitHubResponse<T>> MakeRequest<T>(GitHubRequest request, IGitHubCredentials credentials = null, GitHubRedirect redirect = null) {
      // TODO: Different priorities here
      // Have ability to mark requests important, and only fail if completely out of requests
      var rateLimit = LatestRateLimit;
      if (rateLimit != null && rateLimit.IsOverLimit(RateLimitReserve)) {
        throw new GitHubException($"Rate limit exceeded. Only {rateLimit.RateLimitRemaining} requests left until {rateLimit.RateLimitReset:o}.");
      }

      // Always request the biggest page size
      if (request.Method == HttpMethod.Get
        && typeof(IEnumerable).IsAssignableFrom(typeof(T))
        && !request.Parameters.ContainsKey("per_page")) {
        request.AddParameter("per_page", 100);
      }

      var uri = new Uri(_ApiRoot, request.Uri);
      var httpRequest = new HttpRequestMessage(request.Method, uri) {
        Content = request.CreateBodyContent(),
      };

      // Conditional headers
      httpRequest.Headers.IfModifiedSince = request.LastModified;
      if (!string.IsNullOrWhiteSpace(request.ETag)) {
        httpRequest.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(request.ETag));
      }
      if (request.AcceptHeaderOverride != null) {
        httpRequest.Headers.Accept.Clear();
        httpRequest.Headers.Accept.ParseAdd(request.AcceptHeaderOverride);
      }

      // Authentication
      credentials = credentials ?? DefaultCredentials;
      credentials?.Apply(httpRequest.Headers);

      var response = await _httpClient.SendAsync(httpRequest);

      // Handle redirects
      switch (response.StatusCode) {
        case HttpStatusCode.MovedPermanently:
        case HttpStatusCode.RedirectKeepVerb:
          request.Uri = response.Headers.Location;
          return await MakeRequest<T>(request, credentials, new GitHubRedirect(response.StatusCode, uri, request.Uri, redirect));
        case HttpStatusCode.Redirect:
        case HttpStatusCode.RedirectMethod:
          request.Method = HttpMethod.Get;
          request.Uri = response.Headers.Location;
          return await MakeRequest<T>(request, credentials, new GitHubRedirect(response.StatusCode, uri, request.Uri, redirect));
        default:
          break;
      }

      var result = new GitHubResponse<T>() {
        Credentials = credentials,
        Date = response.Headers.Date.Value,
        IsError = !response.IsSuccessStatusCode,
        Redirect = redirect,
        RequestUri = response.RequestMessage.RequestUri,
        Status = response.StatusCode,
      };

      // Cache Headers
      result.CacheData = new GitHubCacheData() {
        AccessToken = credentials.Parameter,
        ETag = response.Headers.ETag?.Tag,
        LastModified = response.Content?.Headers?.LastModified,
      };

      // Poll Interval
      result.CacheData.PollInterval = response.ParseHeader("X-Poll-Interval", x => (x == null) ? TimeSpan.Zero : TimeSpan.FromSeconds(int.Parse(x)));

      // Expires and Caching Max-Age
      var expires = response.Content?.Headers?.Expires;
      var maxAgeSpan = response.Headers.CacheControl?.SharedMaxAge ?? response.Headers.CacheControl?.MaxAge;
      if (maxAgeSpan != null) {
        var maxAgeExpires = DateTimeOffset.UtcNow.Add(maxAgeSpan.Value);
        if (expires == null || maxAgeExpires < expires) {
          expires = maxAgeExpires;
        }
      }
      result.CacheData.Expires = expires;

      // Rate Limits
      // These aren't always sent. Check for presence and fail gracefully.
      if (response.Headers.Contains("X-RateLimit-Limit")) {
        result.RateLimit = new GitHubRateLimit() {
          RateLimit = response.ParseHeader("X-RateLimit-Limit", x => int.Parse(x)),
          RateLimitRemaining = response.ParseHeader("X-RateLimit-Remaining", x => int.Parse(x)),
          RateLimitReset = response.ParseHeader("X-RateLimit-Reset", x => EpochUtility.ToDateTimeOffset(int.Parse(x))),
        };

        // TODO: Move this up into calling methods to make parallel requests less contentious?
        UpdateInternalRateLimit(result.RateLimit);
      }

      // Scopes
      var scopes = response.ParseHeader<IEnumerable<string>>("X-OAuth-Scopes", x => (x == null) ? null : x.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries));
      if (scopes != null) {
        result.Scopes.UnionWith(scopes);
      }

      // Pagination
      // Screw the RFC, minimally match what GitHub actually sends.
      result.Pagination = response.ParseHeader("Link", x => (x == null) ? null : GitHubPagination.FromLinkHeader(x));

      if (response.IsSuccessStatusCode) {
        // TODO: Handle accepted, no content, etc.
        if (response.StatusCode != HttpStatusCode.NotModified) {
          result.Result = await response.Content.ReadAsAsync<T>(_MediaTypeFormatters);
        }
      } else {
        result.Error = await response.Content.ReadAsAsync<GitHubError>(_MediaTypeFormatters);
      }

      return result;
    }

    private void UpdateInternalRateLimit(GitHubRateLimit rateLimit) {
      // TODO: Does this need a lock?
      lock (this) {
        if (LatestRateLimit == null
          || LatestRateLimit.RateLimitReset < rateLimit.RateLimitReset
          || LatestRateLimit.RateLimitRemaining > rateLimit.RateLimitRemaining) {
          LatestRateLimit = rateLimit;
        }
      }
    }

    private async Task<GitHubResponse<IEnumerable<T>>> EnumerateParallel<T>(GitHubResponse<IEnumerable<T>> firstPage, IGitHubCredentials credentials) {
      var results = new List<T>(firstPage.Result);
      IEnumerable<GitHubResponse<IEnumerable<T>>> batch;

      // TODO: Cancellation (for when errors are encountered)?
      // TODO: Retry failed requests?

      // Only support extrapolation when using pages.
      if (firstPage.Pagination?.CanInterpolate == true) {
        var pages = firstPage.Pagination.Interpolate();
        batch = await Batch(pages.Select<Uri, Func<Task<GitHubResponse<IEnumerable<T>>>>>(
          page => () => MakeRequest<IEnumerable<T>>(
            new GitHubRequest(HttpMethod.Get, "") { Uri = page },
            credentials)));

        foreach (var response in batch) {
          if (response.IsError) {
            // TODO: Add retry logic
            return response;
          } else {
            results.AddRange(response.Result);
          }
        }
      } else {
        var current = firstPage;
        while (current.Pagination?.Next != null) {
          // Ignore cache options, since we're paging because they didn't match
          var nextReq = new GitHubRequest(HttpMethod.Get, "") {
            Uri = current.Pagination.Next,
          };
          current = await MakeRequest<IEnumerable<T>>(nextReq, credentials);

          if (current.IsError) {
            return current;
          } else {
            results.AddRange(current.Result);
          }
        }
        // Just use the last request.
        batch = new[] { current };
      }

      // Keep cache and other headers from first page.
      var final = firstPage;
      final.Result = results;

      var rateLimit = final.RateLimit;
      foreach (var req in batch) {
        // Rate Limit
        var limit = req.RateLimit;
        if (limit.RateLimitReset > rateLimit.RateLimitReset) {
          rateLimit = limit;
        } else if (limit.RateLimitReset == rateLimit.RateLimitReset) {
          rateLimit.RateLimit = Math.Min(rateLimit.RateLimit, limit.RateLimit);
          rateLimit.RateLimitRemaining = Math.Min(rateLimit.RateLimitRemaining, limit.RateLimitRemaining);
        } // else ignore it
      }

      return final;
    }

    public async Task<IEnumerable<T>> Batch<T>(IEnumerable<Func<Task<T>>> batch) {
      var tasks = new List<Task<T>>();
      using (var limit = new SemaphoreSlim(ConcurrencyLimit, ConcurrencyLimit)) {
        foreach (var item in batch) {
          await limit.WaitAsync();

          tasks.Add(Task.Run(async delegate {
            var response = await item();
            limit.Release();
            return response;
          }));
        }

        await Task.WhenAll(tasks);

        var results = new List<T>();
        foreach (var task in tasks) {
          // we know they've completed.
          results.Add(task.Result);
        }

        return results;
      }
    }

    private static string SerializeObject(object value) {
      return JsonConvert.SerializeObject(value, JsonSettings);
    }

    private static T DeserializeObject<T>(string json) {
      return JsonConvert.DeserializeObject<T>(json, JsonSettings);
    }

    private static T JsonRoundTrip<T>(object self) {
      return DeserializeObject<T>(SerializeObject(self));
    }

    private bool disposedValue = false;

    protected virtual void Dispose(bool disposing) {
      if (!disposedValue) {
        if (disposing) {
          _httpClient.Dispose();
          _httpClient = null;
        }

        disposedValue = true;
      }
    }
    public void Dispose() {
      Dispose(true);
    }
  }

  public static class HeaderUtility {
    public static T ParseHeader<T>(this HttpResponseMessage response, string headerName, Func<string, T> selector) {
      var header = response.Headers
        .Where(x => x.Key.Equals(headerName, StringComparison.OrdinalIgnoreCase))
        .SelectMany(x => x.Value)
        .SingleOrDefault();

      return selector(header);
    }
  }
}
