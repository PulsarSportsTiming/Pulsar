using System;
using System.Collections.Generic;
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

        // Helper to open a connection and enable WAL mode for better concurrency
        private async Task<SqliteConnection> OpenConnectionAsync()
        {
            var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // Enable WAL journal mode to allow concurrent readers with a writer
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA journal_mode=WAL;";
                await cmd.ExecuteNonQueryAsync();

                // Use NORMAL synchronous for a balance between durability and performance
                cmd.CommandText = "PRAGMA synchronous=NORMAL;";
                await cmd.ExecuteNonQueryAsync();
            }

            return connection;
        }

        public async Task<IndexList> GetIndexListAsync(RaceEntry entry, CategQueueItem category)
        {
            var indexList = new IndexList();
            
            const string sqlText = """
                                   SELECT
                                   	racers.race_number,
                                   	racers.event_index,
                                   	racers.personal_index,
                                   	class_indexes.min_index AS class_index,
                                   	def_class.min_index AS categ_index
                                   FROM
                                   racers
                                   LEFT OUTER JOIN class_indexes ON
                                       racers.category = class_indexes.category_id AND
                                       racers.class = class_indexes.class_id AND
                                       class_indexes.finish = @Finish
                                   JOIN categories ON racers.category = categories.id
                                   LEFT OUTER JOIN class_indexes AS def_class ON
                                       categories.id = def_class.category_id AND
                                       categories.def_class = def_class.class_id AND
                                       def_class.finish = @Finish
                                   WHERE
                                   	racers.category = @Category AND
                                   	racers.race_number = @RaceNum
                                   """;
            
            await using var connection = await OpenConnectionAsync();
            
            await using var command = connection.CreateCommand();
            command.CommandText = sqlText;
            command.Parameters.AddWithValue("@Category", category.Category);
            command.Parameters.AddWithValue("@RaceNum", entry.RaceNumber);
            command.Parameters.AddWithValue("@Finish", category.Finish);
            
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                indexList.EventIndex = reader["event_index"].ToString();
                indexList.PersonalIndex = reader["personal_index"].ToString();
                indexList.ClassIndex = reader["class_index"].ToString();
                indexList.CategoryIndex = reader["categ_index"].ToString();
            }
            
            return indexList;
        }

        public async Task<RaceEntry> GetRacerDetailsAsync(RaceEntry entry, CategQueueItem category)
        {
            const string sqlText = """
                                   SELECT
                                   	classes.name AS ClassName,
                                   	racers.name AS RacerName,
                                   	racers.model AS Vehicle
                                   FROM racers
                                   LEFT OUTER JOIN classes ON racers.category = classes.category AND racers.class = classes.id
                                   WHERE
                                   	racers.category = @Category AND
                                   	racers.race_number = @RaceNum
                                   """;
            
            await using var connection = await OpenConnectionAsync();
            
            await using var command = connection.CreateCommand();
            command.CommandText = sqlText;
            command.Parameters.AddWithValue("@Category", category.Category);
            command.Parameters.AddWithValue("@RaceNum", entry.RaceNumber);
            
            await using var reader = await command.ExecuteReaderAsync();
            
            while (await reader.ReadAsync())
            {
                entry.Class = reader["ClassName"].ToString();
                entry.Name = reader["RacerName"].ToString();
                entry.Vehicle = reader["Vehicle"].ToString();
            }
            
            return entry;
        }

        public async Task<List<Category>> GetCategoryListAsync()
        {
            var categories = new List<Category>();

            const string sqlText = """
                                   SELECT
                                       categories.id,
                                       categories.disp_order,
                                       categories.name,
                                       categories.finish,
                                       categories.run_timeout,
                                       categories.bump_et,
                                       categories.tree,
                                       tree_types.name AS tree_desc,
                                       categories.elim_mode,
                                       categories.split_tree,
                                       categories.stagger_tree,
                                       categories.start_mode,
                                       categories.stage_freeze,
                                       categories.ds_foul,
                                       categories.sb_elim_speed,
                                       categories.sb_units,
                                       categories.worst_foul,
                                       categories.foul_empty,
                                       categories.stage_settle,
                                       categories.as_stagetostart,
                                       categories.as_variance,
                                       categories.as_timeout,
                                       categories.def_class,
                                       classes.name AS def_class_name,
                                       categories.last_mode,
                                       categories.last_round,
                                       categories.delay_min,
                                       categories.delay_max,
                                        categories.fixed_tree,
                                        categories.fixed_track,
                                        categories.sb_timeout
                                   FROM categories
                                            JOIN tree_types ON categories.tree = tree_types.id
                                            JOIN finish_lines ON categories.finish = finish_lines.id
                                            LEFT OUTER JOIN classes ON categories.def_class = classes.id
                                   ORDER BY categories.disp_order
                                   """;
            await using var connection = await OpenConnectionAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = sqlText;

            await using var reader = await command.ExecuteReaderAsync();

            // local helpers to safely read nullable columns
            int ReadInt(string col)
            {
                var idx = reader.GetOrdinal(col);
                return reader.IsDBNull(idx) ? 0 : reader.GetInt32(idx);
            }

            bool ReadBool(string col)
            {
                var idx = reader.GetOrdinal(col);
                if (reader.IsDBNull(idx)) return false;
                // Try boolean directly, fall back to integer check for 0/1 storage
                try
                {
                    return reader.GetBoolean(idx);
                }
                catch
                {
                    return reader.GetInt32(idx) != 0;
                }
            }

            string? ReadString(string col) => reader[col] as string;

            while (await reader.ReadAsync())
            {
                var category = new Category
                {
                    Id = ReadInt("id"),
                    Order = ReadInt("disp_order"),
                    Name = ReadString("name"),
                    Finish = ReadInt("finish"),
                    RunTimeout = ReadInt("run_timeout"),
                    BumpEt = ReadString("bump_et"),
                    TreeType = ReadInt("tree"),
                };
                // Read elim_mode as int and validate before casting
                var elimInt = ReadInt("elim_mode");
                if (System.Enum.IsDefined(typeof(ElimMode), elimInt))
                    category.ElimMode = (ElimMode)elimInt;
                else
                    category.ElimMode = ElimMode.NoBreakout;
                // Re-open initializer for remaining properties
                category.SplitTreeAllowed = ReadBool("split_tree");
                category.StaggeredStartsAllowed = ReadBool("stagger_tree");
                category.StartMode = ReadInt("start_mode");
                category.StageFreeze = ReadBool("stage_freeze");
                category.DeepStageFoul = ReadBool("ds_foul");
                category.SbElimSpeed = ReadBool("sb_elim_speed");
                category.SbCycleUnits = ReadBool("sb_units");
                category.WorstFoul = ReadBool("worst_foul");
                category.FoulInEmpty = ReadBool("foul_empty");
                category.StageSettle = ReadInt("stage_settle");
                category.AutoStartStageToStart = ReadInt("as_stagetostart");
                category.AutoStartVariance = ReadInt("as_variance");
                category.AutoStartTimeout = ReadInt("as_timeout");
                category.DefaultClass = ReadInt("def_class");
                category.LastMode = ReadInt("last_mode");
                category.LastRound = ReadInt("last_round");
                category.DelayMin = ReadInt("delay_min");
                category.DelayMax = ReadInt("delay_max");
                category.FixedTree = ReadBool("fixed_tree");
                category.FixedTrack = ReadBool("fixed_track");
                category.SbTimeout = ReadInt("sb_timeout");
                
                 categories.Add(category);
             }

             return categories;
         }

        public async Task<List<TreeType>> GetTreeTypesAsync()
        {
            var treeTypes = new List<TreeType>();

            const string sqlText = """
                                   SELECT
                                       tree_types.id,
                                       tree_types.name,
                                       tree_types.countdown_speed,
                                       tree_types.countdown_type
                                   FROM tree_types
                                   ORDER BY tree_types.id;
                                   """;
            await using var connection = await OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sqlText;
            await using var reader = await command.ExecuteReaderAsync();

            var idIdx = reader.GetOrdinal("id");
            var nameIdx = reader.GetOrdinal("name");
            var speedIdx = reader.GetOrdinal("countdown_speed");
            var typeIdx = reader.GetOrdinal("countdown_type");

            while (await reader.ReadAsync())
            {
                var treeType = new TreeType
                {
                    Id = reader.GetInt32(idIdx),
                    Name = reader.GetString(nameIdx),
                    CountdownSpeed = reader.GetInt32(speedIdx),
                    CountdownType = reader.GetInt32(typeIdx)
                };
                treeTypes.Add(treeType);
            }

            return treeTypes;
        }

        public async Task<List<FinishLine>> GetFinishLinesAsync()
        {
            var finishLines = new List<FinishLine>();

            const string sqlText = """
                                   SELECT
                                       finish_lines.id,
                                       timing_points.distance
                                   FROM
                                       finish_lines
                                   JOIN timing_points ON finish_lines.timing_point = timing_points.id
                                   ORDER BY timing_points.distance ASC
                                   """;
            await using var connection = await OpenConnectionAsync();
            
            await using var command = connection.CreateCommand();
            command.CommandText = sqlText;
            
            await using var reader = await command.ExecuteReaderAsync();
            
            var idIdx = reader.GetOrdinal("id");
            var distanceIdx = reader.GetOrdinal("distance");

            while (await reader.ReadAsync())
            {
                var finishLine = new FinishLine()
                {
                    Id = reader.GetInt32(idIdx),
                    Distance = reader.GetInt32(distanceIdx)
                };
                finishLines.Add(finishLine);
            }

            return finishLines;
        }

        // ============================================================
        // Run persistence (writes)
        //
        // Every method below swallows its own exceptions and logs to
        // Console.Error - a failure to persist run data must never throw
        // into a UI-thread event handler or interrupt the live run.
        // ============================================================

        public async Task<long?> ResolveRacerIdAsync(int categoryId, string? raceNumber)
        {
            if (string.IsNullOrWhiteSpace(raceNumber)) return null;

            try
            {
                const string sqlText = """
                                       SELECT racers.id
                                       FROM racers
                                       WHERE racers.category = @Category AND racers.race_number = @RaceNum
                                       """;

                await using var connection = await OpenConnectionAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = sqlText;
                command.Parameters.AddWithValue("@Category", categoryId);
                command.Parameters.AddWithValue("@RaceNum", raceNumber);

                var result = await command.ExecuteScalarAsync();
                if (result == null || result is DBNull) return null;
                return Convert.ToInt64(result);
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine("ResolveRacerIdAsync error: " + ex.Message);
                return null;
            }
        }

        public async Task<long?> InsertRunPairAsync(RunPairInsert record)
        {
            try
            {
                const string sqlText = """
                                       INSERT INTO run_pair (
                                           run_timestamp, category_id, race_mode, round_number, start_mode,
                                           finish_distance, run_timeout, stage_freeze, ds_foul, worst_foul,
                                           tree_delay, as_settle, as_stagetostart, as_variance, as_timeout
                                       ) VALUES (
                                           @RunTimestamp, @CategoryId, @RaceMode, @RoundNumber, @StartMode,
                                           @FinishDistance, @RunTimeout, @StageFreeze, @DsFoul, @WorstFoul,
                                           @TreeDelay, @AsSettle, @AsStageToStart, @AsVariance, @AsTimeout
                                       );
                                       SELECT last_insert_rowid();
                                       """;

                await using var connection = await OpenConnectionAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = sqlText;
                command.Parameters.AddWithValue("@RunTimestamp", record.RunTimestamp);
                command.Parameters.AddWithValue("@CategoryId", record.CategoryId);
                command.Parameters.AddWithValue("@RaceMode", record.RaceMode);
                command.Parameters.AddWithValue("@RoundNumber", (object?)record.RoundNumber ?? DBNull.Value);
                command.Parameters.AddWithValue("@StartMode", record.StartMode);
                command.Parameters.AddWithValue("@FinishDistance", record.FinishDistance);
                command.Parameters.AddWithValue("@RunTimeout", (object?)record.RunTimeout ?? DBNull.Value);
                command.Parameters.AddWithValue("@StageFreeze", record.StageFreeze);
                command.Parameters.AddWithValue("@DsFoul", record.DsFoul);
                command.Parameters.AddWithValue("@WorstFoul", record.WorstFoul);
                command.Parameters.AddWithValue("@TreeDelay", (object?)record.TreeDelay ?? DBNull.Value);
                command.Parameters.AddWithValue("@AsSettle", (object?)record.AsSettle ?? DBNull.Value);
                command.Parameters.AddWithValue("@AsStageToStart", (object?)record.AsStageToStart ?? DBNull.Value);
                command.Parameters.AddWithValue("@AsVariance", (object?)record.AsVariance ?? DBNull.Value);
                command.Parameters.AddWithValue("@AsTimeout", (object?)record.AsTimeout ?? DBNull.Value);

                var result = await command.ExecuteScalarAsync();
                if (result == null || result is DBNull) return null;
                return Convert.ToInt64(result);
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine("InsertRunPairAsync error: " + ex.Message);
                return null;
            }
        }

        public async Task<long?> InsertRunIndvAsync(RunIndvInsert record)
        {
            try
            {
                const string sqlText = """
                                       INSERT INTO run_indv (
                                           pair_id, racer_id, lane, race_number, index_time, tree_type
                                       ) VALUES (
                                           @PairId, @RacerId, @Lane, @RaceNumber, @IndexTime, @TreeType
                                       );
                                       SELECT last_insert_rowid();
                                       """;

                await using var connection = await OpenConnectionAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = sqlText;
                command.Parameters.AddWithValue("@PairId", record.PairId);
                command.Parameters.AddWithValue("@RacerId", (object?)record.RacerId ?? DBNull.Value);
                command.Parameters.AddWithValue("@Lane", record.Lane);
                command.Parameters.AddWithValue("@RaceNumber", (object?)record.RaceNumber ?? DBNull.Value);
                command.Parameters.AddWithValue("@IndexTime", (object?)record.IndexTime ?? DBNull.Value);
                command.Parameters.AddWithValue("@TreeType", record.TreeType);

                var result = await command.ExecuteScalarAsync();
                if (result == null || result is DBNull) return null;
                return Convert.ToInt64(result);
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine("InsertRunIndvAsync error: " + ex.Message);
                return null;
            }
        }

        public async Task UpdateRunIndvReactionTimeAsync(long runIndvId, string? reactionTime)
        {
            try
            {
                const string sqlText = """
                                       UPDATE run_indv SET reaction_time = @ReactionTime WHERE run_indv_id = @Id
                                       """;

                await using var connection = await OpenConnectionAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = sqlText;
                command.Parameters.AddWithValue("@ReactionTime", (object?)reactionTime ?? DBNull.Value);
                command.Parameters.AddWithValue("@Id", runIndvId);

                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine("UpdateRunIndvReactionTimeAsync error: " + ex.Message);
            }
        }

        public async Task UpdateRunPairResultAsync(long pairId, int? firstLane, int? winnerLane)
        {
            try
            {
                const string sqlText = """
                                       UPDATE run_pair SET first_lane = @FirstLane, winner_lane = @WinnerLane WHERE pair_id = @Id
                                       """;

                await using var connection = await OpenConnectionAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = sqlText;
                command.Parameters.AddWithValue("@FirstLane", (object?)firstLane ?? DBNull.Value);
                command.Parameters.AddWithValue("@WinnerLane", (object?)winnerLane ?? DBNull.Value);
                command.Parameters.AddWithValue("@Id", pairId);

                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine("UpdateRunPairResultAsync error: " + ex.Message);
            }
        }

        public async Task UpsertIncrementalEtAsync(long runIndvId, int distanceMm, string et)
        {
            try
            {
                const string sqlText = """
                                       INSERT INTO incremental_et (run_indv_id, distance, et)
                                       VALUES (@RunIndvId, @Distance, @Et)
                                       ON CONFLICT(run_indv_id, distance) DO UPDATE SET et = excluded.et
                                       """;

                await using var connection = await OpenConnectionAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = sqlText;
                command.Parameters.AddWithValue("@RunIndvId", runIndvId);
                command.Parameters.AddWithValue("@Distance", distanceMm);
                command.Parameters.AddWithValue("@Et", et);

                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine("UpsertIncrementalEtAsync error: " + ex.Message);
            }
        }

        public async Task UpsertIncrementalSpeedAsync(long runIndvId, int distanceMm, string speed)
        {
            try
            {
                const string sqlText = """
                                       INSERT INTO incremental_speed (run_indv_id, distance, speed)
                                       VALUES (@RunIndvId, @Distance, @Speed)
                                       ON CONFLICT(run_indv_id, distance) DO UPDATE SET speed = excluded.speed
                                       """;

                await using var connection = await OpenConnectionAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = sqlText;
                command.Parameters.AddWithValue("@RunIndvId", runIndvId);
                command.Parameters.AddWithValue("@Distance", distanceMm);
                command.Parameters.AddWithValue("@Speed", speed);

                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine("UpsertIncrementalSpeedAsync error: " + ex.Message);
            }
        }

        public async Task InsertRunRemarkAsync(long runIndvId, int remark)
        {
            try
            {
                const string sqlText = """
                                       INSERT OR IGNORE INTO run_remark (run_indv_id, remark) VALUES (@RunIndvId, @Remark)
                                       """;

                await using var connection = await OpenConnectionAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = sqlText;
                command.Parameters.AddWithValue("@RunIndvId", runIndvId);
                command.Parameters.AddWithValue("@Remark", remark);

                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine("InsertRunRemarkAsync error: " + ex.Message);
            }
        }

        public async Task DeleteRunRemarkAsync(long runIndvId, int remark)
        {
            try
            {
                const string sqlText = """
                                       DELETE FROM run_remark WHERE run_indv_id = @RunIndvId AND remark = @Remark
                                       """;

                await using var connection = await OpenConnectionAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = sqlText;
                command.Parameters.AddWithValue("@RunIndvId", runIndvId);
                command.Parameters.AddWithValue("@Remark", remark);

                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine("DeleteRunRemarkAsync error: " + ex.Message);
            }
        }
    }
}