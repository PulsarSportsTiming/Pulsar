using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace PulsarUI;

public class SensorLabel : TemplatedControl
{
    public static readonly StyledProperty<string> SensorLabelTextProperty = AvaloniaProperty.Register<SensorLabel, string>(
        "SensorLabelText");

    public static readonly StyledProperty<SensorIcon> LeftSensorIconProperty =
        AvaloniaProperty.Register<SensorLabel, SensorIcon>(
            "LeftSensorIcon");

    public static readonly StyledProperty<SensorIcon> RightSensorIconProperty =
        AvaloniaProperty.Register<SensorLabel, SensorIcon>(
            "RightSensorIcon");

    public string SensorLabelText
    {
        get => GetValue(SensorLabelTextProperty);
        set => SetValue(SensorLabelTextProperty, value);
    }
}