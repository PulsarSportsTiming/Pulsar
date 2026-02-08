using System.Text.Json.Serialization;

namespace PulsarUI.Models
{
    public enum InputLane
    {
        Left = 0,
        Right = 1
    }

    public record DownTrackInput
    {
        [JsonPropertyName("id")]
        public InputIdentifier Id { get; init; } = new InputIdentifier();

        // millimetres
        [JsonPropertyName("distanceMm")]
        public int DistanceMm { get; init; }

        // 0 = left, 1 = right
        [JsonPropertyName("lane")]
        public InputLane Lane { get; init; }
        
        [JsonPropertyName("enabled")]
        public bool Enabled { get; init; } = true;

        [JsonIgnore]
        public decimal DistanceMeters => DistanceMm / 1000m;
    }
}
