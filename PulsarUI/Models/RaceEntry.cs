using CommunityToolkit.Mvvm.ComponentModel;

namespace PulsarUI.Models;

public partial class RaceEntry : ObservableObject
{
    [ObservableProperty] private int _queueIndex;
    [ObservableProperty] private int _lane;
    [ObservableProperty] private string? _raceNumber;
    [ObservableProperty] private string? _handicapIndex;
    [ObservableProperty] private int _tree;
    [ObservableProperty] private int _category;
    [ObservableProperty] private string? _class;
    [ObservableProperty] private string? _name;
    [ObservableProperty] private string? _vehicle;

    public void ClearDetails()
    {
        Class = "";
        Name = "";
        Vehicle = "";
    }
    
    public void ClearAll()
    {
        RaceNumber = "";
        HandicapIndex = "";
        Class = "";
        Name = "";
        Vehicle = "";
        QueueIndex = 0;
        Lane = 0;
        Tree = 0;
        Category = 0;
    }
    
    public RaceEntry Clone(int queueIndex)
    {
        return new RaceEntry
        {
            RaceNumber = this.RaceNumber,
            Name = this.Name,
            Vehicle = this.Vehicle,
            Class = this.Class,
            HandicapIndex = this.HandicapIndex,
            QueueIndex = queueIndex,
            Lane = this.Lane
        };
    }
}