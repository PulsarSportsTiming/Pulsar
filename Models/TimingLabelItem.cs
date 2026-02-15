using CommunityToolkit.Mvvm.ComponentModel;

namespace PulsarUI.Models;

public partial class TimingLabelItem : ObservableObject
{
    [ObservableProperty] private string _label = string.Empty;
    [ObservableProperty] private string _value = string.Empty;
    [ObservableProperty] private bool _isBold;
}

