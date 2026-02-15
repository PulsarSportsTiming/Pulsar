using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using PulsarUI.Models;
using PulsarUI.Services;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using System.Reflection;
using MQTTnet;
using PulsarUI.Helpers; // added for ModeItem/RunModeExtensions

namespace PulsarUI.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        // Initialize to suppress 'non-nullable field must contain a non-null value' warnings
        private DispatcherTimer _clockTimer = default!;
        [ObservableProperty] private string _currentTime = string.Empty;

        // Services may be uninitialized in the design-time constructor path; mark with default! to suppress warnings
        private readonly DatabaseService _databaseService = default!;

        private readonly MqttService _mqttService = default!;
        // Test message content received via MQTT (for UI popup)
        [ObservableProperty]
        private string? _testMessageTopic;
        [ObservableProperty]
        private string? _testMessagePayload;
        [ObservableProperty]
        private bool _showTestMessagePopup;
        
        [ObservableProperty]
        private ObservableCollection<TimingLabelItem> _leftTimingLabels = new();

        [ObservableProperty]
        private ObservableCollection<TimingLabelItem> _rightTimingLabels = new();

        // Enable/disable debug publishing and local logging (toggle while diagnosing)
        private readonly bool _enableDebugPublish = false;
        private readonly bool _enableLocalLog = false;

        private string? _lastConfirmedRaceNum;
        
        public IEnumerable<RunMode> RunModes { get; } = Enum.GetValues(typeof(RunMode)).Cast<RunMode>();

        // Provide async commands so we avoid "async void" lambdas and analyzer warnings; initialized in constructor
        // Initialize with no-op AsyncRelayCommand so they're safe in design-time constructor paths
        public CommunityToolkit.Mvvm.Input.IAsyncRelayCommand LeftRaceNumConfirmedCommand { get; private set; } = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => Task.CompletedTask);
        public CommunityToolkit.Mvvm.Input.IAsyncRelayCommand RightRaceNumConfirmedCommand { get; private set; } = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => Task.CompletedTask);
        public CommunityToolkit.Mvvm.Input.IAsyncRelayCommand LeftIndexConfirmedCommand { get; private set; } = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => Task.CompletedTask);
        public CommunityToolkit.Mvvm.Input.IAsyncRelayCommand RightIndexConfirmedCommand { get; private set; } = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => Task.CompletedTask);

        [GeneratedRegex(@"^(\d{0,2}(\.\d{0,2})?|\.?\d{0,2})?$")]

        private static partial Regex IndexPatternRegex();

        [ObservableProperty]
        private bool _systemEngaged;
        partial void OnSystemEngagedChanged(bool value)
        {
            UpdateButtonStatuses();

            // When the system becomes engaged, create an Engaged Pair with two Runs (left/right).
            if (value)
            {
                // Avoid recreating if already present
                if (EngagedPairModel != null)
                    return;

                var p = new Pair();
                p.Id = Guid.NewGuid();

                // Resolve category for the engaged pair. Prefer CategoryDetails if it appears populated.
                Category? resolvedCat = null;
                if (EngagePairQueueCategory?.CategoryDetails != null && EngagePairQueueCategory.CategoryDetails.Id != 0)
                    resolvedCat = EngagePairQueueCategory.CategoryDetails;
                else if (Categories != null && EngagePairQueueCategory != null)
                    resolvedCat = Categories.FirstOrDefault(c => c != null && c.Id == EngagePairQueueCategory.Category);

                p.Category = resolvedCat;
                p.RunMode = EngagePairQueueCategory?.Mode;

                // Create left and right runs; clone the engaged RaceEntry to decouple UI instances.
                var leftEntry = EngagedRacers.ElementAtOrDefault(0)?.Clone(2) ?? new RaceEntry { QueueIndex = 2, Lane = 0 };
                var rightEntry = EngagedRacers.ElementAtOrDefault(1)?.Clone(2) ?? new RaceEntry { QueueIndex = 2, Lane = 1 };

                var leftRun = new Run { Entry = leftEntry };
                var rightRun = new Run { Entry = rightEntry };

                p.Runs = new Run[] { leftRun, rightRun };

                // Populate expected reaction times for the created runs
                PulsarUI.Services.TimingHelpers.PopulateExpectedReactionTimes(p);

                EngagedPairModel = p;

                MaybeDebug($"Created EngagedPair {p.Id} CategoryId={p.Category?.Id} Mode={p.RunMode}");

                // Refresh timing labels to show only labels up to the engaged pair's finish line distance
                if (EngagePairQueueCategory != null && FinishList != null)
                {
                    // Look up the finish line distance using the EngagePairQueueCategory's Finish ID
                    var finishLine = FinishList.FirstOrDefault(f => f != null && f.Id == EngagePairQueueCategory.Finish);
                    if (finishLine != null)
                    {
                        RefreshTimingLabels(finishLine.Distance);
                    }
                }

                // Serialize the Pair consistently for publishing
                try
                {
                    var options = new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                    };
                    options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
                    // Register DateTime converters to format UTC DateTimes as "yyyy-MM-dd HH:mm:ssZ"
                    options.Converters.Add(new PulsarUI.Services.JsonConverters.DateTimeUtcConverter());
                    options.Converters.Add(new PulsarUI.Services.JsonConverters.NullableDateTimeUtcConverter());
                    // Build a minimal, well-structured payload to avoid serialization issues for consumers
                    var minimal = new
                    {
                        id = p.Id,
                        categoryId = p.Category?.Id,
                        runMode = p.RunMode?.ToString(),
                        runs = p.Runs?.Select(r => new
                        {
                            lane = r.Entry?.Lane,
                            queueIndex = r.Entry?.QueueIndex,
                            raceNumber = r.Entry?.RaceNumber,
                            treeId = r.Entry?.Tree?.Id
                        }).ToArray()
                    };
                    var payload = System.Text.Json.JsonSerializer.Serialize(minimal, options);

                    // Publish to a runqueue topic so consumers that already subscribe to runqueue/* see the Pair with GUID
                    if (_mqttService != null)
                    {
                        _ = _mqttService.PublishMqtt("runqueue/engagedpair/pair", payload);
                    }

                    // Additionally publish the full Pair to a debug topic so external subscribers can inspect it
                    try
                    {
                        if (_mqttService != null)
                        {
                            var full = System.Text.Json.JsonSerializer.Serialize(p, options);
                            // Publish full Pair for debugging/consumers that need the complete object
                            _ = _mqttService.PublishMqtt("pulsarui/engagedpair/full", full);
                        }
                    }
                    catch (Exception ex)
                    {
                        MaybeDebug("EngagedPair debug publish failed: " + ex.Message);
                    }
                }
                catch (Exception ex)
                {
                    MaybeDebug("EngagedPair publish failed: " + ex.Message);
                }
            }
            else
            {
                // Clear when the system is disengaged
                EngagedPairModel = null;
                MaybeDebug("Cleared EngagedPairModel due to disengage");
                
                // Only restore full list if no category is engaged
                if (EngagePairQueueCategory?.Category == 0)
                {
                    RefreshTimingLabels(0);
                }
            }
        }

        [ObservableProperty]
        private bool _runActive;
        partial void OnRunActiveChanged(bool value)
        {
            UpdateButtonStatuses();
        }
        
        [ObservableProperty] private long _runStartTimestampNanoseconds;

        // Category Properties
        [ObservableProperty] private Category? _enterPairCateg;
        // Categories are returned as a non-nullable list of Category objects from the DB service
        [ObservableProperty] private List<Category> _categories = new();
        [ObservableProperty] private List<string> _categComboBoxItems = new();
        [ObservableProperty] private List<string>? _treeComboBoxItems;
        [ObservableProperty] private string? _enterPairSelectedCategComboText;
        private CategQueueItem _enterPairQueueCategory = new CategQueueItem { QueueIndex = 0, Category = 0, Finish = 0, Mode = 0, Round = 1, LastRound = 0 };

        [ObservableProperty] private bool _leftTreeComboActive;
        [ObservableProperty] private bool _rightTreeComboActive;
        [ObservableProperty] private bool _finishLineComboActive;
        [ObservableProperty] private bool _setupActive;
        [ObservableProperty] private bool _setupInactive = true;

        // Strongly-typed references to generated IRelayCommand instances to avoid repeated casts
        private CommunityToolkit.Mvvm.Input.IRelayCommand? _queuePairCommandRef;
        private CommunityToolkit.Mvvm.Input.IRelayCommand? _engagePairCommandRef;
        private CommunityToolkit.Mvvm.Input.IRelayCommand? _clearQueueCommandRef;
        private CommunityToolkit.Mvvm.Input.IRelayCommand? _resetEngageCommandRef;
        private CommunityToolkit.Mvvm.Input.IRelayCommand? _swapEngageCommandRef;

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
        private CategQueueItem _queuePairQueueCategory = new CategQueueItem { QueueIndex = 1, Category = 0, Finish = 0, Mode = 0, Round = 1, LastRound = 0 };

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
        private CategQueueItem _engagePairQueueCategory = new CategQueueItem { QueueIndex = 2, Category = 0, Finish = 0, Mode = 0, Round = 1, LastRound = 0 };
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
        // ModeItem maps RunMode enum values to user-facing names. Use SelectedValue/SelectedValuePath in XAML to bind the enum directly.
        [ObservableProperty] private string? _queuePairModeText;
        [ObservableProperty] private string? _engagePairModeText;
        [ObservableProperty] private string? _engagePairStartModeText;

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
            // Assign the selected TreeType directly to the RaceEntry.Tree (was previously numeric index)
            if (EnterRacers.Count > 0 && EnterRacers[0].Tree != value)
                EnterRacers[0].Tree = value;
        }

        [ObservableProperty] private TreeType? _selectedRightTree;
        partial void OnSelectedRightTreeChanged(TreeType? value)
        {
            if (value == null || TreeList == null) return;
            // Assign the selected TreeType directly to the RaceEntry.Tree (was previously numeric index)
            if (EnterRacers.Count > 1 && EnterRacers[1].Tree != value)
                EnterRacers[1].Tree = value;
        }

        // Finish Line Properties
        [ObservableProperty] private List<FinishLine> _finishList = new();
        [ObservableProperty] private string? _queuePairFinishText;
        [ObservableProperty] private string? _engagePairFinishText;
        // Selected finish item for binding SelectedItem in XAML
        [ObservableProperty] private FinishLine? _selectedFinishLine;
        partial void OnSelectedFinishLineChanged(FinishLine? value)
        {
            if (value == null || EnterPairQueueCategory == null) return;
            // Store the FinishLine Id on the queue item (use Id-based references)
            if (EnterPairQueueCategory.Finish != value.Id)
                EnterPairQueueCategory.Finish = value.Id;
        }

        // Racer Info Properties
        public ObservableCollection<RaceEntry> EnterRacers { get; } = new ObservableCollection<RaceEntry> { new RaceEntry { Lane = 0, QueueIndex = 0 }, new RaceEntry { Lane = 1, QueueIndex = 0 } };
        public ObservableCollection<RaceEntry> QueuedRacers { get; } = new ObservableCollection<RaceEntry> { new RaceEntry { Lane = 0, QueueIndex = 1 }, new RaceEntry { Lane = 1, QueueIndex = 1 } };
        public ObservableCollection<RaceEntry> EngagedRacers { get; } = new ObservableCollection<RaceEntry> { new RaceEntry { Lane = 0, QueueIndex = 2 }, new RaceEntry { Lane = 1, QueueIndex = 2 } };
        // Holds the generated Pair when the system becomes engaged
        public Pair? EngagedPairModel { get; private set; }

        // Cache input map data for refreshing timing labels based on engaged category
        private List<DownTrackInput>? _cachedInputs;
        private List<SpeedTrap>? _cachedTraps;
        private string? _cachedDistanceUnit;
        private string? _cachedSpeedUnit;

        public MainWindowViewModel()
        {
            // Short-circuit heavy initialization in design mode so the XAML designer can instantiate this VM safely.
            if (Avalonia.Controls.Design.IsDesignMode)
            {
                // Ensure there are empty placeholders so bindings in XAML don't NRE
                EnterPairQueueCategory = new CategQueueItem { QueueIndex = 0, Category = 0, Finish = 0, Mode = 0, Round = 1, LastRound = 0 };

                // Also ensure queued racer tree text properties exist for designer
                QueuePairLeftTreeText = string.Empty;
                QueuePairRightTreeText = string.Empty;
                return;
            }
            // Log constructor entry for diagnostics
            LocalLog("MainWindowViewModel ctor start");
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
            // Launch async loads and log that we started them
            // Suppress category publishes while we set up initial queue items so we don't emit unintended MQTT messages
            _suppressCategPublish = true;
            _ = LoadFinishLinesAsync();
            _ = LoadTreeTypesAsync();
            _ = LoadCategoriesAsync();
            LocalLog("Launched LoadFinishLines/LoadTreeTypes/LoadCategories tasks");

            _mqttService = new MqttService();
            
            // Ensure RunManager is attached so MqttService can register runs and compute reaction times
            try
            {
                var runManager = new RunManager();
                _mqttService.AttachRunManager(runManager);
                LocalLog("Attached RunManager to MqttService");
            }
            catch (Exception ex)
            {
                LocalLog("Failed to attach RunManager to MqttService: " + ex.Message);
            }
            // Subscribe to reaction time computed events so ViewModel can update models and UI
            try
            {
                _mqttService.ReactionTimeComputed += (lane, rtNs, runId, detectionSource, detectionTimestampNs) =>
                {
                    // Marshal onto UI thread
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            LocalLog($"ReactionTimeComputed invoked: lane={lane} rtNs={rtNs} runId={runId} detectionSource={detectionSource} detectionTs={detectionTimestampNs}");
                            // Map lane to run index (left=0,right=1)
                            int runIndex = lane.Equals("left", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                            var run = EngagedPairModel?.Runs?.ElementAtOrDefault(runIndex);
                            if (run == null)
                            {
                                LocalLog($"ReactionTimeComputed: no run found for lane={lane} runIndex={runIndex} EngagedPairModel={(EngagedPairModel==null?"null":"hasRuns="+ (EngagedPairModel.Runs?.Length.ToString() ?? "?"))}");
                                return;
                            }
                            if (run != null)
                            {
                                if (run.ReactionTime == null) run.ReactionTime = new ReactionTime();
                                run.ReactionTime.ValueNs = rtNs;
                                // Store the raw detection timestamp (uncalculated)
                                run.ReactionTime.TriggerTimestampNanoseconds = detectionTimestampNs;
                                // Set trigger/detectionSource if appropriate
                                if (detectionSource == "stage") run.ReactionTime.Trigger = (lane == "left") ? InputRole.LeftStage : InputRole.RightStage;
                                else if (detectionSource == "guardA") run.ReactionTime.Trigger = (lane == "left") ? InputRole.LeftGuardA : InputRole.RightGuardA;
                                else if (detectionSource == "guardB") run.ReactionTime.Trigger = (lane == "left") ? InputRole.LeftGuardB : InputRole.RightGuardB;
                                else run.ReactionTime.Trigger = (lane == "left") ? InputRole.LeftStage : InputRole.RightStage;

                                // Update the left/right timing label "Reaction Time" value string
                                var labels = runIndex == 0 ? LeftTimingLabels : RightTimingLabels;
                                if (labels != null && labels.Count > 0)
                                {
                                    // First label is expected to be Reaction Time per TimingLabelHelpers.GenerateTimingLabels
                                    var first = labels.ElementAtOrDefault(0);
                                    if (first != null)
                                    {
                                        first.Value = run.ReactionTime.Value; // formatted numeric string
                                    }
                                }

                                // Handle Foul remark: if reaction time is negative, mark Foul; otherwise remove it
                                try
                                {
                                    var remarksList = run.Remarks?.ToList() ?? new System.Collections.Generic.List<RunRemarks>();
                                    // Ensure ExpectedReactionTimeNs has been populated for this run (timing setup may have happened earlier/later)
                                    if (run.ReactionTime.ExpectedReactionTimeNs == 0)
                                    {
                                        try { PulsarUI.Services.TimingHelpers.PopulateExpectedReactionTimes(EngagedPairModel); }
                                        catch { /* best-effort */ }
                                    }
                                    // Determine foul by comparing computed reaction time against expected RT (delta < 0)
                                    long deltaNs = run.ReactionTime.ValueNs - run.ReactionTime.ExpectedReactionTimeNs;
                                    if (deltaNs < 0)
                                    {
                                        if (!remarksList.Contains(RunRemarks.Foul))
                                        {
                                            remarksList.Add(RunRemarks.Foul);
                                            run.Remarks = remarksList.ToArray();
                                            // Publish a debug MQTT message so external tools or logs can observe remark changes regardless of local logging
                                            try { _ = _mqttService.PublishMqtt($"pulsarui/remark/{lane}", $"Added:Foul:{deltaNs}"); } catch { }
                                            LocalLog($"Added RunRemarks.Foul for lane={lane} deltaNs={deltaNs} ValueNs={run.ReactionTime.ValueNs} ExpectedNs={run.ReactionTime.ExpectedReactionTimeNs}");
                                        }
                                    }
                                    else
                                    {
                                        if (remarksList.Contains(RunRemarks.Foul))
                                        {
                                            remarksList.Remove(RunRemarks.Foul);
                                            run.Remarks = remarksList.ToArray();
                                            try { _ = _mqttService.PublishMqtt($"pulsarui/remark/{lane}", $"Removed:Foul:{deltaNs}"); } catch { }
                                            LocalLog($"Removed RunRemarks.Foul for lane={lane} deltaNs={deltaNs}");
                                        }
                                    }

                                    // Update the dedicated Remarks label
                                    if (labels != null && labels.Count > 0)
                                    {
                                        var resultIndex = labels.ToList().FindIndex(l => string.Equals(l.Label, "Result", StringComparison.OrdinalIgnoreCase));
                                        var remarksIndex = resultIndex >= 0 ? resultIndex + 1 : labels.ToList().FindIndex(l => string.Equals(l.Label, "Remarks", StringComparison.OrdinalIgnoreCase));
                                        if (remarksIndex >= 0 && remarksIndex < labels.Count)
                                        {
                                            var remarksText = (run.Remarks != null && run.Remarks.Length > 0)
                                                ? string.Join(" | ", run.Remarks.Select(r => ((System.Enum)r).GetDisplayName()))
                                                : string.Empty;
                                            labels[remarksIndex].Value = remarksText;
                                            try { _ = _mqttService.PublishMqtt($"pulsarui/remark/{lane}", $"LabelUpdated:{remarksText}"); } catch { }
                                            LocalLog($"Updated Remarks label for lane={lane} remarks='{remarksText}'");
                                        }
                                    }
                                }
                                catch (Exception ex) { LocalLog("ReactionTimeComputed remarks update failed: " + ex.Message); }

                                // Also publish the ReactionTime.Value string over MQTT under timingdata/{lane}/reactiontime/string
                                try
                                {
                                    var topic = $"timingdata/{lane}/reactiontime/string";
                                    var payload = run.ReactionTime.Value ?? string.Empty;
                                    _ = _mqttService.PublishMqtt(topic, payload);
                                }
                                catch (Exception ex)
                                {
                                    LocalLog("Failed to publish reaction time string: " + ex.Message);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LocalLog("ReactionTimeComputed handler error: " + ex.Message);
                        }
                    });
                };

                // Subscribe to incremental time events for down-track inputs
                _mqttService.IncrementalTimeComputed += (lane, downtrackTimestampNs, incNs, speedMpsNullable, runId, dti) =>
                {
                    // Marshal onto Avalonia UI thread
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            // Filter based on engaged category's finish line
                            if (EngagePairQueueCategory != null && FinishList != null && dti != null)
                            {
                                var finishLine = FinishList.FirstOrDefault(f => f != null && f.Id == EngagePairQueueCategory.Finish);
                                if (finishLine != null && dti.DistanceMm > finishLine.Distance)
                                {
                                    // Ignore timing points beyond the finish line
                                    MaybeDebug($"Ignoring timing point beyond finish: {dti.DistanceMm}mm > {finishLine.Distance}mm for lane={lane}");
                                    return;
                                }
                            }
                            MaybeDebug($"VMDBG: IncrementalTimeComputed lane={lane} speed={(speedMpsNullable?.ToString() ?? "<null>")} distance={(dti?.DistanceMm.ToString() ?? "<null>")}");
                            int runIndex = lane.Equals("left", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                            var run = EngagedPairModel?.Runs?.ElementAtOrDefault(runIndex);
                            if (run == null) return;

                            // Prefer to compute incremental relative to the Run's ReactionTime.TriggerTimestampNanoseconds if available
                            long computedIncNs = incNs;
                            if (run.ReactionTime != null && run.ReactionTime.TriggerTimestampNanoseconds > 0)
                            {
                                try
                                {
                                    var delta = downtrackTimestampNs - run.ReactionTime.TriggerTimestampNanoseconds;
                                    computedIncNs = delta < 0 ? 0 : delta;
                                }
                                catch { /* fall back to provided incNs */ }
                            }

                            // Ensure IncrementalTimes list exists
                            var incList = run.IncrementalTimes?.ToList() ?? new System.Collections.Generic.List<IncrementalTime>();

                            // Try to find existing entry by DownTrackInput input index and device
                            var existing = incList.FirstOrDefault(it => it.Input != null && it.Input.Id.InputIndex == dti.Id.InputIndex && it.Input.Id.Device == dti.Id.Device);
                            if (existing != null)
                            {
                                existing.ValueNs = computedIncNs;
                                existing.Input = dti;
                            }
                            else
                            {
                                var newIt = new IncrementalTime { ValueNs = computedIncNs, Input = dti };
                                incList.Add(newIt);
                            }

                            run.IncrementalTimes = incList.ToArray();

                            // Ensure IncrementalSpeeds list exists and update with computed speed
                            var speedList = run.IncrementalSpeeds?.ToList() ?? new System.Collections.Generic.List<IncrementalSpeed>();
                            if (speedMpsNullable.HasValue)
                            {
                                var speedVal = speedMpsNullable.Value;
                                var existingSpeed = speedList.FirstOrDefault(s => s.Input != null && s.Input.Id.InputIndex == dti.Id.InputIndex && s.Input.Id.Device == dti.Id.Device);
                                if (existingSpeed != null)
                                {
                                    existingSpeed.MetersPerSecond = speedVal;
                                    existingSpeed.Input = dti;
                                }
                                else
                                {
                                    var newSp = new IncrementalSpeed { MetersPerSecond = speedVal, Input = dti };
                                    speedList.Add(newSp);
                                }
                                run.IncrementalSpeeds = speedList.ToArray();
                            }

                            // Update UI label corresponding to this DownTrackInput
                            var etLabel = TimingLabelHelpers.FormatDistanceLabel(dti.DistanceMm, AppSettings.DistanceUnit);
                            var labels = runIndex == 0 ? LeftTimingLabels : RightTimingLabels;
                            if (labels != null && labels.Count > 0)
                            {
                                var match = labels.FirstOrDefault(l => string.Equals(l.Label, etLabel, StringComparison.OrdinalIgnoreCase));
                                if (match != null)
                                {
                                    match.Value = TimingLabelHelpers.FormatTimeFromNanosecondsNumeric(computedIncNs);
                                }
                            }

                            // Update the speed-trap label which follows the ET label (if present)
                            try
                            {
                                if (labels != null && labels.Count > 0)
                                {
                                    var speedLabel = TimingLabelHelpers.FormatSpeedTrapLabel(dti.DistanceMm, AppSettings.DistanceUnit, AppSettings.SpeedUnit);
                                    var spMatch = labels.FirstOrDefault(l => string.Equals(l.Label, speedLabel, StringComparison.OrdinalIgnoreCase));
                                    if (spMatch != null)
                                    {
                                        if (speedMpsNullable.HasValue)
                                            spMatch.Value = TimingLabelHelpers.FormatSpeed(speedMpsNullable.Value);
                                        else
                                            spMatch.Value = string.Empty; // clear when speed not yet available
                                    }
                                }
                            }
                            catch { }

                            // Also publish the incrementaltime as a plain string topic for compatibility
                            try
                            {
                                var topic = $"timingdata/{lane}/incrementaltime/string";
                                var payload = TimingLabelHelpers.FormatTimeFromNanosecondsNumeric(computedIncNs);
                                _ = _mqttService.PublishMqtt(topic, payload);
                            }
                            catch (Exception ex)
                            {
                                LocalLog("Failed to publish incremental time string: " + ex.Message);
                            }

                            // Publish the speed as a plain string topic only when available
                            try
                            {
                                if (speedMpsNullable.HasValue)
                                {
                                    var topicSp = $"timingdata/{lane}/speedtrap/string";
                                    var payloadSp = TimingLabelHelpers.FormatSpeed(speedMpsNullable.Value);
                                    _ = _mqttService.PublishMqtt(topicSp, payloadSp);
                                }
                            }
                            catch (Exception ex)
                            {
                                LocalLog("Failed to publish speed string: " + ex.Message);
                            }
                        }
                        catch (Exception ex)
                        {
                            LocalLog("IncrementalTimeComputed handler error: " + ex.Message);
                        }
                    });
                 };
            }
            catch (Exception ex)
            {
                LocalLog("Failed to subscribe to ReactionTimeComputed: " + ex.Message);
            }
            // Attempt to attach an MQTT client using configuration (if MQTTnet is available at runtime)
            try
            {
                var mqttHost = configuration["MQTT:Host"] ?? "127.0.0.1";
                var mqttPort = int.TryParse(configuration["MQTT:Port"], out var p) ? p : 1883;

                // Centralized attach helper: service will try direct API first and fallback to reflection as needed
                try
                {
                    _mqttService.AttachMqttClientFromHost(mqttHost, mqttPort, "PulsarUI");
                    LocalLog("Called AttachMqttClientFromHost on MqttService");
                }
                catch (Exception exAttach)
                {
                    LocalLog("AttachMqttClientFromHost failed: " + exAttach.Message);
                }
            }
            catch (Exception ex)
            {
                MaybeDebug("Failed to attach MQTT client: " + ex.Message);
            }
            // Attempt to attach the InputMapService from the app config path so MQTT lookups can resolve down-track inputs
            try
            {
                // Use the same 'Config' folder casing as the project so the file copied to build output is found on case-sensitive filesystems
                var inputMapPath = Path.Combine(AppContext.BaseDirectory, "Config", "inputmap.json");
                MaybeDebug("MainWindowViewModel: trying inputMapPath='" + inputMapPath + "' exists=" + File.Exists(inputMapPath));
                var ims = new InputMapService(inputMapPath);
                _mqttService.AttachInputMapService(ims);
            }
            catch (Exception ex)
            {
                LocalLog("Failed to attach InputMapService to MqttService: " + ex.Message);
            }
            
            try
            {
                var baseDir = AppContext.BaseDirectory;
                var candidatePaths = new[]
                {
                    Path.Combine(baseDir, "Config", "inputmap.json"),
                    Path.Combine(baseDir, "..", "..", "..", "Config", "inputmap.json"),
                    Path.Combine(baseDir, "..", "..", "Config", "inputmap.json"),
                    Path.Combine(Directory.GetCurrentDirectory(), "Config", "inputmap.json")
                };

                var configPath = candidatePaths.FirstOrDefault(File.Exists)
                                 ?? Path.Combine(baseDir, "Config", "inputmap.json");

                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
                    };

                    var inputs = new List<DownTrackInput>();
                    if (root.TryGetProperty("DownTrackInputs", out var dtiProp))
                        inputs = JsonSerializer.Deserialize<List<DownTrackInput>>(dtiProp.GetRawText(), options) ?? new List<DownTrackInput>();

                    var traps = new List<SpeedTrap>();
                    if (root.TryGetProperty("SpeedTraps", out var stProp))
                        traps = JsonSerializer.Deserialize<List<SpeedTrap>>(stProp.GetRawText(), options) ?? new List<SpeedTrap>();

                    var distanceUnit = configuration["Units:Distance"] ?? "m";
                    var speedUnit = configuration["Units:Speed"] ?? "km/h";

                    // Cache the input map data for later use when refreshing labels based on engaged category
                    _cachedInputs = inputs;
                    _cachedTraps = traps;
                    _cachedDistanceUnit = distanceUnit;
                    _cachedSpeedUnit = speedUnit;

                    // Initialize timing labels with full list (no finish line restriction)
                    RefreshTimingLabels(0);

                }
                else
                {
                    LocalLog("inputmap.json not found in candidate paths; timing labels left empty.");
                }
            }
            catch (Exception ex)
            {
                LocalLog("Failed to load/generate timing labels from inputmap.json: " + ex.Message);
            }
            // Wire up test message handler: surface to ViewModel properties so View can react
            try
            {
                _mqttService.TestMessageReceived += (topic, payload) =>
                {
                    // Marshal onto Avalonia UI thread
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        TestMessageTopic = topic;
                        TestMessagePayload = payload;
                        ShowTestMessagePopup = true;
                    });
                };
            }
            catch (Exception ex)
            {
                LocalLog("Failed to wire TestMessageReceived handler: " + ex.Message);
            }
             
            // Wire RunStartReceived so ViewModel can start a run when the RunStartTrigger role timestamp arrives
            try
            {
                _mqttService.RunStartReceived += (tsNs) =>
                {
                    // Marshal onto Avalonia UI thread
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        // Only start the run if system is engaged
                        if (!SystemEngaged)
                        {
                            MaybeDebug($"RunStartReceived ignored because SystemEngaged==false; tsNs={tsNs}");
                            return;
                        }
             
                        RunActive = true;
                        RunStartTimestampNanoseconds = tsNs;
                        MaybeDebug($"Run started: RunStartTimestampNanoseconds={tsNs}");
                    });
                };
            }
            catch (Exception ex)
            {
                LocalLog("Failed to wire RunStartReceived handler: " + ex.Message);
            }
             
            // Wire LaneNoVehicleChanged so ViewModel can mark Run.Remarks and update Result label
            try
            {
                _mqttService.LaneNoVehicleChanged += (lane, noVehicle) =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            if (EngagedPairModel?.Runs == null) return;
                            int runIndex = string.Equals(lane, "left", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                            var run = EngagedPairModel.Runs.ElementAtOrDefault(runIndex);
                            if (run == null) return;

                            var remarksList = run.Remarks?.ToList() ?? new System.Collections.Generic.List<RunRemarks>();
                            if (noVehicle)
                            {
                                if (!remarksList.Contains(RunRemarks.NoVehicleStaged))
                                {
                                    remarksList.Add(RunRemarks.NoVehicleStaged);
                                    run.Remarks = remarksList.ToArray();
                                }
                            }
                            else
                            {
                                if (remarksList.Contains(RunRemarks.NoVehicleStaged))
                                {
                                    remarksList.Remove(RunRemarks.NoVehicleStaged);
                                    run.Remarks = remarksList.ToArray();
                                }
                            }

                            // Update the dedicated Remarks label (always present) to reflect current remarks for that run
                            var labels = runIndex == 0 ? LeftTimingLabels : RightTimingLabels;
                            if (labels != null && labels.Count > 0)
                            {
                                var resultIndex = labels.ToList().FindIndex(l => string.Equals(l.Label, "Result", StringComparison.OrdinalIgnoreCase));
                                var remarksIndex = resultIndex >= 0 ? resultIndex + 1 : labels.ToList().FindIndex(l => string.Equals(l.Label, "Remarks", StringComparison.OrdinalIgnoreCase));
                                if (remarksIndex >= 0 && remarksIndex < labels.Count)
                                {
                                    var remarksText = (run.Remarks != null && run.Remarks.Length > 0)
                                        ? string.Join(" | ", run.Remarks.Select(r => ((System.Enum)r).GetDisplayName()))
                                        : string.Empty;
                                    labels[remarksIndex].Value = remarksText;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LocalLog("LaneNoVehicleChanged handler error: " + ex.Message);
                        }
                    });
                };
            }
            catch (Exception ex)
            {
                LocalLog("Failed to wire LaneNoVehicleChanged handler: " + ex.Message);
            }

            // Wire LaneDsFoulChanged so ViewModel can mark Run.Remarks for DS fouls
            try
            {
                _mqttService.LaneDsFoulChanged += (lane, dsFoul) =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            if (EngagedPairModel?.Runs == null) return;
                            int runIndex = string.Equals(lane, "left", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                            var run = EngagedPairModel.Runs.ElementAtOrDefault(runIndex);
                            if (run == null) return;

                            var remarksList = run.Remarks?.ToList() ?? new System.Collections.Generic.List<RunRemarks>();
                            if (dsFoul)
                            {
                                if (!remarksList.Contains(RunRemarks.DsFoul))
                                {
                                    remarksList.Add(RunRemarks.DsFoul);
                                    run.Remarks = remarksList.ToArray();
                                }
                            }
                            else
                            {
                                if (remarksList.Contains(RunRemarks.DsFoul))
                                {
                                    remarksList.Remove(RunRemarks.DsFoul);
                                    run.Remarks = remarksList.ToArray();
                                }
                            }

                            // Update the dedicated Remarks label
                            var labels = runIndex == 0 ? LeftTimingLabels : RightTimingLabels;
                            if (labels != null && labels.Count > 0)
                            {
                                var resultIndex = labels.ToList().FindIndex(l => string.Equals(l.Label, "Result", StringComparison.OrdinalIgnoreCase));
                                var remarksIndex = resultIndex >= 0 ? resultIndex + 1 : labels.ToList().FindIndex(l => string.Equals(l.Label, "Remarks", StringComparison.OrdinalIgnoreCase));
                                if (remarksIndex >= 0 && remarksIndex < labels.Count)
                                {
                                    var remarksText = (run.Remarks != null && run.Remarks.Length > 0)
                                        ? string.Join(" | ", run.Remarks.Select(r => ((System.Enum)r).GetDisplayName()))
                                        : string.Empty;
                                    labels[remarksIndex].Value = remarksText;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LocalLog("LaneDsFoulChanged handler error: " + ex.Message);
                        }
                    });
                };
            }
            catch (Exception ex)
            {
                LocalLog("Failed to wire LaneDsFoulChanged handler: " + ex.Message);
            }

            EnterPairQueueCategory = new CategQueueItem
            {
                QueueIndex = 0,
                Category = 1,
                Finish = 0,
                Mode = RunMode.Practice,
                Round = 1,
                LastRound = 0
            };

            EnterPairQueueCategory.PropertyChanged += EnterPairQueueCategoryHandler;
            // Attach handlers for the other two CategQueueItems
            // Ensure Queue/Engage categories remain empty (Category==0) at startup until user queues/engages
            if (QueuePairQueueCategory != null)
            {
                QueuePairQueueCategory.Category = 0;
                // Do not assign null to CategoryDetails (non-nullable). Leave it as-is or replace with a default clone if required.
                QueuePairQueueCategory.PropertyChanged += QueuePairQueueCategoryHandler;
            }
            if (EngagePairQueueCategory != null)
            {
                EngagePairQueueCategory.Category = 0;
                // Do not assign null to CategoryDetails (non-nullable). Leave it as-is or replace with a default clone if required.
                EngagePairQueueCategory.PropertyChanged += EngagePairQueueCategoryHandler;
            }

            EnterPairSelectedCategComboText = CategComboBoxItems?.FirstOrDefault();

            // We've finished initial setup; re-enable category publishes
            _suppressCategPublish = false;

            foreach (var racer in EnterRacers)
                racer.PropertyChanged += EnterPairRacerEntryHandler;
            foreach (var racer in QueuedRacers)
                racer.PropertyChanged += QueuePairRacerEntry_PropertyChanged;

            // Attach collection changed handler for QueuedRacers to maintain derived tree text properties
            QueuedRacers.CollectionChanged += QueuedRacers_CollectionChanged;
            // Ensure handlers for existing items are attached and initial text is set
            for (int i = 0; i < QueuedRacers.Count; i++)
            {
                if (QueuedRacers[i] != null)
                    QueuedRacers[i].PropertyChanged += QueuePairRacer_PropertyChanged;
            }
            UpdateQueuePairTreeTexts();

            // Initialize async commands with actual handlers
            LeftRaceNumConfirmedCommand = new AsyncRelayCommand(async () =>
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

            RightRaceNumConfirmedCommand = new AsyncRelayCommand(async () =>
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

            LeftIndexConfirmedCommand = new AsyncRelayCommand(async () =>
            {
                if (EnterRacers[0] != null)
                    await _mqttService.PubQueueRacersAsync(EnterRacers[0]);
            });

            RightIndexConfirmedCommand = new AsyncRelayCommand(async () =>
            {
                if (EnterRacers[1] != null)
                    await _mqttService.PubQueueRacersAsync(EnterRacers[1]);
            });

            // Capture typed IRelayCommand references generated by the source generator so we can call NotifyCanExecuteChanged without casts
            _queuePairCommandRef = QueuePairCommand as CommunityToolkit.Mvvm.Input.IRelayCommand;
            _engagePairCommandRef = EngagePairCommand as CommunityToolkit.Mvvm.Input.IRelayCommand;
            _clearQueueCommandRef = ClearQueueCommand as CommunityToolkit.Mvvm.Input.IRelayCommand;
            // Capture Reset/Swap command refs so we can update their CanExecute when RunActive changes
            _resetEngageCommandRef = ResetEngagePairCommand as CommunityToolkit.Mvvm.Input.IRelayCommand;
            _swapEngageCommandRef = SwapEngagedPairCommand as CommunityToolkit.Mvvm.Input.IRelayCommand;

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

        // Called when QueuedRacers collection changes so we can attach/detach item handlers and update derived text
        private void QueuedRacers_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (RaceEntry oldR in e.OldItems)
                    oldR.PropertyChanged -= QueuePairRacer_PropertyChanged;
            }
            if (e.NewItems != null)
            {
                foreach (RaceEntry newR in e.NewItems)
                    newR.PropertyChanged += QueuePairRacer_PropertyChanged;
            }
            UpdateQueuePairTreeTexts();
        }

        // Item-level property changed handler — react when Tree changes on a queued racer
        private void QueuePairRacer_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is RaceEntry re)
            {
                if (e.PropertyName == nameof(RaceEntry.Tree) || string.IsNullOrEmpty(e.PropertyName))
                {
                    // If the Tree object changed, we might also want to observe its internal changes (like Name) —
                    // but TreeType does not implement INotifyPropertyChanged. So treat Tree replacement as a full change.
                    UpdateQueuePairTreeTexts();
                }
            }
        }

        // This method computes the textual labels for the queued pair trees and raises property changed notifications
        private void UpdateQueuePairTreeTexts()
        {
            QueuePairLeftTreeText = QueuedRacers.ElementAtOrDefault(0)?.Tree?.Name ?? string.Empty;
            QueuePairRightTreeText = QueuedRacers.ElementAtOrDefault(1)?.Tree?.Name ?? string.Empty;
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
                // Notify RelayCommands so UI updates immediately (use typed refs to avoid repeated casts)
                _queuePairCommandRef?.NotifyCanExecuteChanged();
                _engagePairCommandRef?.NotifyCanExecuteChanged();
                _clearQueueCommandRef?.NotifyCanExecuteChanged();
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

        // When true, EnterPairQueueCategoryHandler will suppress publishing to MQTT so
        // we can make several programmatic changes and then publish once explicitly.
        private bool _suppressCategPublish = false;

        // When true, EnterPairRacerEntryHandler will suppress publishing Tree changes so
        // programmatic resets (ClearAll + set default Tree) don't emit transient nulls.
        private bool _suppressRaceEntryPublish = false;

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
                // Treat EnterPairQueueCategory.Finish as a FinishLine.Id; find matching FinishLine by Id
                var byId = FinishList.FirstOrDefault(f => f != null && f.Id == EnterPairQueueCategory.Finish);
                SelectedFinishLine = byId ?? FinishList.FirstOrDefault();
            }

            // Ensure CategoryDetails is attached when the Category id changes or the categories list is available
            if (Categories != null && Categories.Count > 0)
            {
                var cat = Categories.FirstOrDefault(c => c != null && c.Id == EnterPairQueueCategory.Category);
                if (cat != null)
                {
                    EnterPairQueueCategory.CategoryDetails = cat;
                    FinishLineComboActive = !EnterPairQueueCategory.CategoryDetails.FixedTrack;
                    LeftTreeComboActive = !EnterPairQueueCategory.CategoryDetails.FixedTree;
                    RightTreeComboActive = LeftTreeComboActive && EnterPairQueueCategory.CategoryDetails.SplitTreeAllowed;
                }
            }

            if (IsCategQueueItemDifferent(EnterPairQueueCategory, _lastSentEnterPairQueueCategory))
            {
                // If suppression is active we skip publishing here — a single publish will be done by the caller
                if (!_suppressCategPublish)
                {
                    _ = _mqttService.PubQueueCategAsync(EnterPairQueueCategory);
                    _lastSentEnterPairQueueCategory = EnterPairQueueCategory.Clone(EnterPairQueueCategory.QueueIndex);
                }
            }
        }

        private void QueuePairQueueCategoryHandler(object? sender, PropertyChangedEventArgs e)
        {
            if (QueuePairQueueCategory == null)
                return;
            // Update textual finish and mode for the queued pair based on the stored Finish id
            string finishDesc = string.Empty;

            // If the queued category appears to be the uninitialized/default placeholder (no category selected
            // and finish id is the default 0), don't resolve a FinishLine and leave the UI blank. This avoids
            // showing a real FinishLine that happens to have Id==0 (e.g. "100m") at startup.
            if ((QueuePairQueueCategory.Category == 0 || QueuePairQueueCategory.CategoryDetails == null) && QueuePairQueueCategory.Finish == 0)
            {
                QueuePairFinishText = string.Empty;
            }
            else
            {
                if (FinishList != null && QueuePairQueueCategory != null)
                {
                    var fl = FinishList.FirstOrDefault(f => f != null && f.Id == QueuePairQueueCategory.Finish);
                    if (fl != null) finishDesc = fl.DistanceString ?? string.Empty;
                    else if (QueuePairQueueCategory?.CategoryDetails != null)
                    {
                        var fl2 = FinishList.FirstOrDefault(f => f != null && f.Id == QueuePairQueueCategory.CategoryDetails.Finish);
                        if (fl2 != null) finishDesc = fl2.DistanceString ?? string.Empty;
                    }
                }
                QueuePairFinishText = finishDesc;

                // Debug: publish the finish id values and resolved description so we can trace mismatch
                var dbgMsg = $"QueuePair DEBUG: EnterPairQueueCategory.Finish={EnterPairQueueCategory?.Finish}, SelectedFinishId={SelectedFinishLine?.Id}, CategoryDetails.Finish={EnterPairQueueCategory?.CategoryDetails?.Finish}, ResolvedDesc=\"{QueuePairFinishText}\"";
                MaybeDebug(dbgMsg);
            }

            string modeName = QueuePairQueueCategory != null ? QueuePairQueueCategory.Mode.GetDisplayName() : string.Empty;
            QueuePairModeText = $"{modeName} Round {QueuePairQueueCategory?.Round ?? 0}";

            if (IsCategQueueItemDifferent(QueuePairQueueCategory, _lastSentQueuePairQueueCategory))
            {
                if (QueuePairQueueCategory != null)
                {
                    _ = _mqttService.PubQueueCategAsync(QueuePairQueueCategory);
                    _lastSentQueuePairQueueCategory = QueuePairQueueCategory.Clone(QueuePairQueueCategory.QueueIndex);
                }
            }
        }

        private void EngagePairQueueCategoryHandler(object? sender, PropertyChangedEventArgs e)
        {
            if (EngagePairQueueCategory == null)
                return;
            // Update textual finish and mode for the engaged pair based on stored Finish id
            var finishDesc = string.Empty;
            if (FinishList != null && EngagePairQueueCategory != null)
            {
                var fl = FinishList.FirstOrDefault(f => f != null && f.Id == EngagePairQueueCategory.Finish);
                if (fl != null) finishDesc = fl.DistanceString ?? string.Empty;
                else if (EngagePairQueueCategory?.CategoryDetails != null)
                {
                    var fl2 = FinishList.FirstOrDefault(f => f != null && f.Id == EngagePairQueueCategory.CategoryDetails.Finish);
                    if (fl2 != null) finishDesc = fl2.DistanceString ?? string.Empty;
                }
            }
            EngagePairFinishText = finishDesc;

            // Debug: publish engage finish info
            var dbgMsg2 = $"EngagePair DEBUG: EngagePairQueueCategory.Finish={EngagePairQueueCategory?.Finish}, CategoryDetails.Finish={EngagePairQueueCategory?.CategoryDetails?.Finish}, ResolvedDesc=\"{EngagePairFinishText}\", QueuePairFinishText=\"{QueuePairFinishText}\"";
            MaybeDebug(dbgMsg2);

            string modeName = EngagePairQueueCategory != null ? EngagePairQueueCategory.Mode.GetDisplayName() : string.Empty;
            EngagePairModeText = $"{modeName} Round {EngagePairQueueCategory?.Round ?? 0}";

            if (IsCategQueueItemDifferent(EngagePairQueueCategory, _lastSentEngagePairQueueCategory))
            {
                if (EngagePairQueueCategory != null)
                {
                    _ = _mqttService.PubQueueCategAsync(EngagePairQueueCategory);
                    _lastSentEngagePairQueueCategory = EngagePairQueueCategory.Clone(EngagePairQueueCategory.QueueIndex);
                }
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
                // Notify RelayCommands so UI updates immediately (use typed refs to avoid repeated casts)
                _queuePairCommandRef?.NotifyCanExecuteChanged();
                _engagePairCommandRef?.NotifyCanExecuteChanged();
                _clearQueueCommandRef?.NotifyCanExecuteChanged();
            }
            // Send Tree value over MQTT when it changes
            if (e.PropertyName == nameof(RaceEntry.Tree) && sender is RaceEntry raceEntry)
            {
                // Suppress publishing when we are programmatically resetting entries to avoid
                // emitting intermediate null Tree values. When suppression is active we also
                // skip updating SelectedLeft/Right here; the caller will set SelectedLeft/Right
                // after the reset to the final default Tree.
                if (!_suppressRaceEntryPublish)
                {
                    // Publish the updated RaceEntry, including the new Tree value
                    _ = _mqttService?.PubQueueRacersAsync(raceEntry);
                    // Keep SelectedItem bindings in sync with the RaceEntry.Tree (now a TreeType object)
                    if (TreeList != null)
                    {
                        if (raceEntry.Lane == 0)
                            SelectedLeftTree = raceEntry.Tree ?? SelectedLeftTree;
                        else if (raceEntry.Lane == 1)
                            SelectedRightTree = raceEntry.Tree ?? SelectedRightTree;
                    }
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
                // Capture the current Tree for this lane so we can preserve it if the DB lookup
                // doesn't provide a Tree. This avoids a race where an async DB result clears the Tree.
                TreeType? currentTree = null;
                if (raceEntry != null && TreeList != null)
                {
                    // Try to get the existing value from the current EnterRacers slot if present
                    if (raceEntry.Lane == 0 && EnterRacers.Count > 0) currentTree = EnterRacers[0].Tree;
                    else if (raceEntry.Lane == 1 && EnterRacers.Count > 1) currentTree = EnterRacers[1].Tree;
                }

                // raceEntry is non-null due to the sender check at method entry
                var indexList = await _databaseService.GetIndexListAsync(raceEntry!, EnterPairQueueCategory);
                var indexes = new[]
                {
                    indexList.EventIndex,
                    indexList.PersonalIndex,
                    indexList.ClassIndex,
                    indexList.CategoryIndex
                };
                raceEntry.HandicapIndex = indexes.FirstOrDefault(index => index != "") ?? "00.00";
                raceEntry.ClearDetails();
                raceEntry = await _databaseService.GetRacerDetailsAsync(raceEntry!, EnterPairQueueCategory);
                // If DB didn't include a Tree, restore the previous tree or the category default
                if (raceEntry.Tree == null)
                {
                    raceEntry.Tree = currentTree;
                    if (raceEntry.Tree == null && EnterPairQueueCategory != null && Categories != null && TreeList != null)
                    {
                        var cat = EnterPairQueueCategory.CategoryDetails ?? Categories.FirstOrDefault(c => c != null && c.Id == EnterPairQueueCategory.Category);
                        if (cat != null)
                        {
                            var defaultTree = TreeList.FirstOrDefault(t => t != null && t.Id == cat.TreeType);
                            raceEntry.Tree = defaultTree;
                        }
                    }
                }
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
            if (string.IsNullOrWhiteSpace(EnterPairSelectedCategComboText) || Categories == null || EnterPairQueueCategory == null) return;
            // ComboBox items are formatted as "01 - CategoryName" and the leading two characters are derived from Category.Order (UNIQUE, NOT NULL)
            var text = EnterPairSelectedCategComboText.TrimStart();
            if (text.Length < 2) return;
            if (!int.TryParse(text.Substring(0, 2), out var order)) return;
            var category = Categories.FirstOrDefault(c => c.Order == order);
            if (category == null) return;

            // Suppress automatic category publishes while we update dependent fields (trees/finish)
            _suppressCategPublish = true;

            // Attach details and set numeric category id
            EnterPairQueueCategory.Category = category.Id;
            EnterPairQueueCategory.CategoryDetails = category;
            EnterPairCategText = category.Name;

            // Set Mode/LastRound from category (LastMode stores numeric enum value)
            var lm = category.LastMode;
            if (Enum.IsDefined(typeof(RunMode), lm))
                EnterPairQueueCategory.Mode = (RunMode)lm;
            else
                EnterPairQueueCategory.Mode = RunMode.Practice;
            // Store LastRound on the queue item (don't overwrite current Round unless you prefer)
            EnterPairQueueCategory.LastRound = category.LastRound;

            // Set left/right tree selections to the TreeType associated with the selected category (if available)
            if (TreeList != null && TreeList.Count > 0)
            {
                var treeType = TreeList.FirstOrDefault(t => t != null && t.Id == category.TreeType);
                if (treeType != null)
                {
                    SelectedLeftTree = treeType;
                    SelectedRightTree = treeType;
                }
            }

            // Also set the selected finish line to the FinishLine associated with the selected category (if available)
            if (FinishList != null && FinishList.Count > 0)
            {
                var finishLine = FinishList.FirstOrDefault(f => f != null && f.Id == category.Finish);
                if (finishLine != null)
                {
                    SelectedFinishLine = finishLine;
                }
            }

            // Re-enable publishes and send a single consolidated category message
            _suppressCategPublish = false;
            if (IsCategQueueItemDifferent(EnterPairQueueCategory, _lastSentEnterPairQueueCategory))
            {
                _ = _mqttService.PubQueueCategAsync(EnterPairQueueCategory);
                _lastSentEnterPairQueueCategory = EnterPairQueueCategory.Clone(EnterPairQueueCategory.QueueIndex);
            }
        }

        [RelayCommand]
        private void QueuePair()
        {
            var category = Categories?.Find(c => c?.Id == EnterPairQueueCategory.Category);
            if (category == null) return;
            QueuePairCategText = category.Name;
            // Populate textual finish robustly: prefer SelectedFinishLine, then lookup by EnterPairQueueCategory.Finish as an Id,
            // then fall back to the category's Finish id stored in CategoryDetails.
            string finishDesc = string.Empty;
            if (SelectedFinishLine != null)
            {
                finishDesc = SelectedFinishLine.DistanceString ?? string.Empty;
            }
            else if (FinishList != null && EnterPairQueueCategory != null)
            {
                var fl = FinishList.FirstOrDefault(f => f != null && f.Id == EnterPairQueueCategory.Finish);
                if (fl != null) finishDesc = fl.DistanceString ?? string.Empty;
                else if (EnterPairQueueCategory?.CategoryDetails != null)
                {
                    var fl2 = FinishList.FirstOrDefault(f => f != null && f.Id == EnterPairQueueCategory.CategoryDetails.Finish);
                    if (fl2 != null) finishDesc = fl2.DistanceString ?? string.Empty;
                }
            }

            QueuePairFinishText = finishDesc;

            // Debug: publish the finish id values and resolved description so we can trace mismatch
            var msg = $"QueuePair DEBUG: EnterPairQueueCategory.Finish={EnterPairQueueCategory?.Finish}, SelectedFinishId={SelectedFinishLine?.Id}, CategoryDetails.Finish={EnterPairQueueCategory?.CategoryDetails?.Finish}, ResolvedDesc=\"{QueuePairFinishText}\"";
            MaybeDebug(msg);

            // Null-safe mode text construction to avoid nullable warnings
            string modeName = EnterPairQueueCategory != null ? EnterPairQueueCategory.Mode.GetDisplayName() : string.Empty;

            QueuePairModeText = $"{modeName} Round {EnterPairQueueCategory?.Round ?? 0}";
            // Clone the enter-pair queue item for queued pair and ensure the Finish id is explicit
            if (EnterPairQueueCategory == null) return;
            var queuedClone = EnterPairQueueCategory.Clone(1);
            // The UI's SelectedFinishLine is the authoritative selection for the Enter pair; ensure the queued clone stores that Id
            queuedClone.Finish = SelectedFinishLine?.Id ?? EnterPairQueueCategory.Finish;
            // Also update the EnterPairQueueCategory.Finish so handlers/debugging see a consistent value
            EnterPairQueueCategory.Finish = queuedClone.Finish;
            // Assign the prepared clone so the property setter/handler sees the correct Finish id
            if (queuedClone != null)
                QueuePairQueueCategory = queuedClone;
            EnterPairCategText = category.Name;

            // Suppress RaceEntry Tree publishes while we ClearAll and set defaults
            _suppressRaceEntryPublish = true;

            for (int i = 0; i < EnterRacers.Count; i++)
            {
                QueuedRacers[i] = EnterRacers[i].Clone(1);
                // Temporarily detach handler to prevent async DB lookups reacting to our programmatic ClearAll
                EnterRacers[i].PropertyChanged -= EnterPairRacerEntryHandler;
                EnterRacers[i].ClearAll();

                // Reset the Enter pair Tree for this lane to the category default (if available)
                if (TreeList != null && TreeList.Count > 0 && EnterPairQueueCategory != null && Categories != null)
                {
                    var cat = Categories.Find(c => c != null && c.Id == EnterPairQueueCategory.Category);
                    if (cat != null)
                    {
                        var defaultTree = TreeList.FirstOrDefault(t => t != null && t.Id == cat.TreeType);
                        if (defaultTree != null)
                        {
                            EnterRacers[i].Tree = defaultTree;
                            if (i == 0) SelectedLeftTree = defaultTree;
                            else if (i == 1) SelectedRightTree = defaultTree;
                        }
                    }
                }
                // Reattach the handler after programmatic changes
                EnterRacers[i].PropertyChanged += EnterPairRacerEntryHandler;
            }

            // Re-enable publishes and send consolidated publishes so subscribers only see final state
            _suppressRaceEntryPublish = false;

            // Publish current EnterRacers state (includes Tree defaults) and queued racers
            for (int i = 0; i < EnterRacers.Count; i++)
                _ = _mqttService.PubQueueRacersAsync(EnterRacers[i]);
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
                // Ensure the EnterPairQueueCategory carries the selected Finish id before we clone it for engagement
                EnterPairQueueCategory.Finish = SelectedFinishLine?.Id ?? EnterPairQueueCategory.Finish;
                EngagePairQueueCategory = EnterPairQueueCategory.Clone(2);

                // Suppress RaceEntry Tree publishes while we ClearAll and set defaults for EnterRacers
                _suppressRaceEntryPublish = true;
                for (int i = 0; i < EnterRacers.Count; i++)
                {
                    // Detach handler, clear and reattach after default assignment
                    EnterRacers[i].PropertyChanged -= EnterPairRacerEntryHandler;
                    EnterRacers[i].ClearAll();
                }

                // After clearing, set defaults below (still suppressed until after this block)
            }
            if (category == null) return;

            // Compute and set the textual Finish and Mode for the engaged pair so UI updates appropriately
            string engageFinishDesc = string.Empty;
            if (EngagePairQueueCategory != null)
            {
                // Prefer a direct lookup by the queue category's Finish id
                if (FinishList != null)
                {
                    var fl = FinishList.FirstOrDefault(f => f != null && f.Id == EngagePairQueueCategory.Finish);
                    if (fl != null) engageFinishDesc = fl.DistanceString ?? string.Empty;
                }

                // If still empty, prefer CategoryDetails' Finish id
                if (string.IsNullOrEmpty(engageFinishDesc) && EngagePairQueueCategory.CategoryDetails != null && FinishList != null)
                {
                    var fl2 = FinishList.FirstOrDefault(f => f != null && f.Id == EngagePairQueueCategory.CategoryDetails.Finish);
                    if (fl2 != null) engageFinishDesc = fl2.DistanceString ?? string.Empty;
                }

                // Finally fall back to previously computed QueuePairFinishText when engaging from queue
                if (string.IsNullOrEmpty(engageFinishDesc) && queueHasEntries)
                {
                    engageFinishDesc = QueuePairFinishText ?? string.Empty;
                }
            }
            EngagePairFinishText = engageFinishDesc;

            // Debug: publish engage finish info
            var msg2 = $"EngagePair DEBUG: EngagePairQueueCategory.Finish={EngagePairQueueCategory?.Finish}, CategoryDetails.Finish={EngagePairQueueCategory?.CategoryDetails?.Finish}, ResolvedDesc=\"{EngagePairFinishText}\", QueuePairFinishText=\"{QueuePairFinishText}\"";
            MaybeDebug(msg2);

            // Set EngagePairModeText similarly to QueuePairModeText so UI shows the engaged mode/round
            string modeName = EngagePairQueueCategory != null ? EngagePairQueueCategory.Mode.GetDisplayName() : string.Empty;
            EngagePairModeText = $"{modeName} Round {EngagePairQueueCategory?.Round ?? 0}";

            // After engaging, only reset the Enter Pair UI lanes to the EnterPair category default
            // when we engaged from the Enter pair (i.e., no queued entries). Do not clear EnterRacers
            // when the user engaged a queued pair — preserve the Enter pair inputs.
            if (!queueHasEntries)
            {
                if (EnterPairQueueCategory != null && Categories != null && TreeList != null && TreeList.Count > 0)
                {
                    var enterCat = EnterPairQueueCategory.CategoryDetails ?? Categories.FirstOrDefault(c => c != null && c.Id == EnterPairQueueCategory.Category);
                    if (enterCat != null)
                    {
                        var defaultTree = TreeList.FirstOrDefault(t => t != null && t.Id == enterCat.TreeType);
                        for (int i = 0; i < EnterRacers.Count; i++)
                        {
                            // Clear textual details and reset Tree to default for the Enter pair lanes
                            // Ensure we operate on an object that won't fire handlers while we change it
                            EnterRacers[i].PropertyChanged -= EnterPairRacerEntryHandler;
                            EnterRacers[i].ClearAll();
                            if (defaultTree != null)
                            {
                                EnterRacers[i].Tree = defaultTree;
                                if (i == 0) SelectedLeftTree = defaultTree;
                                else if (i == 1) SelectedRightTree = defaultTree;
                            }
                            EnterRacers[i].PropertyChanged += EnterPairRacerEntryHandler;
                        }
                    }
                }

                // Re-enable publishes and emit consolidated EnterRacers publishes so subscribers only see final state
                _suppressRaceEntryPublish = false;
                if (EnterPairQueueCategory != null)
                {
                    for (int i = 0; i < EnterRacers.Count; i++)
                        _ = _mqttService.PubQueueRacersAsync(EnterRacers[i]);
                }
            }

            EngagePairCategText = category.Name; 
            EngagePairStartModeText = category.StartMode switch
            {
                0 => "Remote (Console) Start",
                1 => "Remote/Console",
                2 => "Console",
                3 =>
                    $"Auto ({category.AutoStartTimeout}, {Convert.ToDecimal(category.AutoStartStageToStart) / 1000:N1})",
                _ => "Unknown Start Mode"
            };
            SystemEngaged = true;
            // Also publish an engaged Pair payload (include GUID and runs) so runqueue consumers see a single object
            try
            {
                var p = new Pair { };
                p.Id = Guid.NewGuid();
                // Resolve category similar to OnSystemEngagedChanged
                Category? resolvedCat = null;
                if (EngagePairQueueCategory?.CategoryDetails != null && EngagePairQueueCategory.CategoryDetails.Id != 0)
                    resolvedCat = EngagePairQueueCategory.CategoryDetails;
                else if (Categories != null)
                    resolvedCat = Categories.FirstOrDefault(c => c != null && c.Id == EngagePairQueueCategory.Category);
                p.Category = resolvedCat;
                p.RunMode = EngagePairQueueCategory?.Mode;
                // Ensure EngagedRacers has two entries
                var left = EngagedRacers.ElementAtOrDefault(0) ?? new RaceEntry { Lane = 0, QueueIndex = 2 };
                var right = EngagedRacers.ElementAtOrDefault(1) ?? new RaceEntry { Lane = 1, QueueIndex = 2 };
                var leftRun = new Run { Entry = left.Clone(2) };
                var rightRun = new Run { Entry = right.Clone(2) };
                p.Runs = new Run[] { leftRun, rightRun };
                // Populate expected reaction times for the created runs
                PulsarUI.Services.TimingHelpers.PopulateExpectedReactionTimes(p);
                // Serialize and publish
                var options = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
                options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
                // Register DateTime converters so RunStartTime serializes with our desired format
                options.Converters.Add(new PulsarUI.Services.JsonConverters.DateTimeUtcConverter());
                options.Converters.Add(new PulsarUI.Services.JsonConverters.NullableDateTimeUtcConverter());
                var minimal = new
                {
                    id = p.Id,
                    categoryId = p.Category?.Id,
                    runMode = p.RunMode?.ToString(),
                    runs = p.Runs?.Select(r => new { lane = r.Entry?.Lane, queueIndex = r.Entry?.QueueIndex, raceNumber = r.Entry?.RaceNumber, treeId = r.Entry?.Tree?.Id }).ToArray()
                };
                var payload = System.Text.Json.JsonSerializer.Serialize(minimal, options);
                if (_mqttService != null) _ = _mqttService.PublishMqtt("runqueue/engagedpair/pair", payload);

                // Publish full Pair to debug topic so subscribers can see complete Pair including GUID and computed ReactionTimes
                try
                {
                    if (_mqttService != null)
                    {
                        var full = System.Text.Json.JsonSerializer.Serialize(p, options);
                        _ = _mqttService.PublishMqtt("pulsarui/engagedpair/full", full);
                    }
                }
                catch (Exception ex)
                {
                    LocalLog("EngagedPair debug publish failed: " + ex.Message);
                }
            }
            catch (Exception ex)
            {
                MaybeDebug("EngagePair immediate publish failed: " + ex.Message);
            }
             UpdateButtonStatuses();
        }

        [RelayCommand]
        private void ResetEngagePair()
        {
            SystemEngaged = false;
            _mqttService.ResetSystemAsync();
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
        private void SetupEnable()
        {
            SetupActive = !SetupActive;
            SetupInactive = !SetupActive;

            if (SetupActive)
            {
                
            }
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
            {
                racer.PropertyChanged -= EnterPairRacerEntryHandler;
                racer.ClearAll();
            }
            // After clearing, ensure each Enter lane's Tree is set to the selected category default (if available)
            if (EnterPairQueueCategory != null && Categories != null && TreeList != null && TreeList.Count > 0)
            {
                var enterCat = EnterPairQueueCategory.CategoryDetails ?? Categories.FirstOrDefault(c => c != null && c.Id == EnterPairQueueCategory.Category);
                var defaultTree = enterCat != null ? TreeList.FirstOrDefault(t => t != null && t.Id == enterCat.TreeType) : null;
                for (int i = 0; i < EnterRacers.Count; i++)
                {
                    // Clear textual fields already done; assign default Tree
                    if (defaultTree != null)
                    {
                        EnterRacers[i].Tree = defaultTree;
                        if (i == 0) SelectedLeftTree = defaultTree;
                        else if (i == 1) SelectedRightTree = defaultTree;
                    }
                    // Reattach handler after programmatic edit
                    EnterRacers[i].PropertyChanged += EnterPairRacerEntryHandler;
                    await _mqttService.PubQueueRacersAsync(EnterRacers[i]);
                }
            }
            else
            {
                // No default available, still publish cleared EnterRacers so subscribers know lanes are cleared
                for (int i = 0; i < EnterRacers.Count; i++)
                {
                    EnterRacers[i].PropertyChanged += EnterPairRacerEntryHandler;
                    await _mqttService.PubQueueRacersAsync(EnterRacers[i]);
                }
            }
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
            // Load categories (DB returns a non-null List<Category>)
            Categories = await _databaseService.GetCategoryListAsync();

            // Build the combo box items from the loaded categories
            CategComboBoxItems = Categories
                .Select(c => $"{c.Order.ToString("D2")} - {c.Name}")
                .ToList();

            // Prefer selecting category with Order == 1 (displayed as "01 - Name") on startup if present
            try
            {
                var firstOrder1 = CategComboBoxItems.FirstOrDefault(i => i != null && i.StartsWith("01 -"));
                if (!string.IsNullOrWhiteSpace(firstOrder1))
                    EnterPairSelectedCategComboText = firstOrder1;
            }
            catch (Exception ex)
            {
                LocalLog("CategComboBoxItems firstOrder1 lookup failed: " + ex.Message);
            }

            // Ensure the selected combo text is initialized now that the category items exist
            // Prefer selecting startup category by the 2-digit Order parsed from EnterPairSelectedCategComboText
            Category? startupCategory = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(EnterPairSelectedCategComboText) && EnterPairSelectedCategComboText.Trim().Length >= 2)
                {
                    var t = EnterPairSelectedCategComboText.Trim();
                    if (int.TryParse(t.Substring(0, 2), out var parsedOrder))
                    {
                        startupCategory = Categories.FirstOrDefault(c => c != null && c.Order == parsedOrder);
                    }
                }
            }
            catch (Exception) { /* ignore parse lookup failures */ }

            // Fallback: prefer matching Category.Id if no Order match, else first category
            if (startupCategory == null)
                startupCategory = Categories.FirstOrDefault(c => c != null && c.Id == EnterPairQueueCategory?.Category) ?? Categories.FirstOrDefault();
            if (startupCategory != null)
            {
                EnterPairQueueCategory.CategoryDetails = startupCategory;
                EnterPairQueueCategory.Category = startupCategory.Id;
                EnterPairQueueCategory.Finish = startupCategory.Finish;
                EnterPairCategText = startupCategory.Name;
                EnterPairSelectedCategComboText = $"{startupCategory.Order.ToString("D2")} - {startupCategory.Name}";

                // If finish lines are already loaded, ensure SelectedFinishLine matches the category's finish id
                if (FinishList != null && FinishList.Count > 0)
                {
                    var finishById = FinishList.FirstOrDefault(f => f != null && f.Id == startupCategory.Finish);
                    if (finishById != null)
                        SelectedFinishLine = finishById;
                }
            }
            else
            {
                EnterPairSelectedCategComboText = CategComboBoxItems.FirstOrDefault();
            }

            // Publish/log a small startup message for diagnostics
            var loadedMsg = $"Loaded {Categories.Count} categories; startupCategoryId={EnterPairQueueCategory?.Category}; selectedFinishId={SelectedFinishLine?.Id}";
            MaybeDebug(loadedMsg);

            InitializeStartupCategoryAndFinish();
        }

        private async Task LoadTreeTypesAsync()
        {
            TreeList = await _databaseService.GetTreeTypesAsync();
            // Initialize SelectedLeftTree/SelectedRightTree from existing EnterRacers' Tree values
            if (TreeList != null && TreeList.Count > 0)
            {
                // If a category is already selected and has a TreeType, prefer that
                if (EnterPairQueueCategory?.CategoryDetails != null)
                {
                    var ct = EnterPairQueueCategory.CategoryDetails.TreeType;
                    var treeById = TreeList.FirstOrDefault(t => t.Id == ct);
                    if (treeById != null)
                    {
                        SelectedLeftTree = treeById;
                        SelectedRightTree = treeById;
                        return;
                    }
                }

                // If EnterRacers already have Tree values (TreeType), use them; otherwise pick sensible defaults
                if (EnterRacers.Count > 0 && EnterRacers[0].Tree != null)
                    SelectedLeftTree = EnterRacers[0].Tree;
                else
                    SelectedLeftTree = TreeList.FirstOrDefault();

                if (EnterRacers.Count > 1 && EnterRacers[1].Tree != null)
                    SelectedRightTree = EnterRacers[1].Tree;
                else
                    SelectedRightTree = TreeList.ElementAtOrDefault(1) ?? TreeList.FirstOrDefault();
            }
        }

        private async Task LoadFinishLinesAsync()
        {
            FinishList = await _databaseService.GetFinishLinesAsync();
            // Initialize SelectedFinishLine from EnterPairQueueCategory's Finish index or CategoryDetails
            if (FinishList != null && FinishList.Count > 0)
            {
                // Compatibility migration: some persisted/legacy values may have stored Finish as a list index
                // rather than the canonical FinishLine.Id. Detect that case and convert to the correct Id.
                int MapToFinishId(int storedValue)
                {
                    if (FinishList.Any(f => f.Id == storedValue))
                        return storedValue; // already an Id
                    if (storedValue >= 0 && storedValue < FinishList.Count)
                        return FinishList[storedValue].Id; // treat as index -> map to Id
                    return storedValue; // leave as-is if it doesn't match either
                }

                // Apply migration to any queued/engaged CategQueueItems so UI/logic uses Ids consistently
                try
                {
                    if (EnterPairQueueCategory != null)
                    {
                        var before = EnterPairQueueCategory.Finish;
                        var after = MapToFinishId(before);
                        if (before != after)
                        {
                            EnterPairQueueCategory.Finish = after;
                            if (_enableDebugPublish)
                                await _mqttService.PublishMqtt("pulsarui/debug", $"MIGRATE: EnterPairQueueCategory.Finish {before} -> {after}");
                        }
                    }
                    if (QueuePairQueueCategory != null)
                    {
                        var before = QueuePairQueueCategory.Finish;
                        var after = MapToFinishId(before);
                        if (before != after)
                        {
                            QueuePairQueueCategory.Finish = after;
                            if (_enableDebugPublish)
                                await _mqttService.PublishMqtt("pulsarui/debug", $"MIGRATE: QueuePairQueueCategory.Finish {before} -> {after}");
                        }
                    }
                    if (EngagePairQueueCategory != null)
                    {
                        var before = EngagePairQueueCategory.Finish;
                        var after = MapToFinishId(before);
                        if (before != after)
                        {
                            EngagePairQueueCategory.Finish = after;
                            if (_enableDebugPublish)
                                await _mqttService.PublishMqtt("pulsarui/debug", $"MIGRATE: EngagePairQueueCategory.Finish {before} -> {after}");
                        }
                    }
                }
                catch (Exception) { /* best-effort migration; swallow errors to avoid blocking startup */ }

                // If a category is already selected and has a Finish id, prefer that
                if (EnterPairQueueCategory?.CategoryDetails != null)
                {
                    var fId = EnterPairQueueCategory.CategoryDetails.Finish;
                    var finishById = FinishList.FirstOrDefault(f => f.Id == fId);
                    if (finishById != null)
                    {
                        SelectedFinishLine = finishById;
                        // Refresh any derived textual bindings that rely on FinishList
                        QueuePairQueueCategoryHandler(QueuePairQueueCategory, new PropertyChangedEventArgs(""));
                        EngagePairQueueCategoryHandler(EngagePairQueueCategory, new PropertyChangedEventArgs(""));

                        // Debug: publish finish list and initial mapping
                        try
                        {
                            var listSummary = string.Join(", ", FinishList.Select(f => $"{f.Id}:{f.DistanceString}"));
                            var initialMapping = $"FinishList={listSummary}; EnterPairQueueCategory.Finish={EnterPairQueueCategory?.Finish}; SelectedFinishLineId={SelectedFinishLine?.Id}";
                            MaybeDebug(initialMapping);
                        }
                        catch (Exception) { /* ignore logging errors */ }

                        return;
                    }
                }

                // If EnterPairQueueCategory.Finish refers to a FinishLine.Id, find by Id
                if (EnterPairQueueCategory != null)
                {
                    var findById = FinishList.FirstOrDefault(f => f.Id == EnterPairQueueCategory.Finish);
                    if (findById != null)
                    {
                        SelectedFinishLine = findById;
                        QueuePairQueueCategoryHandler(QueuePairQueueCategory, new PropertyChangedEventArgs(""));
                        EngagePairQueueCategoryHandler(EngagePairQueueCategory, new PropertyChangedEventArgs(""));

                        try
                        {
                            var listSummary = string.Join(", ", FinishList.Select(f => $"{f.Id}:{f.DistanceString}"));
                            var initialMapping = $"FinishList={listSummary}; EnterPairQueueCategory.Finish={EnterPairQueueCategory.Finish}; SelectedFinishLineId={SelectedFinishLine?.Id}";
                            MaybeDebug(initialMapping);
                        }
                        catch (Exception) { /* ignore logging errors */ }

                        return;
                    }
                }
                SelectedFinishLine = FinishList.FirstOrDefault();

                try
                {
                    var listSummary = string.Join(", ", FinishList.Select(f => $"{f.Id}:{f.DistanceString}"));
                    var initialMapping = $"FinishList={listSummary}; EnterPairQueueCategory.Finish={EnterPairQueueCategory?.Finish}; SelectedFinishLineId={SelectedFinishLine?.Id}";
                    MaybeDebug(initialMapping);
                }
                catch (Exception) { /* ignore logging errors */ }

                QueuePairQueueCategoryHandler(QueuePairQueueCategory, new PropertyChangedEventArgs(""));
                EngagePairQueueCategoryHandler(EngagePairQueueCategory, new PropertyChangedEventArgs(""));
                // In case categories finished loading earlier/later, ensure startup selection is initialized
                InitializeStartupCategoryAndFinish();
            }
        }

        private void InitializeStartupCategoryAndFinish()
        {
            try
            {
                if (Categories == null || Categories.Count == 0) return;
                if (EnterPairQueueCategory == null) return;

                // Prefer selecting startup category by the two-digit Order parsed from EnterPairSelectedCategComboText,
                // then fall back to the existing numeric Category id, then the first category.
                Category? startupCategory = null;
                try
                {
                    if (!string.IsNullOrWhiteSpace(EnterPairSelectedCategComboText) && EnterPairSelectedCategComboText.Trim().Length >= 2)
                    {
                        var t = EnterPairSelectedCategComboText.Trim();
                        if (int.TryParse(t.Substring(0, 2), out var parsedOrder))
                            startupCategory = Categories.FirstOrDefault(c => c != null && c.Order == parsedOrder);
                    }
                }
                catch (Exception) { /* ignore parse lookup failures */ }

                if (startupCategory == null)
                    startupCategory = Categories.FirstOrDefault(c => c != null && c.Id == EnterPairQueueCategory.Category) ?? Categories.FirstOrDefault();
                if (startupCategory != null)
                {
                    EnterPairQueueCategory.CategoryDetails = startupCategory;
                    EnterPairQueueCategory.Category = startupCategory.Id;
                    // Ensure the queue item's Finish id matches the category's configured finish
                    EnterPairQueueCategory.Finish = startupCategory.Finish;
                    EnterPairCategText = startupCategory.Name;
                    EnterPairSelectedCategComboText = $"{startupCategory.Order.ToString("D2")} - {startupCategory.Name}";

                    // If finish lines are loaded, ensure SelectedFinishLine matches the category's finish id
                    if (FinishList != null && FinishList.Count > 0)
                    {
                        var finishById = FinishList.FirstOrDefault(f => f != null && f.Id == startupCategory.Finish);
                        if (finishById != null)
                            SelectedFinishLine = finishById;
                    }

                    // Refresh derived textual displays
                    QueuePairQueueCategoryHandler(QueuePairQueueCategory, new PropertyChangedEventArgs(""));
                    EngagePairQueueCategoryHandler(EngagePairQueueCategory, new PropertyChangedEventArgs(""));

                    var msg = $"Initialized startupCategoryId={EnterPairQueueCategory.Category}; selectedFinishId={SelectedFinishLine?.Id}";
                    MaybeDebug(msg);
                }
            }
            catch (Exception ex)
            {
                LocalLog($"InitializeStartupCategoryAndFinish error: {ex.Message}");
            }
        }

        // Centralized helper - only publishes/logs when debug flags are enabled
        private void MaybeDebug(string msg)
        {
            if (_enableLocalLog)
            {
                try { File.AppendAllText("/tmp/pulsarui_debug.log", DateTime.Now.ToString("o") + " " + msg + "\n"); } catch (Exception) { /* ignore */ }
            }
            if (_enableDebugPublish && _mqttService != null)
            {
                try { _ = _mqttService.PublishMqtt("pulsarui/debug", msg); } catch (Exception) { if (_enableLocalLog) { MaybeDebug("PublishMqtt failed"); } }
            }
        }

        // Local logging helper used throughout ViewModel
        private void LocalLog(string msg)
        {
            if (!_enableLocalLog) return;
            try
            {
                File.AppendAllText("/tmp/pulsarui_debug.log", DateTime.Now.ToString("o") + " " + msg + "\n");
            }
            catch { }
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

        // F3 (Reset) enabled when system is engaged and not currently running
        public bool IsF3Enabled => SystemEngaged && !RunActive;

        // F4 (Swap) enabled when system is engaged and not currently running
        public bool IsF4Enabled => SystemEngaged && !RunActive;


        // Centralized helper to update all button-related bindings
        private void UpdateButtonStatuses()
        {
            OnPropertyChanged(nameof(IsF5Enabled));
            OnPropertyChanged(nameof(IsF10Enabled));
            OnPropertyChanged(nameof(IsF11Enabled));
            OnPropertyChanged(nameof(IsF3Enabled));
            OnPropertyChanged(nameof(IsF4Enabled));

            // Notify generated command refs so UI CanExecute is re-evaluated
            try { _resetEngageCommandRef?.NotifyCanExecuteChanged(); } catch { }
            try { _swapEngageCommandRef?.NotifyCanExecuteChanged(); } catch { }
            try { _queuePairCommandRef?.NotifyCanExecuteChanged(); } catch { }
            try { _engagePairCommandRef?.NotifyCanExecuteChanged(); } catch { }
            try { _clearQueueCommandRef?.NotifyCanExecuteChanged(); } catch { }
        }

        // CanExecute helpers used by the CommunityToolkit source generator for the generated commands
        // Naming convention: Can<MethodName>() will be discovered and used as the CanExecute predicate.
        private bool CanResetEngagePair()
        {
            return SystemEngaged && !RunActive;
        }

        private bool CanSwapEngagedPair()
        {
            return SystemEngaged && !RunActive;
        }

        // Refresh timing labels based on engaged category's finish line distance.
        // If finishLineMm > 0, only labels up to that distance are displayed (before Result).
        // If finishLineMm == 0, the full list of labels is displayed.
        private void RefreshTimingLabels(int finishLineMm)
        {
            if (_cachedInputs == null || _cachedDistanceUnit == null || _cachedSpeedUnit == null)
                return;

            var labelsPerLane = TimingLabelHelpers.GenerateTimingLabels(
                _cachedInputs,
                _cachedTraps,
                _cachedDistanceUnit,
                _cachedSpeedUnit,
                finishLineMm);

            if (labelsPerLane.TryGetValue(InputLane.Left, out var leftLabels))
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    LeftTimingLabels.Clear();
                    foreach (var l in leftLabels)
                        LeftTimingLabels.Add(l);
                    // Ensure a dedicated Remarks label exists immediately after Result and remains visible
                    var idx = LeftTimingLabels.ToList().FindIndex(x => string.Equals(x.Label, "Result", StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0)
                    {
                        var next = idx + 1;
                        if (!(next < LeftTimingLabels.Count && string.Equals(LeftTimingLabels[next].Label, "Remarks", StringComparison.OrdinalIgnoreCase)))
                        {
                            LeftTimingLabels.Insert(next, new TimingLabelItem { Label = "Remarks", Value = string.Empty });
                        }
                    }
                });
            }

            if (labelsPerLane.TryGetValue(InputLane.Right, out var rightLabels))
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    RightTimingLabels.Clear();
                    foreach (var l in rightLabels)
                        RightTimingLabels.Add(l);
                    // Ensure a dedicated Remarks label exists immediately after Result and remains visible
                    var idx = RightTimingLabels.ToList().FindIndex(x => string.Equals(x.Label, "Result", StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0)
                    {
                        var next = idx + 1;
                        if (!(next < RightTimingLabels.Count && string.Equals(RightTimingLabels[next].Label, "Remarks", StringComparison.OrdinalIgnoreCase)))
                        {
                            RightTimingLabels.Insert(next, new TimingLabelItem { Label = "Remarks", Value = string.Empty });
                        }
                    }
                });
            }
        }
    }
}


