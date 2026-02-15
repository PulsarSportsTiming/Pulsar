using CommunityToolkit.Mvvm.ComponentModel;
using PulsarUI.Services;

namespace PulsarUI.Models;

public partial class FinishLine : ObservableObject
{
    [ObservableProperty] private int _id;
    [ObservableProperty] private int _distance;
    public string DistanceString => GetDistanceString();
    
    private string GetDistanceString() => TimingLabelHelpers.FormatDistanceLabel(Distance,AppSettings.DistanceUnit);
}