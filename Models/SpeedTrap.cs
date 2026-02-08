using System.Text.Json.Serialization;

namespace PulsarUI.Models
{
    // Matches the JSON structure in Config/inputmap.json where SpeedTraps are:
    // { "Id": { "Start": 16288, "End": 18288 } }
    public record SpeedTrapId
    {
        [JsonPropertyName("Start")]
        public int Start { get; init; }

        [JsonPropertyName("End")]
        public int End { get; init; }
    }

    public record SpeedTrap
    {
        [JsonPropertyName("Id")]
        public SpeedTrapId Id { get; init; } = new SpeedTrapId();

        [JsonIgnore]
        public int StartMm => Id?.Start ?? 0;

        [JsonIgnore]
        public int EndMm => Id?.End ?? 0;
    }
}
