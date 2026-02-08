using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace PulsarUI;

public class CompetitorInfo : TemplatedControl
{
    public static readonly StyledProperty<string> CIRaceNumProperty = AvaloniaProperty.Register<CompetitorInfo, string>(
        "CIRaceNum");
    
    public static readonly StyledProperty<string> CIIndexProperty = AvaloniaProperty.Register<CompetitorInfo, string>(
        "CIIndex");
    
    public static readonly StyledProperty<string> CIClassProperty = AvaloniaProperty.Register<CompetitorInfo, string>(
        "CIClass");
    
    public static readonly StyledProperty<string> CINameProperty = AvaloniaProperty.Register<CompetitorInfo, string>(
        "CIName");
    
    public static readonly StyledProperty<string> CIModelProperty = AvaloniaProperty.Register<CompetitorInfo, string>(
        "CIModel");
    
    public static readonly StyledProperty<SolidColorBrush> CIBackgroundProperty = AvaloniaProperty.Register<CompetitorInfo, SolidColorBrush>(
        "CIBackground");

    public string CIRaceNum
    {
        get => GetValue(CIRaceNumProperty);
        set => SetValue(CIRaceNumProperty, value);
    }
    
    public string CIIndex
    {
        get => GetValue(CIIndexProperty);
        set => SetValue(CIIndexProperty, value);
    }
    
    public string CIClass
    {
        get => GetValue(CIClassProperty);
        set => SetValue(CIClassProperty, value);
    }
    public string CIName
    {
        get => GetValue(CINameProperty);
        set => SetValue(CINameProperty, value);
    }
    
    public string CIModel
    {
        get => GetValue(CIModelProperty);
        set => SetValue(CIModelProperty, value);
    }
    
    public SolidColorBrush CIBackground
    {
        get => GetValue(CIBackgroundProperty);
        set => SetValue(CIBackgroundProperty, value);
    }
}