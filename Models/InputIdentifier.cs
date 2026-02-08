using System.Text.Json.Serialization;

namespace PulsarUI.Models
{
    public record InputIdentifier
    {
        public string Device { get; init; } = string.Empty;
        public int InputIndex { get; init; }

        [JsonPropertyName("enabled")]
        public bool Enabled { get; init; } = true;

        public override string ToString() => $"{Device}:{InputIndex}";
    }
}
