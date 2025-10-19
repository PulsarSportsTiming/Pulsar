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

namespace PulsarUI.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private DispatcherTimer _clockTimer;
        [ObservableProperty] private string _currentTime;

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
            OnPropertyChanged(nameof(IsF5Enabled));
            OnPropertyChanged(nameof(IsF10Enabled));
            OnPropertyChanged(nameof(IsF11Enabled));
        }

        [ObservableProperty]
        private bool _runActive;
        partial void OnRunActiveChanged(bool value)
        {
            OnPropertyChanged(nameof(IsF5Enabled));
            OnPropertyChanged(nameof(IsF10Enabled));
            OnPropertyChanged(nameof(IsF11Enabled));
        }

        // Category Properties
        [ObservableProperty] private Category? _enterPairCateg;
        [ObservableProperty] private List<Category?>? _categories;
        [ObservableProperty] private List<string>? _categComboBoxItems;
        [ObservableProperty] private string? _enterPairSelectedCategComboText;
        private CategQueueItem _enterPairQueueCategory;
        
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
        private CategQueueItem _queuePairQueueCategory;
        
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
        private CategQueueItem _engagePairQueueCategory;
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
        [ObservableProperty] private List<string> _modeList;
        [ObservableProperty] private string? _queuePairModeText;
        [ObservableProperty] private string? _engagePairModeText;

        // Tree Type Properties
        [ObservableProperty] private List<string> _treeList;
        [ObservableProperty] private string? _queuePairLeftTreeText;
        [ObservableProperty] private string? _queuePairRightTreeText;
        [ObservableProperty] private string? _engagePairLeftTreeText;
        [ObservableProperty] private string? _engagePairRightTreeText;

        // Finish Line Properties
        [ObservableProperty] private List<string?> _finishList;
        [ObservableProperty] private string? _queuePairFinishText;
        [ObservableProperty] private string? _engagePairFinishText;

        // Racer Info Properties
        public ObservableCollection<RaceEntry> EnterRacers { get; } = new ObservableCollection<RaceEntry> { new RaceEntry { Lane = 0, QueueIndex = 0 }, new RaceEntry { Lane = 1, QueueIndex = 0 } };
        public ObservableCollection<RaceEntry> QueuedRacers { get; } = new ObservableCollection<RaceEntry> { new RaceEntry { Lane = 0, QueueIndex = 1 }, new RaceEntry { Lane = 1, QueueIndex = 1 } };
        public ObservableCollection<RaceEntry> EngagedRacers { get; } = new ObservableCollection<RaceEntry> { new RaceEntry { Lane = 0, QueueIndex = 2 }, new RaceEntry { Lane = 1, QueueIndex = 2 } };

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
                OnPropertyChanged(nameof(IsF5Enabled));
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
                OnPropertyChanged(nameof(IsF5Enabled));
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
                OnPropertyChanged(nameof(IsF5Enabled));
                OnPropertyChanged(nameof(IsF10Enabled));
                OnPropertyChanged(nameof(IsF11Enabled));
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
                OnPropertyChanged(nameof(IsF5Enabled));
                OnPropertyChanged(nameof(IsF10Enabled));
                OnPropertyChanged(nameof(IsF11Enabled));
            }
            // Send Tree value over MQTT when it changes
            if (e.PropertyName == nameof(RaceEntry.Tree) && sender is RaceEntry raceEntry)
            {
                // Publish the updated RaceEntry, including the new Tree value
                _ = _mqttService?.PubQueueRacersAsync(raceEntry);
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
        partial void OnEnterPairSelectedCategComboTextChanged(string? text)
        {
            if (EnterPairSelectedCategComboText == null || Categories == null) return;
            // Parse the category name from the ComboBox string (format: "01 - Sportsman ET")
            var split = EnterPairSelectedCategComboText.Split(" - ", 2);
            if (split.Length < 2) return;
            var categoryName = split[1].Trim();
            var category = Categories.FirstOrDefault(c => c != null && c.CategoryName == categoryName);
            if (category == null) return;
            EnterPairQueueCategory.Category = category.CategoryId;
            EnterPairCategText = category.CategoryName;
        }

        [RelayCommand]
        private void QueuePair()
        {
            var category = Categories?.Find(c => c?.CategoryId == EnterPairQueueCategory.Category);
            if (category == null) return;
            QueuePairCategText = category.CategoryName;
            QueuePairFinishText = FinishList[EnterPairQueueCategory.Finish];
            QueuePairModeText = ModeList[EnterPairQueueCategory.Mode] + " Round " + EnterPairQueueCategory.Round;
            QueuePairQueueCategory = EnterPairQueueCategory.Clone(1);
            EnterPairCategText = category.CategoryName;
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
            OnPropertyChanged(nameof(IsF5Enabled));
            OnPropertyChanged(nameof(IsF10Enabled));
            OnPropertyChanged(nameof(IsF11Enabled));
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
                category = Categories?.Find(c => c?.CategoryId == QueuePairQueueCategory.Category);
                EngagePairQueueCategory = QueuePairQueueCategory.Clone(2);
            }
            else
            {
                category = Categories?.Find(c => c?.CategoryId == EnterPairQueueCategory.Category);
                EngagePairQueueCategory = EnterPairQueueCategory.Clone(2);
                for (int i = 0; i < EnterRacers.Count; i++)
                {
                    EnterRacers[i].ClearAll();
                    _ = _mqttService.PubQueueRacersAsync(new RaceEntry { QueueIndex = 0, Lane = i });
                }
                OnPropertyChanged(nameof(IsF5Enabled));
            }
            if (category == null) return;
            EngagePairCategText = category.CategoryName;
            SystemEngaged = true;
            OnPropertyChanged(nameof(IsF10Enabled));
        }

        [RelayCommand]
        private void ResetEngagePair()
        {
            SystemEngaged = false;
            OnPropertyChanged(nameof(IsF10Enabled));
            foreach (var racer in EngagedRacers)
                racer.ClearAll();
            OnPropertyChanged(nameof(IsF5Enabled));
            OnPropertyChanged(nameof(IsF10Enabled));
            OnPropertyChanged(nameof(IsF11Enabled));
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
            OnPropertyChanged(nameof(IsF5Enabled));
            OnPropertyChanged(nameof(IsF10Enabled));
            OnPropertyChanged(nameof(IsF11Enabled));
        }

        public async Task ClearEnterPairQueue()
        {
            foreach (var racer in EnterRacers)
                racer.ClearAll();
            for (int i = 0; i < EnterRacers.Count; i++)
                await _mqttService.PubQueueRacersAsync(new RaceEntry { QueueIndex = 0, Lane = i });
            OnPropertyChanged(nameof(IsF5Enabled));
            OnPropertyChanged(nameof(IsF10Enabled));
            OnPropertyChanged(nameof(IsF11Enabled));
        }

        public async Task ClearEngagePairQueue()
        {
            foreach (var racer in EngagedRacers)
                racer.ClearAll();
            for (int i = 0; i < EngagedRacers.Count; i++)
                await _mqttService.PubQueueRacersAsync(new RaceEntry { QueueIndex = 2, Lane = i });
            OnPropertyChanged(nameof(IsF5Enabled));
            OnPropertyChanged(nameof(IsF10Enabled));
            OnPropertyChanged(nameof(IsF11Enabled));
        }
        
        private async Task LoadCategoriesAsync()
        {
            Categories = await _databaseService.GetCategoryListAsync();

            CategComboBoxItems = Categories.Select(c => c != null ? $"{(c.CategoryOrder.ToString("D2"))} - {c.CategoryName}" : null).ToList();

            await _mqttService.PublishMqtt("pulsarui/sysmsg","Loading categories...");
        }

        private async Task LoadTreeTypesAsync()
        {
            TreeList = await _databaseService.GetTreeTypesAsync();
        }

        private async Task LoadFinishLinesAsync()
        {
            FinishList = await _databaseService.GetFinishLinesAsync();
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
    }
}
