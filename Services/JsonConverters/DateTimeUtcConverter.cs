using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PulsarUI.Services.JsonConverters
{
    // Converter for non-nullable DateTime (assumes DateTime.Kind is UTC)
    public class DateTimeUtcConverter : JsonConverter<DateTime>
    {
        // Format used: "yyyy-MM-dd HH:mm:ssZ" (space between date and time, Z suffix)

        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String) throw new JsonException("Invalid date format");
            var s = reader.GetString();
            if (string.IsNullOrWhiteSpace(s)) return DateTime.MinValue;
            // Accept formats with or without trailing Z and with 'T' or space
            s = s.Trim();
            // Try parse common patterns
            if (DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var dt))
                return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            if (DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out dt) || DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out dt))
                return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            throw new JsonException("Invalid date format");
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            // Ensure UTC
            var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
            // Format without fractional seconds, space between date and time, then Z
            var s = utc.ToString("yyyy-MM-dd HH:mm:ss") + "Z";
            writer.WriteStringValue(s);
        }
    }

    // Converter for nullable DateTime
    public class NullableDateTimeUtcConverter : JsonConverter<DateTime?>
    {
        public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            if (reader.TokenType != JsonTokenType.String) throw new JsonException("Invalid date format");
            var s = reader.GetString();
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            if (DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var dt) || DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out dt) || DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out dt))
                return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            throw new JsonException("Invalid date format");
        }

        public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
        {
            if (!value.HasValue)
            {
                writer.WriteNullValue();
                return;
            }
            var utc = value.Value.Kind == DateTimeKind.Utc ? value.Value : value.Value.ToUniversalTime();
            var s = utc.ToString("yyyy-MM-dd HH:mm:ss") + "Z";
            writer.WriteStringValue(s);
        }
    }
}
