using System.ComponentModel;

namespace PulsarUI.Models
{
    public enum RunMode
    {
        [Description("Practice")]
        Practice = 0,
        [Description("Qualifying")]
        Qualifying,
        [Description("Eliminations")]
        Eliminations,
        [Description("Q+E Combo")]
        QeCombo
    }
}