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
                                       finish_lines.description AS finish_desc,
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
                    ElimMode = ReadInt("elim_mode"),
                    SplitTreeAllowed = ReadBool("split_tree"),
                    StaggeredStartsAllowed = ReadBool("stagger_tree"),
                    StartMode = ReadInt("start_mode"),
                    StageFreeze = ReadBool("stage_freeze"),
                    DeepStageFoul = ReadBool("ds_foul"),
                    SbElimSpeed = ReadBool("sb_elim_speed"),
                    SbCycleUnits = ReadBool("sb_units"),
                    WorstFoul = ReadBool("worst_foul"),
                    FoulInEmpty = ReadBool("foul_empty"),
                    StageSettle = ReadInt("stage_settle"),
                    AutoStartStageToStart = ReadInt("as_stagetostart"),
                    AutoStartVariance = ReadInt("as_variance"),
                    AutoStartTimeout = ReadInt("as_timeout"),
                    DefaultClass = ReadInt("def_class"),
                    LastMode = ReadInt("last_mode"),
                    LastRound = ReadInt("last_round"),
                    DelayMin = ReadInt("delay_min"),
                    DelayMax = ReadInt("delay_max"),
                    FixedTree = ReadBool("fixed_tree"),
                    FixedTrack = ReadBool("fixed_track"),
                    SbTimeout = ReadInt("sb_timeout")
                };
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
                                       finish_lines.timing_point,
                                       finish_lines.description
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
            var timingPointIdx = reader.GetOrdinal("timing_point");
            var descriptionIdx = reader.GetOrdinal("description");

            while (await reader.ReadAsync())
            {
                var finishLine = new FinishLine()
                {
                    Id = reader.GetInt32(idIdx),
                    TimingPointId = reader.GetInt32(timingPointIdx),
                    Description = reader.GetString(descriptionIdx)
                };
                finishLines.Add(finishLine);
            }

            return finishLines;
        }
    }
}