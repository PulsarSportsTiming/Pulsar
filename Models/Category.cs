namespace PulsarUI.Models;

public class Category
{
    public int Id { get; set; }
    public int Order { get; set; }
    public string? Name { get; set; }
    public int Finish { get; set; }
    public int RunTimeout { get; set; }
    public string? BumpEt { get; set; }
    public int TreeType { get; set; }
    public ElimMode ElimMode { get; set; }
    public bool SplitTreeAllowed { get; set; }
    public bool StaggeredStartsAllowed { get; set; }
    public int StartMode { get; set; }
    public bool StageFreeze { get; set; }
    public bool DeepStageFoul { get; set; }
    public bool SbElimSpeed { get; set; }
    public bool SbCycleUnits { get; set; }
    public bool WorstFoul { get; set; }
    public bool FoulInEmpty { get; set; }
    public int StageSettle { get; set; }
    public int AutoStartStageToStart { get; set; }
    public int AutoStartVariance { get; set; }
    public int AutoStartTimeout { get; set; }
    public int DefaultClass { get; set; }
    public int LastMode { get; set; }
    public int LastRound { get; set; }
    public int DelayMin { get; set; }
    public int DelayMax { get; set; }
    public bool FixedTree { get; set; }
    public bool FixedTrack { get; set; }
    public int SbTimeout { get; set; }
}