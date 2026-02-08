using System;
using System.ComponentModel;
using System.Linq;

namespace PulsarUI.Helpers
{
    public static class RunModeExtensions
    {
        // Get a human-friendly display name for an enum value.
        // Uses DescriptionAttribute when present, otherwise falls back to enum name.
        public static string GetDisplayName(this Enum value)
        {
            if (value == null) return string.Empty;
            var mem = value.GetType().GetMember(value.ToString()).FirstOrDefault();
            if (mem != null)
            {
                var desc = mem.GetCustomAttributes(typeof(DescriptionAttribute), false)
                    .OfType<DescriptionAttribute>()
                    .FirstOrDefault();
                if (desc != null) return desc.Description;
            }
            return value.ToString() ?? string.Empty;
        }
    }
}