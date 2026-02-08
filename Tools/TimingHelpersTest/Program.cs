using System;
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
}
