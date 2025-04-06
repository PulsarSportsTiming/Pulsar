namespace PulsarUI.Models;

public class RaceEntry
{
    public int QueueIndex { get; set; }
    public int Lane { get; set; }
    public string? RaceNumber { get; set; }
    public string? HandicapIndex { get; set; }
    public int Tree { get; set; }
    
    public int Category { get; set; }
    
    public int Finish { get; set; }
}