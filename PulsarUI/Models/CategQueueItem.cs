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

    public void ClearDetails()
    {
        QueueIndex = 0;
        Category = 0;
        Mode = 0;
        Round = 0;
        LastRound = 0;
        Finish = 0;
    }
    public CategQueueItem Clone(int queueIndex)
    {
        return new CategQueueItem
        {
            QueueIndex = queueIndex,
            Category = this.Category,
            Mode = this.Mode,
            Round = this.Round,
            LastRound = this.LastRound,
            Finish = this.Finish
        };
    }
}