using CommunityToolkit.Mvvm.ComponentModel;

namespace PulsarUI.Models;

public partial class CategQueueItem : ObservableObject
{
    [ObservableProperty] private int _queueIndex;
    [ObservableProperty] private int _category;
    [ObservableProperty] private int _mode;
    [ObservableProperty] private int _round;
    [ObservableProperty] private int _lastRound;
    [ObservableProperty] private int _finish;
}