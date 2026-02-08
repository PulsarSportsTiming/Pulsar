using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace PulsarUI;

public class EnterPairControl : TemplatedControl
{
    public static readonly StyledProperty<string> EPCClassProperty = AvaloniaProperty.Register<SensorLabel, string>(
        "EPCClass");
    
    public static readonly StyledProperty<string> EPCRaceNumProperty = AvaloniaProperty.Register<SensorLabel, string>(
        "EPCRaceNum");
    
    public static readonly StyledProperty<string> EPCIndexProperty = AvaloniaProperty.Register<SensorLabel, string>(
        "EPCIndex");
    
    public static readonly StyledProperty<string> EPCNameProperty = AvaloniaProperty.Register<SensorLabel, string>(
        "EPCName");
    
    public static readonly StyledProperty<string> EPCModelProperty = AvaloniaProperty.Register<SensorLabel, string>(
        "EPCModel");
    
    public string EPCClass
    {
        get => GetValue(EPCClassProperty);
        set => SetValue(EPCClassProperty, value);
    }
    
    public string EPCRaceNum
    {
        get => GetValue(EPCRaceNumProperty);
        set => SetValue(EPCRaceNumProperty, value);
    }
    
    public string EPCIndex
    {
        get => GetValue(EPCIndexProperty);
        set => SetValue(EPCIndexProperty, value);
    }
    
    public string EPCName
    {
        get => GetValue(EPCNameProperty);
        set => SetValue(EPCNameProperty, value);
    }
    
    public string EPCModel
    {
        get => GetValue(EPCModelProperty);
        set => SetValue(EPCModelProperty, value);
    }
}