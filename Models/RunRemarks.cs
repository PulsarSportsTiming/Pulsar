using System.ComponentModel;

namespace PulsarUI.Models
{
    public enum RunRemarks 
    {
        [Description("No Vehicle Staged")]
        NoVehicleStaged = 0,
        [Description("Run Aborted")]
        Aborted,
        [Description("Breakout")]
        Breakout,
        [Description("Foul")]
        Foul,
        [Description("DS Foul")]
        DsFoul
    }
}