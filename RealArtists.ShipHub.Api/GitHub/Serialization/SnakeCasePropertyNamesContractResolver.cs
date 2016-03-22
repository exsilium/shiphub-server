﻿namespace RealArtists.ShipHub.Api.GitHub.Serialization {
  using Newtonsoft.Json.Serialization;

  public class SnakeCasePropertyNamesContractResolver : DefaultContractResolver {
    public SnakeCasePropertyNamesContractResolver()
      : base() {
    }

    protected override string ResolvePropertyName(string propertyName) {
      return SnakeCaseUtils.ToSnakeCase(propertyName);
    }
  }
}