using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PulsarUI.Models;

namespace PulsarUI.Interfaces
{
    public interface IDatabaseService
    {
        Task WriteQueueCategoriesAsync(CategQueueItem category);
        Task WriteQueueRacersAsync(RaceEntry entry);
        Task<IndexList> GetIndexListAsync(RaceEntry entry, CategQueueItem category);
        
        Task<RaceEntry> GetRacerDetailsAsync(RaceEntry entry, CategQueueItem category);
        
        Task<List<Category>> GetCategoryListAsync();

        Task<List<string?>> GetTreeTypesAsync();
        
        Task<List<string?>> GetFinishLinesAsync();
    }
}