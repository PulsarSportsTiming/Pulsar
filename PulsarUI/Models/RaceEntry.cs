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
}