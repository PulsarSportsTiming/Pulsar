using System.ComponentModel;

namespace PulsarUI.Models;

public enum ElimMode
{
    [Description("No Breakout")]
    NoBreakout = 0,
    [Description("Breakout")]
    Breakout,
    [Description("Breakout (Different Class)")]
    BreakoutDiffClass
}