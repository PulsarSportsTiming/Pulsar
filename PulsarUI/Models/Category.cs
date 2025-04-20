namespace PulsarUI.Models;

public class Category
{
    public int CategoryOrder { get; set; }
    
    public int CategoryId { get; set; }
    public string? CategoryName { get; set; }
    
    public string? CategoryTreeType { get; set; }
    
    public string? CategoryFinish { get; set; }
    
    public int LastMode { get; set; }
    
    public int LastRound { get; set; }
}