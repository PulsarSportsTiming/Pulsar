using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using PulsarUI.Models;
using PulsarUI.Services;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;
using Avalonia.Controls.Documents;
using System.Collections.ObjectModel;
using Microsoft.Extensions.Configuration;

namespace PulsarUI.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        // Initialize to suppress 'non-nullable field must contain a non-null value' warnings
        private DispatcherTimer _clockTimer = default!;
        [ObservableProperty] private string _currentTime = string.Empty;

        private readonly DatabaseService _databaseService;
        
        private readonly MqttService _mqttService;

        private string? _lastConfirmedRaceNum;
        
        public ICommand LeftRaceNumConfirmedCommand { get; }
        public ICommand RightRaceNumConfirmedCommand { get; }
        public ICommand LeftIndexConfirmedCommand { get; }
        public ICommand RightIndexConfirmedCommand { get; }

        [GeneratedRegex(@"^(\d{0,2}(\.\d{0,2})?|\.?\d{0,2})?$")]

        private static partial Regex IndexPatternRegex();

        [ObservableProperty]
        private bool _systemEngaged;
        partial void OnSystemEngagedChanged(bool value)
        {
            UpdateButtonStatuses();
        }

        [ObservableProperty]
        private bool _runActive;
        partial void OnRunActiveChanged(bool value)
        {
            UpdateButtonStatuses();
        }

        // Category Properties
        [ObservableProperty] private Category? _enterPairCateg;
        [ObservableProperty] private List<Category?>? _categories;
        [ObservableProperty] private List<string>? _categComboBoxItems;
        [ObservableProperty] private List<string>? _treeComboBoxItems;
        [ObservableProperty] private string? _enterPairSelectedCategComboText;
        private CategQueueItem _enterPairQueueCategory = default!;
        
        public CategQueueItem EnterPairQueueCategory
        {
            get => _enterPairQueueCategory;
            set
            {
                if (_enterPairQueueCategory != null)
                    _enterPairQueueCategory.PropertyChanged -= EnterPairQueueCategoryHandler;
                _enterPairQueueCategory = value;
                if (_enterPairQueueCategory != null)
                    _enterPairQueueCategory.PropertyChanged += EnterPairQueueCategoryHandler;
                OnPropertyChanged(nameof(EnterPairQueueCategory));
            }
        }
        
        [ObservableProperty] private string? _enterPairCategText;
        private CategQueueItem _queuePairQueueCategory = default!;
        
        public CategQueueItem QueuePairQueueCategory
        {
            get => _queuePairQueueCategory;
            set
            {
                if (_queuePairQueueCategory != null)
                    _queuePairQueueCategory.PropertyChanged -= QueuePairQueueCategoryHandler;
                _queuePairQueueCategory = value;
                if (_queuePairQueueCategory != null)
                    _queuePairQueueCategory.PropertyChanged += QueuePairQueueCategoryHandler;
                OnPropertyChanged(nameof(QueuePairQueueCategory));
                // Manually trigger the handler to ensure it fires after assignment
                QueuePairQueueCategoryHandler(_queuePairQueueCategory, new PropertyChangedEventArgs(""));
            }
        }
        [ObservableProperty] private string? _queuePairCategText;
        private CategQueueItem _engagePairQueueCategory = default!;
        public CategQueueItem EngagePairQueueCategory
        {
            get => _engagePairQueueCategory;
            set
            {
                if (_engagePairQueueCategory != null)
                    _engagePairQueueCategory.PropertyChanged -= EngagePairQueueCategoryHandler;
                _engagePairQueueCategory = value;
                if (_engagePairQueueCategory != null)
                    _engagePairQueueCategory.PropertyChanged += EngagePairQueueCategoryHandler;
                OnPropertyChanged(nameof(EngagePairQueueCategory));
                // Manually trigger the handler to ensure it fires after assignment
                EngagePairQueueCategoryHandler(_engagePairQueueCategory, new PropertyChangedEventArgs(""));
            }
        }
        [ObservableProperty] private string? _engagePairCategText;

        // Race Mode Properties
        [ObservableProperty] private List<string> _modeList = new();
        [ObservableProperty] private string? _queuePairModeText;
        [ObservableProperty] private string? _engagePairModeText;

        // Tree Type Properties
        [ObservableProperty] private List<TreeType> _treeList = new();
        [ObservableProperty] private string? _queuePairLeftTreeText;
        [ObservableProperty] private string? _queuePairRightTreeText;
        [ObservableProperty] private string? _engagePairLeftTreeText;
        [ObservableProperty] private string? _engagePairRightTreeText;
        // Selected tree items for binding SelectedItem in XAML (strongly-typed)
        [ObservableProperty] private TreeType? _selectedLeftTree;
        partial void OnSelectedLeftTreeChanged(TreeType? value)
        {
            if (value == null || TreeList == null) return;
            var idx = TreeList.IndexOf(value);
            if (idx >= 0 && EnterRacers.Count > 0 && EnterRacers[0].Tree != idx)
                EnterRacers[0].Tree = idx;
        }

        [ObservableProperty] private TreeType? _selectedRightTree;
        partial void OnSelectedRightTreeChanged(TreeType? value)
        {
            if (value == null || TreeList == null) return;
            var idx = TreeList.IndexOf(value);
            if (idx >= 0 && EnterRacers.Count > 1 && EnterRacers[1].Tree != idx)
                EnterRacers[1].Tree = idx;
        }

        // Finish Line Properties
        [ObservableProperty] private List<FinishLine> _finishList = new();
        [ObservableProperty] private string? _queuePairFinishText;
        [ObservableProperty] private string? _engagePairFinishText;
        // Selected finish item for binding SelectedItem in XAML
        [ObservableProperty] private FinishLine? _selectedFinishLine;
        partial void OnSelectedFinishLineChanged(FinishLine? value)
        {
            if (value == null || FinishList == null || EnterPairQueueCategory == null) return;
            var idx = FinishList.IndexOf(value);
            if (idx >= 0 && EnterPairQueueCategory.Finish != idx)
                EnterPairQueueCategory.Finish = idx;
        }

        // Racer Info Properties
        public ObservableCollection<RaceEntry> EnterRacers { get; } = new ObservableCollection<RaceEntry> { new RaceEntry { Lane = 0, QueueIndex = 0 }, new RaceEntry { Lane = 1, QueueIndex = 0 } };
        public ObservableCollection<RaceEntry> QueuedRacers { get; } = new ObservableCollection<RaceEntry> { new RaceEntry { Lane = 0, QueueIndex = 1 }, new RaceEntry { Lane = 1, QueueIndex = 1 } };
        public ObservableCollection<RaceEntry> EngagedRacers { get; } = new ObservableCollection<RaceEntry> { new RaceEntry { Lane = 0, QueueIndex = 2 }, new RaceEntry { Lane = 1, QueueIndex = 2 } };

        public MainWindowViewModel()
        {
            // Short-circuit heavy initialization in design mode so the XAML designer can instantiate this VM safely.
            if (Avalonia.Controls.Design.IsDesignMode)
            {
                // Provide minimal defaults used by the design-time preview
                ModeList = new List<string>
                {
                    "Practice",
                    "Qualifying",
                    "Eliminations",
                    "Q+E Combo"
                };
                // Ensure there are empty placeholders so bindings in XAML don't NRE
                EnterPairQueueCategory = new CategQueueItem { QueueIndex = 0, Category = 0, Finish = 0, Mode = 0, Round = 1, LastRound = 0 };
                return;
            }
             // Date/Time Display
             var now = DateTime.Now;
             var msToNextSecond = 1000 - now.Millisecond;

             Task.Delay(msToNextSecond).ContinueWith(_ => { StartClock(); });

            // Build configuration from appsettings.json and appsettings.local.json (local overrides)
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            // Try to read the DB connection string from configuration; fall back to the previous hard-coded path
            var dbConnectionString = configuration.GetSection("Database")["ConnectionString"]
                                     ?? configuration["ConnectionString"]
                                     ?? "Data Source=/home/david/PulsarDB.db";

            _databaseService = new DatabaseService(dbConnectionString);
            _ = LoadFinishLinesAsync();
            _ = LoadTreeTypesAsync();
            _ = LoadCategoriesAsync();
            
            _mqttService = new MqttService();
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
            // Attach handlers for the other two CategQueueItems
            if (QueuePairQueueCategory != null)
                QueuePairQueueCategory.PropertyChanged += QueuePairQueueCategoryHandler;
            if (EngagePairQueueCategory != null)
                EngagePairQueueCategory.PropertyChanged += EngagePairQueueCategoryHandler;

            EnterPairSelectedCategComboText = CategComboBoxItems?.FirstOrDefault();

            foreach (var racer in EnterRacers)
                racer.PropertyChanged += EnterPairRacerEntryHandler;
            foreach (var racer in QueuedRacers)
                racer.PropertyChanged += QueuePairRacerEntry_PropertyChanged;
            // EngagedRacers can have a handler if needed

            EnterPairRacerEntryHandler(EnterRacers[0], new PropertyChangedEventArgs(nameof(RaceEntry.RaceNumber)));
            EnterPairRacerEntryHandler(EnterRacers[1], new PropertyChangedEventArgs(nameof(RaceEntry.RaceNumber)));

            ModeList =
            [
                "Practice",
                "Qualifying",
                "Eliminations",
                "Q+E Combo"
            ];

            LeftRaceNumConfirmedCommand = new RelayCommand(async () =>
            {
                var raceNum = EnterRacers[0]?.RaceNumber;

                if (!string.IsNullOrWhiteSpace(raceNum) && raceNum != _lastConfirmedRaceNum)
                {
                    _lastConfirmedRaceNum = raceNum;
                    // Fire your existing MQTT handler (which builds JSON from the model)
                    if (EnterRacers[0] != null)
                        await _mqttService.PubQueueRacersAsync(EnterRacers[0]);
                }
                // Ensure F5 updates after confirming
                UpdateButtonStatuses();
            });

            RightRaceNumConfirmedCommand = new RelayCommand(async () =>
            {
                var raceNum = EnterRacers[1]?.RaceNumber;

                if (!string.IsNullOrWhiteSpace(raceNum) && raceNum != _lastConfirmedRaceNum)
                {
                    _lastConfirmedRaceNum = raceNum;
                    if (EnterRacers[1] != null)
                        await _mqttService.PubQueueRacersAsync(EnterRacers[1]);
                }
                // Ensure F5 updates after confirming
                UpdateButtonStatuses();
            });

            LeftIndexConfirmedCommand = new RelayCommand(async () =>
            {
                if (EnterRacers[0] != null)
                    await _mqttService.PubQueueRacersAsync(EnterRacers[0]);
            });

            RightIndexConfirmedCommand = new RelayCommand(async () =>
            {
                if (EnterRacers[1] != null)
                    await _mqttService.PubQueueRacersAsync(EnterRacers[1]);
            });

            // Keep handlers in sync if collection items are replaced or changed
            EnterRacers.CollectionChanged += (s, e) =>
            {
                if (e.OldItems != null)
                    foreach (RaceEntry oldR in e.OldItems) oldR.PropertyChanged -= EnterPairRacerEntryHandler;
                if (e.NewItems != null)
                    foreach (RaceEntry newR in e.NewItems) newR.PropertyChanged += EnterPairRacerEntryHandler;
                UpdateButtonStatuses();
            };

            QueuedRacers.CollectionChanged += (s, e) =>
            {
                if (e.OldItems != null)
                    foreach (RaceEntry oldR in e.OldItems) oldR.PropertyChanged -= QueuePairRacerEntry_PropertyChanged;
                if (e.NewItems != null)
                    foreach (RaceEntry newR in e.NewItems) newR.PropertyChanged += QueuePairRacerEntry_PropertyChanged;
                UpdateButtonStatuses();
            };
        }

        // Stub for missing event handler to fix compile error
        private void QueuePairRacerEntry_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RaceEntry.RaceNumber) ||
                e.PropertyName == nameof(RaceEntry.HandicapIndex) ||
                e.PropertyName == nameof(RaceEntry.Class) ||
                e.PropertyName == nameof(RaceEntry.Name) ||
                e.PropertyName == nameof(RaceEntry.Vehicle))
            {
                UpdateButtonStatuses();
                // Notify RelayCommands so UI updates immediately
                (QueuePairCommand as CommunityToolkit.Mvvm.Input.IRelayCommand)?.NotifyCanExecuteChanged();
                (EngagePairCommand as CommunityToolkit.Mvvm.Input.IRelayCommand)?.NotifyCanExecuteChanged();
                (ClearQueueCommand as CommunityToolkit.Mvvm.Input.IRelayCommand)?.NotifyCanExecuteChanged();
            }
            // TODO: Implement logic if needed
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

        private CategQueueItem? _lastSentEnterPairQueueCategory;
        private CategQueueItem? _lastSentQueuePairQueueCategory;
        private CategQueueItem? _lastSentEngagePairQueueCategory;

        private bool IsCategQueueItemDifferent(CategQueueItem? a, CategQueueItem? b)
        {
            if (a == null || b == null) return true;
            return a.QueueIndex != b.QueueIndex ||
                   a.Category != b.Category ||
                   a.Finish != b.Finish ||
                   a.Mode != b.Mode ||
                   a.Round != b.Round ||
                   a.LastRound != b.LastRound;
        }

        private void EnterPairQueueCategoryHandler(object? sender, PropertyChangedEventArgs e)
        {
            if (EnterPairQueueCategory == null)
                return;
            // Sync selected finish item to numeric finish index
            if (FinishList != null && FinishList.Count > 0)
            {
                SelectedFinishLine = (EnterPairQueueCategory.Finish >= 0 && EnterPairQueueCategory.Finish < FinishList.Count)
                    ? FinishList[EnterPairQueueCategory.Finish]
                    : FinishList.FirstOrDefault();
            }

            if (IsCategQueueItemDifferent(EnterPairQueueCategory, _lastSentEnterPairQueueCategory))
            {
                _ = _mqttService.PubQueueCategAsync(EnterPairQueueCategory);
                _lastSentEnterPairQueueCategory = EnterPairQueueCategory.Clone(EnterPairQueueCategory.QueueIndex);
            }
        }

        private void QueuePairQueueCategoryHandler(object? sender, PropertyChangedEventArgs e)
        {
            if (QueuePairQueueCategory == null)
                return;
            if (FinishList != null && FinishList.Count > 0)
            {
                // Optionally keep a queue-pair selected finish property; for now keep the textual binding updated elsewhere
            }
            if (IsCategQueueItemDifferent(QueuePairQueueCategory, _lastSentQueuePairQueueCategory))
            {
                _ = _mqttService.PubQueueCategAsync(QueuePairQueueCategory);
                _lastSentQueuePairQueueCategory = QueuePairQueueCategory.Clone(QueuePairQueueCategory.QueueIndex);
            }
        }

        private void EngagePairQueueCategoryHandler(object? sender, PropertyChangedEventArgs e)
        {
            if (EngagePairQueueCategory == null)
                return;
            if (IsCategQueueItemDifferent(EngagePairQueueCategory, _lastSentEngagePairQueueCategory))
            {
                _ = _mqttService.PubQueueCategAsync(EngagePairQueueCategory);
                _lastSentEngagePairQueueCategory = EngagePairQueueCategory.Clone(EngagePairQueueCategory.QueueIndex);
            }
        }

        private void EnterPairRacerEntryHandler(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RaceEntry.RaceNumber) ||
                e.PropertyName == nameof(RaceEntry.HandicapIndex) ||
                e.PropertyName == nameof(RaceEntry.Class) ||
                e.PropertyName == nameof(RaceEntry.Name) ||
                e.PropertyName == nameof(RaceEntry.Vehicle))
            {
                UpdateButtonStatuses();
                // Notify RelayCommands so UI updates immediately
                (QueuePairCommand as CommunityToolkit.Mvvm.Input.IRelayCommand)?.NotifyCanExecuteChanged();
                (EngagePairCommand as CommunityToolkit.Mvvm.Input.IRelayCommand)?.NotifyCanExecuteChanged();
                (ClearQueueCommand as CommunityToolkit.Mvvm.Input.IRelayCommand)?.NotifyCanExecuteChanged();
            }
            // Send Tree value over MQTT when it changes
            if (e.PropertyName == nameof(RaceEntry.Tree) && sender is RaceEntry raceEntry)
            {
                // Publish the updated RaceEntry, including the new Tree value
                _ = _mqttService?.PubQueueRacersAsync(raceEntry);
                // Keep SelectedItem bindings in sync with the numeric Tree index
                if (TreeList != null)
                {
                    if (raceEntry.Lane == 0)
                        SelectedLeftTree = (raceEntry.Tree >= 0 && raceEntry.Tree < TreeList.Count) ? TreeList[raceEntry.Tree] : null;
                    else if (raceEntry.Lane == 1)
                        SelectedRightTree = (raceEntry.Tree >= 0 && raceEntry.Tree < TreeList.Count) ? TreeList[raceEntry.Tree] : null;
                }
            }
            _ = EnterPairRacerEntryHandlerAsync(sender, e);
        }

        private async Task EnterPairRacerEntryHandlerAsync(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not RaceEntry raceEntry) return;
            switch (raceEntry.Lane)
            {
                case 0:
                    if (EnterRacers[0] == null) return;
                    EnterRacers[0].PropertyChanged -= EnterPairRacerEntryHandler;
                    break;
                case 1:
                    if (EnterRacers[1] == null) return;
                    EnterRacers[1].PropertyChanged -= EnterPairRacerEntryHandler;
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
                    EnterRacers[0] = raceEntry;
                    EnterRacers[0].PropertyChanged += EnterPairRacerEntryHandler;
                    break;
                case 1:
                    EnterRacers[1] = raceEntry;
                    EnterRacers[1].PropertyChanged += EnterPairRacerEntryHandler;
                    break;
            }
        }
        partial void OnEnterPairSelectedCategComboTextChanged(string? value)
        {
            if (EnterPairSelectedCategComboText == null || Categories == null) return;
            // Parse the category name from the ComboBox string (format: "01 - Sportsman ET")
            var split = EnterPairSelectedCategComboText.Split(" - ", 2);
            if (split.Length < 2) return;
            var categoryName = split[1].Trim();
            var category = Categories.FirstOrDefault(c => c != null && c.Name == categoryName);
            if (category == null) return;
            EnterPairQueueCategory.Category = category.Id;
            EnterPairCategText = category.Name;
        }

        [RelayCommand]
        private void QueuePair()
        {
            var category = Categories?.Find(c => c?.Id == EnterPairQueueCategory.Category);
            if (category == null) return;
            QueuePairCategText = category.Name;
            QueuePairFinishText = "holla";// FinishList[EnterPairQueueCategory.Finish];
            QueuePairModeText = ModeList[EnterPairQueueCategory.Mode] + " Round " + EnterPairQueueCategory.Round;
            QueuePairQueueCategory = EnterPairQueueCategory.Clone(1);
            EnterPairCategText = category.Name;
            for (int i = 0; i < EnterRacers.Count; i++)
            {
                QueuedRacers[i] = EnterRacers[i].Clone(1);
                EnterRacers[i].ClearAll();
            }
            // Send MQTT nulls for EnterPair
            for (int i = 0; i < EnterRacers.Count; i++)
                _ = _mqttService.PubQueueRacersAsync(new RaceEntry { QueueIndex = 0, Lane = i });
            foreach (var racer in QueuedRacers)
            {
                _ = _mqttService.PubQueueRacersAsync(racer);
            }
            UpdateButtonStatuses();
        }

        [RelayCommand]
        private void EngagePair()
        {
            bool queueHasEntries = QueuedRacers.Any(r => !string.IsNullOrEmpty(r.RaceNumber));
            for (int i = 0; i < EngagedRacers.Count; i++)
            {
                EngagedRacers[i] = queueHasEntries
                    ? QueuedRacers[i].Clone(2)
                    : EnterRacers[i].Clone(2);
                _ = _mqttService.PubQueueRacersAsync(EngagedRacers[i]);
            }
            for (int i = 0; i < QueuedRacers.Count; i++)
            {
                // Publish an explicit queuedpair null entry to clear subscribers' queued pair
                _ = _mqttService.PubQueueRacersAsync(new RaceEntry { QueueIndex = 1, Lane = i, RaceNumber = null, HandicapIndex = null });
                // Then clear the local queued entry
                QueuedRacers[i].ClearAll();
            }
            Category? category;
            if (queueHasEntries)
            {
                category = Categories?.Find(c => c?.Id == QueuePairQueueCategory.Category);
                EngagePairQueueCategory = QueuePairQueueCategory.Clone(2);
            }
            else
            {
                category = Categories?.Find(c => c?.Id == EnterPairQueueCategory.Category);
                EngagePairQueueCategory = EnterPairQueueCategory.Clone(2);
                for (int i = 0; i < EnterRacers.Count; i++)
                {
                    EnterRacers[i].ClearAll();
                    _ = _mqttService.PubQueueRacersAsync(new RaceEntry { QueueIndex = 0, Lane = i });
                }
                UpdateButtonStatuses();
            }
            if (category == null) return;
            EngagePairCategText = category.Name;
            SystemEngaged = true;
            UpdateButtonStatuses();
        }

        [RelayCommand]
        private void ResetEngagePair()
        {
            SystemEngaged = false;
            UpdateButtonStatuses();
            foreach (var racer in EngagedRacers)
                racer.ClearAll();
            UpdateButtonStatuses();
        }

        [RelayCommand]
        private void SwapEngagedPair()
        {
            // Ensure collection has two slots; if missing, add empty placeholders
            while (EngagedRacers.Count < 2)
            {
                EngagedRacers.Add(new RaceEntry { QueueIndex = 2, Lane = EngagedRacers.Count });
            }

            // Capture current entries (may contain null/empty RaceNumber)
            var left = EngagedRacers[0];
            var right = EngagedRacers[1];

            // Swap their data: create clones to avoid mutating original objects in unexpected ways
            var swappedLeft = (right != null) ? right.Clone(2) : new RaceEntry { QueueIndex = 2, Lane = 0 };
            var swappedRight = (left != null) ? left.Clone(2) : new RaceEntry { QueueIndex = 2, Lane = 1 };

            // Ensure lanes are correct after swap
            swappedLeft.Lane = 0;
            swappedRight.Lane = 1;

            // Assign swapped entries back to the collection
            EngagedRacers[0] = swappedLeft;
            EngagedRacers[1] = swappedRight;

            // Publish the updated engaged pair to MQTT so subscribers see the swap (left then right)
            _ = _mqttService.PubQueueRacersAsync(EngagedRacers[0]);
            _ = _mqttService.PubQueueRacersAsync(EngagedRacers[1]);

            // Notify bindings that engaged racers changed
            OnPropertyChanged(nameof(EngagedRacers));
        }

        [RelayCommand]
        private async Task ClearQueue()
        {
            foreach (var racer in QueuedRacers)
                racer.ClearAll();
            for (int i = 0; i < QueuedRacers.Count; i++)
                await _mqttService.PubQueueRacersAsync(new RaceEntry { QueueIndex = 1, Lane = i });
            UpdateButtonStatuses();
        }

        public async Task ClearEnterPairQueue()
        {
            foreach (var racer in EnterRacers)
                racer.ClearAll();
            for (int i = 0; i < EnterRacers.Count; i++)
                await _mqttService.PubQueueRacersAsync(new RaceEntry { QueueIndex = 0, Lane = i });
            UpdateButtonStatuses();
        }

        public async Task ClearEngagePairQueue()
        {
            foreach (var racer in EngagedRacers)
                racer.ClearAll();
            for (int i = 0; i < EngagedRacers.Count; i++)
                await _mqttService.PubQueueRacersAsync(new RaceEntry { QueueIndex = 2, Lane = i });
            UpdateButtonStatuses();
        }
        
        private async Task LoadCategoriesAsync()
        {
            Categories = await _databaseService.GetCategoryListAsync();

            CategComboBoxItems = Categories.Select(c => c != null ? $"{(c.Order.ToString("D2"))} - {c.Name}" : null).ToList();

            await _mqttService.PublishMqtt("pulsarui/sysmsg","Loading categories...");
        }

        private async Task LoadTreeTypesAsync()
        {
            TreeList = await _databaseService.GetTreeTypesAsync();
            // Initialize SelectedLeftTree/SelectedRightTree from existing EnterRacers' Tree indices
            if (TreeList != null && TreeList.Count > 0)
            {
                if (EnterRacers.Count > 0 && EnterRacers[0].Tree >= 0 && EnterRacers[0].Tree < TreeList.Count)
                    SelectedLeftTree = TreeList[EnterRacers[0].Tree];
                else
                    SelectedLeftTree = TreeList.FirstOrDefault();

                if (EnterRacers.Count > 1 && EnterRacers[1].Tree >= 0 && EnterRacers[1].Tree < TreeList.Count)
                    SelectedRightTree = TreeList[EnterRacers[1].Tree];
                else
                    SelectedRightTree = TreeList.ElementAtOrDefault(1) ?? TreeList.FirstOrDefault();
            }
        }

        private async Task LoadFinishLinesAsync()
        {
            FinishList = await _databaseService.GetFinishLinesAsync();
            // Initialize SelectedFinishLine from EnterPairQueueCategory's Finish index
            if (FinishList != null && FinishList.Count > 0 && EnterPairQueueCategory != null)
            {
                if (EnterPairQueueCategory.Finish >= 0 && EnterPairQueueCategory.Finish < FinishList.Count)
                    SelectedFinishLine = FinishList[EnterPairQueueCategory.Finish];
                else
                    SelectedFinishLine = FinishList.FirstOrDefault();
            }
        }

        // F5 (Queue) is enabled if any EnterRacers have a RaceNumber and all QueuedRacers are empty
        public bool IsF5Enabled =>
            EnterRacers.Any(r => !string.IsNullOrEmpty(r.RaceNumber)) &&
            QueuedRacers.All(r => string.IsNullOrEmpty(r.RaceNumber));

        // F10 (Engage) is enabled if there are queued or entered racers, and system is not engaged or running
        public bool IsF10Enabled =>
            (QueuedRacers.Any(r => !string.IsNullOrEmpty(r.RaceNumber)) ||
             EnterRacers.Any(r => !string.IsNullOrEmpty(r.RaceNumber))) &&
            !SystemEngaged &&
            !RunActive;

        // F11 (Clear Queue) is enabled only when any QueuedRacers have a RaceNumber
        public bool IsF11Enabled =>
            QueuedRacers.Any(r => !string.IsNullOrEmpty(r.RaceNumber));

        // Centralized helper to update all button-related bindings
        private void UpdateButtonStatuses()
        {
            OnPropertyChanged(nameof(IsF5Enabled));
            OnPropertyChanged(nameof(IsF10Enabled));
            OnPropertyChanged(nameof(IsF11Enabled));
        }
    }
}
