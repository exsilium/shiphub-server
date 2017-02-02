﻿namespace RealArtists.ShipHub.Common.GitHub {
  using System;
  using System.Collections.Generic;
  using System.Net;
  using System.Runtime.Serialization;
  using Newtonsoft.Json;

  public class GitHubError {
    public HttpStatusCode Status { get; set; }
    public string Message { get; set; }
    public string DocumentationUrl { get; set; }
    public IEnumerable<GitHubEntityError> Errors { get; set; }
    public bool IsAbuse { get { return Message.Contains("abuse"); } }

    public GitHubException ToException() {
      return new GitHubException(this);
    }

    public override string ToString() {
      return this.SerializeObject(Formatting.None);
    }
  }

  public class GitHubEntityError {
    public string Resource { get; set; }
    public string Field { get; set; }
    public string Message { get; set; }
    public string DocumentationUrl { get; set; }
    public string Code { get; set; }
  }
}
