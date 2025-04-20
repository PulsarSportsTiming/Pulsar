using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.Entity.Infrastructure;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using PulsarUI.Models;
using PulsarUI.Services;
using System.Timers;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Timer = System.Timers.Timer;

namespace PulsarUI.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private DispatcherTimer _clockTimer;
        [ObservableProperty] private string _currentTime;

        private readonly DatabaseService _databaseService;

        [GeneratedRegex(@"^(\d{0,2}(\.\d{0,2})?|\.?\d{0,2})?$")]

        private static partial Regex IndexPatternRegex();

        // Category Properties
        [ObservableProperty] private Category? _enterPairCateg;
        [ObservableProperty] private List<string>? _categComboBoxItems;
        [ObservableProperty] private string? _enterPairSelectedCategComboText;
        [ObservableProperty] private List<Category?>? _categories;
        [ObservableProperty] private CategQueueItem _enterPairQueueCategory;
        [ObservableProperty] private string? _enterPairCategText;
        [ObservableProperty] private CategQueueItem _queuePairQueueCategory;
        [ObservableProperty] private CategQueueItem _engagedPairQueueCategory;
        [ObservableProperty] private string? _queuePairCategText;

        // Race Mode Properties
        [ObservableProperty] private List<string> _modeList;
        [ObservableProperty] private string? _queuePairModeText;

        // Tree Type Properties
        [ObservableProperty] private List<string> _treeList;
        [ObservableProperty] private string? _queuePairLeftTreeText;
        [ObservableProperty] private string? _queuePairRightTreeText;

        // Finish Line Properties
        [ObservableProperty] private List<string?> _finishList;
        [ObservableProperty] private string? _queuePairFinishText;

        // Racer Info Properties
        [ObservableProperty] private RaceEntry? _leftEnterPairRacerEntry;
        [ObservableProperty] private RaceEntry? _rightEnterPairRacerEntry;
        [ObservableProperty] private RaceEntry? _leftQueuePairRacerEntry;
        [ObservableProperty] private RaceEntry? _rightQueuePairRacerEntry;
        [ObservableProperty] private RaceEntry? _leftEngagePairRacerEntry;
        [ObservableProperty] private RaceEntry? _rightEngagePairRacerEntry;

        public MainWindowViewModel()
        {
            // Date/Time Display
            var now = DateTime.Now;
            var msToNextSecond = 1000 - now.Millisecond;

            Task.Delay(msToNextSecond).ContinueWith(_ => { StartClock(); });

            _databaseService = new DatabaseService("Data Source=/home/david/PulsarDB.db");
            _ = LoadFinishLinesAsync();
            _ = LoadTreeTypesAsync();
            _ = LoadCategoriesAsync();

            EnterPairQueueCategory = new CategQueueItem
            {
                QueueIndex = 0,
                Category = 1,
                Finish = 0,
                Mode = 0,
                Round = 1,
                LastRound = 0
            };

            EnterPairQueueCategory.PropertyChanged += EnterPairQueueCategoryHandler;
            EnterPairSelectedCategComboText = CategComboBoxItems?.FirstOrDefault();

            LeftEnterPairRacerEntry = new RaceEntry
            {
                Category = 0,
                Class = "",
                HandicapIndex = "00.00",
                Lane = 0,
                Name = "",
                QueueIndex = 0,
                RaceNumber = "",
                Tree = 0,
                Vehicle = ""
            };
            LeftEnterPairRacerEntry.PropertyChanged += EnterPairRacerEntryHandler;

            RightEnterPairRacerEntry = new RaceEntry
            {
                Category = 0,
                Class = "",
                HandicapIndex = "00.00",
                Lane = 1,
                Name = "",
                QueueIndex = 0,
                RaceNumber = "",
                Tree = 0,
                Vehicle = ""
            };
            RightEnterPairRacerEntry.PropertyChanged += EnterPairRacerEntryHandler;

            QueuePairQueueCategory = new CategQueueItem
            {
                QueueIndex = 1,
                Category = 1,
                Finish = 0,
                Mode = 0,
                Round = 1,
                LastRound = 0
            };
            
            LeftQueuePairRacerEntry = new RaceEntry
            {
                Category = 0,
                Class = "",
                HandicapIndex = "00.00",
                Lane = 0,
                Name = "",
                QueueIndex = 1,
                RaceNumber = "",
                Tree = 0,
                Vehicle = ""
            };
            
            RightQueuePairRacerEntry = new RaceEntry
            {
                Category = 0,
                Class = "",
                HandicapIndex = "00.00",
                Lane = 1,
                Name = "",
                QueueIndex = 1,
                RaceNumber = "",
                Tree = 0,
                Vehicle = ""
            };

            ModeList =
            [
                "Practice",
                "Qualifying",
                "Eliminations",
                "Q+E Combo"
            ];
        }

        private void StartClock()
        {
            _clockTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _clockTimer.Tick += (_, _) => { CurrentTime = DateTime.Now.ToString(CultureInfo.CurrentCulture); };
            _clockTimer.Start();
        }

        private void EnterPairQueueCategoryHandler(object? sender, PropertyChangedEventArgs e)
        {
            EnterPairQueueCategory.PropertyChanged -= EnterPairQueueCategoryHandler;
            switch (e.PropertyName)
            {
                case nameof(CategQueueItem.Category):
                {
                    var category = Categories?.Find(c => c?.CategoryId == EnterPairQueueCategory.Category);

                    if (category == null) return;

                    if (category.CategoryFinish != null)
                    {
                        EnterPairQueueCategory.Finish = FinishList.IndexOf(category.CategoryFinish);
                    }

                    if (category.CategoryTreeType != null && RightEnterPairRacerEntry != null &&
                        LeftEnterPairRacerEntry != null)
                    {
                        LeftEnterPairRacerEntry.Tree =
                            RightEnterPairRacerEntry.Tree = TreeList.IndexOf(category.CategoryTreeType);
                    }

                    EnterPairQueueCategory.Mode = category.LastMode;
                    EnterPairQueueCategory.LastRound = category.LastRound;
                    EnterPairQueueCategory.Round = category.LastRound + (category.LastRound == 0 ? 1 : 0);

                    EnterPairCategText = category.CategoryName;
                    EnterPairSelectedCategComboText =
                        category.CategoryOrder.ToString("D2") + " - " + category.CategoryName;
                    break;
                }
            }

            EnterPairQueueCategory.PropertyChanged += EnterPairQueueCategoryHandler;
            _ = _databaseService.WriteQueueCategoriesAsync(EnterPairQueueCategory);
        }

        private void EnterPairRacerEntryHandler(object? sender, PropertyChangedEventArgs e)
        {
            _ = EnterPairRacerEntryHandlerAsync(sender, e);
        }

        private async Task EnterPairRacerEntryHandlerAsync(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not RaceEntry raceEntry) return;
            switch (raceEntry.Lane)
            {
                case 0:
                    if (LeftEnterPairRacerEntry == null) return;
                    LeftEnterPairRacerEntry.PropertyChanged -= EnterPairRacerEntryHandler;
                    break;
                case 1:
                    if (RightEnterPairRacerEntry == null) return;
                    RightEnterPairRacerEntry.PropertyChanged -= EnterPairRacerEntryHandler;
                    break;
            }
            
            if (e.PropertyName == nameof(RaceEntry.RaceNumber))
            {
                var indexList = await _databaseService.GetIndexListAsync(raceEntry, EnterPairQueueCategory);
                var indexes = new[]
                {
                    indexList.EventIndex,
                    indexList.PersonalIndex,
                    indexList.ClassIndex,
                    indexList.CategoryIndex
                };
                raceEntry.HandicapIndex = indexes.FirstOrDefault(index => index != "") ?? "00.00";
                raceEntry.ClearDetails();
                raceEntry = await _databaseService.GetRacerDetailsAsync(raceEntry, EnterPairQueueCategory);
            }
            switch (raceEntry.Lane)
            {
                case 0:
                    LeftEnterPairRacerEntry = raceEntry;
                    LeftEnterPairRacerEntry.PropertyChanged += EnterPairRacerEntryHandler;
                    break;
                case 1:
                    RightEnterPairRacerEntry = raceEntry;
                    RightEnterPairRacerEntry.PropertyChanged += EnterPairRacerEntryHandler;
                    break;
            }
            await _databaseService.WriteQueueRacersAsync(raceEntry);
        }
        partial void OnEnterPairSelectedCategComboTextChanged(string? text)
        {
            if (EnterPairSelectedCategComboText == null || Categories == null) return;
            var categoryOrder = int.Parse(EnterPairSelectedCategComboText[..2]);

            EnterPairQueueCategory.Category = Categories
                .FirstOrDefault(c => c != null && c.CategoryOrder == categoryOrder)?.CategoryId ?? 0;
        }

        [RelayCommand]
        private void QueuePair()
        {
            var category = Categories?.Find(c => c?.CategoryId == EnterPairQueueCategory.Category);
            if (category == null) return;
            QueuePairCategText = category.CategoryName;
            QueuePairFinishText = FinishList[EnterPairQueueCategory.Finish];
            QueuePairModeText = ModeList[EnterPairQueueCategory.Mode] + " Round " +
                                EnterPairQueueCategory.Round;
            if (LeftEnterPairRacerEntry != null) QueuePairLeftTreeText = TreeList[LeftEnterPairRacerEntry.Tree];
            if (RightEnterPairRacerEntry != null) QueuePairRightTreeText = TreeList[RightEnterPairRacerEntry.Tree];

            QueuePairQueueCategory = EnterPairQueueCategory.Clone(1);
            //reset tree types to category default in LeftEnterPairRacerEntry and RightEnterPairRacerEntry

            EnterPairQueueCategory.Mode = category.LastMode;
            EnterPairQueueCategory.LastRound = category.LastRound;
            EnterPairQueueCategory.Round = category.LastRound + (category.LastRound == 0 ? 1 : 0);

            EnterPairCategText = category.CategoryName;

            if (LeftEnterPairRacerEntry != null)
            {
                LeftQueuePairRacerEntry = LeftEnterPairRacerEntry?.Clone(1);
                LeftEnterPairRacerEntry.ClearDetails();
                LeftEnterPairRacerEntry.RaceNumber = "";
            }
            else
            {
                LeftQueuePairRacerEntry = new RaceEntry();
            }

            if (RightEnterPairRacerEntry != null)
            {
                RightQueuePairRacerEntry = RightEnterPairRacerEntry?.Clone(1);
                RightEnterPairRacerEntry.ClearDetails();
                RightEnterPairRacerEntry.RaceNumber = "";
            }
            else
            {
                RightQueuePairRacerEntry = new RaceEntry();
            }

            _ = _databaseService.WriteQueueCategoriesAsync(QueuePairQueueCategory);
            if (LeftQueuePairRacerEntry != null) _ = _databaseService.WriteQueueRacersAsync(LeftQueuePairRacerEntry);
            if (RightQueuePairRacerEntry != null) _ = _databaseService.WriteQueueRacersAsync(RightQueuePairRacerEntry);
        }
        private async Task LoadCategoriesAsync()
        {
            Categories = await _databaseService.GetCategoryListAsync();
            
            CategComboBoxItems = Categories.Select(c => c != null ? $"{(c.CategoryOrder.ToString("D2"))} - {c.CategoryName}" : null).ToList();
        }

        private async Task LoadTreeTypesAsync()
        {
            TreeList = await _databaseService.GetTreeTypesAsync();
        }

        private async Task LoadFinishLinesAsync()
        {
            FinishList = await _databaseService.GetFinishLinesAsync();
        }
    }
    
}
