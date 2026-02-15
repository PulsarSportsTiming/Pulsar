using System.ComponentModel;

namespace PulsarUI.Models;

public enum ElimMode
{
    [Description("No Breakout")]
    NoBreakout = 0,
    [Description("Breakout Allowed")]
    BreakoutAllowed,
    [Description("Breakout (Different Class)")]
    BreakoutDiffClass
}