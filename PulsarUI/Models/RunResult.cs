using System.ComponentModel;

namespace PulsarUI.Models
{
    public enum RunResult
    {
        [Description("Lose")]
        Lose = 1,
        [Description("First")]
        FirstFinish,
        [Description("Winner")]
        Winner
    }
}