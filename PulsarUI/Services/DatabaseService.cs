using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using PulsarUI.Interfaces;
using PulsarUI.Models;

namespace PulsarUI.Services
{
    public class DatabaseService : IDatabaseService
    {
        private readonly string _connectionString;

        public DatabaseService(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task WriteQueueAsync(RaceEntry entry)
        {
            const string sqlText = @"
                INSERT OR REPLACE INTO run_queue_racers (queue_index, lane, race_number, handicap_index, tree)
                VALUES (
                    @QueueIndex, 
                    @Lane, 
                    @RaceNum, 
                    @HandicapIndex, 
                    COALESCE((SELECT tree FROM run_queue_racers WHERE queue_index = @QueueIndex AND lane = @Lane), 0)
                );";

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = sqlText;
            command.Parameters.AddWithValue("@QueueIndex", entry.QueueIndex);
            command.Parameters.AddWithValue("@Lane", entry.Lane);
            command.Parameters.AddWithValue("@RaceNum", entry.RaceNumber);
            command.Parameters.AddWithValue("@HandicapIndex", entry.HandicapIndex);

            await command.ExecuteNonQueryAsync();
        }
    }
}