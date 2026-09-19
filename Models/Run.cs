using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PulsarUI.Models;

public partial class Run : ObservableObject
{
    [ObservableProperty] private RaceEntry? _entry;
    // Not mutually exclusive, except Winner/Lose (a run can be e.g. both FirstFinish and Lose,
    // or both FirstFinish and Winner, but never both Winner and Lose at the same time).
    [ObservableProperty] private RunResult[] _results = [];
    [ObservableProperty] private RunRemarks[] _remarks = [];
    // This lane's own finish-line ET (duration, ns). Null until known. Set once,
    // regardless of finish order - the canonical, order-independent "has this lane
    // finished" signal, used to gate Winner/Lose decisions (as opposed to
    // RunResult.FirstFinish, which may be assigned/corrected before both lanes finish).
    [ObservableProperty] private long? _finishEtNs;
    // FinishEtNs - IndexNs (ns); negative = breakout amount. Null when not applicable/not a breakout.
    [ObservableProperty] private long? _breakoutDeltaNs;
    [ObservableProperty] private ReactionTime? _reactionTime;
    [ObservableProperty] private IncrementalTime[] _incrementalTimes = [];
    [ObservableProperty] private IncrementalSpeed[] _incrementalSpeeds = [];
}