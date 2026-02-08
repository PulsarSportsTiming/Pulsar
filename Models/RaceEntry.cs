using CommunityToolkit.Mvvm.ComponentModel;

namespace PulsarUI.Models;

public partial class RaceEntry : ObservableObject
{
    [ObservableProperty] private int _queueIndex;
    [ObservableProperty] private int _lane;
    [ObservableProperty] private string? _raceNumber;
    [ObservableProperty] private string? _handicapIndex;
    [ObservableProperty] private TreeType? _tree;
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
        // Preserve Lane and QueueIndex so clearing a single lane doesn't change its sideCan you change my code so the tree for each lane resets to the category default in the enter pair and associated combobox after I queue or engage a pair?
        //Tree = null; // Tree is now a TreeType object; clear to null
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
            Lane = this.Lane,
            Tree = this.Tree // preserve TreeType reference when cloning
        };
    }
}