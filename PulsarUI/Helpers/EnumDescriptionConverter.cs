using System;
using System.Globalization;
using Avalonia.Data.Converters;
using PulsarUI.Helpers;

namespace PulsarUI.Helpers
{
    public class EnumDescriptionConverter : IValueConverter
    {
        // Converts enum -> string (DescriptionAttribute if present, else ToString()).
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null) return string.Empty;
            if (value is Enum e) return e.GetDisplayName();
            return value.ToString() ?? string.Empty;
        }

        // ConvertBack not supported for non-editable ComboBoxes; throw to highlight this.
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException("ConvertBack is not supported by EnumDescriptionConverter. Bind SelectedItem to an enum property instead of converting strings back to enum values.");
        }
    }
}