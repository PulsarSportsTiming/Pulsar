using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace PulsarUI;

public class CategoryButton : TemplatedControl
{
    public static readonly StyledProperty<string> CBCategNameProperty = AvaloniaProperty.Register<CategoryButton, string>(
        "CBCategName");
    
    public string CBCategName
    {
        get => GetValue(CBCategNameProperty);
        set => SetValue(CBCategNameProperty, value);
    }
}