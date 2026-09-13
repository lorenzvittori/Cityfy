using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CityFy.Retrieve.Dump.Spotify.JsonConverters
{
    // JsonConverter to tolerate empty strings or non-standard values for nullable DateTime
    public class NullableDateTimeJsonConverter : JsonConverter<DateTime?>
    {
        public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType == JsonTokenType.String)
            {
                var s = reader.GetString();
                if (string.IsNullOrWhiteSpace(s))
                    return null;

                // Try parse ISO formats first
                if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
                    return dt;

                // Try parse common RFC formats
                if (DateTime.TryParseExact(s, new[] { "yyyy-MM-ddTHH:mm:ssZ", "yyyy-MM-ddTHH:mm:ss.fffZ" }, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out dt))
                    return dt;

                // Fallback: try Unix epoch seconds as string
                if (long.TryParse(s, out var seconds))
                {
                    try { return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime; } catch { }
                }

                return null;
            }

            if (reader.TokenType == JsonTokenType.Number)
            {
                // Treat number as unix epoch seconds
                if (reader.TryGetInt64(out var v))
                {
                    try { return DateTimeOffset.FromUnixTimeSeconds(v).UtcDateTime; } catch { }
                }
            }

            return null;
        }

        public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
        {
            if (value.HasValue)
            {
                writer.WriteStringValue(value.Value.ToString("o"));
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }
}
