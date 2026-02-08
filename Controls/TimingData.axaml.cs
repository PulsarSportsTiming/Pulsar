using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace PulsarUI;

public class TimingData : TemplatedControl
{
    public static readonly StyledProperty<string> TimingLabelProperty = AvaloniaProperty.Register<TimingData, string>(
        "TimingLabel");
    
    public static readonly StyledProperty<string> TimingValueProperty = AvaloniaProperty.Register<TimingData, string>(
        "TimingValue");

    public string TimingLabel
    {
        get => GetValue(TimingLabelProperty);
        set => SetValue(TimingLabelProperty, value);
    }
    
    public string TimingValue
    {
        get => GetValue(TimingValueProperty);
        set => SetValue(TimingValueProperty, value);
    }
}