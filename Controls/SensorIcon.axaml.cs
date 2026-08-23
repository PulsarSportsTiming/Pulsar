using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace PulsarUI;

public class SensorIcon : TemplatedControl
{
    // Bindable status name matching one of the style classes defined in SensorIcon.axaml
    // ("OK", "Blocked", "Error", "OKInactive", "ErrorInactive"). Setting this property
    // updates the control's Classes collection so the corresponding style is applied.
    public static readonly StyledProperty<string?> StatusProperty =
        AvaloniaProperty.Register<SensorIcon, string?>(nameof(Status));

    private static readonly string[] StatusClassNames = { "OK", "Blocked", "Error", "OKInactive", "ErrorInactive" };

    public string? Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == StatusProperty)
        {
            foreach (var className in StatusClassNames)
            {
                Classes.Remove(className);
            }

            var status = change.GetNewValue<string?>();
            if (!string.IsNullOrEmpty(status))
            {
                Classes.Add(status);
            }
        }
    }
}