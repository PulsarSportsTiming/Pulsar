using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using PulsarUI.Interfaces;
using PulsarUI.Models;
using PulsarUI.Services;

namespace PulsarUI.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly IDatabaseService _databaseService;

        [GeneratedRegex(@"^(\d{0,2}(\.\d{0,2})?|\.?\d{0,2})?$")]
    
        private static partial Regex IndexPatternRegex();

        // Category Properties
        [ObservableProperty] private Category? _enterPairCateg;
        [ObservableProperty] private string? _enterPairSelectedCategText;
        [ObservableProperty] private int _enterPairSelectedCategIndex;
        [ObservableProperty] private List<string>? _categComboBoxItems;
        [ObservableProperty] private List<Category?>? _categories;
        
        // Race Mode Properties
        [ObservableProperty] private List<string> _enterPairModeComboBoxItems;
        [ObservableProperty] private int _enterPairSelectedModeIndex;
        [ObservableProperty] private string? _enterPairSelectedModeName;
        
        // Round Number Properties
        [ObservableProperty] private int _enterPairRound;
        
        // Tree Type Properties
        [ObservableProperty] private List<string> _enterPairTreeComboBoxItems;
        [ObservableProperty] private int _enterPairLeftSelectedTreeIndex;
        [ObservableProperty] private string? _enterPairLeftSelectedTreeName;
        [ObservableProperty] private int _enterPairRightSelectedTreeIndex;
        [ObservableProperty] private string? _enterPairRightSelectedTreeName;
        
        // Finish Line Properties
        [ObservableProperty] private List<string> _enterPairFinishComboBoxItems;
        [ObservableProperty] private int _enterPairFinishIndex;
        [ObservableProperty] private string _enterPairFinishName;
        [ObservableProperty] private int _enterPairSelectedFinishIndex;
        [ObservableProperty] private string? _enterPairSelectedFinishName;
        
        // Race Number Properties
        [ObservableProperty] private string? _leftEnterPairRaceNum;
        [ObservableProperty] private string? _rightEnterPairRaceNum;
        
        // Index/Dial-in Properties
        [ObservableProperty] private string? _leftEnterPairIndex;
        [ObservableProperty] private string? _rightEnterPairIndex;
        
        // Racer Info Properties
        [ObservableProperty] private string? _leftEnterPairClass;
        [ObservableProperty] private string? _rightEnterPairClass;
        [ObservableProperty] private string? _leftEnterPairName;
        [ObservableProperty] private string? _rightEnterPairName;
        [ObservableProperty] private string? _leftEnterPairVehicle;
        [ObservableProperty] private string? _rightEnterPairVehicle;

        public MainWindowViewModel()
        {
            _databaseService = new DatabaseService("Data Source=/home/david/PulsarDB.db");
            _ = LoadFinishLinesAsync();
            _ = LoadTreeTypesAsync();
            _ = LoadCategoriesAsync();
            EnterPairSelectedCategIndex = 0;
            EnterPairModeComboBoxItems =
            [
                "Practice",
                "Demonstration",
                "Qualifying",
                "Eliminations",
                "Q+E Combo"
            ];
            EnterPairSelectedModeIndex = 0;
            EnterPairRound = 1;
        }
        partial void OnEnterPairSelectedCategTextChanged(string? value)
        {
            if (Categories != null)
                EnterPairCateg =
                    Categories.FirstOrDefault(c => value != null && c != null && c.CategoryIndex == int.Parse(value[..2]));

            if (CategComboBoxItems != null && EnterPairCateg is { CategoryTreeType: not null })
                EnterPairLeftSelectedTreeIndex = EnterPairRightSelectedTreeIndex =
                    EnterPairTreeComboBoxItems.IndexOf(EnterPairCateg.CategoryTreeType);

            if (EnterPairCateg != null && EnterPairCateg.CategoryFinish != null)
                EnterPairSelectedFinishIndex = EnterPairFinishComboBoxItems.IndexOf(EnterPairCateg.CategoryFinish);
        }

        private async Task LoadCategoriesAsync()
        {
            Categories = await _databaseService.GetCategoryListAsync();
            
            var formattedItems = Categories.Select(c => $"{(c.CategoryIndex.ToString("D2"))} - {c.CategoryName}").ToList();
            
            CategComboBoxItems = formattedItems;
        }

        private async Task LoadTreeTypesAsync()
        {
            var treeTypes = await _databaseService.GetTreeTypesAsync();
            
            EnterPairTreeComboBoxItems = treeTypes;
        }

        private async Task LoadFinishLinesAsync()
        {
            var finishLines = await _databaseService.GetFinishLinesAsync();
            
            EnterPairFinishComboBoxItems = finishLines;
        }
       
        public async Task EnterPairRaceNumProcess(int lane)
        {
            var raceEntry = new RaceEntry
            {
                QueueIndex = 0,
                Lane = lane,
                Finish = EnterPairFinishIndex
            };
            switch (lane)
            {
                case 0:
                    raceEntry.RaceNumber = LeftEnterPairRaceNum;
                    raceEntry.Tree = EnterPairLeftSelectedTreeIndex;
                    break;
                case 1:
                    raceEntry.RaceNumber = RightEnterPairRaceNum;
                    raceEntry.Tree = EnterPairRightSelectedTreeIndex;
                    break;
            }
            raceEntry.HandicapIndex = await GetVehIndexAsync(raceEntry.Lane);
            await _databaseService.WriteQueueAsync(raceEntry);
            await GetEntryDetailsAsync(raceEntry.Lane);
        }

        private async Task<string> GetVehIndexAsync(int lane)
        {
            var entry = new RaceEntry
            {
                Category = EnterPairCateg.CategoryIndex,
                Finish = EnterPairFinishIndex,
                RaceNumber = lane switch
                {
                    0 => LeftEnterPairRaceNum,
                    1 => RightEnterPairRaceNum,
                    _ => null
                }
            };

            var indexList = await _databaseService.GetIndexListAsync(entry);

            var indexes = new[]
            {
                indexList.EventIndex,
                indexList.PersonalIndex,
                indexList.ClassIndex,
                indexList.CategoryIndex
            };

            switch (lane)
            {
                case 0:
                    LeftEnterPairIndex = indexes.FirstOrDefault(index => index != "") ?? "00.00";
                    break;
                case 1:
                    RightEnterPairIndex = indexes.FirstOrDefault(index => index != "") ?? "00.00";
                    break;
            }

            return indexes.FirstOrDefault(index => index != "") ?? "00.00";
        }
        
        private async Task GetEntryDetailsAsync(int lane)
        {
            var entry = new RaceEntry
            {
                Category = EnterPairCateg.CategoryIndex
            };

            entry.RaceNumber = lane switch
            {
                0 => LeftEnterPairRaceNum,
                1 => RightEnterPairRaceNum,
                _ => entry.RaceNumber
            };
            
            var racerDetails = await _databaseService.GetRacerDetailsAsync(entry);

            switch (lane)
            {
                case 0:
                    LeftEnterPairClass = racerDetails.Class;
                    LeftEnterPairName = racerDetails.Name;
                    LeftEnterPairVehicle = racerDetails.Vehicle;
                    break;
                case 1:
                    RightEnterPairClass = racerDetails.Class;
                    RightEnterPairName = racerDetails.Name;
                    RightEnterPairVehicle = racerDetails.Vehicle;
                    break;
            }
        }

        public async Task EnterPairIndexProcess(int lane)
        {
            var raceEntry = new RaceEntry
            {
                QueueIndex = 0,
                Lane = lane,
                Finish = EnterPairFinishIndex
            };
            switch (lane)
            {
                case 0:
                    raceEntry.RaceNumber = LeftEnterPairRaceNum;
                    raceEntry.HandicapIndex = LeftEnterPairIndex;
                    raceEntry.Tree = EnterPairLeftSelectedTreeIndex;
                    break;
                case 1:
                    raceEntry.RaceNumber = RightEnterPairRaceNum;
                    raceEntry.HandicapIndex = RightEnterPairIndex;
                    raceEntry.Tree = EnterPairRightSelectedTreeIndex;
                    break;
            }
            await _databaseService.WriteQueueAsync(raceEntry);
        }
    }
    
}
