using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Data;
using System.Globalization;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using StopCut.Controls;
using StopCut.Models;
using StopCut.Mvvm;
using StopCut.Services;

namespace StopCut.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly DataStore _store = new();
    private readonly TigerStopController _tigerStop = new();
    private readonly StringBuilder _log = new();
    private readonly List<(DateTime Time, bool Incoming, byte[] Data)> _terminalEntries = new();
    private readonly StringBuilder _terminal = new();
    private bool _terminalHex;
    private bool _terminalTimestamps = true;
    private string _terminalInput = "";
    private string _terminalLineEnding = "\\r";
    private readonly List<byte> _rxPending = new();
    private readonly DispatcherTimer _rxFlushTimer = new() { Interval = TimeSpan.FromMilliseconds(1000) };
    private bool _rxSawCr;
    private readonly DispatcherTimer _serialQueryTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private bool _awaitingSerial;
    private readonly StringBuilder _versionReply = new();
    private readonly StringBuilder _versionRaw = new();
    private bool _serialConfirmed;
    private int _versionRetries;
    private bool _awaitingPosition;
    private bool _awaitingMmEnable;
    private readonly DispatcherTimer _moveReplyTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private string _lastMoveCommand = "";
    private string _padEntry = "";
    private int _mmEnableLinesLeft;
    private LengthUnit _units;
    private bool _searching;
    private string _machineStatus = "";
    private string _currentPosition = "-";

    private Material? _selectedMaterial;
    private PlannedCut? _selectedCut;
    private Leftover? _selectedLeftover;
    private Material? _newLeftoverMaterial;
    private string _newLeftoverLength = "";
    private string _manualPosition = "";
    private string _planSummary = "No plan yet. Pick a material and press Build plan.";
    private string _connectionStatus = "Not connected";
    private bool _isConnected;
    private bool _autoSendNext = true;
    private string[] _ports = Array.Empty<string>();

    public MainViewModel()
    {
        var data = _store.Load();
        if (_store.LoadProblem is { } problem)
            Application.Current?.Dispatcher.InvokeAsync(() =>
                MessageBox.Show(problem, "StopCut", MessageBoxButton.OK, MessageBoxImage.Warning));
        Materials = new ObservableCollection<Material>(data.Materials);
        Leftovers = new ObservableCollection<Leftover>(data.Leftovers);
        foreach (var m in Materials.Where(m => m.Letter.Length == 0)) m.Letter = NextMaterialLetter();
        // Re-ID leftovers saved without an ID, or with one that doesn't start with their material's letter.
        foreach (var l in Leftovers.OrderBy(l => l.Created).ToList())
        {
            var material = Materials.FirstOrDefault(m => m.Id == l.MaterialId);
            if (material is null || Regex.IsMatch(l.Tag, $"^{Regex.Escape(material.Letter)}\\d+$")) continue;
            l.Tag = "";
            l.Tag = NextLeftoverTag(material);
        }
        Serial = data.Serial;
        _units = data.Units;
        _jobsFolder = data.JobsFolder;
        _spacerMinPartMm = data.SpacerMinPartMm;
        _spacerLengthMm = data.SpacerLengthMm;
        _importedJobFiles = data.ImportedJobFiles;
        if (Serial.MoveCommandTemplate is "{pos}" or "mg{pos}")
        {
            // Settings saved by earlier versions of the app.
            Serial.MoveCommandTemplate = SerialSettings.DefaultMoveCommand;
            Serial.Terminator = SerialSettings.DefaultTerminator;
        }
        foreach (var m in Materials) WatchCuts(m);
        Materials.CollectionChanged += (_, e) =>
        {
            foreach (var m in e.NewItems?.OfType<Material>() ?? Enumerable.Empty<Material>())
            {
                WatchCuts(m);
                if (m.Letter.Length == 0) m.Letter = NextMaterialLetter();
            }
        };
        JobMaterials = new ListCollectionView(Materials) { Filter = o => o is Material m && MaterialInJob(m) };
        RefreshJobs();
        SelectedMaterial = Materials.FirstOrDefault();
        NewLeftoverMaterial = SelectedMaterial;

        _tigerStop.Log += line => Application.Current?.Dispatcher.InvokeAsync(() => AppendLog(line));
        _tigerStop.Received += data => Application.Current?.Dispatcher.InvokeAsync(() => OnReceived(data));
        _rxFlushTimer.Tick += (_, _) => FlushRx();
        _moveReplyTimer.Tick += (_, _) =>
        {
            _moveReplyTimer.Stop();
            MachineStatus = $"No answer from the TigerStop to the move ({_lastMoveCommand}). " +
                            "Check the Terminal tab to see what it sent back.";
        };
        _serialQueryTimer.Tick += (_, _) => SerialQueryTimedOut();
        _tigerStop.Sent += data => Application.Current?.Dispatcher.InvokeAsync(() => AddTerminalEntry(false, data));

        AddMaterialCommand = new RelayCommand(AddMaterial);
        ImportCommand = new RelayCommand(ImportPartsList);
        UpdateFromJobsFolderCommand = new RelayCommand(() => ScanJobsFolder(true));
        DeleteJobCommand = new RelayCommand(DeleteJob, () => JobFilter is not null);
        DeleteMaterialCommand = new RelayCommand(() => DeleteMaterial(SelectedMaterial), () => SelectedMaterial is not null);
        AddLibraryMaterialCommand = new RelayCommand(AddLibraryMaterial);
        DeleteLibraryMaterialCommand = new RelayCommand(() => DeleteMaterial(LibrarySelection), () => LibrarySelection is not null);
        ResetDoneCommand = new RelayCommand(ResetDone, () => SelectedMaterial is not null);
        SaveCommand = new RelayCommand(Save);
        BuildPlanCommand = new RelayCommand(
            () => BuildPlan(SelectedMaterial is { } m ? new[] { m } : Array.Empty<Material>()),
            () => SelectedMaterial is not null);
        BuildJobPlanCommand = new RelayCommand(
            () => BuildPlan(Materials.Where(MaterialInJob).ToList()),
            () => Materials.Any(MaterialInJob));
        NextMaterialCommand = new RelayCommand(NextMaterial, () => NextMaterialCut() is not null);
        SendPositionCommand = new RelayCommand(SendPosition, () => IsConnected && SelectedCut is { Status: CutStatus.Pending });
        CutDoneCommand = new RelayCommand(CutDone, () => SelectedCut is { Status: CutStatus.Pending });
        SkipCutCommand = new RelayCommand(SkipCut, () => SelectedCut is { Status: CutStatus.Pending });
        AddLeftoverCommand = new RelayCommand(AddLeftover, () => NewLeftoverMaterial is not null);
        DeleteLeftoverCommand = new RelayCommand(DeleteLeftover, () => SelectedLeftover is not null);
        RefreshPortsCommand = new RelayCommand(RefreshPorts);
        ConnectCommand = new RelayCommand(Connect, () => !IsConnected && !_searching);
        FindTigerStopCommand = new RelayCommand(FindTigerStop, () => !_searching);
        HomeCommand = new RelayCommand(() => SendMachineCommand(TigerStopController.HomeCommand), () => IsConnected);
        StopCommand = new RelayCommand(() => SendMachineCommand(TigerStopController.StopCommand), () => IsConnected);
        EmergencyStopCommand = new RelayCommand(() => SendMachineCommand(TigerStopController.EmergencyStopCommand), () => IsConnected);
        QueryPositionCommand = new RelayCommand(QueryPosition, () => IsConnected);
        PadKeyCommand = new RelayCommand(p => PadKey(p as string));
        PadBackspaceCommand = new RelayCommand(() => PadEntry = PadEntry.Length > 0 ? PadEntry[..^1] : "");
        PadClearCommand = new RelayCommand(() => PadEntry = "");
        // Always enabled so a press always says what happened, even when not connected.
        PadGoCommand = new RelayCommand(PadGo);
        DisconnectCommand = new RelayCommand(Disconnect, () => IsConnected);
        MoveManualCommand = new RelayCommand(MoveManual, () => IsConnected);
        SendTerminalCommand = new RelayCommand(SendTerminal, () => IsConnected);
        ClearTerminalCommand = new RelayCommand(ClearTerminal);

        RefreshPorts();
    }

    // ---- Data ----

    public ObservableCollection<Material> Materials { get; }
    public ObservableCollection<Leftover> Leftovers { get; }
    public ObservableCollection<PlannedCut> Plan { get; } = new();
    public SerialSettings Serial { get; }

    public LengthUnit[] UnitOptions { get; } = Enum.GetValues<LengthUnit>();

    /// <summary>
    /// Units lengths are entered and shown in. Changing it converts every stored length
    /// (materials, cut lists, leftovers, position offset) so nothing changes physically.
    /// </summary>
    public LengthUnit Units
    {
        get => _units;
        set
        {
            var old = _units;
            if (!Set(ref _units, value)) return;
            ConvertAllLengths(old, value);
            OnPropertyChanged(nameof(UnitLabel));
            Save();
        }
    }

    public string UnitLabel => Models.Units.Label(Units);

    public LengthUnit MachineUnits
    {
        get => Serial.MachineUnits;
        set
        {
            if (Serial.MachineUnits == value) return;
            Serial.MachineUnits = value;
            OnPropertyChanged();
            Save();
        }
    }

    private void ConvertAllLengths(LengthUnit from, LengthUnit to)
    {
        double C(double v) => Models.Units.Convert(v, from, to);
        foreach (var m in Materials)
        {
            m.StockLength = C(m.StockLength);
            m.Kerf = C(m.Kerf);
            m.TrimCut = C(m.TrimCut);
            m.MinLeftover = C(m.MinLeftover);
            foreach (var cut in m.Cuts) cut.Length = C(cut.Length);
        }
        foreach (var l in Leftovers) l.Length = C(l.Length);
        Serial.PositionOffset = C(Serial.PositionOffset);
        OnPropertyChanged(nameof(Serial));

        Plan.Clear();
        UpdateStick();
        PlanSummary = $"Units changed to {Models.Units.Label(to)}. Build the plan again.";
        CurrentPosition = "-";
        if (IsConnected) QueryPosition();
    }

    private double ToMachine(double appLength) => Models.Units.Convert(appLength, Units, Serial.MachineUnits);

    private string FormatMachinePosition(double machineValue) =>
        $"{Models.Units.Convert(machineValue, Serial.MachineUnits, Units):0.###} {UnitLabel}";

    public Material? SelectedMaterial
    {
        get => _selectedMaterial;
        set
        {
            if (Set(ref _selectedMaterial, value)) RefreshCutsView();
        }
    }

    // ---- Jobs ----

    public const string AllJobs = "All jobs";
    public const string NoJob = "No job";
    private string _selectedJob = AllJobs;
    private bool _refreshingJobs;
    private ICollectionView? _selectedMaterialCuts;

    /// <summary>Job names to pick from: "All jobs", each imported job, and "No job" for hand-entered cuts.</summary>
    public ObservableCollection<string> Jobs { get; } = new();

    public string SelectedJob
    {
        get => _selectedJob;
        set
        {
            if (_refreshingJobs || !Set(ref _selectedJob, value ?? AllJobs)) return;
            JobMaterials.Refresh();
            if (SelectedMaterial is not null && !MaterialInJob(SelectedMaterial))
                SelectedMaterial = JobMaterials.Cast<Material>().FirstOrDefault();
            RefreshCutsView();
        }
    }

    /// <summary>Materials that have cuts in the selected job (plus empty ones, so new materials show up).</summary>
    public ICollectionView JobMaterials { get; }

    /// <summary>The selected material's cuts, limited to the selected job.</summary>
    public ICollectionView? SelectedMaterialCuts { get => _selectedMaterialCuts; private set => Set(ref _selectedMaterialCuts, value); }

    private string? JobFilter => SelectedJob switch
    {
        AllJobs => null,
        NoJob => "",
        _ => SelectedJob,
    };

    private bool InSelectedJob(CutItem c) => JobFilter is not { } job || c.Job == job;

    private bool MaterialInJob(Material m) => m.Cuts.Count == 0 || m.Cuts.Any(InSelectedJob);

    private void RefreshCutsView()
    {
        SelectedMaterialCuts = SelectedMaterial is null
            ? null
            : new ListCollectionView(SelectedMaterial.Cuts) { Filter = o => o is CutItem c && InSelectedJob(c) };
    }

    private void RefreshJobs()
    {
        var names = Materials.SelectMany(m => m.Cuts).Select(c => c.Job).Distinct().ToList();
        var wanted = new List<string> { AllJobs };
        wanted.AddRange(names.Where(n => n.Length > 0).OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
        if (names.Contains("")) wanted.Add(NoJob);
        if (!wanted.Contains(SelectedJob)) wanted.Add(SelectedJob);
        if (wanted.SequenceEqual(Jobs)) return;
        // Rebuilding the list makes bound combo boxes push null back; ignore that.
        _refreshingJobs = true;
        try
        {
            Jobs.Clear();
            foreach (var n in wanted) Jobs.Add(n);
        }
        finally
        {
            _refreshingJobs = false;
        }
        OnPropertyChanged(nameof(SelectedJob));
    }

    /// <summary>Rows added by hand go into the selected job.</summary>
    private void WatchCuts(Material m) => m.Cuts.CollectionChanged += (_, e) =>
    {
        if (e.Action != NotifyCollectionChangedAction.Add || JobFilter is not { Length: > 0 } job) return;
        foreach (var c in e.NewItems!.OfType<CutItem>())
            if (c.Job.Length == 0) c.Job = job;
    };

    private void DeleteJob()
    {
        if (JobFilter is not { } job) return;
        var count = Materials.Sum(m => m.Cuts.Count(c => c.Job == job));
        if (MessageBox.Show($"Delete all {count} cut lines of \"{SelectedJob}\"?", "Delete job",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        foreach (var m in Materials)
            foreach (var c in m.Cuts.Where(c => c.Job == job).ToList())
                m.Cuts.Remove(c);
        SelectedJob = AllJobs;
        RefreshJobs();
        Save();
    }
    public PlannedCut? SelectedCut
    {
        get => _selectedCut;
        set
        {
            if (!Set(ref _selectedCut, value)) return;
            UpdateStick();
            if (value is { Status: CutStatus.Pending })
                ShowNowCutting(value.Length, $"{value.Label}   (stick {value.BarNumber})");
        }
    }

    // ---- Large "now cutting / last cut" display ----

    private string _nowCuttingSize = "-";
    private string _nowCuttingLabel = "";
    private string _lastCutSize = "-";
    private string _lastCutLabel = "";

    public string NowCuttingSize { get => _nowCuttingSize; private set => Set(ref _nowCuttingSize, value); }
    public string NowCuttingLabel { get => _nowCuttingLabel; private set => Set(ref _nowCuttingLabel, value); }
    public string LastCutSize { get => _lastCutSize; private set => Set(ref _lastCutSize, value); }
    public string LastCutLabel { get => _lastCutLabel; private set => Set(ref _lastCutLabel, value); }

    private StickDrawing? _currentStick;
    private string _currentStickTitle = "";

    /// <summary>The stick the selected cut comes from, for the stick drawing.</summary>
    public StickDrawing? CurrentStick { get => _currentStick; private set => Set(ref _currentStick, value); }
    public string CurrentStickTitle { get => _currentStickTitle; private set => Set(ref _currentStickTitle, value); }

    private string _stickToUse = "";
    private bool _stickIsLeftover;

    /// <summary>Which stick to load: "USE LEFTOVER A3" or "NEW STICK"; empty when there is no plan.</summary>
    public string StickToUse { get => _stickToUse; private set => Set(ref _stickToUse, value); }
    public bool StickIsLeftover { get => _stickIsLeftover; private set => Set(ref _stickIsLeftover, value); }

    private void UpdateStick()
    {
        var bar = SelectedCut?.Bar ?? Plan.LastOrDefault()?.Bar;
        CurrentMaterialName = SelectedCut is { Status: CutStatus.Pending } now ? now.Bar.Material.Name : "";
        SpacerWarning = SelectedCut is { Status: CutStatus.Pending, Spacer: > 0 } spaced ? spaced.SpacerNote : "";
        if (bar is null)
        {
            CurrentStick = null;
            CurrentStickTitle = "";
            StickToUse = "";
            StickIsLeftover = false;
            return;
        }
        var m = bar.Material;
        var pieces = bar.Cuts.Select(c => (c.Length, c == SelectedCut && c.Status == CutStatus.Pending
            ? PieceState.Current
            : c.Status switch { CutStatus.Done => PieceState.Done, CutStatus.Skipped => PieceState.Skipped, _ => PieceState.Pending }))
            .ToList();
        CurrentStick = new StickDrawing($"Stick {bar.Number}", bar.StartLength, m.TrimCut, m.Kerf, m.MinLeftover, pieces, UnitLabel);
        StickIsLeftover = bar.LeftoverId is not null;
        StickToUse = SelectedCut is not { Status: CutStatus.Pending } ? ""
            : StickIsLeftover ? $"USE LEFTOVER {(bar.LeftoverTag.Length > 0 ? bar.LeftoverTag : bar.Source)}"
            : "NEW STICK";
        var sticks = Plan.Select(c => c.Bar).Distinct().Count();
        CurrentStickTitle = $"Stick {bar.Number} of {sticks} ({m.Name}): {bar.Source}, {bar.StartLength:0.###} {UnitLabel}" +
                            $"  ({bar.Cuts.Count(c => c.Status == CutStatus.Done)}/{bar.Cuts.Count} cut)";
    }

    private void ShowNowCutting(double length, string label)
    {
        NowCuttingSize = $"{length:0.###} {UnitLabel}";
        NowCuttingLabel = label;
    }
    public Leftover? SelectedLeftover { get => _selectedLeftover; set => Set(ref _selectedLeftover, value); }
    public Material? NewLeftoverMaterial { get => _newLeftoverMaterial; set => Set(ref _newLeftoverMaterial, value); }
    public string NewLeftoverLength { get => _newLeftoverLength; set => Set(ref _newLeftoverLength, value); }
    public string ManualPosition { get => _manualPosition; set => Set(ref _manualPosition, value); }
    public string PlanSummary { get => _planSummary; private set => Set(ref _planSummary, value); }
    public string ConnectionStatus { get => _connectionStatus; private set => Set(ref _connectionStatus, value); }
    public bool IsConnected { get => _isConnected; private set => Set(ref _isConnected, value); }
    public bool AutoSendNext { get => _autoSendNext; set => Set(ref _autoSendNext, value); }
    public string[] Ports { get => _ports; private set => Set(ref _ports, value); }
    public string LogText => _log.ToString();

    public int[] BaudRates { get; } = { 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200 };
    public Parity[] ParityOptions { get; } = Enum.GetValues<Parity>();
    public StopBits[] StopBitsOptions { get; } = { StopBits.One, StopBits.OnePointFive, StopBits.Two };
    public Handshake[] HandshakeOptions { get; } = Enum.GetValues<Handshake>();

    // ---- Commands ----

    public ICommand AddMaterialCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand UpdateFromJobsFolderCommand { get; }
    public ICommand DeleteJobCommand { get; }
    public ICommand DeleteMaterialCommand { get; }
    public ICommand AddLibraryMaterialCommand { get; }
    public ICommand DeleteLibraryMaterialCommand { get; }
    public ICommand ResetDoneCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand BuildPlanCommand { get; }
    public ICommand BuildJobPlanCommand { get; }
    public ICommand NextMaterialCommand { get; }
    public ICommand SendPositionCommand { get; }
    public ICommand CutDoneCommand { get; }
    public ICommand SkipCutCommand { get; }
    public ICommand AddLeftoverCommand { get; }
    public ICommand DeleteLeftoverCommand { get; }
    public ICommand RefreshPortsCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand MoveManualCommand { get; }
    public ICommand FindTigerStopCommand { get; }
    public ICommand HomeCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand EmergencyStopCommand { get; }
    public ICommand QueryPositionCommand { get; }
    public ICommand PadKeyCommand { get; }
    public ICommand PadBackspaceCommand { get; }
    public ICommand PadClearCommand { get; }
    public ICommand PadGoCommand { get; }

    // ---- Dashboard number pad ----

    /// <summary>Length typed on the dashboard pad, in the current units.</summary>
    public string PadEntry { get => _padEntry; set => Set(ref _padEntry, value); }

    private void PadKey(string? key)
    {
        if (string.IsNullOrEmpty(key) || PadEntry.Length >= 10) return;
        if (key == "." && PadEntry.Contains('.')) return;
        PadEntry = key == "." && PadEntry.Length == 0 ? "0." : PadEntry + key;
    }

    private void PadGo()
    {
        if (!IsConnected)
        {
            MachineStatus = "Not connected. Press Connect at the top, or Settings > TigerStop connection > Find TigerStop.";
            return;
        }
        if (!double.TryParse(PadEntry, NumberStyles.Float, CultureInfo.InvariantCulture, out var length) || length <= 0)
        {
            MachineStatus = "Enter a length first";
            return;
        }
        ShowNowCutting(length, "Manual");
        MoveTo(length);
    }

    /// <summary>What the amp last reported: moving, arrived, rejected...</summary>
    public string MachineStatus { get => _machineStatus; private set => Set(ref _machineStatus, value); }

    /// <summary>Last position the amp reported.</summary>
    public string CurrentPosition { get => _currentPosition; private set => Set(ref _currentPosition, value); }
    public ICommand SendTerminalCommand { get; }
    public ICommand ClearTerminalCommand { get; }

    // ---- Terminal ----

    public string TerminalText => _terminal.ToString();
    public string TerminalInput { get => _terminalInput; set => Set(ref _terminalInput, value); }

    /// <summary>Appended to typed terminal input. Escapes \r \n \t are allowed; empty sends nothing extra.</summary>
    public string TerminalLineEnding { get => _terminalLineEnding; set => Set(ref _terminalLineEnding, value); }

    public bool TerminalHex
    {
        get => _terminalHex;
        set { if (Set(ref _terminalHex, value)) RenderTerminal(); }
    }

    public bool TerminalTimestamps
    {
        get => _terminalTimestamps;
        set { if (Set(ref _terminalTimestamps, value)) RenderTerminal(); }
    }

    private void SendTerminal()
    {
        try
        {
            _tigerStop.SendRaw(TigerStopController.Unescape(TerminalInput) + TigerStopController.Unescape(TerminalLineEnding));
            TerminalInput = "";
        }
        catch (Exception ex)
        {
            AppendLog("Send failed: " + ex.Message);
            MessageBox.Show(ex.Message, "Terminal", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClearTerminal()
    {
        _terminalEntries.Clear();
        RenderTerminal();
    }

    private void AddTerminalEntry(bool incoming, byte[] data)
    {
        _terminalEntries.Add((DateTime.Now, incoming, data));
        if (_terminalEntries.Count > 5000)
        {
            _terminalEntries.RemoveRange(0, 1000);
            RenderTerminal();
            return;
        }
        AppendTerminalEntry(_terminalEntries[^1]);
        OnPropertyChanged(nameof(TerminalText));
    }

    /// <summary>
    /// Serial data arrives in arbitrary chunks (often one byte at a time), so collect it into lines.
    /// A line ends at CR, LF or CRLF; a partial line is shown after a short pause with no data.
    /// </summary>
    private void OnReceived(byte[] data)
    {
        foreach (var b in data)
        {
            if (_rxSawCr)
            {
                _rxSawCr = false;
                if (b == (byte)'\n')
                {
                    _rxPending.Add(b);
                    FlushRx();
                    continue;
                }
                FlushRx();
            }
            _rxPending.Add(b);
            if (b == (byte)'\r') _rxSawCr = true;
            else if (b == (byte)'\n' || _rxPending.Count >= 1024) FlushRx();
        }
        _rxFlushTimer.Stop();
        if (_rxPending.Count > 0) _rxFlushTimer.Start();

        if (_awaitingSerial)
        {
            // Firmware 5.x sends its banner slowly and in pieces; check it as it arrives instead of
            // waiting for each piece's line to be flushed.
            _versionRaw.Append(Encoding.ASCII.GetString(data));
            var raw = Regex.Replace(_versionRaw.ToString(), @"[\r\n\0]+", " ").Trim();
            if (TigerStopController.IsVersionReply(raw) && Regex.IsMatch(raw, @"\d\.\d")) ConfirmTigerStop(raw);
        }
    }

    private void FlushRx()
    {
        _rxFlushTimer.Stop();
        _rxSawCr = false;
        if (_rxPending.Count == 0) return;
        var line = _rxPending.ToArray();
        _rxPending.Clear();
        AddTerminalEntry(true, line);
        var text = Encoding.ASCII.GetString(line).TrimEnd('\r', '\n');
        AppendLog("RX " + TigerStopController.Escape(text));
        HandleMachineLine(text);
    }

    /// <summary>
    /// Reads the amp's replies (TigerStop RS-232 Comm Spec) to keep status and position current:
    /// "MGS x" move started, "MGF" move finished, "X EC=n text" error, a bare number for P.
    /// </summary>
    private void HandleMachineLine(string line)
    {
        var text = line.Trim('\0', ' ', '\r', '\n');
        if (text.Length == 0) return;

        if (!_serialConfirmed && IsMachineReply(text))
        {
            // Any real reply (a move, stop, error or position) proves the amp is there, even if the
            // version query went unanswered.
            ConfirmTigerStop(null);
        }
        else if (_awaitingSerial)
        {
            // The amp can send its banner in pieces, e.g. "TigerSt" / "op Amp II" / "I ver = 5.23".
            _versionReply.Append(text);
            var banner = _versionReply.ToString();
            if (TigerStopController.IsVersionReply(banner) && banner.Any(char.IsDigit)) ConfirmTigerStop(banner);
            return;
        }

        if (_awaitingMmEnable && !text.StartsWith("X", StringComparison.Ordinal))
        {
            var numbers = Regex.Matches(text, @"-?\d+(\.\d+)?");
            var isEcho = text.StartsWith("d", StringComparison.OrdinalIgnoreCase) && numbers.Count == 1;
            if (!isEcho && numbers.Count > 0 &&
                double.TryParse(numbers[^1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var mm) &&
                mm is 0 or 1)
            {
                _awaitingMmEnable = false;
                MachineUnits = mm == 1 ? LengthUnit.Millimeters : LengthUnit.Inches;
                AppendLog($"Amp works in {(mm == 1 ? "millimeters" : "inches")} (MMEnable = {mm})");
                QueryPosition();
                return;
            }
            if (--_mmEnableLinesLeft <= 0)
            {
                _awaitingMmEnable = false;
                QueryPosition();
            }
            return;
        }

        if (text.Contains("MGS") || text.Contains("MGF") || text.StartsWith("X", StringComparison.Ordinal)) _moveReplyTimer.Stop();

        var error = Regex.Match(text, @"^X\s*EC\s*=\s*(\d+)\s*(.*)$");
        if (error.Success)
        {
            var code = int.Parse(error.Groups[1].Value, CultureInfo.InvariantCulture);
            MachineStatus = $"TigerStop error {code}: {DescribeError(code, error.Groups[2].Value)}";
            AppendLog(MachineStatus);
            if (_awaitingMmEnable)
            {
                _awaitingMmEnable = false;
                QueryPosition();
            }
            return;
        }

        if (text.Contains("MGF"))
        {
            MachineStatus = "In position";
            if (TryParseNumber(text, out var pos)) CurrentPosition = FormatMachinePosition(pos);
            else QueryPosition();
        }
        else if (text.Contains("MGS"))
        {
            MachineStatus = TryParseNumber(text, out var target) ? $"Moving to {FormatMachinePosition(target)}" : "Moving";
        }
        else if (text.Contains("MSS") || text.Contains("MES"))
        {
            MachineStatus = "Stopped";
            QueryPosition();
        }
        else if (_awaitingPosition && TryParseNumber(text, out var reported))
        {
            _awaitingPosition = false;
            CurrentPosition = FormatMachinePosition(reported);
        }
    }

    /// <summary>True for lines only a TigerStop sends: MGS/MGF/MSS/MES, "X EC=n", or a bare position.</summary>
    private static bool IsMachineReply(string text) =>
        Regex.IsMatch(text, @"^(MGS|MGF|MSS|MES|MHS|MHF)\b") ||
        Regex.IsMatch(text, @"^X\s*EC\s*=\s*\d+") ||
        Regex.IsMatch(text, @"^-?\d+(\.\d+)?$");

    /// <summary>Marks the connection as a working TigerStop and reads the amp's units.</summary>
    private void ConfirmTigerStop(string? banner)
    {
        if (_serialConfirmed) return;
        var wasChecking = _awaitingSerial;
        _serialConfirmed = true;
        _awaitingSerial = false;
        _serialQueryTimer.Stop();
        ConnectionStatus = banner is null
            ? $"Connected on {Serial.PortName}: TigerStop is answering"
            : $"Connected on {Serial.PortName}: {Regex.Replace(banner, @"\s+", " ").Trim()}";
        if (!wasChecking) return;
        MachineStatus = "Ready";
        // Ask which units the amp uses (D44 = MMEnable: 0 inches, 1 mm); position follows.
        _awaitingMmEnable = true;
        _mmEnableLinesLeft = 3;
        SendMachineCommand("D44");
    }

    private static bool TryParseNumber(string text, out double value)
    {
        var match = Regex.Match(text, @"-?\d+(\.\d+)?");
        value = 0;
        return match.Success && double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string DescribeError(int code, string text) => code switch
    {
        1 => "unknown command",
        2 => "bad argument",
        4 => "bad move parameter",
        5 => "machine is busy moving",
        6 => "position is past the maximum limit",
        7 => "position is below the minimum limit",
        8 or 9 => "limit switch tripped",
        10 => "machine not ready (home it first?)",
        12 => "enable code not available (serial control not enabled on this amp)",
        28 => "AIK / serial automation not enabled on this amp",
        29 => "AIK full auto not enabled on this amp",
        30 => "E-stop is active",
        31 => "position error too large during the move",
        _ => string.IsNullOrWhiteSpace(text) ? "see TigerStop error code list" : text.Trim(),
    };

    private void RenderTerminal()
    {
        _terminal.Clear();
        foreach (var entry in _terminalEntries) AppendTerminalEntry(entry);
        OnPropertyChanged(nameof(TerminalText));
    }

    private void AppendTerminalEntry((DateTime Time, bool Incoming, byte[] Data) entry)
    {
        if (TerminalTimestamps) _terminal.Append(entry.Time.ToString("HH:mm:ss.fff")).Append("  ");
        _terminal.Append(entry.Incoming ? "RX  " : "TX  ");
        if (TerminalHex)
            _terminal.Append(string.Join(" ", entry.Data.Select(b => b.ToString("X2"))))
                     .Append("   | ").Append(Printable(entry.Data));
        else
            _terminal.Append(TigerStopController.Escape(Encoding.ASCII.GetString(entry.Data).TrimEnd('\r', '\n')));
        _terminal.AppendLine();
    }

    private static string Printable(byte[] data) =>
        new string(data.Select(b => b is >= 32 and < 127 ? (char)b : '.').ToArray());

    // ---- Materials and cut lists ----

    private void AddMaterial()
    {
        var material = new Material();
        if (Units == LengthUnit.Millimeters)
        {
            // Defaults are in inches.
            material.StockLength = Models.Units.Convert(material.StockLength, LengthUnit.Inches, Units);
            material.Kerf = Models.Units.Convert(material.Kerf, LengthUnit.Inches, Units);
            material.MinLeftover = Models.Units.Convert(material.MinLeftover, LengthUnit.Inches, Units);
        }
        Materials.Add(material);
        SelectedMaterial = material;
    }

    /// <summary>
    /// Imports an ERP parts list CSV: groups parts by material (creating missing materials),
    /// converts lengths from mm, and adds them to each material's cut list after a confirmation.
    /// </summary>
    private void ImportPartsList()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import parts list",
            Filter = "Parts list (*.csv)|*.csv|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog() != true) return;

        PartsListImport import;
        try
        {
            import = PartsListImporter.Read(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Import", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if (import.Parts.Count == 0)
        {
            MessageBox.Show("No parts found in this file.\n\n" + string.Join("\n", import.Problems.Take(10)),
                "Import", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var job = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName).Trim();
        var groups = import.Parts.GroupBy(p => (p.MaterialCode, p.MaterialDescription)).ToList();
        var summary = new StringBuilder();
        summary.AppendLine($"Job: {job}");
        summary.AppendLine($"{import.Parts.Count} parts ({import.Parts.Sum(p => p.Quantity)} pieces) in {groups.Count} materials:");
        summary.AppendLine();
        foreach (var g in groups)
        {
            var exists = FindMaterial(g.Key.MaterialCode, g.First().MaterialName) is not null;
            summary.AppendLine($"{g.First().MaterialName}{(exists ? "" : "  (new)")}");
            foreach (var p in g)
                summary.AppendLine($"    {p.Quantity} x {Models.Units.Convert(p.LengthMm, LengthUnit.Millimeters, Units):0.###} {UnitLabel}   {p.Label}");
        }
        if (import.Problems.Count > 0)
        {
            summary.AppendLine();
            summary.AppendLine($"Skipped {import.Problems.Count} lines:");
            foreach (var problem in import.Problems.Take(10)) summary.AppendLine("    " + problem);
        }
        summary.AppendLine();
        summary.Append("Add these to the cut lists?");

        if (MessageBox.Show(summary.ToString(), "Import parts list", MessageBoxButton.YesNo, MessageBoxImage.Question)
            != MessageBoxResult.Yes) return;

        var first = AddImportedParts(job, import, replaceJob: false);
        RefreshJobs();
        SelectedJob = job;
        SelectedMaterial = first;
        Save();
    }

    /// <summary>
    /// Adds an imported parts list to the cut lists as <paramref name="job"/>, creating missing materials.
    /// With <paramref name="replaceJob"/> the job's existing lines are replaced, keeping done counts of
    /// lines that are still in the file. Returns the first material touched.
    /// </summary>
    private Material? AddImportedParts(string job, PartsListImport import, bool replaceJob)
    {
        var oldDone = new Dictionary<(string, string, double), int>();
        if (replaceJob)
            foreach (var m in Materials)
                foreach (var c in m.Cuts.Where(c => c.Job == job).ToList())
                {
                    var key = (m.Name, c.Label, Math.Round(c.Length, 3));
                    oldDone[key] = oldDone.GetValueOrDefault(key) + c.Done;
                    m.Cuts.Remove(c);
                }

        var groups = import.Parts.GroupBy(p => (p.MaterialCode, p.MaterialDescription)).ToList();
        Material? first = null;
        foreach (var g in groups)
        {
            var sample = g.First();
            var material = FindMaterial(sample.MaterialCode, sample.MaterialName);
            if (material is null)
            {
                material = new Material { Name = sample.MaterialName, Code = sample.MaterialCode };
                if (sample.StockLengthMm is double stock)
                    material.StockLength = Models.Units.Convert(stock, LengthUnit.Millimeters, Units);
                else
                    material.StockLength = Models.Units.Convert(material.StockLength, LengthUnit.Inches, Units);
                material.Kerf = Models.Units.Convert(material.Kerf, LengthUnit.Inches, Units);
                material.MinLeftover = Models.Units.Convert(material.MinLeftover, LengthUnit.Inches, Units);
                Materials.Add(material);
            }
            first ??= material;

            foreach (var p in g)
            {
                var length = Models.Units.Convert(p.LengthMm, LengthUnit.Millimeters, Units);
                var existing = material.Cuts.FirstOrDefault(c =>
                    c.Job == job && c.Label == p.Label && Math.Abs(c.Length - length) < 0.001);
                if (existing is not null) existing.Quantity += p.Quantity;
                else
                {
                    existing = new CutItem { Job = job, Label = p.Label, Length = length, Quantity = p.Quantity };
                    material.Cuts.Add(existing);
                }
                var key = (material.Name, p.Label, Math.Round(length, 3));
                if (oldDone.TryGetValue(key, out var done) && done > 0)
                {
                    var carried = Math.Min(done, existing.Quantity - existing.Done);
                    existing.Done += carried;
                    oldDone[key] = done - carried;
                }
            }
        }
        return first;
    }

    // ---- Jobs folder ----

    private string _jobsFolder = "";
    private Dictionary<string, string> _importedJobFiles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Folder of job CSVs loaded at startup and by "Update from jobs folder".</summary>
    public string JobsFolder
    {
        get => _jobsFolder;
        set
        {
            if (!Set(ref _jobsFolder, value?.Trim() ?? "")) return;
            Save();
            ScanJobsFolder(true);
        }
    }

    /// <summary>Loads every CSV in the jobs folder that is new or has changed since it was last loaded.</summary>
    public void ScanJobsFolder(bool interactive)
    {
        if (JobsFolder.Length == 0)
        {
            if (interactive)
                MessageBox.Show("Set the jobs folder first (Settings > Jobs folder...).", "Jobs folder",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!System.IO.Directory.Exists(JobsFolder))
        {
            var missing = $"Jobs folder not found: {JobsFolder}";
            MachineStatus = missing;
            if (interactive) MessageBox.Show(missing, "Jobs folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var added = new List<string>();
        var updated = new List<string>();
        var problems = new List<string>();
        foreach (var path in System.IO.Directory.EnumerateFiles(JobsFolder, "*.csv").OrderBy(p => p))
        {
            var name = System.IO.Path.GetFileName(path);
            string stamp;
            try
            {
                var info = new System.IO.FileInfo(path);
                stamp = $"{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            }
            catch (Exception ex)
            {
                problems.Add($"{name}: {ex.Message}");
                continue;
            }
            var seen = _importedJobFiles.TryGetValue(name, out var oldStamp);
            if (seen && oldStamp == stamp) continue;

            PartsListImport import;
            try
            {
                import = PartsListImporter.Read(path);
            }
            catch (Exception ex)
            {
                problems.Add($"{name}: {ex.Message}");
                continue;
            }
            if (import.Parts.Count == 0)
            {
                problems.Add($"{name}: no parts found");
                continue;
            }
            var job = System.IO.Path.GetFileNameWithoutExtension(path).Trim();
            // The file is the job's source of truth, so it replaces lines from an earlier load or a manual import.
            AddImportedParts(job, import, replaceJob: true);
            _importedJobFiles[name] = stamp;
            (seen ? updated : added).Add(job);
        }

        if (added.Count + updated.Count > 0)
        {
            RefreshJobs();
            Save();
        }

        var message = new StringBuilder();
        if (added.Count > 0) message.AppendLine($"New jobs: {string.Join(", ", added)}");
        if (updated.Count > 0) message.AppendLine($"Updated jobs: {string.Join(", ", updated)}");
        if (problems.Count > 0)
        {
            message.AppendLine("Could not load:");
            foreach (var p in problems.Take(10)) message.AppendLine("    " + p);
        }
        if (message.Length == 0) message.Append("No new or changed job files.");

        if (added.Count + updated.Count + problems.Count > 0)
            MachineStatus = $"Jobs folder: {added.Count} new, {updated.Count} updated" +
                            (problems.Count > 0 ? $", {problems.Count} could not be loaded" : "");
        if (interactive)
            MessageBox.Show(message.ToString().TrimEnd(), "Jobs folder", MessageBoxButton.OK,
                problems.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private Material? FindMaterial(string code, string name) =>
        Materials.FirstOrDefault(m => code.Length > 0 && m.Code == code) ??
        Materials.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

    private Material? _librarySelection;

    /// <summary>Material selected in the material library window.</summary>
    public Material? LibrarySelection { get => _librarySelection; set => Set(ref _librarySelection, value); }

    private void AddLibraryMaterial()
    {
        AddMaterial();
        LibrarySelection = SelectedMaterial;
    }

    private void DeleteMaterial(Material? material)
    {
        if (material is null) return;
        var cutLines = material.Cuts.Count;
        var jobs = material.Cuts.Select(c => c.Job).Where(j => j.Length > 0).Distinct().Count();
        var leftovers = Leftovers.Count(l => l.MaterialId == material.Id);

        var message = $"Delete \"{material.Name}\"?";
        if (cutLines > 0 || leftovers > 0)
        {
            message += "\n\nThis material is still in use:";
            if (cutLines > 0) message += $"\n  - {cutLines} cut lines" + (jobs > 0 ? $" in {jobs} job(s)" : "");
            if (leftovers > 0) message += $"\n  - {leftovers} leftovers";
            message += "\n\nThey will be deleted too.";
        }
        if (MessageBox.Show(message, "Delete material", MessageBoxButton.YesNo,
                cutLines > 0 || leftovers > 0 ? MessageBoxImage.Warning : MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        foreach (var leftover in Leftovers.Where(l => l.MaterialId == material.Id).ToList())
            Leftovers.Remove(leftover);
        if (Plan.Any(c => c.Bar.Material == material))
        {
            Plan.Clear();
            SelectedCut = null;
            PlanSummary = "The planned material was deleted. Build the plan again.";
        }
        Materials.Remove(material);
        if (SelectedMaterial == material) SelectedMaterial = Materials.FirstOrDefault();
        if (NewLeftoverMaterial == material) NewLeftoverMaterial = Materials.FirstOrDefault();
        Save();
    }

    private void ResetDone()
    {
        if (SelectedMaterial is null) return;
        foreach (var cut in SelectedMaterial.Cuts.Where(InSelectedJob)) cut.Done = 0;
    }

    // ---- Cutting ----

    /// <summary>
    /// Plans the selected job's remaining cuts for the given materials, one after another. Sticks are
    /// numbered through the whole plan so each one has its own number.
    /// </summary>
    private void BuildPlan(IReadOnlyList<Material> materials)
    {
        if (materials.Count == 0) return;

        NewLeftoverTag = "";
        NewLeftoverText = "";
        Plan.Clear();
        var bars = new List<PlannedBar>();
        var unplaced = new List<CutItem>();
        var spacerMin = Models.Units.Convert(SpacerMinPartMm, LengthUnit.Millimeters, Units);
        var spacer = Models.Units.Convert(SpacerLengthMm, LengthUnit.Millimeters, Units);
        foreach (var material in materials)
        {
            var result = CutPlanner.Plan(material, Leftovers.Where(l => l.MaterialId == material.Id), InSelectedJob);
            unplaced.AddRange(result.Unplaced);
            foreach (var bar in result.Bars)
            {
                bar.Number = bars.Count + 1;
                bars.Add(bar);
                foreach (var cut in bar.Cuts)
                {
                    if (SpacerLengthMm > 0 && cut.Length < spacerMin)
                    {
                        cut.Spacer = spacer;
                        cut.SpacerNote = $"USE {SpacerLengthMm:0.#} mm SPACER";
                    }
                    Plan.Add(cut);
                }
            }
        }

        var newBars = bars.Count(b => b.LeftoverId is null);
        var what = materials.Count == 1 ? materials[0].Name : $"{materials.Count} materials";
        var spaced = Plan.Count(c => c.Spacer > 0);
        PlanSummary = $"{SelectedJob} / {what}: {Plan.Count} cuts on {bars.Count} sticks " +
                      $"({newBars} new stock, {bars.Count - newBars} leftovers)." +
                      (spaced > 0 ? $" {spaced} short cuts need the {SpacerLengthMm:0.#} mm spacer." : "");

        if (unplaced.Count > 0)
        {
            var parts = string.Join(", ", unplaced.Select(c => $"{c.Label} {c.Length}").Distinct());
            MessageBox.Show($"These parts are longer than a full stick and were left out:\n{parts}",
                "Cut plan", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        SelectedCut = Plan.FirstOrDefault();
        UpdateStick();
    }

    /// <summary>First pending cut of the next material after the current cut's material.</summary>
    private PlannedCut? NextMaterialCut()
    {
        if (SelectedCut is not { } current) return null;
        var index = Plan.IndexOf(current);
        return Plan.Skip(index + 1).FirstOrDefault(c => c.Status == CutStatus.Pending && c.Bar.Material != current.Bar.Material);
    }

    /// <summary>Moves on to the next material in the plan; cuts left on this material stay pending.</summary>
    private void NextMaterial()
    {
        if (NextMaterialCut() is not { } next) return;
        SelectedCut = next;
        if (AutoSendNext && IsConnected) MoveTo(next.StopPosition);
    }

    // ---- Short-part spacer ----

    private double _spacerMinPartMm = 100;
    private double _spacerLengthMm = 300;
    private string _spacerWarning = "";

    public double SpacerMinPartMm { get => _spacerMinPartMm; set { if (Set(ref _spacerMinPartMm, Math.Max(0, value))) Save(); } }
    public double SpacerLengthMm { get => _spacerLengthMm; set { if (Set(ref _spacerLengthMm, Math.Max(0, value))) Save(); } }

    /// <summary>"USE 300 mm SPACER" while the current cut needs the spacer; empty otherwise.</summary>
    public string SpacerWarning { get => _spacerWarning; private set => Set(ref _spacerWarning, value); }

    private string _currentMaterialName = "";

    /// <summary>Material of the cut being made, shown beside NOW CUTTING.</summary>
    public string CurrentMaterialName { get => _currentMaterialName; private set => Set(ref _currentMaterialName, value); }

    private void SendPosition()
    {
        if (SelectedCut is { } cut) MoveTo(cut.StopPosition);
    }

    private void CutDone()
    {
        if (SelectedCut is not { } cut) return;
        cut.Status = CutStatus.Done;
        cut.Item.Done++;
        LastCutSize = $"{cut.Length:0.###} {UnitLabel}";
        LastCutLabel = cut.Label;
        if (Plan.All(c => c.Status != CutStatus.Pending))
        {
            NowCuttingSize = "-";
            NowCuttingLabel = "All cuts finished";
        }
        FinishBarIfComplete(cut.Bar);
        Save();
        SelectNextCut();
        UpdateStick();
    }

    private void SkipCut()
    {
        if (SelectedCut is not { } cut) return;
        cut.Status = CutStatus.Skipped;
        FinishBarIfComplete(cut.Bar);
        Save();
        SelectNextCut();
        UpdateStick();
    }

    private void SelectNextCut()
    {
        SelectedCut = Plan.FirstOrDefault(c => c.Status == CutStatus.Pending);
        if (SelectedCut is null)
        {
            PlanSummary += " All cuts finished.";
            return;
        }
        if (AutoSendNext && IsConnected) MoveTo(SelectedCut.StopPosition);
    }

    /// <summary>
    /// Once every cut on a bar is done or skipped, the leftover it came from is used up and
    /// the remainder, if long enough, goes back into leftovers.
    /// </summary>
    // ---- Leftover IDs ----

    private string _newLeftoverTag = "";
    private string _newLeftoverText = "";

    /// <summary>ID of the leftover just made during cutting, shown large so it can be written on the piece.</summary>
    public string NewLeftoverTag { get => _newLeftoverTag; private set => Set(ref _newLeftoverTag, value); }
    public string NewLeftoverText { get => _newLeftoverText; private set => Set(ref _newLeftoverText, value); }

    /// <summary>
    /// Leftover ID = the material's letter + the lowest free number (A1, A2, ...). Numbers of
    /// used-up leftovers are free again, so the IDs stay short.
    /// </summary>
    private string NextLeftoverTag(Material material)
    {
        var letter = material.Letter.Length > 0 ? material.Letter : "X";
        var used = Leftovers.Select(l => l.Tag).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var n = 1;
        while (used.Contains($"{letter}{n}")) n++;
        return $"{letter}{n}";
    }

    /// <summary>First letter (A..Z, then AA, AB...) no material uses yet.</summary>
    private string NextMaterialLetter()
    {
        var used = Materials.Select(m => m.Letter).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; ; i++)
        {
            var name = i < 26 ? ((char)('A' + i)).ToString() : $"{(char)('A' + i / 26 - 1)}{(char)('A' + i % 26)}";
            if (!used.Contains(name)) return name;
        }
    }

    private void FinishBarIfComplete(PlannedBar bar)
    {
        if (bar.Finished) return;
        var material = bar.Material;
        if (bar.Cuts.Any(c => c.Status == CutStatus.Pending)) return;
        bar.Finished = true;

        var done = bar.Cuts.Where(c => c.Status == CutStatus.Done).ToList();
        if (done.Count == 0) return;

        if (bar.LeftoverId is Guid id && Leftovers.FirstOrDefault(l => l.Id == id) is { } source)
            Leftovers.Remove(source);

        var remaining = bar.StartLength - material.TrimCut - done.Sum(c => c.Length + material.Kerf);
        if (remaining > 0 && remaining >= material.MinLeftover)
        {
            var tag = NextLeftoverTag(material);
            Leftovers.Add(new Leftover
            {
                Tag = tag,
                MaterialId = material.Id,
                MaterialName = material.Name,
                Length = Math.Round(remaining, 3),
                Note = $"From stick {bar.Number}",
            });
            AppendLog($"Leftover {tag} saved: {material.Name} {remaining:0.###}");
            NewLeftoverTag = tag;
            NewLeftoverText = $"Mark the leftover from stick {bar.Number}: {remaining:0.###} {UnitLabel}, {material.Name}";
        }
    }

    // ---- Leftovers ----

    private void AddLeftover()
    {
        if (NewLeftoverMaterial is not { } material) return;
        if (!TryParseLength(NewLeftoverLength, out var length) || length <= 0)
        {
            MessageBox.Show("Enter a valid length.", "Leftovers", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Leftovers.Add(new Leftover { Tag = NextLeftoverTag(material), MaterialId = material.Id, MaterialName = material.Name, Length = length, Note = "Added manually" });
        NewLeftoverLength = "";
        Save();
    }

    private void DeleteLeftover()
    {
        if (SelectedLeftover is null) return;
        Leftovers.Remove(SelectedLeftover);
        Save();
    }

    // ---- Connection ----

    private void RefreshPorts()
    {
        Ports = SerialPort.GetPortNames().OrderBy(p => p).ToArray();
        if (Ports.Length > 0 && !Ports.Contains(Serial.PortName))
        {
            Serial.PortName = Ports[0];
            OnPropertyChanged(nameof(Serial));
        }
    }

    private void Connect()
    {
        try
        {
            _tigerStop.Open(Serial);
            IsConnected = true;
            ConnectionStatus = $"Port {Serial.PortName} open, checking for a TigerStop...";
            Save();
            _awaitingSerial = true;
            _serialConfirmed = false;
            _versionRetries = 0;
            _versionReply.Clear();
            _versionRaw.Clear();
            _serialQueryTimer.Start();
            _tigerStop.SendCommand(TigerStopController.VersionCommand, Serial);
        }
        catch (Exception ex)
        {
            AppendLog("Connect failed: " + ex.Message);
            MessageBox.Show(ex.Message, "Connect", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SerialQueryTimedOut()
    {
        _serialQueryTimer.Stop();
        if (!_awaitingSerial) return;
        if (_versionRetries++ == 0)
        {
            // Slow amps can miss or answer late to the first query; ask once more.
            _versionReply.Clear();
            _versionRaw.Clear();
            _serialQueryTimer.Start();
            try { _tigerStop.SendCommand(TigerStopController.VersionCommand, Serial); } catch { }
            return;
        }
        _awaitingSerial = false;
        // Not an error: some amps don't answer the version query. The status turns to "Connected"
        // as soon as the amp answers anything (a move, position or error).
        ConnectionStatus = $"Port {Serial.PortName} open. The TigerStop didn't answer the version query, " +
                           "but commands are sent. If moves don't work, check the baud rate or use Find TigerStop.";
    }

    private async void FindTigerStop()
    {
        Disconnect();
        _searching = true;
        ConnectionStatus = "Searching COM ports for a TigerStop...";
        try
        {
            var found = await Task.Run(TigerStopController.FindConnections);
            if (found.Count == 0)
            {
                ConnectionStatus = "No TigerStop found. Check the cable (a null-modem cable may be needed) and that the amp is on.";
                return;
            }
            RefreshPorts();
            Serial.PortName = found[0].Port;
            Serial.BaudRate = found[0].Baud;
            Serial.DataBits = 8;
            Serial.Parity = Parity.None;
            Serial.StopBits = StopBits.One;
            Serial.Handshake = Handshake.None;
            OnPropertyChanged(nameof(Serial));
            AppendLog($"Found TigerStop on {found[0].Port} at {found[0].Baud} baud");
        }
        finally
        {
            _searching = false;
            CommandManager.InvalidateRequerySuggested();
        }
        Connect();
    }

    private void SendMachineCommand(string command)
    {
        try
        {
            _tigerStop.SendCommand(command, Serial);
        }
        catch (Exception ex)
        {
            AppendLog("Send failed: " + ex.Message);
            MessageBox.Show(ex.Message, "StopCut", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void QueryPosition()
    {
        _awaitingPosition = true;
        SendMachineCommand(TigerStopController.PositionQueryCommand);
    }

    private void Disconnect()
    {
        _tigerStop.Close();
        FlushRx();
        _awaitingSerial = false;
        _serialConfirmed = false;
        _serialQueryTimer.Stop();
        MachineStatus = "";
        IsConnected = false;
        ConnectionStatus = "Not connected";
    }

    private void MoveManual()
    {
        if (TryParseLength(ManualPosition, out var position)) MoveTo(position);
        else MessageBox.Show("Enter a valid position.", "Move", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void MoveTo(double length)
    {
        try
        {
            var command = TigerStopController.FormatMove(ToMachine(length + Serial.PositionOffset), Serial);
            _tigerStop.SendRaw(command);
            _lastMoveCommand = TigerStopController.Escape(command);
            MachineStatus = $"Sent move to {length + Serial.PositionOffset:0.###} {UnitLabel}, waiting for the TigerStop...";
            _moveReplyTimer.Stop();
            _moveReplyTimer.Start();
        }
        catch (Exception ex)
        {
            AppendLog("Send failed: " + ex.Message);
            MessageBox.Show(ex.Message, "StopCut", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ---- Helpers ----

    private static bool TryParseLength(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private void AppendLog(string line)
    {
        _log.AppendLine($"{DateTime.Now:HH:mm:ss}  {line}");
        if (_log.Length > 20000) _log.Remove(0, _log.Length - 20000);
        OnPropertyChanged(nameof(LogText));
    }

    public void Save()
    {
        RefreshJobs();
        foreach (var leftover in Leftovers)
            if (Materials.FirstOrDefault(m => m.Id == leftover.MaterialId) is { } material)
                leftover.MaterialName = material.Name;

        try
        {
            _store.Save(new AppData
            {
                Materials = Materials.ToList(), Leftovers = Leftovers.ToList(), Serial = Serial, Units = Units,
                JobsFolder = JobsFolder, ImportedJobFiles = _importedJobFiles,
                SpacerMinPartMm = SpacerMinPartMm, SpacerLengthMm = SpacerLengthMm,
            });
        }
        catch (Exception ex)
        {
            AppendLog("Save failed: " + ex.Message);
        }
    }

    public void Dispose()
    {
        Save();
        _tigerStop.Dispose();
    }
}
