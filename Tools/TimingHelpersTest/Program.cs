using System;
using System.Linq;
using PulsarUI.Models;
using PulsarUI.Services;

internal class Program
{
    private static void Main()
    {
        Console.WriteLine($"TimingHelpersTest running on {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        try { TestPracticeMode(); } catch (Exception ex) { Console.WriteLine("Practice test threw: " + ex); }
        try { TestQeComboDifferentIndexes(); } catch (Exception ex) { Console.WriteLine("QeCombo test threw: " + ex); }
        try { TestEliminationsEqualIndexes(); } catch (Exception ex) { Console.WriteLine("Elim test threw: " + ex); }
        try { TestWinnerLose_SingleFoul(); } catch (Exception ex) { Console.WriteLine("WinnerLose SingleFoul test threw: " + ex); }
        try { TestWinnerLose_BothFoul_WorstFoulFalse(); } catch (Exception ex) { Console.WriteLine("WinnerLose BothFoul WorstFoulFalse test threw: " + ex); }
        try { TestWinnerLose_BothFoul_WorstFoulTrue(); } catch (Exception ex) { Console.WriteLine("WinnerLose BothFoul WorstFoulTrue test threw: " + ex); }
        try { TestWinnerLose_WorstFoulTrue_PendingSecondReactionTime(); } catch (Exception ex) { Console.WriteLine("WinnerLose WorstFoulTrue PendingSecondReactionTime test threw: " + ex); }
        try { TestWinnerLose_WorstFoulTrue_SingleFoulOnceBothReceived(); } catch (Exception ex) { Console.WriteLine("WinnerLose WorstFoulTrue SingleFoulOnceBothReceived test threw: " + ex); }
        try { TestWinnerLose_NoFoulNoFinish(); } catch (Exception ex) { Console.WriteLine("WinnerLose NoFoulNoFinish test threw: " + ex); }
        try { TestWinnerLose_FirstFinishNoFoul(); } catch (Exception ex) { Console.WriteLine("WinnerLose FirstFinishNoFoul test threw: " + ex); }
        try { TestWinnerLose_FoulOverridesFirstFinish(); } catch (Exception ex) { Console.WriteLine("WinnerLose FoulOverridesFirstFinish test threw: " + ex); }
        try { TestWinnerLose_IndeterminateTie(); } catch (Exception ex) { Console.WriteLine("WinnerLose IndeterminateTie test threw: " + ex); }
        try { TestWinnerLose_ByeRun(); } catch (Exception ex) { Console.WriteLine("WinnerLose ByeRun test threw: " + ex); }
        try { TestWinnerLose_NonElimMode(); } catch (Exception ex) { Console.WriteLine("WinnerLose NonElimMode test threw: " + ex); }
        try { TestWinnerLose_SingleDsFoul(); } catch (Exception ex) { Console.WriteLine("WinnerLose SingleDsFoul test threw: " + ex); }
        try { TestWinnerLose_BothDsFoul_NobodyGoesThrough(); } catch (Exception ex) { Console.WriteLine("WinnerLose BothDsFoul test threw: " + ex); }
        try { TestWinnerLose_DsFoulTakesPriorityOverFirstFinish(); } catch (Exception ex) { Console.WriteLine("WinnerLose DsFoulTakesPriorityOverFirstFinish test threw: " + ex); }
        try { TestWinnerLose_DsFoulSelfCorrects(); } catch (Exception ex) { Console.WriteLine("WinnerLose DsFoulSelfCorrects test threw: " + ex); }
        try { TestWinnerLose_ByeRunOverridesDsFoul(); } catch (Exception ex) { Console.WriteLine("WinnerLose ByeRunOverridesDsFoul test threw: " + ex); }
        try { TestWinnerLose_ByeRunOverridesBothDsFoul(); } catch (Exception ex) { Console.WriteLine("WinnerLose ByeRunOverridesBothDsFoul test threw: " + ex); }
        try { TestWinnerLose_EarlyFirstFinishDoesNotPrematurelyDecideWinner(); } catch (Exception ex) { Console.WriteLine("WinnerLose EarlyFirstFinishDoesNotPrematurelyDecideWinner test threw: " + ex); }
        try { TestBreakout_EvaluateRemark_BelowIndex(); } catch (Exception ex) { Console.WriteLine("Breakout EvaluateRemark BelowIndex test threw: " + ex); }
        try { TestBreakout_EvaluateRemark_AboveIndex(); } catch (Exception ex) { Console.WriteLine("Breakout EvaluateRemark AboveIndex test threw: " + ex); }
        try { TestBreakout_EvaluateRemark_NoBreakoutCategory(); } catch (Exception ex) { Console.WriteLine("Breakout EvaluateRemark NoBreakoutCategory test threw: " + ex); }
        try { TestBreakout_EvaluateRemark_NonElimMode(); } catch (Exception ex) { Console.WriteLine("Breakout EvaluateRemark NonElimMode test threw: " + ex); }
        try { TestBreakout_EvaluateRemark_BlankIndex(); } catch (Exception ex) { Console.WriteLine("Breakout EvaluateRemark BlankIndex test threw: " + ex); }
        try { TestWinnerLose_Breakout_SingleBreakout_OtherFinishedClean(); } catch (Exception ex) { Console.WriteLine("WinnerLose Breakout SingleBreakout OtherFinishedClean test threw: " + ex); }
        try { TestWinnerLose_Breakout_BothBreakout_UnequalMagnitudes(); } catch (Exception ex) { Console.WriteLine("WinnerLose Breakout BothBreakout UnequalMagnitudes test threw: " + ex); }
        try { TestWinnerLose_Breakout_BothBreakout_EqualMagnitudes_NoWin(); } catch (Exception ex) { Console.WriteLine("WinnerLose Breakout BothBreakout EqualMagnitudes test threw: " + ex); }
        try { TestWinnerLose_Breakout_WaitingForOtherEt_NoNowNs(); } catch (Exception ex) { Console.WriteLine("WinnerLose Breakout WaitingForOtherEt NoNowNs test threw: " + ex); }
        try { TestWinnerLose_Breakout_EarlyDecision_NotYetExceeded(); } catch (Exception ex) { Console.WriteLine("WinnerLose Breakout EarlyDecision NotYetExceeded test threw: " + ex); }
        try { TestWinnerLose_Breakout_EarlyDecision_Exceeded(); } catch (Exception ex) { Console.WriteLine("WinnerLose Breakout EarlyDecision Exceeded test threw: " + ex); }
        try { TestWinnerLose_BreakoutDiffClass_SameClass_SkipsToFirstFinish(); } catch (Exception ex) { Console.WriteLine("WinnerLose BreakoutDiffClass SameClass test threw: " + ex); }
        try { TestWinnerLose_BreakoutDiffClass_DifferentClass_BehavesLikeBreakout(); } catch (Exception ex) { Console.WriteLine("WinnerLose BreakoutDiffClass DifferentClass test threw: " + ex); }
        try { TestWinnerLose_Breakout_FoulBeatsBreakout(); } catch (Exception ex) { Console.WriteLine("WinnerLose Breakout FoulBeatsBreakout test threw: " + ex); }
        try { TestWinnerLose_Breakout_ByeRun_StillGetsRemarkAndWin(); } catch (Exception ex) { Console.WriteLine("WinnerLose Breakout ByeRun test threw: " + ex); }
        Console.WriteLine("All tests run.");
    }

    private static void TestPracticeMode()
    {
        Console.WriteLine("Running Practice test");
        var pair = new Pair { Id = Guid.NewGuid(), RunMode = RunMode.Practice };
        var left = new RaceEntry { Lane = 0, Tree = new TreeType { Id = 1, CountdownSpeed = 500, CountdownType = 0 }, HandicapIndex = "00.20" };
        var right = new RaceEntry { Lane = 1, Tree = new TreeType { Id = 2, CountdownSpeed = 1000, CountdownType = 1 }, HandicapIndex = "00.20" };
        pair.Runs = new Run[] { new Run { Entry = left }, new Run { Entry = right } };
        TimingHelpers.PopulateExpectedReactionTimes(pair);
        Console.WriteLine($"Practice expected L={pair.Runs[0].ReactionTime?.ExpectedReactionTimeNs} R={pair.Runs[1].ReactionTime?.ExpectedReactionTimeNs}");
    }

    private static void TestQeComboDifferentIndexes()
    {
        var pair = new Pair { Id = Guid.NewGuid(), RunMode = RunMode.QeCombo };
        var left = new RaceEntry { Lane = 0, Tree = new TreeType { Id = 1, CountdownSpeed = 500, CountdownType = 0 }, HandicapIndex = "00.30" };
        var right = new RaceEntry { Lane = 1, Tree = new TreeType { Id = 2, CountdownSpeed = 400, CountdownType = 0 }, HandicapIndex = "00.20" };
        pair.Runs = new Run[] { new Run { Entry = left }, new Run { Entry = right } };
        TimingHelpers.PopulateExpectedReactionTimes(pair);
        Console.WriteLine($"QeCombo diff expected L={pair.Runs[0].ReactionTime?.ExpectedReactionTimeNs} R={pair.Runs[1].ReactionTime?.ExpectedReactionTimeNs}");
    }

    private static void TestEliminationsEqualIndexes()
    {
        var pair = new Pair { Id = Guid.NewGuid(), RunMode = RunMode.Eliminations };
        var left = new RaceEntry { Lane = 0, Tree = new TreeType { Id = 1, CountdownSpeed = 700, CountdownType = 1 }, HandicapIndex = "00.00" };
        var right = new RaceEntry { Lane = 1, Tree = new TreeType { Id = 2, CountdownSpeed = 700, CountdownType = 1 }, HandicapIndex = "00.00" };
        pair.Runs = new Run[] { new Run { Entry = left }, new Run { Entry = right } };
        TimingHelpers.PopulateExpectedReactionTimes(pair);
        Console.WriteLine($"Elim equal expected L={pair.Runs[0].ReactionTime?.ExpectedReactionTimeNs} R={pair.Runs[1].ReactionTime?.ExpectedReactionTimeNs}");
    }

    private static string DescribeRun(string label, Run run)
    {
        var remarks = run.Remarks != null && run.Remarks.Length > 0 ? string.Join(",", run.Remarks) : "<none>";
        var results = run.Results != null && run.Results.Length > 0 ? string.Join(",", run.Results) : "<none>";
        return $"{label}: Remarks=[{remarks}] Results=[{results}]";
    }

    private static Pair MakeElimPair(bool worstFoul, out Run left, out Run right)
    {
        var category = new Category { Id = 1, WorstFoul = worstFoul };
        var leftEntry = new RaceEntry { Lane = 0 };
        var rightEntry = new RaceEntry { Lane = 1 };
        left = new Run { Entry = leftEntry };
        right = new Run { Entry = rightEntry };
        return new Pair
        {
            Id = Guid.NewGuid(),
            RunMode = RunMode.Eliminations,
            Category = category,
            Runs = new Run[] { left, right }
        };
    }

    private static Pair MakeBreakoutPair(ElimMode elimMode, string? leftClass, string? rightClass, out Run left, out Run right)
    {
        var category = new Category { Id = 1, ElimMode = elimMode };
        var leftEntry = new RaceEntry { Lane = 0, Class = leftClass };
        var rightEntry = new RaceEntry { Lane = 1, Class = rightClass };
        left = new Run { Entry = leftEntry };
        right = new Run { Entry = rightEntry };
        return new Pair
        {
            Id = Guid.NewGuid(),
            RunMode = RunMode.Eliminations,
            Category = category,
            Runs = new Run[] { left, right }
        };
    }

    private static void TestWinnerLose_SingleFoul()
    {
        Console.WriteLine("Running WinnerLose SingleFoul test (expect Left=Lose, Right=Winner)");
        var pair = MakeElimPair(worstFoul: false, out var left, out var right);
        left.Remarks = new[] { RunRemarks.Foul };
        left.ReactionTime = new ReactionTime { ValueNs = -100, ExpectedReactionTimeNs = 0, TriggerTimestampNanoseconds = 1000 };
        right.ReactionTime = new ReactionTime { ValueNs = 50, ExpectedReactionTimeNs = 0, TriggerTimestampNanoseconds = 1050 };
        TimingHelpers.EvaluateWinnerLoseResult(pair);
        Console.WriteLine(DescribeRun("Left", left));
        Console.WriteLine(DescribeRun("Right", right));
    }

    private static void TestWinnerLose_BothFoul_WorstFoulFalse()
    {
        Console.WriteLine("Running WinnerLose BothFoul WorstFoulFalse test (expect Left=Lose via earlier trigger ts, despite less-negative delta)");
        var pair = MakeElimPair(worstFoul: false, out var left, out var right);
        left.Remarks = new[] { RunRemarks.Foul };
        right.Remarks = new[] { RunRemarks.Foul };
        // Left triggers earlier (smaller timestamp) but has a smaller (less negative) delta.
        left.ReactionTime = new ReactionTime { ValueNs = -10, ExpectedReactionTimeNs = 0, TriggerTimestampNanoseconds = 1000 };
        right.ReactionTime = new ReactionTime { ValueNs = -500, ExpectedReactionTimeNs = 0, TriggerTimestampNanoseconds = 2000 };
        TimingHelpers.EvaluateWinnerLoseResult(pair);
        Console.WriteLine(DescribeRun("Left", left));
        Console.WriteLine(DescribeRun("Right", right));
    }

    private static void TestWinnerLose_BothFoul_WorstFoulTrue()
    {
        Console.WriteLine("Running WinnerLose BothFoul WorstFoulTrue test (expect Right=Lose via more-negative delta, despite earlier left trigger ts)");
        var pair = MakeElimPair(worstFoul: true, out var left, out var right);
        left.Remarks = new[] { RunRemarks.Foul };
        right.Remarks = new[] { RunRemarks.Foul };
        left.ReactionTime = new ReactionTime { ValueNs = -10, ExpectedReactionTimeNs = 0, TriggerTimestampNanoseconds = 1000 };
        right.ReactionTime = new ReactionTime { ValueNs = -500, ExpectedReactionTimeNs = 0, TriggerTimestampNanoseconds = 2000 };
        TimingHelpers.EvaluateWinnerLoseResult(pair);
        Console.WriteLine(DescribeRun("Left", left));
        Console.WriteLine(DescribeRun("Right", right));
    }

    private static void TestWinnerLose_WorstFoulTrue_PendingSecondReactionTime()
    {
        Console.WriteLine("Running WinnerLose WorstFoulTrue PendingSecondReactionTime test (expect no decision yet, then decided once, no swap)");
        var pair = MakeElimPair(worstFoul: true, out var left, out var right);

        // Phase 1: left fouls, right hasn't received a reaction time yet.
        left.Remarks = new[] { RunRemarks.Foul };
        left.ReactionTime = new ReactionTime { ValueNs = -10, ExpectedReactionTimeNs = 0, TriggerTimestampNanoseconds = 1000 };
        TimingHelpers.EvaluateWinnerLoseResult(pair);
        Console.WriteLine("  Phase 1 (right pending): " + DescribeRun("Left", left));
        Console.WriteLine("  Phase 1 (right pending): " + DescribeRun("Right", right));
        if ((left.Results?.Contains(RunResult.Lose) ?? false) || (right.Results?.Contains(RunResult.Winner) ?? false))
        {
            Console.WriteLine("  FAIL: decision made before right's reaction time was received");
        }

        // Phase 2: right's reaction time arrives - it fouled worse than left.
        right.Remarks = new[] { RunRemarks.Foul };
        right.ReactionTime = new ReactionTime { ValueNs = -500, ExpectedReactionTimeNs = 0, TriggerTimestampNanoseconds = 2000 };
        TimingHelpers.EvaluateWinnerLoseResult(pair);
        Console.WriteLine("  Phase 2 (both received): " + DescribeRun("Left", left));
        Console.WriteLine("  Phase 2 (both received): " + DescribeRun("Right", right));
    }

    private static void TestWinnerLose_WorstFoulTrue_SingleFoulOnceBothReceived()
    {
        Console.WriteLine("Running WinnerLose WorstFoulTrue SingleFoulOnceBothReceived test (expect pending, then Left=Lose once right's clean RT is in)");
        var pair = MakeElimPair(worstFoul: true, out var left, out var right);

        // Phase 1: left fouls, right hasn't received a reaction time yet - should stay pending.
        left.Remarks = new[] { RunRemarks.Foul };
        left.ReactionTime = new ReactionTime { ValueNs = -100, ExpectedReactionTimeNs = 0, TriggerTimestampNanoseconds = 1000 };
        TimingHelpers.EvaluateWinnerLoseResult(pair);
        Console.WriteLine("  Phase 1 (right pending): " + DescribeRun("Left", left));
        Console.WriteLine("  Phase 1 (right pending): " + DescribeRun("Right", right));

        // Phase 2: right's (clean, non-foul) reaction time arrives - only left fouled.
        right.ReactionTime = new ReactionTime { ValueNs = 50, ExpectedReactionTimeNs = 0, TriggerTimestampNanoseconds = 1500 };
        TimingHelpers.EvaluateWinnerLoseResult(pair);
        Console.WriteLine("  Phase 2 (both received): " + DescribeRun("Left", left));
        Console.WriteLine("  Phase 2 (both received): " + DescribeRun("Right", right));
    }

    private static void TestWinnerLose_NoFoulNoFinish()
    {
        Console.WriteLine("Running WinnerLose NoFoulNoFinish test (expect neither Winner nor Lose)");
        var pair = MakeElimPair(worstFoul: false, out var left, out var right);
        TimingHelpers.EvaluateWinnerLoseResult(pair);
        Console.WriteLine(DescribeRun("Left", left));
        Console.WriteLine(DescribeRun("Right", right));
    }

    private static void TestWinnerLose_FirstFinishNoFoul()
    {
        Console.WriteLine("Running WinnerLose FirstFinishNoFoul test (expect Left=Winner+FirstFinish, Right=Lose)");
        var pair = MakeElimPair(worstFoul: false, out var left, out var right);
        // Winner/Lose is now gated on Run.FinishEtNs being known for BOTH lanes (not
        // just RunResult.FirstFinish), so both must be set here to simulate "both finished".
        left.FinishEtNs = 9_500_000_000;
        right.FinishEtNs = 9_600_000_000;
        left.Results = new[] { RunResult.FirstFinish };
        TimingHelpers.EvaluateWinnerLoseResult(pair);
        Console.WriteLine(DescribeRun("Left", left));
        Console.WriteLine(DescribeRun("Right", right));
        if (!(left.Results?.Contains(RunResult.Winner) ?? false) || !(right.Results?.Contains(RunResult.Lose) ?? false))
        {
            Console.WriteLine("  FAIL: expected Left=Winner, Right=Lose once both lanes' FinishEtNs are known");
        }
    }

    private static void TestWinnerLose_EarlyFirstFinishDoesNotPrematurelyDecideWinner()
    {
        Console.WriteLine("Running WinnerLose EarlyFirstFinishDoesNotPrematurelyDecideWinner test (expect neither Winner nor Lose)");
        var pair = MakeElimPair(worstFoul: false, out var left, out var right);
        // Simulate MaybeCheckAndCompleteRun's new *early/tentative* FirstFinish tag: left
        // finished and is tentatively first, but right hasn't finished yet at all (no
        // FinishEtNs). The overall Winner/Lose decision must NOT be made off FirstFinish
        // alone - it must wait for both lanes' FinishEtNs.
        left.FinishEtNs = 9_500_000_000;
        left.Results = new[] { RunResult.FirstFinish };
        TimingHelpers.EvaluateWinnerLoseResult(pair);
        Console.WriteLine(DescribeRun("Left", left));
        Console.WriteLine(DescribeRun("Right", right));
        if ((left.Results?.Contains(RunResult.Winner) ?? false) || (right.Results?.Contains(RunResult.Lose) ?? false))
        {
            Console.WriteLine("  FAIL: Winner/Lose was decided from an early FirstFinish tag before the other lane finished");
        }
    }

    private static void TestWinnerLose_FoulOverridesFirstFinish()
    {
        Console.WriteLine("Running WinnerLose FoulOverridesFirstFinish test (expect Left=Lose+FirstFinish (retained), Right=Winner)");
        var pair = MakeElimPair(worstFoul: false, out var left, out var right);
        left.Results = new[] { RunResult.FirstFinish };
        left.Remarks = new[] { RunRemarks.Foul };
        left.ReactionTime = new ReactionTime { ValueNs = -100, ExpectedReactionTimeNs = 0, TriggerTimestampNanoseconds = 1000 };
        TimingHelpers.EvaluateWinnerLoseResult(pair);
        Console.WriteLine(DescribeRun("Left", left));
        Console.WriteLine(DescribeRun("Right", right));
    }

    private static void TestWinnerLose_IndeterminateTie()
    {
        Console.WriteLine("Running WinnerLose IndeterminateTie test (expect neither Winner nor Lose)");
        var pair = MakeElimPair(worstFoul: false, out var left, out var right);
        left.FinishEtNs = 9_500_000_000;
        right.FinishEtNs = 9_500_000_000;
        left.Results = new[] { RunResult.Indeterminate };
        right.Results = new[] { RunResult.Indeterminate };
        TimingHelpers.EvaluateWinnerLoseResult(pair);
        Console.WriteLine(DescribeRun("Left", left));
        Console.WriteLine(DescribeRun("Right", right));
    }

    private static void TestWinnerLose_ByeRun()
    {
        Console.WriteLine("Running WinnerLose ByeRun test (expect Left=Winner despite its own Foul, Right stays empty)");
        var pair = MakeElimPair(worstFoul: false, out var left, out var right);
        right.Remarks = new[] { RunRemarks.NoVehicleStaged };
        left.Remarks = new[] { RunRemarks.Foul };
        left.ReactionTime = new ReactionTime { ValueNs = -100, ExpectedReactionTimeNs = 0, TriggerTimestampNanoseconds = 1000 };
        TimingHelpers.EvaluateWinnerLoseResult(pair);
        Console.WriteLine(DescribeRun("Left", left));
        Console.WriteLine(DescribeRun("Right", right));
        if (!(left.Results?.Contains(RunResult.Winner) ?? false))
        {
            Console.WriteLine("  FAIL: solo staged lane was not awarded Winner despite a regular Foul");
        }
    }

    private static void TestWinnerLose_NonElimMode()
    {
        Console.WriteLine("Running WinnerLose NonElimMode test (Qualifying + foul, expect no Winner/Lose)");
        var pair = MakeElimPair(worstFoul: false, out var left, out var right);
        pair.RunMode = RunMode.Qualifying;
        left.Remarks = new[] { RunRemarks.Foul };
        left.ReactionTime = new ReactionTime { ValueNs = -100, ExpectedReactionTimeNs = 0, TriggerTimestampNanoseconds = 1000 };
        TimingHelpers.EvaluateWinnerLoseResult(pair);
        Console.WriteLine(DescribeRun("Left", left));
        Console.WriteLine(DescribeRun("Right", right));
    }

    private static void TestWinnerLose_SingleDsFoul()
    {
        Console.WriteLine("Running WinnerLose SingleDsFoul test (expect Left=Lose, Right=Winner)");
        var pair = MakeElimPair(worstFoul: false, out var left, out var right);
        left.Remarks = new[] { RunRemarks.DsFoul };
        TimingHelpers.EvaluateWinnerLoseResult(pair);
        Console.WriteLine(DescribeRun("Left", left));
        Console.WriteLine(DescribeRun("Right", right));
    }

    private static void TestWinnerLose_BothDsFoul_NobodyGoesThrough()
    {
        Console.WriteLine("Running WinnerLose BothDsFoul test (expect Left=Lose, Right=Lose, neither Winner)");
        var pair = MakeElimPair(worstFoul: false, out var left, out var right);
        left.Remarks = new[] { RunRemarks.DsFoul };
        right.Remarks = new[] { RunRemarks.DsFoul };
        TimingHelpers.EvaluateWinnerLoseResult(pair);
        Console.WriteLine(DescribeRun("Left", left));
        Console.WriteLine(DescribeRun("Right", right));
        if ((left.Results?.Contains(RunResult.Winner) ?? false) || (right.Results?.Contains(RunResult.Winner) ?? false))
        {
            Console.WriteLine("  FAIL: a Winner was assigned when both lanes had DsFoul");
        }
    }

    private static void TestWinnerLose_DsFoulTakesPriorityOverFirstFinish()
    {
        Console.WriteLine("Running WinnerLose DsFoulTakesPriorityOverFirstFinish test (expect Left=Lose+FirstFinish (retained), Right=Winner)");
        var pair = MakeElimPair(worstFoul: false, out var left, out var right);
        left.Results = new[] { RunResult.FirstFinish };
        left.Remarks = new[] { RunRemarks.DsFoul };
        TimingHelpers.EvaluateWinnerLoseResult(pair);
        Console.WriteLine(DescribeRun("Left", left));
        Console.WriteLine(DescribeRun("Right", right));
    }

    private static void TestWinnerLose_DsFoulSelfCorrects()
    {
        Console.WriteLine("Running WinnerLose DsFoulSelfCorrects test (DsFoul retracted -> falls back to no-decision)");
        var pair = MakeElimPair(worstFoul: false, out var left, out var right);
        left.Remarks = new[] { RunRemarks.DsFoul };
        TimingHelpers.EvaluateWinnerLoseResult(pair);
        Console.WriteLine("  Phase 1 (DsFoul present): " + DescribeRun("Left", left));
        Console.WriteLine("  Phase 1 (DsFoul present): " + DescribeRun("Right", right));

        // DsFoul is retracted (e.g. a correction from the treeserver).
        left.Remarks = Array.Empty<RunRemarks>();
        TimingHelpers.EvaluateWinnerLoseResult(pair);
        Console.WriteLine("  Phase 2 (DsFoul retracted): " + DescribeRun("Left", left));
        Console.WriteLine("  Phase 2 (DsFoul retracted): " + DescribeRun("Right", right));
    }

    private static void TestWinnerLose_ByeRunOverridesDsFoul()
    {
        Console.WriteLine("Running WinnerLose ByeRunOverridesDsFoul test (expect Left=Winner despite its own DsFoul, Right stays empty)");
        var pair = MakeElimPair(worstFoul: false, out var left, out var right);
        right.Remarks = new[] { RunRemarks.NoVehicleStaged };
        left.Remarks = new[] { RunRemarks.DsFoul };
        TimingHelpers.EvaluateWinnerLoseResult(pair);
        Console.WriteLine(DescribeRun("Left", left));
        Console.WriteLine(DescribeRun("Right", right));
        if (!(left.Results?.Contains(RunResult.Winner) ?? false) || (left.Results?.Contains(RunResult.Lose) ?? false))
        {
            Console.WriteLine("  FAIL: solo staged lane was not awarded Winner despite a deep-stage foul");
        }
    }

    private static void TestWinnerLose_ByeRunOverridesBothDsFoul()
    {
        Console.WriteLine("Running WinnerLose ByeRunOverridesBothDsFoul test (defensive: even if the empty lane somehow also carries DsFoul, bye rule still wins)");
        var pair = MakeElimPair(worstFoul: false, out var left, out var right);
        right.Remarks = new[] { RunRemarks.NoVehicleStaged, RunRemarks.DsFoul };
        left.Remarks = new[] { RunRemarks.DsFoul };
        TimingHelpers.EvaluateWinnerLoseResult(pair);
        Console.WriteLine(DescribeRun("Left", left));
        Console.WriteLine(DescribeRun("Right", right));
        if (!(left.Results?.Contains(RunResult.Winner) ?? false))
        {
            Console.WriteLine("  FAIL: solo staged lane was not awarded Winner");
        }
    }

    // ---- Breakout tests ----

    private static void TestBreakout_EvaluateRemark_BelowIndex()
    {
        Console.WriteLine("Running Breakout EvaluateRemark BelowIndex test (expect Breakout remark + negative BreakoutDeltaNs)");
        var category = new Category { Id = 1, ElimMode = ElimMode.Breakout };
        var run = new Run { Entry = new RaceEntry { Lane = 0, HandicapIndex = "09.50" } };
        // ET 9.45s < Index 9.50s -> breakout by 0.05s
        TimingHelpers.EvaluateBreakoutRemark(run, RunMode.Eliminations, category, 9_450_000_000L);
        Console.WriteLine(DescribeRun("Run", run) + $" BreakoutDeltaNs={run.BreakoutDeltaNs}");
        if (!(run.Remarks?.Contains(RunRemarks.Breakout) ?? false) || run.BreakoutDeltaNs != -50_000_000L)
        {
            Console.WriteLine("  FAIL: expected Breakout remark with BreakoutDeltaNs=-50000000");
        }
    }

    private static void TestBreakout_EvaluateRemark_AboveIndex()
    {
        Console.WriteLine("Running Breakout EvaluateRemark AboveIndex test (expect no Breakout remark, null delta)");
        var category = new Category { Id = 1, ElimMode = ElimMode.Breakout };
        var run = new Run { Entry = new RaceEntry { Lane = 0, HandicapIndex = "09.50" } };
        // ET 9.55s > Index 9.50s -> no breakout
        TimingHelpers.EvaluateBreakoutRemark(run, RunMode.Eliminations, category, 9_550_000_000L);
        Console.WriteLine(DescribeRun("Run", run) + $" BreakoutDeltaNs={run.BreakoutDeltaNs}");
        if ((run.Remarks?.Contains(RunRemarks.Breakout) ?? false) || run.BreakoutDeltaNs != null)
        {
            Console.WriteLine("  FAIL: expected no Breakout remark and null BreakoutDeltaNs");
        }
    }

    private static void TestBreakout_EvaluateRemark_NoBreakoutCategory()
    {
        Console.WriteLine("Running Breakout EvaluateRemark NoBreakoutCategory test (expect never flagged)");
        var category = new Category { Id = 1, ElimMode = ElimMode.NoBreakout };
        var run = new Run { Entry = new RaceEntry { Lane = 0, HandicapIndex = "09.50" } };
        TimingHelpers.EvaluateBreakoutRemark(run, RunMode.Eliminations, category, 9_450_000_000L);
        Console.WriteLine(DescribeRun("Run", run));
        if (run.Remarks?.Contains(RunRemarks.Breakout) ?? false)
        {
            Console.WriteLine("  FAIL: NoBreakout category should never flag Breakout");
        }
    }

    private static void TestBreakout_EvaluateRemark_NonElimMode()
    {
        Console.WriteLine("Running Breakout EvaluateRemark NonElimMode test (Qualifying, expect never flagged)");
        var category = new Category { Id = 1, ElimMode = ElimMode.Breakout };
        var run = new Run { Entry = new RaceEntry { Lane = 0, HandicapIndex = "09.50" } };
        TimingHelpers.EvaluateBreakoutRemark(run, RunMode.Qualifying, category, 9_450_000_000L);
        Console.WriteLine(DescribeRun("Run", run));
        if (run.Remarks?.Contains(RunRemarks.Breakout) ?? false)
        {
            Console.WriteLine("  FAIL: non-Elimination/QeCombo mode should never flag Breakout");
        }
    }

    private static void TestBreakout_EvaluateRemark_BlankIndex()
    {
        Console.WriteLine("Running Breakout EvaluateRemark BlankIndex test (expect never flagged, index parses to 0)");
        var category = new Category { Id = 1, ElimMode = ElimMode.Breakout };
        var run = new Run { Entry = new RaceEntry { Lane = 0, HandicapIndex = "" } };
        TimingHelpers.EvaluateBreakoutRemark(run, RunMode.Eliminations, category, 9_450_000_000L);
        Console.WriteLine(DescribeRun("Run", run));
        if (run.Remarks?.Contains(RunRemarks.Breakout) ?? false)
        {
            Console.WriteLine("  FAIL: blank Index should parse to 0 and never flag Breakout against a positive ET");
        }
    }

    private static void TestWinnerLose_Breakout_SingleBreakout_OtherFinishedClean()
    {
        Console.WriteLine("Running WinnerLose Breakout SingleBreakout OtherFinishedClean test (expect Right=Winner despite Left finishing first)");
        var pair = MakeBreakoutPair(ElimMode.Breakout, null, null, out var left, out var right);
        left.FinishEtNs = 9_450_000_000; // broke out
        left.BreakoutDeltaNs = -50_000_000;
        left.Remarks = new[] { RunRemarks.Breakout };
        left.Results = new[] { RunResult.FirstFinish }; // left crossed the stripe first
        right.FinishEtNs = 9_600_000_000; // finished clean (no breakout)
        TimingHelpers.EvaluateWinnerLoseResult(pair);
        Console.WriteLine(DescribeRun("Left", left));
        Console.WriteLine(DescribeRun("Right", right));
        if (!(right.Results?.Contains(RunResult.Winner) ?? false) || !(left.Results?.Contains(RunResult.Lose) ?? false))
        {
            Console.WriteLine("  FAIL: expected the non-breakout lane (Right) to win despite Left finishing first");
        }
    }

    private static void TestWinnerLose_Breakout_BothBreakout_UnequalMagnitudes()
    {
        Console.WriteLine("Running WinnerLose Breakout BothBreakout UnequalMagnitudes test (expect Left=Winner, least-negative breakout)");
        var pair = MakeBreakoutPair(ElimMode.Breakout, null, null, out var left, out var right);
        left.FinishEtNs = 9_450_000_000;
        left.BreakoutDeltaNs = -50_000_000; // smaller undershoot
        left.Remarks = new[] { RunRemarks.Breakout };
        right.FinishEtNs = 9_300_000_000;
        right.BreakoutDeltaNs = -200_000_000; // worse breakout
        right.Remarks = new[] { RunRemarks.Breakout };
        TimingHelpers.EvaluateWinnerLoseResult(pair);
        Console.WriteLine(DescribeRun("Left", left));
        Console.WriteLine(DescribeRun("Right", right));
        if (!(left.Results?.Contains(RunResult.Winner) ?? false) || !(right.Results?.Contains(RunResult.Lose) ?? false))
        {
            Console.WriteLine("  FAIL: expected Left (least-negative breakout) to win");
        }
    }

    private static void TestWinnerLose_Breakout_BothBreakout_EqualMagnitudes_NoWin()
    {
        Console.WriteLine("Running WinnerLose Breakout BothBreakout EqualMagnitudes test (expect neither Winner nor Lose)");
        var pair = MakeBreakoutPair(ElimMode.Breakout, null, null, out var left, out var right);
        left.FinishEtNs = 9_450_000_000;
        left.BreakoutDeltaNs = -50_000_000;
        left.Remarks = new[] { RunRemarks.Breakout };
        right.FinishEtNs = 9_450_000_000;
        right.BreakoutDeltaNs = -50_000_000; // exactly equal breakout
        right.Remarks = new[] { RunRemarks.Breakout };
        TimingHelpers.EvaluateWinnerLoseResult(pair);
        Console.WriteLine(DescribeRun("Left", left));
        Console.WriteLine(DescribeRun("Right", right));
        if ((left.Results?.Contains(RunResult.Winner) ?? false) || (right.Results?.Contains(RunResult.Winner) ?? false)
            || (left.Results?.Contains(RunResult.Lose) ?? false) || (right.Results?.Contains(RunResult.Lose) ?? false))
        {
            Console.WriteLine("  FAIL: expected no win awarded when both lanes break out by exactly the same amount");
        }
    }

    private static void TestWinnerLose_Breakout_WaitingForOtherEt_NoNowNs()
    {
        Console.WriteLine("Running WinnerLose Breakout WaitingForOtherEt NoNowNs test (expect no decision yet)");
        var pair = MakeBreakoutPair(ElimMode.Breakout, null, null, out var left, out var right);
        left.FinishEtNs = 9_450_000_000;
        left.BreakoutDeltaNs = -50_000_000;
        left.Remarks = new[] { RunRemarks.Breakout };
        // right hasn't finished yet - no FinishEtNs.
        TimingHelpers.EvaluateWinnerLoseResult(pair); // no nowNs supplied
        Console.WriteLine(DescribeRun("Left", left));
        Console.WriteLine(DescribeRun("Right", right));
        if ((left.Results?.Contains(RunResult.Winner) ?? false) || (left.Results?.Contains(RunResult.Lose) ?? false)
            || (right.Results?.Contains(RunResult.Winner) ?? false) || (right.Results?.Contains(RunResult.Lose) ?? false))
        {
            Console.WriteLine("  FAIL: expected no decision while the other lane hasn't finished and no nowNs was supplied");
        }
    }

    private static void TestWinnerLose_Breakout_EarlyDecision_NotYetExceeded()
    {
        Console.WriteLine("Running WinnerLose Breakout EarlyDecision NotYetExceeded test (expect still waiting)");
        var pair = MakeBreakoutPair(ElimMode.Breakout, null, null, out var left, out var right);
        left.FinishEtNs = 9_450_000_000; // broke out by 0.05s (Index 9.50)
        left.BreakoutDeltaNs = -50_000_000;
        left.Remarks = new[] { RunRemarks.Breakout };

        // Right launched at t=1_000_000_000 (ns, absolute), index 9.50s -> threshold = 9.50 - 0.05 = 9.45s after launch.
        right.Entry!.HandicapIndex = "09.50";
        right.ReactionTime = new ReactionTime { TriggerTimestampNanoseconds = 1_000_000_000 };
        // Only 9.0s has elapsed since right's launch - below the 9.45s threshold.
        long nowNs = 1_000_000_000 + 9_000_000_000;
        TimingHelpers.EvaluateWinnerLoseResult(pair, nowNs);
        Console.WriteLine(DescribeRun("Left", left));
        Console.WriteLine(DescribeRun("Right", right));
        if ((left.Results?.Contains(RunResult.Winner) ?? false) || (right.Results?.Contains(RunResult.Winner) ?? false))
        {
            Console.WriteLine("  FAIL: expected no early decision yet (threshold not exceeded)");
        }
    }

    private static void TestWinnerLose_Breakout_EarlyDecision_Exceeded()
    {
        Console.WriteLine("Running WinnerLose Breakout EarlyDecision Exceeded test (expect Right awarded the win early)");
        var pair = MakeBreakoutPair(ElimMode.Breakout, null, null, out var left, out var right);
        left.FinishEtNs = 9_450_000_000; // broke out by 0.05s (Index 9.50)
        left.BreakoutDeltaNs = -50_000_000;
        left.Remarks = new[] { RunRemarks.Breakout };

        // Right launched at t=1_000_000_000 (ns, absolute), index 9.50s -> threshold = 9.45s after launch.
        right.Entry!.HandicapIndex = "09.50";
        right.ReactionTime = new ReactionTime { TriggerTimestampNanoseconds = 1_000_000_000 };
        // 9.5s has elapsed since right's launch - above the 9.45s threshold.
        long nowNs = 1_000_000_000 + 9_500_000_000;
        TimingHelpers.EvaluateWinnerLoseResult(pair, nowNs);
        Console.WriteLine(DescribeRun("Left", left));
        Console.WriteLine(DescribeRun("Right", right));
        if (!(right.Results?.Contains(RunResult.Winner) ?? false) || !(left.Results?.Contains(RunResult.Lose) ?? false))
        {
            Console.WriteLine("  FAIL: expected Right to be awarded the win early once the projection threshold was exceeded");
        }
    }

    private static void TestWinnerLose_BreakoutDiffClass_SameClass_SkipsToFirstFinish()
    {
        Console.WriteLine("Running WinnerLose BreakoutDiffClass SameClass test (expect Left=Winner via plain first-finish, breakout ignored)");
        var pair = MakeBreakoutPair(ElimMode.BreakoutDiffClass, "Super Street", "super street", out var left, out var right);
        left.FinishEtNs = 9_450_000_000;
        left.BreakoutDeltaNs = -50_000_000;
        left.Remarks = new[] { RunRemarks.Breakout };
        left.Results = new[] { RunResult.FirstFinish };
        right.FinishEtNs = 9_600_000_000;
        TimingHelpers.EvaluateWinnerLoseResult(pair);
        Console.WriteLine(DescribeRun("Left", left));
        Console.WriteLine(DescribeRun("Right", right));
        if (!(left.Results?.Contains(RunResult.Winner) ?? false) || !(right.Results?.Contains(RunResult.Lose) ?? false))
        {
            Console.WriteLine("  FAIL: same-class BreakoutDiffClass should skip breakout and award the plain first-finish winner");
        }
    }

    private static void TestWinnerLose_BreakoutDiffClass_DifferentClass_BehavesLikeBreakout()
    {
        Console.WriteLine("Running WinnerLose BreakoutDiffClass DifferentClass test (expect Right=Winner, breakout decides)");
        var pair = MakeBreakoutPair(ElimMode.BreakoutDiffClass, "Super Street", "Modified", out var left, out var right);
        left.FinishEtNs = 9_450_000_000;
        left.BreakoutDeltaNs = -50_000_000;
        left.Remarks = new[] { RunRemarks.Breakout };
        left.Results = new[] { RunResult.FirstFinish };
        right.FinishEtNs = 9_600_000_000;
        TimingHelpers.EvaluateWinnerLoseResult(pair);
        Console.WriteLine(DescribeRun("Left", left));
        Console.WriteLine(DescribeRun("Right", right));
        if (!(right.Results?.Contains(RunResult.Winner) ?? false) || !(left.Results?.Contains(RunResult.Lose) ?? false))
        {
            Console.WriteLine("  FAIL: different-class BreakoutDiffClass should let the non-breakout lane win despite Left finishing first");
        }
    }

    private static void TestWinnerLose_Breakout_FoulBeatsBreakout()
    {
        Console.WriteLine("Running WinnerLose Breakout FoulBeatsBreakout test (expect Right=Lose via foul, despite Left's breakout)");
        var pair = MakeBreakoutPair(ElimMode.Breakout, null, null, out var left, out var right);
        left.FinishEtNs = 9_450_000_000;
        left.BreakoutDeltaNs = -50_000_000;
        left.Remarks = new[] { RunRemarks.Breakout };
        right.Remarks = new[] { RunRemarks.Foul };
        right.ReactionTime = new ReactionTime { ValueNs = -100, ExpectedReactionTimeNs = 0, TriggerTimestampNanoseconds = 1000 };
        TimingHelpers.EvaluateWinnerLoseResult(pair);
        Console.WriteLine(DescribeRun("Left", left));
        Console.WriteLine(DescribeRun("Right", right));
        if (!(left.Results?.Contains(RunResult.Winner) ?? false) || !(right.Results?.Contains(RunResult.Lose) ?? false))
        {
            Console.WriteLine("  FAIL: expected foul to beat breakout (Left wins despite its own breakout)");
        }
    }

    private static void TestWinnerLose_Breakout_ByeRun_StillGetsRemarkAndWin()
    {
        Console.WriteLine("Running WinnerLose Breakout ByeRun test (expect Left=Winner via bye rule, Breakout remark still present)");
        var pair = MakeBreakoutPair(ElimMode.Breakout, null, null, out var left, out var right);
        right.Remarks = new[] { RunRemarks.NoVehicleStaged };
        left.FinishEtNs = 9_450_000_000;
        left.BreakoutDeltaNs = -50_000_000;
        left.Remarks = new[] { RunRemarks.Breakout };
        TimingHelpers.EvaluateWinnerLoseResult(pair);
        Console.WriteLine(DescribeRun("Left", left));
        Console.WriteLine(DescribeRun("Right", right));
        if (!(left.Results?.Contains(RunResult.Winner) ?? false) || !(left.Remarks?.Contains(RunRemarks.Breakout) ?? false))
        {
            Console.WriteLine("  FAIL: expected solo staged lane to win via the bye rule and still carry its Breakout remark");
        }
    }
}
