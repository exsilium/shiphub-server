﻿namespace RealArtists.ShipHub.Api.Tests {
  using System;
  using System.Collections.Generic;
  using System.Collections.Specialized;
  using System.IO;
  using System.Net;
  using System.Net.Fakes;
  using System.Threading;
  using System.Threading.Tasks;
  using System.Web;
  using ChargeBee.Api;
  using ChargeBee.Api.Fakes;
  using Microsoft.Azure.WebJobs;
  using Microsoft.QualityTools.Testing.Fakes;
  using Moq;
  using Newtonsoft.Json.Linq;
  using NUnit.Framework;
  using Common.DataModel;
  using Common.GitHub;
  using QueueClient.Messages;
  using QueueProcessor;
  using QueueProcessor.Tracing;
  using System.Linq;

  [TestFixture]
  [AutoRollback]
  public class BillingQueueTests {

    /// <summary>
    /// ChargeBee's client library is not easily tested.  Instead of trying to shim all
    /// of its things, we'll use this to watch outgoing HTTP requests and return fake
    /// responses.
    /// </summary>
    private static void ShimChargeBeeWebApi(Func<string, string, Dictionary<string, string>, object> callback) {
      Dictionary<object, MemoryStream> streams = new Dictionary<object, MemoryStream>();

      // Workaround to make the request body of HttpWebRequest inspectable after
      // it has been written to.
      ShimWebRequest.CreateString = (string url) => {
        HttpWebRequest req = (HttpWebRequest)ShimsContext.ExecuteWithoutShims(() => WebRequest.Create(url));
        var shim = new ShimHttpWebRequest(req) {
          // Force a MemoryStream to be returned.  Otherwise, we won't be able
          // to inspect the request body (it's normally write-only).
          GetRequestStream = () => {
            if (!streams.ContainsKey(req)) {
              streams[req] = new MemoryStream();
            }
            return streams[req];
          },
        };
        return shim;
      };

      ApiConfig.Configure("fake-site-name", "fake-site-key");
      ShimApiUtil.SendRequestHttpWebRequestHttpStatusCodeOut =
        (HttpWebRequest req, out HttpStatusCode code) => {
          NameValueCollection nvc;

          if (req.Method.Equals("POST")) {
            var stream = req.GetRequestStream();
            stream.Position = 0;
            string body;
            using (var reader = new StreamReader(stream)) {
              body = reader.ReadToEnd();
            }
            nvc = HttpUtility.ParseQueryString(body);
          } else {
            nvc = HttpUtility.ParseQueryString(req.RequestUri.Query);
          }

          var data = new Dictionary<string, string>();
          foreach (var key in nvc.AllKeys) {
            data[key] = nvc[key];
          }

          code = HttpStatusCode.OK;
          object result = callback(req.Method, req.RequestUri.AbsolutePath, data);
          return JToken.FromObject(result).ToString(Newtonsoft.Json.Formatting.Indented);
        };
    }

    [Test]
    public async Task WillCreateTrialIfNeeded() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);

        var changeMessages = new List<ChangeMessage>();
        var collectorMock = new Mock<IAsyncCollector<ChangeMessage>>();
        collectorMock.Setup(x => x.AddAsync(It.IsAny<ChangeMessage>(), It.IsAny<CancellationToken>()))
          .Returns((ChangeMessage msg, CancellationToken token) => {
            changeMessages.Add(msg);
            return Task.CompletedTask;
          });

        var mockClient = new Mock<IGitHubClient>();
        mockClient
          .Setup(x => x.User(It.IsAny<IGitHubCacheDetails>()))
          .ReturnsAsync(new GitHubResponse<Common.GitHub.Models.Account>(null) {
            Result = new Common.GitHub.Models.Account() {
              Id = user.Id,
              Login = "aroon",
              Name = "Aroon Pahwa",
              Email = "aroon@pureimaginary.com",
              Type = Common.GitHub.Models.GitHubAccountType.User,
            }
          });

        bool createdAccount = false;
        bool createdSubscription = false;
        bool setSubscriptionToCancel = false;

        using (ShimsContext.Create()) {
          ShimChargeBeeWebApi((string method, string path, Dictionary<string, string> data) => {
            if (method.Equals("GET") && path.Equals("/api/v2/customers")) {
              Assert.AreEqual(new Dictionary<string, string>() { { "id[is]", $"user-{user.Id}" } }, data);
              // Pretend no existing customers found for this id.
              return new {
                list = new object[0],
                next_offset = null as string,
              };
            } else if (method.Equals("POST") && path.Equals("/api/v2/customers")) {
              Assert.AreEqual(
                new Dictionary<string, string> {
                      { "id", $"user-{user.Id}" },
                      { "first_name", $"Aroon"},
                      { "last_name", $"Pahwa"},
                },
                data);
              createdAccount = true;
              // Fake response for customer creation.
              return new {
                customer = new {
                  id = $"user-{user.Id}",
                },
              };
            } else if (method.Equals("GET") && path.Equals("/api/v2/subscriptions")) {
              Assert.AreEqual($"user-{user.Id}", data["customer_id[is]"]);
              Assert.AreEqual("personal", data["plan_id[is]"]);

              // Pretend no existing subscriptions found.
              return new {
                list = new object[0],
                next_offset = null as string,
              };
            } else if (method.Equals("POST") && path.Equals($"/api/v2/customers/user-{user.Id}/subscriptions")) {
              Assert.AreEqual(
                new Dictionary<string, string> {
                      { "plan_id", "personal"},
                },
                data);
              createdSubscription = true;
              // Fake response for creating the subscription.
              return new {
                subscription = new {
                  id = "some-sub-id",
                  status = "in_trial",
                },
              };
            } else if (method.Equals("POST") && path.Equals($"/api/v2/subscriptions/some-sub-id/cancel")) {
              Assert.AreEqual(new Dictionary<string, string> { { "end_of_term", "true" } }, data);
              setSubscriptionToCancel = true;
              // Fake response for scheduling the trial to auto cancel at end of month.
              return new {
                subscription = new {
                  id = "some-sub-id",
                  status = "in_trial",
                },
              };
            } else {
              Assert.Fail($"Unexpected {method} to {path}");
              return null;
            }
          });

          await (new BillingQueueHandler(new DetailedExceptionLogger()))
            .GetOrCreateSubscriptionHelper(new UserIdMessage(user.Id), collectorMock.Object, mockClient.Object, Console.Out);
        }

        var sub = context.Subscriptions.Single(x => x.AccountId == user.Id);

        Assert.AreEqual(SubscriptionState.InTrial, sub.State);
        Assert.IsTrue(createdAccount);
        Assert.IsTrue(createdSubscription);
        Assert.IsTrue(setSubscriptionToCancel);
      }
    }
  }
}
