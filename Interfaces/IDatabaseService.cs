using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PulsarUI.Models;

namespace PulsarUI.Interfaces
{
    public interface IDatabaseService
    {
        Task<IndexList> GetIndexListAsync(RaceEntry entry, CategQueueItem category);
        
        Task<RaceEntry> GetRacerDetailsAsync(RaceEntry entry, CategQueueItem category);
        
        Task<List<Category>> GetCategoryListAsync();

        Task<List<TreeType>> GetTreeTypesAsync();
        
        Task<List<FinishLine>> GetFinishLinesAsync();

        // --- Run persistence (writes) ---

        Task<long?> ResolveRacerIdAsync(int categoryId, string? raceNumber);

        Task<long?> InsertRunPairAsync(RunPairInsert record);

        Task<long?> InsertRunIndvAsync(RunIndvInsert record);

        Task UpdateRunIndvReactionTimeAsync(long runIndvId, string? reactionTime);

        Task UpdateRunPairResultAsync(long pairId, int? firstLane, int? winnerLane);

        Task UpsertIncrementalEtAsync(long runIndvId, int distanceMm, string et);

        Task UpsertIncrementalSpeedAsync(long runIndvId, int distanceMm, string speed);

        Task InsertRunRemarkAsync(long runIndvId, int remark);

        Task DeleteRunRemarkAsync(long runIndvId, int remark);
    }
}