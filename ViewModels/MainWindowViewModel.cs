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
    public partial class MainWindowViewModel : ObservableObject
    {
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

        // --- Live System Status panel state ---
        // Status strings match SensorIcon style class names: "OK", "Blocked", "Error", "OKInactive".
        private const string StatusOk = "OK";
        private const string StatusBlocked = "Blocked";
        private const string StatusError = "Error";
        private const string StatusInactive = "OKInactive";

        [ObservableProperty] private string _leftPreStageStatus = StatusBlocked;
        [ObservableProperty] private string _rightPreStageStatus = StatusBlocked;
        [ObservableProperty] private string _leftStageLockStatus = StatusBlocked;
        [ObservableProperty] private string _rightStageLockStatus = StatusBlocked;
        [ObservableProperty] private string _leftStageStatus = StatusBlocked;
        [ObservableProperty] private string _rightStageStatus = StatusBlocked;
        [ObservableProperty] private string _leftGuardAStatus = StatusError;
        [ObservableProperty] private string _rightGuardAStatus = StatusError;
        [ObservableProperty] private string _leftGuardBStatus = StatusError;
        [ObservableProperty] private string _rightGuardBStatus = StatusError;

        public ObservableCollection<DownTrackStatusRow> DownTrackStatusRows { get; } = new();
        private readonly Dictionary<int, DownTrackStatusRow> _downTrackRowsByDistance = new();
        // Records whether a given role participates in live status tracking (false = disabled in inputmap.json).
        private readonly Dictionary<InputRole, bool> _roleEnabled = new();
        private InputMapService? _inputMapService;

        // Enable/disable debug publishing and local logging (toggle while diagnosing)
        // Read from environment variables so debugging can be turned on without changing code repeatedly.
        // Set PULSAR_ENABLE_DEBUG=1 to enable VM debug messages (writes to /tmp/pulsarui_debug.log via LocalLog)
        // Set PULSAR_LOCAL_LOG=1 to enable input-map service local logging (writes to /tmp/pulsarui_config.log)
        private readonly bool _enableDebugPublish = string.Equals(Environment.GetEnvironmentVariable("PULSAR_ENABLE_DEBUG"), "1");
        private readonly bool _enableLocalLog = string.Equals(Environment.GetEnvironmentVariable("PULSAR_LOCAL_LOG"), "1");

        private string? _lastConfirmedRaceNum;
        [ObservableProperty]
        private string? _currentTime;
        
        public IEnumerable<RunMode> RunModes { get; } = Enum.GetValues(typeof(RunMode)).Cast<RunMode>();

        // Provide async commands so we avoid "async void" lambdas and analyzer warnings; initialized in constructor
        // Initialize with no-op AsyncRelayCommand so they're safe in design-time constructor paths
        public CommunityToolkit.Mvvm.Input.IAsyncRelayCommand LeftRaceNumConfirmedCommand { get; private set; } = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => Task.CompletedTask);
        public CommunityToolkit.Mvvm.Input.IAsyncRelayCommand RightRaceNumConfirmedCommand { get; private set; } = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => Task.CompletedTask);
        public CommunityToolkit.Mvvm.Input.IAsyncRelayCommand LeftIndexConfirmedCommand { get; private set; } = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => Task.CompletedTask);
        public CommunityToolkit.Mvvm.Input.IAsyncRelayCommand RightIndexConfirmedCommand { get; private set; } = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => Task.CompletedTask);
        public CommunityToolkit.Mvvm.Input.IAsyncRelayCommand? _startCommandRef;

        [GeneratedRegex(@"^(\d{0,2}(\.\d{0,2})?|\.?\d{0,2})?$")]

        private static partial Regex IndexPatternRegex();

        [ObservableProperty]
        private bool _systemEngaged;
        partial void OnSystemEngagedChanged(bool value)
        {
            UpdateButtonStatuses();

            if (value)
            {
                // Avoid recreating if already present
                if (EngagedPairModel != null)
                    return;

                // A new system/pair is being engaged for a fresh run attempt: clear any stale
                // per-lane "no vehicle" state left over from a previous attempt so it can't leak
                // into this one.
                try
                {
                    _mqttService?.ResetLaneNoVehicleFlags();
                }
                catch (Exception ex)
                {
                    LocalLog("ResetLaneNoVehicleFlags failed: " + ex.Message);
                }

                var p = new Pair { Id = Guid.NewGuid() };

                // A fresh pair is being engaged - drop any DB identity left over from the
                // previous run so persistence calls for the new run start from a clean slate.
                _currentRunPairId = null;
                _currentRunIndvIds[0] = null;
                _currentRunIndvIds[1] = null;

                // Resolve category for the engaged pair. Prefer CategoryDetails if populated.
                Category? resolvedCat = EngagePairQueueCategory?.CategoryDetails?.Id != 0
                    ? EngagePairQueueCategory.CategoryDetails
                    : (Categories != null && EngagePairQueueCategory != null ? Categories.FirstOrDefault(c => c != null && c.Id == EngagePairQueueCategory.Category) : null);

                p.Category = resolvedCat;
                p.RunMode = EngagePairQueueCategory?.Mode;

                // Create left/right runs from EngagedRacers (clone to decouple UI instances)
                var leftEntry = EngagedRacers.ElementAtOrDefault(0)?.Clone(2) ?? new RaceEntry { QueueIndex = 2, Lane = 0 };
                var rightEntry = EngagedRacers.ElementAtOrDefault(1)?.Clone(2) ?? new RaceEntry { QueueIndex = 2, Lane = 1 };
                p.Runs = new[] { new Run { Entry = leftEntry }, new Run { Entry = rightEntry } };

                // Populate expected reaction times
                PulsarUI.Services.TimingHelpers.PopulateExpectedReactionTimes(p);

                // A fresh pair is being engaged - drop any snapshot kept around for Clear (F9)
                // from a previous pair so it can't leak into this one.
                _lastEngagedPairModel = null;

                EngagedPairModel = p;
                MaybeDebug($"Created EngagedPair {p.Id} CategoryId={p.Category?.Id} Mode={p.RunMode}");

                // A fresh pair starts with empty Remarks/Results on both lanes; clear the
                // last-published MQTT cache so this run's own remarks/results (even if they
                // happen to match the previous run's, e.g. another "Winner") are published
                // rather than being suppressed as "unchanged" from the prior run.
                _lastPublishedResults.Clear();
                _lastPublishedRemarks.Clear();

                // Refresh timing labels to match engaged pair finish line (if configured)
                if (EngagePairQueueCategory != null && FinishList != null)
                {
                    var finishLine = FinishList.FirstOrDefault(f => f != null && f.Id == EngagePairQueueCategory.Finish);
                    if (finishLine != null) RefreshTimingLabels(finishLine.Distance);
                }

                // Build serialization options shared by both publishes
                var options = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
                options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
                options.Converters.Add(new PulsarUI.Services.JsonConverters.DateTimeUtcConverter());
                options.Converters.Add(new PulsarUI.Services.JsonConverters.NullableDateTimeUtcConverter());

                // Publish minimal runqueue payload
                try
                {
                    var minimal = new
                    {
                        id = p.Id,
                        categoryId = p.Category?.Id,
                        runMode = p.RunMode?.ToString(),
                        runs = p.Runs?.Select(r => new { lane = r.Entry?.Lane, queueIndex = r.Entry?.QueueIndex, raceNumber = r.Entry?.RaceNumber, treeId = r.Entry?.Tree?.Id }).ToArray()
                    };
                    var payload = System.Text.Json.JsonSerializer.Serialize(minimal, options);
                    if (_mqttService != null) _ = _mqttService.PublishMqtt("runqueue/engagedpair/pair", payload);

                    // Additionally publish the full Pair to debug topic with finish metadata
                    int finishIndex = EngagePairQueueCategory?.Finish ?? 0;
                    int finishDistanceMm = 0;
                    if (FinishList != null && finishIndex != 0)
                    {
                        var fl = FinishList.FirstOrDefault(f => f != null && f.Id == finishIndex);
                        if (fl != null) finishDistanceMm = fl.Distance;
                    }
                    var fullObj = new { pair = p, finish = new { id = finishIndex, distanceMm = finishDistanceMm }, speedUnit = AppSettings.SpeedUnit };
                    var full = System.Text.Json.JsonSerializer.Serialize(fullObj, options);
                    if (_mqttService != null) _ = _mqttService.PublishMqtt("pulsarui/engagedpair/full", full);
                }
                catch (Exception ex)
                {
                    MaybeDebug("EngagedPair debug publish failed: " + ex.Message);
                }
            }
            else
            {
                // Keep a snapshot of the just-disengaged pair so CanClear()/Clear() (F9) can
                // still see/reset its lane details afterwards - SystemEngaged is set false as
                // part of normal run completion (see MaybeCheckAndCompleteRun), at which point
                // EngagedPairModel is nulled out below, but the operator still needs to be able
                // to clear the results that were just displayed.
                _lastEngagedPairModel = EngagedPairModel;

                // Clear when the system is disengaged
                EngagedPairModel = null;
                MaybeDebug("Cleared EngagedPairModel due to disengage");
                // Defensive: ensure the breakout-check timer isn't left running against a
                // cleared pair if this path is ever reached without RunActive having
                // already been set false first.
                StopBreakoutCheckTimer();

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
            try { _abortRunCommandRef?.NotifyCanExecuteChanged(); } catch { }

            if (value)
            {
                StartBreakoutCheckTimer();
                StartRunTimer();
            }
            else
            {
                StopBreakoutCheckTimer();
                StopRunTimer();
            }
        }

        // Run timeout timer. Begins when the run is made active and continues for the duration
        // of the run for the time specified in the category configuration, unless stopped by
        // the run completing naturally. Run Aborted is remarked on each non-finished lane
        private DispatcherTimer? _runTimer;

        private void StartRunTimer()
        {
            StopRunTimer();
            _runTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(EngagedPairModel.Category.RunTimeout) };
            _runTimer.Tick += (_, _) =>
            {
                AbortRun();
            };
            _runTimer.Start();
        }
        
        private void StopRunTimer()
        {
            if (_runTimer == null) return;
            _runTimer.Stop();
            _runTimer = null;
        }

        // Periodic (1s) check that can award an early breakout-based win once it becomes
        // mathematically certain a still-running lane cannot tie/out-breakout a lane that
        // already finished with a breakout, without waiting for its literal finish event.
        // See TimingHelpers.EvaluateWinnerLoseResult's nowNs parameter. Started/stopped
        // alongside RunActive (mirrors the existing _clockTimer pattern).
        private DispatcherTimer? _breakoutCheckTimer;

        private void StartBreakoutCheckTimer()
        {
            StopBreakoutCheckTimer();
            _breakoutCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _breakoutCheckTimer.Tick += (_, _) =>
            {
                try
                {
                    long nowNs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L;
                    RefreshWinnerLoseResult(nowNs);
                }
                catch (Exception ex) { LocalLog("BreakoutCheckTimer tick error: " + ex.Message); }
            };
            _breakoutCheckTimer.Start();
        }

        private void StopBreakoutCheckTimer()
        {
            if (_breakoutCheckTimer != null)
            {
                _breakoutCheckTimer.Stop();
                _breakoutCheckTimer = null;
            }
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
        private CommunityToolkit.Mvvm.Input.IRelayCommand? _abortRunCommandRef;
        private CommunityToolkit.Mvvm.Input.IRelayCommand? _clearCommandRef;

        // Explicit ICommand properties for ClearQueue and SwapEngagedPair (initialized in ctor)
        public CommunityToolkit.Mvvm.Input.IRelayCommand ClearQueueCommand { get; private set; } = default!;
        public CommunityToolkit.Mvvm.Input.IRelayCommand SwapEngagedPairCommand { get; private set; } = default!;

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

        // Snapshot of the most recently disengaged Pair, kept only so CanClear()/Clear() (F9)
        // can still see and reset lane details after SystemEngaged/EngagedPairModel have
        // already been cleared (e.g. on normal run completion). Reset to null as soon as a
        // fresh pair is engaged - see OnSystemEngagedChanged.
        private Pair? _lastEngagedPairModel;

        // Cache input map data for refreshing timing labels based on engaged category
        private List<DownTrackInput>? _cachedInputs;
        private List<SpeedTrap>? _cachedTraps;
        private string? _cachedDistanceUnit;
        private string? _cachedSpeedUnit;
        private bool _runCompletionLogged;
        private readonly string _runLogPath = "/tmp/pulsarui_runlog.txt";
        private DispatcherTimer? _clockTimer;

        // --- Run persistence (run_pair/run_indv) in-memory identity ---
        // Set once the current pair's run_pair/run_indv rows are inserted (RunStartReceived
        // handler); null means persistence isn't available for the current run (insert
        // failed, tables missing, or the insert hasn't completed yet) - all downstream
        // persistence calls must treat null as "skip silently". Reset defensively whenever
        // a fresh pair is engaged (see OnSystemEngagedChanged).
        private long? _currentRunPairId;
        private readonly long?[] _currentRunIndvIds = new long?[2]; // [0]=left, [1]=right

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
                                    // Diagnostic: log expected vs actual to help trace incorrect RT calculations
                                    try
                                    {
                                        long expectedNs = run.ReactionTime.ExpectedReactionTimeNs;
                                        long valueNs = run.ReactionTime.ValueNs;
                                        long deltaDebugNs = valueNs - expectedNs;
                                        var tree = run.Entry?.Tree;
                                        var hi = run.Entry?.HandicapIndex ?? string.Empty;
                                        LocalLog($"ReactionTime debug: lane={lane} runIndex={runIndex} ValueNs={valueNs} ExpectedNs={expectedNs} DeltaNs={deltaDebugNs} HandicapIndex={hi} TreeCountdownSpeed={tree?.CountdownSpeed} TreeCountdownType={tree?.CountdownType} PairRunMode={(EngagedPairModel?.RunMode.ToString() ?? "<null>")}");
                                    }
                                    catch { }
                                    // Determine foul by comparing computed reaction time against expected RT (delta < 0)
                                    long deltaNs = run.ReactionTime.ValueNs - run.ReactionTime.ExpectedReactionTimeNs;
                                    if (deltaNs < 0)
                                    {
                                        if (!remarksList.Contains(RunRemarks.Foul))
                                        {
                                            remarksList.Add(RunRemarks.Foul);
                                            run.Remarks = remarksList.ToArray();
                                            LocalLog($"Added RunRemarks.Foul for lane={lane} deltaNs={deltaNs} ValueNs={run.ReactionTime.ValueNs} ExpectedNs={run.ReactionTime.ExpectedReactionTimeNs}");
                                            SyncRunRemark(runIndex, RunRemarks.Foul, true);
                                        }
                                    }
                                    else
                                    {
                                        if (remarksList.Contains(RunRemarks.Foul))
                                        {
                                            remarksList.Remove(RunRemarks.Foul);
                                            run.Remarks = remarksList.ToArray();
                                            LocalLog($"Removed RunRemarks.Foul for lane={lane} deltaNs={deltaNs}");
                                            SyncRunRemark(runIndex, RunRemarks.Foul, false);
                                        }
                                    }

                                    // Persist the computed reaction time (raw decimal seconds) against this lane's
                                    // run_indv row, now that it's known.
                                    try
                                    {
                                        var indvId = _currentRunIndvIds.ElementAtOrDefault(runIndex);
                                        if (indvId is { } id)
                                        {
                                            _ = _databaseService.UpdateRunIndvReactionTimeAsync(id, FormatRawSeconds(deltaNs));
                                        }
                                    }
                                    catch (Exception ex) { LocalLog("Persist reaction_time failed: " + ex.Message); }

                                    // Re-evaluate Winner/Lose (foul-based, or conventional first-finish fallback)
                                    // and refresh the Remarks label for both lanes, since either can change.
                                    RefreshWinnerLoseResult();
                                }
                                catch (Exception ex) { LocalLog("ReactionTimeComputed remarks update failed: " + ex.Message); }

                                // Reaction time string is now included in the timingdata/{lane}/reactiontime JSON payload
                                // (reactionTimeString). Publish the payload here so the string matches the on-screen value
                                // (which is computed as ValueNs - ExpectedReactionTimeNs).
                                try
                                {
                                    var topic = $"timingdata/{lane}/reactiontime";
                                    var reactionString = run.ReactionTime.Value?.TrimStart('+') ?? string.Empty;
                                    var payloadObj = new
                                    {
                                        reactionTimeNanoseconds = run.ReactionTime.ValueNs,
                                        reactionTimeString = reactionString,
                                        runId = runId.ToString(),
                                        detectionSource = detectionSource
                                    };
                                    var payloadJson = System.Text.Json.JsonSerializer.Serialize(payloadObj);
                                    _ = _mqttService.PublishMqtt(topic, payloadJson);
                                    LocalLog($"Published timingdata/{lane}/reactiontime payload: {payloadJson}");
                                }
                                catch (Exception ex)
                                {
                                    LocalLog("Failed to publish reactiontime MQTT payload: " + ex.Message);
                                }

                                // A reaction time was just recorded for this lane - re-evaluate F9/Clear availability.
                                UpdateButtonStatuses();
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
                                existing.TimestampNanoseconds = downtrackTimestampNs;
                            }
                            else
                            {
                                var newIt = new IncrementalTime { ValueNs = computedIncNs, Input = dti, TimestampNanoseconds = downtrackTimestampNs };
                                incList.Add(newIt);
                            }

                            run.IncrementalTimes = incList.ToArray();

                            // Persist this incremental ET against the lane's run_indv row (upsert by distance).
                            try
                            {
                                var indvIdForEt = _currentRunIndvIds.ElementAtOrDefault(runIndex);
                                if (indvIdForEt is { } etId)
                                {
                                    _ = _databaseService.UpsertIncrementalEtAsync(etId, dti.DistanceMm, FormatRawSeconds(computedIncNs));
                                }
                            }
                            catch (Exception ex) { LocalLog("Persist incremental_et failed: " + ex.Message); }

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

                                // Persist this incremental speed against the lane's run_indv row (upsert by distance).
                                try
                                {
                                    var indvIdForSpeed = _currentRunIndvIds.ElementAtOrDefault(runIndex);
                                    if (indvIdForSpeed is { } spId)
                                    {
                                        _ = _databaseService.UpsertIncrementalSpeedAsync(spId, dti.DistanceMm, FormatRawDecimal(speedVal));
                                    }
                                }
                                catch (Exception ex) { LocalLog("Persist incremental_speed failed: " + ex.Message); }
                            }

                            // Update UI label corresponding to this DownTrackInput
                            // Note: labels generated by TimingLabelHelpers.GenerateTimingLabels include the " ET" suffix,
                            // so include it when matching the label here.
                            var etLabel = TimingLabelHelpers.FormatDistanceLabel(dti.DistanceMm, AppSettings.DistanceUnit) + " ET";
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

                            // Call completion checker: determine finish distance from engaged category and ask MaybeCheckAndCompleteRun to evaluate
                            try
                            {
                                int finishDistanceMm = 0;
                                if (EngagePairQueueCategory != null && FinishList != null)
                                {
                                    var fl = FinishList.FirstOrDefault(f => f != null && f.Id == EngagePairQueueCategory.Finish);
                                    if (fl != null) finishDistanceMm = fl.Distance;
                                }
                                MaybeCheckAndCompleteRun(runIndex, finishDistanceMm, run);
                            }
                            catch (Exception ex)
                            {
                                LocalLog("MaybeCheckAndCompleteRun invocation failed: " + ex.Message);
                            }

                            // Incremental time and speed strings are now included in the timingdata/{lane}/incrementaltime
                            // JSON payload (incrementalString and speedString). Separate /string topic publishes are not required.

                            // New incremental speed/ET (and possibly a finish ET) were just recorded - re-evaluate F9/Clear availability.
                            UpdateButtonStatuses();
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
                var mqttHost = configuration["MQTT:Host"] ?? "192.168.20.100";
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
                _inputMapService = ims;
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

            // Build the live System Status panel's initial state (default statuses + DownTrack rows) from
            // the loaded input map. Must run after _inputMapService/_cachedInputs are populated above.
            try
            {
                InitializeSensorStatusPanel();
            }
            catch (Exception ex)
            {
                LocalLog("Failed to initialize sensor status panel: " + ex.Message);
            }

            // Wire up live sensor status events so the System Status panel reflects rising/falling edges
            // regardless of whether a run is currently active.
            try
            {
                _mqttService.InputRoleStatusChanged += (role, direction) =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        try { ApplyInputRoleStatus(role, direction); }
                        catch (Exception ex) { LocalLog("ApplyInputRoleStatus failed: " + ex.Message); }
                    });
                };
                _mqttService.DownTrackInputStatusChanged += (dti, direction) =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        try { ApplyDownTrackInputStatus(dti, direction); }
                        catch (Exception ex) { LocalLog("ApplyDownTrackInputStatus failed: " + ex.Message); }
                    });
                };
            }
            catch (Exception ex)
            {
                LocalLog("Failed to wire sensor status handlers: " + ex.Message);
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
             
                        _runCompletionLogged = false;
                        RunActive = true;
                        RunStartTimestampNanoseconds = tsNs;
                        MaybeDebug($"Run started: RunStartTimestampNanoseconds={tsNs}");

                        // Persist run_pair + run_indv rows now that the run is active.
                        // Fire-and-forget: never blocks/throws into this handler.
                        try
                        {
                            PersistRunStartAsync(EngagedPairModel, EngagePairQueueCategory, FinishList, tsNs);
                        }
                        catch (Exception ex)
                        {
                            LocalLog("PersistRunStartAsync invocation failed: " + ex.Message);
                        }
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
                                    SyncRunRemark(runIndex, RunRemarks.NoVehicleStaged, true);
                                }
                            }
                            else
                            {
                                if (remarksList.Contains(RunRemarks.NoVehicleStaged))
                                {
                                    remarksList.Remove(RunRemarks.NoVehicleStaged);
                                    run.Remarks = remarksList.ToArray();
                                    SyncRunRemark(runIndex, RunRemarks.NoVehicleStaged, false);
                                }
                            }

                            // Re-evaluate Winner/Lose (bye-run rule can now apply) and refresh
                            // the Remarks label for both lanes, since either can change.
                            RefreshWinnerLoseResult();
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

                            // Deep-stage fouls are only tracked when the engaged category has
                            // DeepStageFoul enabled. Applies regardless of RunMode (Practice,
                            // Qualifying, Eliminations, Q+E Combo) - the Winner/Lose consequence
                            // (handled by EvaluateWinnerLoseResult) is what's scoped to Eliminations/QeCombo.
                            bool categoryTracksDsFoul = EngagedPairModel.Category?.DeepStageFoul ?? false;

                            var remarksList = run.Remarks?.ToList() ?? new System.Collections.Generic.List<RunRemarks>();
                            if (dsFoul && categoryTracksDsFoul)
                            {
                                if (!remarksList.Contains(RunRemarks.DsFoul))
                                {
                                    remarksList.Add(RunRemarks.DsFoul);
                                    run.Remarks = remarksList.ToArray();
                                    SyncRunRemark(runIndex, RunRemarks.DsFoul, true);
                                }
                            }
                            else
                            {
                                if (remarksList.Contains(RunRemarks.DsFoul))
                                {
                                    remarksList.Remove(RunRemarks.DsFoul);
                                    run.Remarks = remarksList.ToArray();
                                    SyncRunRemark(runIndex, RunRemarks.DsFoul, false);
                                }
                            }

                            // Re-evaluate Winner/Lose (a DsFoul lane loses outright; both lanes
                            // having DsFoul means neither wins) and refresh both lanes' labels.
                            RefreshWinnerLoseResult();
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

            // Explicit ICommand properties for ClearQueue and SwapEngagedPair (initialize in ctor)
            ClearQueueCommand = new RelayCommand(() => ClearQueue());
            SwapEngagedPairCommand = new RelayCommand(() => SwapEngagedPair());

            // Capture typed IRelayCommand references generated by the source generator so we can call NotifyCanExecuteChanged without casts
            _queuePairCommandRef = QueuePairCommand as CommunityToolkit.Mvvm.Input.IRelayCommand;
            _engagePairCommandRef = EngagePairCommand as CommunityToolkit.Mvvm.Input.IRelayCommand;
            _clearQueueCommandRef = ClearQueueCommand as CommunityToolkit.Mvvm.Input.IRelayCommand;
            _startCommandRef = StartRunCommand as CommunityToolkit.Mvvm.Input.IAsyncRelayCommand;
            // Capture Reset/Swap command refs so we can update their CanExecute when RunActive changes
            _resetEngageCommandRef = ResetEngagePairCommand as CommunityToolkit.Mvvm.Input.IRelayCommand;
            _swapEngageCommandRef = SwapEngagedPairCommand as CommunityToolkit.Mvvm.Input.IRelayCommand;
            // Capture AbortRun command ref so we can re-evaluate CanExecute when RunActive changes
            _abortRunCommandRef = AbortRunCommand as CommunityToolkit.Mvvm.Input.IRelayCommand;
            // Capture Clear command ref so we can re-evaluate CanExecute as lane details change
            _clearCommandRef = ClearCommand as CommunityToolkit.Mvvm.Input.IRelayCommand;

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

        [RelayCommand(CanExecute = nameof(CanQueuePair))]
        private void QueuePair()
        {
            if (!CanQueuePair()) return;
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

        [RelayCommand(CanExecute = nameof(CanClear))]
        private void Clear()
        {
            if (!CanClear()) return;

            try
            {
                // Clear racer/index details for both engaged lanes and republish them.
                for (int i = 0; i < EngagedRacers.Count; i++)
                {
                    EngagedRacers[i].ClearAll();
                    try { _ = _mqttService.PubQueueRacersAsync(EngagedRacers[i]); } catch { }
                }

                var pair = EngagedPairModel ?? _lastEngagedPairModel;
                var left = pair?.Runs?.ElementAtOrDefault(0);
                var right = pair?.Runs?.ElementAtOrDefault(1);

                ClearRunTimingDetails(left);
                ClearRunTimingDetails(right);

                // The just-cleared snapshot no longer has anything worth clearing.
                _lastEngagedPairModel = null;

                // Blank every on-screen timing label (Reaction Time, ET/speed traps, Result, Remarks) for both lanes.
                foreach (var label in LeftTimingLabels) label.Value = string.Empty;
                foreach (var label in RightTimingLabels) label.Value = string.Empty;

                // Force republish of the (now empty) Result/Remarks even if they match a previously-cached empty state.
                _lastPublishedResults.Clear();
                _lastPublishedRemarks.Clear();
                UpdateResultLabel(0, left);
                UpdateResultLabel(1, right);
                RefreshRemarksLabel(0, left);
                RefreshRemarksLabel(1, right);

                _mqttService.ClearDisplays();

                UpdateButtonStatuses();
            }
            catch (Exception ex)
            {
                LocalLog("Clear error: " + ex.Message);
            }
        }

        // Resets a single lane's collected timing data back to its pre-run state (Entry/racer
        // details are cleared separately via EngagedRacers[i].ClearAll()).
        private static void ClearRunTimingDetails(Run? run)
        {
            if (run == null) return;
            run.Entry?.ClearAll();
            run.ReactionTime = null;
            run.IncrementalTimes = Array.Empty<IncrementalTime>();
            run.IncrementalSpeeds = Array.Empty<IncrementalSpeed>();
            run.Remarks = Array.Empty<RunRemarks>();
            run.Results = Array.Empty<RunResult>();
            run.FinishEtNs = null;
            run.BreakoutDeltaNs = null;
        }
        
        [RelayCommand(CanExecute = nameof(CanEngagePair))]
        private void EngagePair()
        {
            if (!CanEngagePair()) return;
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
                        // Include finish index and finish distance (mm) alongside the full Pair
                        int finishIndex = EngagePairQueueCategory?.Finish ?? 0;
                        int finishDistanceMm = 0;
                        if (FinishList != null && finishIndex != 0)
                        {
                            var fl = FinishList.FirstOrDefault(f => f != null && f.Id == finishIndex);
                            if (fl != null) finishDistanceMm = fl.Distance;
                        }

                        var fullObj = new { pair = p, finishIndex = finishIndex, finishDistanceMm = finishDistanceMm, speedUnit = AppSettings.SpeedUnit };
                        var full = System.Text.Json.JsonSerializer.Serialize(fullObj, options);
                        // Publish full Pair (with finish metadata) for debugging/consumers that need the complete object
                        _ = _mqttService.PublishMqtt("pulsarui/engagedpair/full", full);
                    }
                }
                catch (Exception ex)
                {
                    MaybeDebug("EngagedPair debug publish failed: " + ex.Message);
                }

                // Publish run configuration (runconfig/left, runconfig/right, runconfig/setup)
                try
                {
                    if (_mqttService != null)
                    {
                        var racers = p.Runs?.Select(r => r.Entry) ?? Enumerable.Empty<RaceEntry>();
                        _ = _mqttService.PubRunConfigAsync(EngagePairQueueCategory, racers);
                    }
                }
                catch (Exception ex)
                {
                    MaybeDebug("PubRunConfigAsync failed: " + ex.Message);
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
            _mqttService.ClearDisplays();
            UpdateButtonStatuses();
        }

        // Implement ClearQueue used by the manually-created ClearQueueCommand
        private void ClearQueue()
        {
            try
            {
                for (int i = 0; i < QueuedRacers.Count; i++)
                {
                    QueuedRacers[i].ClearAll();
                    // Publish an explicit null queued entry so subscribers clear their view
                    try { _ = _mqttService.PubQueueRacersAsync(new RaceEntry { QueueIndex = 1, Lane = i, RaceNumber = null, HandicapIndex = null }); } catch { }
                }
                UpdateButtonStatuses();
            }
            catch (Exception ex)
            {
                LocalLog("ClearQueue error: " + ex.Message);
            }
        }

        // Implement SwapEngagedPair used by the manually-created SwapEngagedPairCommand
        private void SwapEngagedPair()
        {
            try
            {
                // Swap the UI EngagedRacers
                if (EngagedRacers.Count >= 2)
                {
                    var left = EngagedRacers[0];
                    var right = EngagedRacers[1];
                    EngagedRacers[0] = right.Clone(2);
                    EngagedRacers[1] = left.Clone(2);
                }

                // Also swap the underlying runs if present
                if (EngagedPairModel?.Runs != null && EngagedPairModel.Runs.Length >= 2)
                {
                    var tmp = EngagedPairModel.Runs[0];
                    EngagedPairModel.Runs[0] = EngagedPairModel.Runs[1];
                    EngagedPairModel.Runs[1] = tmp;
                }

                // Publish the swapped engaged racers
                for (int i = 0; i < EngagedRacers.Count; i++)
                {
                    try { _ = _mqttService.PubQueueRacersAsync(EngagedRacers[i]); } catch { }
                }

                UpdateButtonStatuses();
            }
            catch (Exception ex)
            {
                LocalLog("SwapEngagedPair error: " + ex.Message);
            }
        }

        [RelayCommand(CanExecute = nameof(CanStartRun))]
        private async Task StartRun()
        {
            await _mqttService.ConsoleStartAsync();
        }

        private bool CanStartRun()
        {
            return SystemEngaged;
        }

        // CanExecute for QueuePair (F5): block queuing a new pair from Enter while the
        // queue slot already holds a populated pair. The queue must be cleared (F11) first.
        private bool CanQueuePair()
        {
            return !QueuedRacers.Any(r => !string.IsNullOrEmpty(r.RaceNumber));
        }
        
        // CanExecute for Clear (F9): only when no run is active, the system is disengaged, and
        // at least one lane has collected timing details worth clearing (a result, a reaction
        // time, a finish ET, or a finish speed). Falls back to _lastEngagedPairModel because
        // EngagedPairModel is already nulled out by the time SystemEngaged goes false (see
        // OnSystemEngagedChanged), which happens as part of normal run completion.
        private bool CanClear()
        {
            if (RunActive || SystemEngaged) return false;

            var pair = EngagedPairModel ?? _lastEngagedPairModel;
            var left = pair?.Runs?.ElementAtOrDefault(0);
            var right = pair?.Runs?.ElementAtOrDefault(1);
            var hasLaneDetails = RunHasDetails(left) || RunHasDetails(right);
            return hasLaneDetails;
        }

        // True if this lane's run has a result, a reaction time, a finish ET, or a finish speed.
        // Note: run.ReactionTime is non-null as soon as a pair is engaged (PopulateExpectedReactionTimes
        // eagerly creates it just to hold ExpectedReactionTimeNs for display), so a non-null check alone
        // would incorrectly report "has details" before any real reaction has actually been recorded -
        // ValueNs is only ever set once a real reaction time arrives (see the ReactionTimeComputed handler).
        private static bool RunHasDetails(Run? run)
        {
            if (run == null) return false;
            bool hasResult = run.Results is { Length: > 0 };
            bool hasReactionTime = run.ReactionTime != null && run.ReactionTime.ValueNs != 0;
            bool hasFinishEt = run.FinishEtNs.HasValue;
            bool hasFinishSpeed = run.IncrementalSpeeds is { Length: > 0 };
            return hasResult || hasReactionTime || hasFinishEt || hasFinishSpeed;
        }

        // CanExecute for EngagePair (F10): block engaging while a run is active or the
        // system is already engaged. Must wait for RunActive/SystemEngaged to clear first.
        private bool CanEngagePair()
        {
            return !RunActive && !SystemEngaged;
        }

        // Command to enable/disable the Setup UI (bound to F7 in XAML).
        // Generated RelayCommand will produce a SetupEnableCommand ICommand property.
        [RelayCommand]
        private void SetupEnable()
        {
            // Toggle setup active/inactive flags so UI switches between setup and normal modes.
            SetupActive = !SetupActive;
            SetupInactive = !SetupActive;
            // Ensure button states are refreshed after the toggle
            UpdateButtonStatuses();
        }

        [RelayCommand(CanExecute = nameof(CanAbortRun))]
        private void AbortRun()
        {
            try
            {
                if (!RunActive) return;
                if (EngagedPairModel?.Runs == null) return;

                // Capture the pair before SystemEngaged=false nulls EngagedPairModel below,
                // so the run_pair completion update below still has a valid reference.
                var pair = EngagedPairModel;

                for (int i = 0; i < pair.Runs.Length; i++)
                {
                    var run = pair.Runs.ElementAtOrDefault(i);
                    if (run == null) continue;

                    // Skip lanes explicitly marked as NoVehicleStaged
                    var hasNoVehicle = run.Remarks?.Contains(RunRemarks.NoVehicleStaged) ?? false;
                    if (hasNoVehicle) continue;

                    var remarksList = run.Remarks?.ToList() ?? new System.Collections.Generic.List<RunRemarks>();
                    if (!remarksList.Contains(RunRemarks.Aborted))
                    {
                        remarksList.Add(RunRemarks.Aborted);
                        run.Remarks = remarksList.ToArray();
                        SyncRunRemark(i, RunRemarks.Aborted, true);

                        // Update Remarks label in UI and publish each remark on MQTT.
                        RefreshRemarksLabel(i, run);
                    }
                }

                // Persist whatever first_lane/winner_lane state exists (typically both still
                // null for an aborted run) against the run_pair row created when the run went active.
                PersistRunPairCompletion(pair);

                // Mark run inactive; do not change SystemEngaged per your instruction
                RunActive = false;
                SystemEngaged = false;
                MaybeDebug("AbortRun: marked run inactive and added Aborted remark where applicable");
            }
            catch (Exception ex)
            {
                LocalLog("AbortRun error: " + ex.Message);
            }
        }

        // CanExecute for AbortRun command
        private bool CanAbortRun()
        {
            return RunActive;
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

        // Builds the System Status panel's initial state: per-role default status (Blocked for
        // Pre-Stage/Stage Lock/Stage, Error for Guard A/Guard B, or Inactive when disabled in
        // inputmap.json), and the DownTrackStatusRows collection (one row per distinct distance,
        // sorted ascending, Left = lane 0 / Right = lane 1).
        private void InitializeSensorStatusPanel()
        {
            // Roles participating in live tracking (RunStartTrigger/Unknown are excluded).
            var trackedRoles = new[]
            {
                InputRole.LeftPreStage, InputRole.RightPreStage,
                InputRole.LeftStageLock, InputRole.RightStageLock,
                InputRole.LeftStage, InputRole.RightStage,
                InputRole.LeftGuardA, InputRole.RightGuardA,
                InputRole.LeftGuardB, InputRole.RightGuardB
            };

            foreach (var role in trackedRoles)
            {
                bool enabled = true;
                try
                {
                    if (_inputMapService != null && _inputMapService.TryGetIdentifier(role, out var id))
                    {
                        enabled = id.Enabled;
                    }
                    else
                    {
                        LocalLog($"InitializeSensorStatusPanel: role={role} not found in inputmap.json RoleMap; defaulting to enabled");
                    }
                }
                catch (Exception ex)
                {
                    LocalLog($"InitializeSensorStatusPanel: TryGetIdentifier failed for role={role}: {ex.Message}");
                }

                _roleEnabled[role] = enabled;
                if (!enabled)
                {
                    SetRoleStatus(role, StatusInactive);
                }
            }

            DownTrackStatusRows.Clear();
            _downTrackRowsByDistance.Clear();

            if (_cachedInputs == null) return;

            var distanceUnit = _cachedDistanceUnit ?? AppSettings.DistanceUnit;

            var byDistance = _cachedInputs
                .GroupBy(i => i.DistanceMm)
                .OrderBy(g => g.Key);

            foreach (var group in byDistance)
            {
                var distanceMm = group.Key;
                var leftEntry = group.FirstOrDefault(i => i.Lane == InputLane.Left);
                var rightEntry = group.FirstOrDefault(i => i.Lane == InputLane.Right);

                var leftEnabled = leftEntry?.Enabled ?? false;
                var rightEnabled = rightEntry?.Enabled ?? false;

                var row = new DownTrackStatusRow
                {
                    DistanceMm = distanceMm,
                    DistanceLabel = TimingLabelHelpers.FormatDistanceLabel(distanceMm, distanceUnit),
                    LeftEnabled = leftEnabled,
                    RightEnabled = rightEnabled,
                    LeftStatus = leftEnabled ? StatusError : StatusInactive,
                    RightStatus = rightEnabled ? StatusError : StatusInactive
                };

                DownTrackStatusRows.Add(row);
                _downTrackRowsByDistance[distanceMm] = row;
            }
        }

        // Sets the status property backing a given tracked role. Centralized so both initialization
        // and live edge updates share the same role -> property mapping.
        private void SetRoleStatus(InputRole role, string status)
        {
            switch (role)
            {
                case InputRole.LeftPreStage: LeftPreStageStatus = status; break;
                case InputRole.RightPreStage: RightPreStageStatus = status; break;
                case InputRole.LeftStageLock: LeftStageLockStatus = status; break;
                case InputRole.RightStageLock: RightStageLockStatus = status; break;
                case InputRole.LeftStage: LeftStageStatus = status; break;
                case InputRole.RightStage: RightStageStatus = status; break;
                case InputRole.LeftGuardA: LeftGuardAStatus = status; break;
                case InputRole.RightGuardA: RightGuardAStatus = status; break;
                case InputRole.LeftGuardB: LeftGuardBStatus = status; break;
                case InputRole.RightGuardB: RightGuardBStatus = status; break;
            }
        }

        // Applies a live edge (rising/falling) to the corresponding role's status.
        // Falling edge -> Blocked (Pre-Stage/Stage Lock/Stage) or Error (Guard A/Guard B).
        // Rising edge -> OK. RunStartTrigger/Unknown are ignored. Disabled roles never change.
        private void ApplyInputRoleStatus(InputRole role, bool direction)
        {
            if (role == InputRole.RunStartTrigger || role == InputRole.Unknown) return;
            if (_roleEnabled.TryGetValue(role, out var enabled) && !enabled) return;

            bool isGuard = role is InputRole.LeftGuardA or InputRole.RightGuardA
                                  or InputRole.LeftGuardB or InputRole.RightGuardB;
            string status = direction ? StatusOk : (isGuard ? StatusError : StatusBlocked);

            SetRoleStatus(role, status);
        }

        // Applies a live edge (rising/falling) to the DownTrackStatusRow matching the given input's distance.
        // Falling edge -> Error. Rising edge -> OK. Disabled lanes never change.
        private void ApplyDownTrackInputStatus(DownTrackInput dti, bool direction)
        {
            if (dti == null) return;
            if (!_downTrackRowsByDistance.TryGetValue(dti.DistanceMm, out var row)) return;

            if (dti.Lane == InputLane.Left)
            {
                if (!row.LeftEnabled) return;
                row.LeftStatus = direction ? StatusOk : StatusError;
            }
            else
            {
                if (!row.RightEnabled) return;
                row.RightStatus = direction ? StatusOk : StatusError;
            }
        }

        // Simple local logger used for development diagnostics. Writes to /tmp/pulsarui_debug.log when enabled.
        private void LocalLog(string message)
        {
            try
            {
                // Prefer appsettings toggle in future; for now write always to help debugging during development
                System.IO.File.AppendAllText("/tmp/pulsarui_debug.log", DateTime.Now.ToString("o") + " " + message + Environment.NewLine);
            }
            catch
            {
                // best-effort logging, ignore failures
            }
        }

        // Controlled debug output that's a no-op unless _enableDebugPublish is true
        private void MaybeDebug(string message)
        {
            if (_enableDebugPublish)
            {
                LocalLog("DEBUG: " + message);
            }
        }

        // Update the enabled/visible state of toolbar buttons and notify commands
        private void UpdateButtonStatuses()
        {
            try
            {
                // Notify derived boolean properties bound in XAML (IsF3Enabled/IsF4Enabled/IsF5Enabled/IsF10Enabled etc.)
                OnPropertyChanged(nameof(IsF3Enabled));
                OnPropertyChanged(nameof(IsF4Enabled));
                OnPropertyChanged(nameof(IsF5Enabled));
                OnPropertyChanged(nameof(IsF6Enabled));
                OnPropertyChanged(nameof(IsF7Enabled));
                OnPropertyChanged(nameof(IsF8Enabled));
                OnPropertyChanged(nameof(IsF9Enabled));
                OnPropertyChanged(nameof(IsF10Enabled));
                OnPropertyChanged(nameof(IsF11Enabled));

                // Notify command CanExecute state for captured refs
                _queuePairCommandRef?.NotifyCanExecuteChanged();
                _engagePairCommandRef?.NotifyCanExecuteChanged();
                _clearQueueCommandRef?.NotifyCanExecuteChanged();
                _resetEngageCommandRef?.NotifyCanExecuteChanged();
                _swapEngageCommandRef?.NotifyCanExecuteChanged();
                _abortRunCommandRef?.NotifyCanExecuteChanged();
                _startCommandRef?.NotifyCanExecuteChanged();
                _clearCommandRef?.NotifyCanExecuteChanged();
            }
            catch { }
        }

        // Expose a few convenience boolean properties used by XAML bindings (conservative defaults)
        public bool IsF3Enabled => true; // Reset engage pair
        public bool IsF4Enabled => true; // Swap engaged pair
        public bool IsF5Enabled => CanQueuePair(); // Queue
        public bool IsF6Enabled => SystemEngaged; // Start
        public bool IsF7Enabled => true; // Setup
        public bool IsF8Enabled => RunActive; // Abort
        public bool IsF9Enabled => CanClear(); // Clear
        public bool IsF10Enabled => CanEngagePair(); // Engage
        public bool IsF11Enabled => true; // Clear

        // Async loaders for finishes/trees/categories: load from database service and set properties
        private async Task LoadFinishLinesAsync()
        {
            try
            {
                var list = await _databaseService.GetFinishLinesAsync();
                FinishList = list ?? new List<FinishLine>();
            }
            catch (Exception ex)
            {
                LocalLog("LoadFinishLinesAsync failed: " + ex.Message);
                FinishList = new List<FinishLine>();
            }
        }

        private async Task LoadTreeTypesAsync()
        {
            try
            {
                var list = await _databaseService.GetTreeTypesAsync();
                TreeList = list ?? new List<TreeType>();
                // Populate Tree combo items if needed
                TreeComboBoxItems = TreeList.Select(t => t.Name ?? string.Empty).ToList();
            }
            catch (Exception ex)
            {
                LocalLog("LoadTreeTypesAsync failed: " + ex.Message);
                TreeList = new List<TreeType>();
                TreeComboBoxItems = new List<string>();
            }
        }

        private async Task LoadCategoriesAsync()
        {
            try
            {
                var list = await _databaseService.GetCategoryListAsync();
                Categories = list ?? new List<Category>();
                // Build combo box textual items like "01 - Name" using Order
                CategComboBoxItems = Categories.Select(c => (c.Order.ToString("D2") + " - " + (c.Name ?? string.Empty))).ToList();
            }
            catch (Exception ex)
            {
                LocalLog("LoadCategoriesAsync failed: " + ex.Message);
                Categories = new List<Category>();
                CategComboBoxItems = new List<string>();
            }
        }

        // Remembers the last-published Remarks/Results content per lane (keyed by "left"/"right"),
        // so PublishLaneValues can skip re-publishing when nothing has actually changed - e.g.
        // EvaluateWinnerLoseResult/UpdateResultLabel/RefreshRemarksLabel are called every second
        // by the breakout-check timer (and on every other refresh) regardless of whether the
        // underlying Remarks/Results actually changed since last time.
        private readonly Dictionary<string, RunResult[]> _lastPublishedResults = new();
        private readonly Dictionary<string, RunRemarks[]> _lastPublishedRemarks = new();

        // Order-independent content equality for small Remarks/Results arrays.
        private static bool SameValues<T>(T[]? a, T[]? b) where T : struct, Enum
        {
            var x = a ?? Array.Empty<T>();
            var y = b ?? Array.Empty<T>();
            if (x.Length != y.Length) return false;
            foreach (var v in x) if (!y.Contains(v)) return false;
            foreach (var v in y) if (!x.Contains(v)) return false;
            return true;
        }

        // Update the dedicated "Result" timing label for a lane to reflect its Run.Results, if any.
        // Results are not mutually exclusive (e.g. a run can be both FirstFinish and Lose), so
        // all current entries are joined, the same way the "Remarks" label joins Run.Remarks.
        // Publish each current remark/result value for a lane as its own plain-text MQTT
        // payload on timingdata/{lane}/remark or timingdata/{lane}/result (one publish per
        // value, using its display name, e.g. "Breakout", "Winner"), but only when the
        // collection's content actually changed since the last publish for that lane/kind -
        // otherwise this would resend the same batch every time this is refreshed (e.g. once a
        // second, from the breakout-check timer, until both lanes finish). If the array is
        // empty, publishes a single empty payload so subscribers can detect the cleared state.
        private void PublishLaneValues<T>(string lane, string kind, T[]? values, Dictionary<string, T[]> lastPublished) where T : struct, Enum
        {
            try
            {
                var current = values ?? Array.Empty<T>();
                if (lastPublished.TryGetValue(lane, out var previous) && SameValues(previous, current))
                {
                    return; // unchanged - nothing to publish
                }
                lastPublished[lane] = current;

                var topic = $"timingdata/{lane}/{kind}";
                if (current.Length == 0)
                {
                    _ = _mqttService.PublishMqtt(topic, string.Empty);
                    return;
                }
                foreach (var value in current)
                {
                    _ = _mqttService.PublishMqtt(topic, ((Enum)(object)value).GetDisplayName());
                }
            }
            catch (Exception ex)
            {
                LocalLog($"PublishLaneValues error ({kind}/{lane}): " + ex.Message);
            }
        }

        private void UpdateResultLabel(int runIndex, Run? run)
        {
            try
            {
                if (run == null) return;
                var labels = runIndex == 0 ? LeftTimingLabels : RightTimingLabels;
                if (labels == null || labels.Count == 0) return;
                var resultIndex = labels.ToList().FindIndex(l => string.Equals(l.Label, "Result", StringComparison.OrdinalIgnoreCase));
                if (resultIndex >= 0 && resultIndex < labels.Count)
                {
                    var resultText = (run.Results != null && run.Results.Length > 0)
                        ? string.Join(" | ", run.Results.Select(r => ((System.Enum)r).GetDisplayName()))
                        : string.Empty;
                    labels[resultIndex].Value = resultText;
                    var lane = runIndex == 0 ? "left" : "right";
                    PublishLaneValues(lane, "result", run.Results, _lastPublishedResults);
                }
            }
            catch (Exception ex)
            {
                LocalLog("UpdateResultLabel error: " + ex.Message);
            }
        }

        // Add a RunResult entry to a run's Results array if not already present (idempotent).
        private static void AddRunResult(Run run, RunResult value)
        {
            var list = run.Results?.ToList() ?? new System.Collections.Generic.List<RunResult>();
            if (!list.Contains(value))
            {
                list.Add(value);
                run.Results = list.ToArray();
            }
        }

        // Remove a RunResult entry from a run's Results array if present (idempotent).
        private static void RemoveRunResult(Run run, RunResult value)
        {
            var list = run.Results?.ToList() ?? new System.Collections.Generic.List<RunResult>();
            if (list.Contains(value))
            {
                list.Remove(value);
                run.Results = list.ToArray();
            }
        }

        // Refresh the "Remarks" timing label for a lane from its current Run.Remarks,
        // and publish each current remark as its own plain-text MQTT payload.
        private void RefreshRemarksLabel(int runIndex, Run? run)
        {
            try
            {
                if (run == null) return;
                var labels = runIndex == 0 ? LeftTimingLabels : RightTimingLabels;
                if (labels == null || labels.Count == 0) return;
                var resultIndex = labels.ToList().FindIndex(l => string.Equals(l.Label, "Result", StringComparison.OrdinalIgnoreCase));
                var remarksIndex = resultIndex >= 0 ? resultIndex + 1 : labels.ToList().FindIndex(l => string.Equals(l.Label, "Remarks", StringComparison.OrdinalIgnoreCase));
                if (remarksIndex >= 0 && remarksIndex < labels.Count)
                {
                    var remarksText = (run.Remarks != null && run.Remarks.Length > 0)
                        ? string.Join(" | ", run.Remarks.Select(r => ((System.Enum)r).GetDisplayName()))
                        : string.Empty;
                    labels[remarksIndex].Value = remarksText;
                    var lane = runIndex == 0 ? "left" : "right";
                    PublishLaneValues(lane, "remark", run.Remarks, _lastPublishedRemarks);
                }
            }
            catch (Exception ex)
            {
                LocalLog("RefreshRemarksLabel error: " + ex.Message);
            }
        }

        // ============================================================
        // Run persistence (run_pair / run_indv / incremental_et /
        // incremental_speed / run_remark) helpers
        // ============================================================

        // Formats a nanosecond duration as raw decimal seconds text (invariant culture,
        // trimmed trailing zeros, no unit suffix, no forced sign) - preserves the exact
        // decimal representation rather than the UI's resolution-rounded display string.
        private static string FormatRawSeconds(long ns) =>
            (ns / 1_000_000_000m).ToString("0.#########", CultureInfo.InvariantCulture);

        private static string FormatRawDecimal(decimal value) =>
            value.ToString("0.#########", CultureInfo.InvariantCulture);

        // Inserts the run_pair row and both run_indv rows for the pair that just became
        // active, storing the returned ids in _currentRunPairId/_currentRunIndvIds so later
        // events (reaction time, incremental readings, remarks, completion) can reference
        // them. Fire-and-forget from the RunStartReceived handler; never throws.
        private async void PersistRunStartAsync(Pair? pair, CategQueueItem? engagedCategory, List<FinishLine>? finishList, long runStartTimestampNs)
        {
            try
            {
                if (pair?.Category == null || pair.Runs == null || pair.Runs.Length < 2)
                {
                    LocalLog("PersistRunStartAsync: skipped - pair or category not available");
                    return;
                }

                var category = pair.Category;

                int finishDistanceMm = 0;
                if (engagedCategory != null && finishList != null)
                {
                    var fl = finishList.FirstOrDefault(f => f != null && f.Id == engagedCategory.Finish);
                    if (fl != null) finishDistanceMm = fl.Distance;
                }

                var isoTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(runStartTimestampNs / 1_000_000L)
                    .ToUniversalTime().ToString("o");

                var runPairRecord = new RunPairInsert
                {
                    RunTimestamp = isoTimestamp,
                    CategoryId = category.Id,
                    RaceMode = (int)(pair.RunMode ?? RunMode.Practice),
                    RoundNumber = engagedCategory?.Round,
                    StartMode = category.StartMode,
                    FinishDistance = finishDistanceMm,
                    RunTimeout = category.RunTimeout,
                    StageFreeze = category.StageFreeze,
                    DsFoul = category.DeepStageFoul,
                    WorstFoul = category.WorstFoul,
                    TreeDelay = null,
                    AsSettle = category.StageSettle,
                    AsStageToStart = category.AutoStartStageToStart,
                    AsVariance = category.AutoStartVariance,
                    AsTimeout = category.AutoStartTimeout
                };

                var pairId = await _databaseService.InsertRunPairAsync(runPairRecord);
                if (pairId == null)
                {
                    LocalLog("PersistRunStartAsync: InsertRunPairAsync failed, persistence disabled for this run");
                    return;
                }

                _currentRunPairId = pairId;

                for (int i = 0; i < 2; i++)
                {
                    var run = pair.Runs.ElementAtOrDefault(i);
                    var entry = run?.Entry;

                    long? racerId = null;
                    try
                    {
                        racerId = await _databaseService.ResolveRacerIdAsync(category.Id, entry?.RaceNumber);
                    }
                    catch (Exception ex)
                    {
                        LocalLog("PersistRunStartAsync: ResolveRacerIdAsync failed: " + ex.Message);
                    }

                    var treeType = entry?.Tree?.Id ?? category.TreeType;

                    var indvRecord = new RunIndvInsert
                    {
                        PairId = pairId.Value,
                        RacerId = racerId,
                        Lane = entry?.Lane ?? i,
                        RaceNumber = entry?.RaceNumber,
                        IndexTime = entry?.HandicapIndex,
                        TreeType = treeType
                    };

                    var indvId = await _databaseService.InsertRunIndvAsync(indvRecord);
                    _currentRunIndvIds[i] = indvId;
                    if (indvId == null)
                    {
                        LocalLog($"PersistRunStartAsync: InsertRunIndvAsync failed for lane={i}");
                    }
                }
            }
            catch (Exception ex)
            {
                LocalLog("PersistRunStartAsync error: " + ex.Message);
            }
        }

        // Inserts or deletes a run_remark row to mirror a Run.Remarks add/remove, keyed off
        // the in-memory run_indv id for that lane. No-ops silently if persistence isn't
        // available for the current run (id not yet known / insert failed).
        private void SyncRunRemark(int runIndex, RunRemarks remark, bool present)
        {
            var id = _currentRunIndvIds.ElementAtOrDefault(runIndex);
            if (id is not { } runIndvId) return;

            if (present)
                _ = _databaseService.InsertRunRemarkAsync(runIndvId, (int)remark);
            else
                _ = _databaseService.DeleteRunRemarkAsync(runIndvId, (int)remark);
        }

        // Updates run_pair.first_lane/winner_lane from the pair's current Results once the
        // pair completes or is aborted. No-ops if persistence isn't available for this run.
        private void PersistRunPairCompletion(Pair? pair)
        {
            if (_currentRunPairId is not { } pairId || pair?.Runs == null) return;

            int? firstLane = null;
            int? winnerLane = null;
            for (int i = 0; i < pair.Runs.Length; i++)
            {
                var r = pair.Runs[i];
                if (r?.Results?.Contains(RunResult.FirstFinish) == true) firstLane = i;
                if (r?.Results?.Contains(RunResult.Winner) == true) winnerLane = i;
            }

            _ = _databaseService.UpdateRunPairResultAsync(pairId, firstLane, winnerLane);
        }

        // Re-evaluate Winner/Lose (RunResult entries) for the engaged pair and refresh both
        // lanes' Result labels. Also refreshes both lanes' Remarks labels, since this is called
        // immediately whenever a remark that can affect the decision (Foul, NoVehicleStaged) is
        // received/toggled - Foul (and any other remark) must be reflected on screen the moment
        // it's received, not only once something else happens to touch the labels later.
        private void RefreshWinnerLoseResult(long? nowNs = null)
        {
            try { PulsarUI.Services.TimingHelpers.EvaluateWinnerLoseResult(EngagedPairModel, nowNs); }
            catch (Exception ex) { LocalLog("EvaluateWinnerLoseResult error: " + ex.Message); }
            var left = EngagedPairModel?.Runs?.ElementAtOrDefault(0);
            var right = EngagedPairModel?.Runs?.ElementAtOrDefault(1);
            UpdateResultLabel(0, left);
            UpdateResultLabel(1, right);
            RefreshRemarksLabel(0, left);
            RefreshRemarksLabel(1, right);

            // Results may have just changed - re-evaluate F9/Clear availability.
            UpdateButtonStatuses();
        }

        // Check completion conditions for a run and, when finished, optionally set RunActive=false and log run to a file
        private void MaybeCheckAndCompleteRun(int runIndex, int finishDistanceMm, Run run)
        {
            try
            {
                if (run == null || runIndex < 0) return;

                // If finishDistanceMm == 0 treat as no explicit finish (don't auto-complete here)
                if (finishDistanceMm == 0) return;

                // Determine if this run has a timing at the finish (in IncrementalTimes) or a result time
                var hasFinishTime = false;
                long finishTimestampNs = 0;
                long finishEtNs = 0;

                if (run.IncrementalTimes != null && run.IncrementalTimes.Length > 0)
                {
                    foreach (var it in run.IncrementalTimes)
                    {
                        if (it?.Input?.DistanceMm == finishDistanceMm && it.ValueNs > 0)
                        {
                            hasFinishTime = true;
                            finishTimestampNs = it.TimestampNanoseconds;
                            finishEtNs = it.ValueNs;
                            break;
                        }
                    }
                }

                // Also consider ReactionTime as a proxy for completion if necessary
                if (!hasFinishTime && run.ReactionTime != null && run.ReactionTime.ValueNs > 0 && finishDistanceMm == 0)
                    hasFinishTime = true;

                if (!hasFinishTime) return;

                // Record this lane's own finish ET (duration, ns) - the canonical,
                // order-independent "this lane has finished" signal, used both by
                // breakout evaluation and by EvaluateWinnerLoseResult to gate the
                // Winner/Lose decision (as opposed to RunResult.FirstFinish below,
                // which may now be assigned early/tentatively for display purposes).
                run.FinishEtNs = finishEtNs;

                try
                {
                    PulsarUI.Services.TimingHelpers.EvaluateBreakoutRemark(run, EngagedPairModel?.RunMode, EngagedPairModel?.Category, finishEtNs, out var breakoutChanged, out var isBreakoutNow);
                    if (breakoutChanged)
                    {
                        SyncRunRemark(runIndex, RunRemarks.Breakout, isBreakoutNow);
                    }
                }
                catch (Exception ex)
                {
                    LocalLog("EvaluateBreakoutRemark error: " + ex.Message);
                }

                // A finish time exists for this lane. If the other lane either has a finish or is NoVehicleStaged,
                // then conclude the pair and mark RunActive false and log the pair.
                bool otherLaneFinished = true;
                bool otherLaneNoVehicle = false;
                Run? other = null;
                int otherIndex = runIndex == 0 ? 1 : 0;
                long otherFinishTimestampNs = 0;
                if (EngagedPairModel?.Runs != null)
                {
                    other = EngagedPairModel.Runs.ElementAtOrDefault(otherIndex);
                    if (other != null)
                    {
                        otherLaneNoVehicle = other.Remarks?.Contains(RunRemarks.NoVehicleStaged) ?? false;
                        // Check if other has finish time
                        otherLaneFinished = false;
                        if (other.IncrementalTimes != null)
                        {
                            foreach (var it2 in other.IncrementalTimes)
                            {
                                if (it2?.Input?.DistanceMm == finishDistanceMm && it2.ValueNs > 0)
                                {
                                    otherLaneFinished = true;
                                    otherFinishTimestampNs = it2.TimestampNanoseconds;
                                    break;
                                }
                            }
                        }
                    }
                }

                bool thisLaneNoVehicle = run.Remarks?.Contains(RunRemarks.NoVehicleStaged) ?? false;

                // Assign "first to the stripe" (RunResult.FirstFinish) as soon as it can be
                // established, for on-screen display - the instant THIS lane's own finish
                // arrives, if the other lane hasn't finished yet, this lane is tentatively
                // first. Once the other lane's own finish later arrives (its own call to
                // this method), the comparison is redone from the actual hardware finish
                // timestamps and will correct/confirm/swap the decision (e.g. if the other
                // lane's MQTT message was merely delayed in transit but its real hardware
                // timestamp turns out earlier). This block is NOT gated on "already
                // decided" - it fully recomputes every time either lane's own finish
                // arrives. NOTE: this only controls the FirstFinish *display* tag - the
                // overall Winner/Lose decision in EvaluateWinnerLoseResult is separately
                // and still correctly gated on BOTH lanes' Run.FinishEtNs being known, so
                // awarding this tag early does not award the run early.
                if (!thisLaneNoVehicle && !otherLaneNoVehicle && finishTimestampNs > 0)
                {
                    try
                    {
                        if (!otherLaneFinished || otherFinishTimestampNs <= 0)
                        {
                            // Only this lane has finished so far - tentatively first.
                            AddRunResult(run, RunResult.FirstFinish);
                            RemoveRunResult(run, RunResult.Indeterminate);
                            if (other != null)
                            {
                                RemoveRunResult(other, RunResult.FirstFinish);
                                RemoveRunResult(other, RunResult.Indeterminate);
                            }
                        }
                        else if (finishTimestampNs == otherFinishTimestampNs)
                        {
                            RemoveRunResult(run, RunResult.FirstFinish);
                            AddRunResult(run, RunResult.Indeterminate);
                            if (other != null)
                            {
                                RemoveRunResult(other, RunResult.FirstFinish);
                                AddRunResult(other, RunResult.Indeterminate);
                            }
                            LocalLog($"MaybeCheckAndCompleteRun: finish-line tie at {finishTimestampNs}ns; both runs marked Indeterminate.");
                        }
                        else if (finishTimestampNs < otherFinishTimestampNs)
                        {
                            AddRunResult(run, RunResult.FirstFinish);
                            RemoveRunResult(run, RunResult.Indeterminate);
                            if (other != null)
                            {
                                RemoveRunResult(other, RunResult.FirstFinish);
                                RemoveRunResult(other, RunResult.Indeterminate);
                            }
                        }
                        else
                        {
                            RemoveRunResult(run, RunResult.FirstFinish);
                            RemoveRunResult(run, RunResult.Indeterminate);
                            if (other != null)
                            {
                                AddRunResult(other, RunResult.FirstFinish);
                                RemoveRunResult(other, RunResult.Indeterminate);
                            }
                        }

                        UpdateResultLabel(runIndex, run);
                        if (other != null) UpdateResultLabel(otherIndex, other);
                    }
                    catch (Exception ex)
                    {
                        LocalLog("MaybeCheckAndCompleteRun: failed to assign First-finish result: " + ex.Message);
                    }
                }

                // Re-evaluate Winner/Lose every time we check for completion: picks up the
                // conventional first-finish win as soon as RunResult.FirstFinish is assigned
                // above (foul-based decisions, evaluated earlier in the run, always take priority).
                RefreshWinnerLoseResult();

                if (otherLaneFinished || otherLaneNoVehicle)
                {
                    // We consider the run/pair complete, but keep the system engaged until processes have finished
                    RunActive = false;

                    // Log the completed pair to a temporary file for now
                    try
                     {
                        var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = false };
                        var payload = System.Text.Json.JsonSerializer.Serialize(EngagedPairModel, options);
                        System.IO.File.AppendAllText(_runLogPath, DateTime.Now.ToString("o") + " " + payload + Environment.NewLine);
                        _runCompletionLogged = true;
                    }
                    catch (Exception ex)
                    {
                        LocalLog("MaybeCheckAndCompleteRun: failed to write run log: " + ex.Message);
                    }

                    // Persist first_lane/winner_lane against the run_pair row created when the
                    // run went active. Captured before SystemEngaged=false (below) nulls
                    // EngagedPairModel.
                    try
                    {
                        PersistRunPairCompletion(EngagedPairModel);
                    }
                    catch (Exception ex)
                    {
                        LocalLog("MaybeCheckAndCompleteRun: PersistRunPairCompletion failed: " + ex.Message);
                    }

                    // Build and publish timingdata/complete payload
                    try
                    {
                        var pair = EngagedPairModel;
                        // Diagnostic log before attempting to build/publish
                        LocalLog($"MaybeCheckAndCompleteRun: preparing timingdata/complete publish. pairPresent={(pair!=null)} runsCount={(pair?.Runs?.Length.ToString() ?? "null")} mqttServicePresent={(_mqttService!=null)} RunStartNs={RunStartTimestampNanoseconds}");
                        if (pair != null && pair.Runs != null && _mqttService != null)
                        {
                            // Timestamp: use RunStartTimestampNanoseconds (ns) -> milliseconds
                            long ms = RunStartTimestampNanoseconds / 1_000_000L;
                            var isoTs = DateTimeOffset.FromUnixTimeMilliseconds(ms).ToUniversalTime().ToString("o");

                            var categoryId = pair.Category?.Id ?? EngagePairQueueCategory?.Category ?? 0;
                            var roundMode = pair.RunMode?.ToString() ?? EngagePairQueueCategory?.Mode.ToString() ?? string.Empty;
                            var roundNumber = EngagePairQueueCategory?.Round ?? 0;

                            var runsList = pair.Runs.Select((r, idx) =>
                            {
                                var laneLetter = idx == 0 ? "L" : "R";
                                var raceNumber = r?.Entry?.RaceNumber ?? string.Empty;
                                var indexStr = r?.Entry?.HandicapIndex ?? string.Empty;
                                // Reaction time: delta = ValueNs - ExpectedReactionTimeNs -> seconds
                                decimal reaction = 0m;
                                if (r?.ReactionTime != null)
                                {
                                    try
                                    {
                                        var deltaNs = r.ReactionTime.ValueNs - r.ReactionTime.ExpectedReactionTimeNs;
                                        reaction = decimal.Round(deltaNs / 1_000_000_000m, 4);
                                    }
                                    catch { reaction = 0m; }
                                }

                                var incTimes = (r?.IncrementalTimes ?? System.Array.Empty<IncrementalTime>())
                                    .Where(it => it?.Input != null)
                                    .Select(it => new
                                    {
                                        distanceMm = it.Input!.DistanceMm,
                                        time = decimal.Round(it.ValueNs / 1_000_000_000m, 4)
                                    }).ToArray();

                                var incSpeeds = (r?.IncrementalSpeeds ?? System.Array.Empty<IncrementalSpeed>())
                                    .Where(sp => sp?.Input != null)
                                    .Select(sp => new
                                    {
                                        distanceMm = sp.Input!.DistanceMm,
                                        speed = decimal.Round(sp.MetersPerSecond, 3)
                                    }).ToArray();

                                return new
                                {
                                    lane = laneLetter,
                                    raceNumber,
                                    index = indexStr,
                                    deepStage = false,
                                    reaction,
                                    incrementalTimes = incTimes,
                                    incrementalSpeeds = incSpeeds
                                };
                            }).ToArray();

                            var completePayload = new
                            {
                                pairId = pair.Id,
                                timestamp = isoTs,
                                category = categoryId,
                                roundMode = roundMode,
                                roundNumber = roundNumber,
                                runs = runsList
                            };

                            var serOptions = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
                            var payloadJson = System.Text.Json.JsonSerializer.Serialize(completePayload, serOptions);
                            LocalLog("MaybeCheckAndCompleteRun: invoking _mqttService.PublishMqtt for timingdata/complete");
                            _ = _mqttService.PublishMqtt("timingdata/complete", payloadJson);
                            LocalLog("MaybeCheckAndCompleteRun: PublishMqtt invoked for timingdata/complete");
                            MaybeDebug("Published timingdata/complete payload: " + payloadJson);
                        }
                        else
                        {
                            LocalLog("MaybeCheckAndCompleteRun: skipped publish because preconditions not met (pair or mqtt service missing)");
                        }
                    }
                    catch (Exception ex)
                    {
                        LocalLog("MaybeCheckAndCompleteRun: failed to publish timingdata/complete: " + ex.Message);
                    }
                    
                    // Ensure the system is no longer engaged when the run/pair completes
                    SystemEngaged = false;

                    UpdateButtonStatuses();
                }
            }
            catch (Exception ex)
            {
                LocalLog("MaybeCheckAndCompleteRun error: " + ex.Message);
            }
        }
    }
}
