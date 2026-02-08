using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PulsarUI.Models;

public partial class Run : ObservableObject
{
    [ObservableProperty] private RaceEntry? _entry;
    [ObservableProperty] private RunResult? _result;
    [ObservableProperty] private RunRemarks? _remark;
    [ObservableProperty] private RunFouls? _fouls;
    [ObservableProperty] private ReactionTime? _reactionTime;
    [ObservableProperty] private IncrementalTime[] _incrementalTimes = [];
    [ObservableProperty] private IncrementalSpeed[] _incrementalSpeeds = [];
}