namespace PulsarUI.Models;
using Microsoft.Data.Sqlite;

public static class PairModel
{
    public static void WriteQueueRaceNum(int queueIndex, int Lane, string RaceNum)
    {
        string SQLText =
            "INSERT OR REPLACE INTO run_queue_racers (queue_index, lane, race_number, handicap_index, tree)\n" +
            "VALUES (\n" +
            "    @QueueIndex, -- queue_index\n" +
            "    @Lane, -- lane\n" +
            "    @RaceNum, -- race_number\n" +
            "    COALESCE((SELECT handicap_index FROM run_queue_racers WHERE queue_index = @QueueIndex AND lane = @Lane), 0), -- handicap_index\n" +
            "    COALESCE((SELECT tree FROM run_queue_racers WHERE queue_index = @QueueIndex AND lane = @Lane), 0) -- tree\n" +
            ");";
        
        using (var sqLiteConnection = new SqliteConnection("Data Source=/home/david/PulsarDB.db"))
        {
            sqLiteConnection.Open();

            var sqLiteCommand = sqLiteConnection.CreateCommand();
            sqLiteCommand.CommandText = SQLText;
            sqLiteCommand.Parameters.AddWithValue("@RaceNum", RaceNum);
            sqLiteCommand.Parameters.AddWithValue("@QueueIndex", queueIndex);
            sqLiteCommand.Parameters.AddWithValue("@Lane", Lane);
            sqLiteCommand.ExecuteNonQuery();
        }
    }
}