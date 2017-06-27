﻿namespace RealArtists.ShipHub.Common {
  using System.Collections.Generic;
  using System.Net.Http.Formatting;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Converters;
  using Newtonsoft.Json.Serialization;

  public static class JsonUtility {
    public static JsonSerializerSettings JsonSerializerSettings { get; } = CreateSaneDefaultSettings();
    public static JsonSerializer JsonSerializer { get; } = JsonSerializer.Create(JsonSerializerSettings);

    public static JsonMediaTypeFormatter JsonMediaTypeFormatter { get; } = new JsonMediaTypeFormatter() { SerializerSettings = JsonSerializerSettings };
    public static IEnumerable<MediaTypeFormatter> MediaTypeFormatters { get; } = new[] { JsonMediaTypeFormatter };

    public static JsonSerializerSettings CreateSaneDefaultSettings() {
      var settings = new JsonSerializerSettings() {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        DateFormatHandling = DateFormatHandling.IsoDateFormat,
        DateParseHandling = DateParseHandling.DateTimeOffset,
        DateTimeZoneHandling = DateTimeZoneHandling.Utc,
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Include,
      };

      settings.Converters.Add(new StringEnumConverter() {
        AllowIntegerValues = false,
      });

      return settings;
    }

    public static string SerializeObject(this object value, Formatting? formatting = null) {
      if (value == null) {
        return null;
      }

      if (formatting != null) {
        return JsonConvert.SerializeObject(value, formatting.Value, JsonSerializerSettings);
      } else {
        return JsonConvert.SerializeObject(value, JsonSerializerSettings);
      }
    }

    public static T DeserializeObject<T>(this string json)
      where T : class {
      if (json.IsNullOrWhiteSpace()) {
        return null;
      }

      return JsonConvert.DeserializeObject<T>(json, JsonSerializerSettings);
    }
  }
}
