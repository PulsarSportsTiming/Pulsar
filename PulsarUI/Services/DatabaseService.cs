using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using PulsarUI.Interfaces;
using PulsarUI.Models;
using Tmds.DBus.Protocol;

namespace PulsarUI.Services
{
    public class DatabaseService(string connectionString) : IDatabaseService
    {
        public async Task WriteQueueCategoriesAsync(CategQueueItem category)
        {
            const string sqlText = @"
                INSERT OR REPLACE INTO run_queue (queue_index, category, mode, round, finish)
                VALUES(
                       @QueueIndex,
                       @Category,
                       @Mode,
                       @Round,
                       @Finish
                );";
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = sqlText;
            command.Parameters.AddWithValue("@QueueIndex", category.QueueIndex);
            command.Parameters.AddWithValue("@Category", category.Category);
            command.Parameters.AddWithValue("@Mode", category.Mode);
            command.Parameters.AddWithValue("@Round", category.Round);
            command.Parameters.AddWithValue("@Finish", category.Finish);
            
            await command.ExecuteNonQueryAsync();
        }
        public async Task WriteQueueRacersAsync(RaceEntry entry)
        {
            const string sqlText = @"
                INSERT OR REPLACE INTO run_queue_racers (queue_index, lane, race_number, handicap_index, tree)
                VALUES (
                    @QueueIndex, 
                    @Lane, 
                    @RaceNum, 
                    @HandicapIndex,
                    @Tree
                );";

            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = sqlText;
            command.Parameters.AddWithValue("@QueueIndex", entry.QueueIndex);
            command.Parameters.AddWithValue("@Lane", entry.Lane);
            command.Parameters.AddWithValue("@RaceNum", entry.RaceNumber);
            command.Parameters.AddWithValue("@HandicapIndex", entry.HandicapIndex);
            command.Parameters.AddWithValue("@Tree", entry.Tree);

            await command.ExecuteNonQueryAsync();
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
            
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            
            await using var command = connection.CreateCommand();
            command.CommandText = sqlText;
            command.Parameters.AddWithValue("@Category", category.Category);
            command.Parameters.AddWithValue("@RaceNum", entry.RaceNumber);
            command.Parameters.AddWithValue("Finish", category.Finish);
            
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
            
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            
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
            List<Category> categories = [];
            
            const string sqlText = """
                                   SELECT
                                    categories.id,
                                   	categories.disp_order,
                                   	categories.name,
                                   	tree_types.name AS tree,
                                   	finish_lines.description,
                                   	last_mode,
                                   	last_round
                                   FROM categories
                                   JOIN tree_types ON categories.tree = tree_types.id
                                   JOIN finish_lines ON categories.finish = finish_lines.id
                                   ORDER BY categories.disp_order
                                   """;
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            
            await using var command = connection.CreateCommand();
            command.CommandText = sqlText;
            
            await using var reader = await command.ExecuteReaderAsync();
            
            while (await reader.ReadAsync())
            {
                var category = new Category
                {
                    CategoryId = reader.GetInt32(reader.GetOrdinal("id")),
                    CategoryOrder = reader.GetInt32(reader.GetOrdinal("disp_order")),
                    CategoryName = reader["name"].ToString(),
                    CategoryTreeType = reader["tree"].ToString(),
                    CategoryFinish = reader["description"].ToString(),
                    LastMode = reader.GetInt32(reader.GetOrdinal("last_mode")),
                    LastRound = reader.GetInt32(reader.GetOrdinal("last_round"))
                };
                categories.Add(category);
            }
            
            return categories;
        }

        public async Task<List<string?>> GetTreeTypesAsync()
        {
            List<string?> treeTypes = [];
            
            const string sqlText =
                "SELECT tree_types.name FROM tree_types ORDER BY tree_types.id;";
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            
            await using var command = connection.CreateCommand();
            command.CommandText = sqlText;
            
            await using var reader = await command.ExecuteReaderAsync();
            
            while (await reader.ReadAsync())
            {
                treeTypes.Add(reader["name"].ToString());
            }

            return treeTypes;
        }

        public async Task<List<string?>> GetFinishLinesAsync()
        {
            List<string?> finishLines = [];
            
            const string sqlText =
                "SELECT finish_lines.description FROM finish_lines ORDER BY id;";
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            
            await using var command = connection.CreateCommand();
            command.CommandText = sqlText;
            
            await using var reader = await command.ExecuteReaderAsync();
            
            while (await reader.ReadAsync())
            {
                finishLines.Add(reader["description"].ToString());
            }

            return finishLines;
        }
    }
}