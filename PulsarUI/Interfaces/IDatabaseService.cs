using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PulsarUI.Models;

namespace PulsarUI.Interfaces
{
    public interface IDatabaseService
    {
        Task WriteQueueAsync(RaceEntry entry);
        Task<IndexList> GetIndexListAsync(RaceEntry entry);
        
        Task<RacerDetails> GetRacerDetailsAsync(RaceEntry entry);
        
        Task<List<Category>> GetCategoryListAsync();

        Task<List<string?>> GetTreeTypesAsync();
        
        Task<List<string?>> GetFinishLinesAsync();
    }
}