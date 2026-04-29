using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;

namespace VrcOscSender;

file record SavedParameter(string Name, string Type, bool BoolValue, int IntValue, float FloatValue);
file record SavedSettings(string Host, int Port, List<SavedParameter> Parameters);

public class MainViewModel : INotifyPropertyChanged
{
    private static readonly string SavePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VrcOscSender", "params.json");

    private string _host = "127.0.0.1";
    private int _port = 9000;
    private string _statusMessage = "Ready — click Connect to start.";
    private bool _isConnected;
    private string _avatarId = "—";
    private string _avatarStatus = "Not listening";

    private OscClient? _client;
    private readonly OscListener _listener;

    public ObservableCollection<string> LogHistory { get; } = new();

    // ── Logging ───────────────────────────────────────────────

    /// <summary>
    /// Adds a timestamped entry to the log history without changing the status bar.
    /// Use for detailed debug info that would be noisy in the status bar.
    /// </summary>
    private void Log(string message)
    {
        var entry = $"[{DateTime.Now:HH:mm:ss.fff}]  {message}";
        Application.Current.Dispatcher.Invoke(() =>
        {
            LogHistory.Insert(0, entry);
            if (LogHistory.Count > 500) LogHistory.RemoveAt(LogHistory.Count - 1);
        });
    }

    // ── Properties ────────────────────────────────────────────

    public string Host
    {
        get => _host;
        set { _host = value; OnPropertyChanged(); }
    }

    public int Port
    {
        get => _port;
        set { _port = value; OnPropertyChanged(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            _statusMessage = value;
            OnPropertyChanged();
            Log(value);
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        set { _isConnected = value; OnPropertyChanged(); OnPropertyChanged(nameof(ConnectLabel)); }
    }

    public string ConnectLabel => IsConnected ? "Disconnect" : "Connect";

    public string AvatarId
    {
        get => _avatarId;
        set { _avatarId = value; OnPropertyChanged(); }
    }

    public string AvatarStatus
    {
        get => _avatarStatus;
        set { _avatarStatus = value; OnPropertyChanged(); }
    }

    public ObservableCollection<OscParameter> Parameters { get; } = new();
    public ParamType[] ParamTypes { get; } = Enum.GetValues<ParamType>();

    public ICommand ToggleConnectCommand   { get; }
    public ICommand AddParameterCommand    { get; }
    public ICommand RemoveParameterCommand { get; }
    public ICommand SendAllCommand         { get; }
    public ICommand SendOneCommand         { get; }
    public ICommand CopyAvatarIdCommand    { get; }
    public ICommand ShowLogCommand         { get; }

    public MainViewModel()
    {
        _listener = new OscListener(9001);
        _listener.AvatarChanged += id => Application.Current.Dispatcher.Invoke(() =>
        {
            AvatarId      = id;
            AvatarStatus  = "Detected  ✓";
            StatusMessage = $"Avatar changed → {id}";
            Log($"OSC /avatar/change received: {id}");
        });
        _listener.ListenError += err => Application.Current.Dispatcher.Invoke(() =>
        {
            AvatarStatus  = "Listen error";
            StatusMessage = $"Listener error: {err}";
            Log($"OSC listener error: {err}");
        });

        ToggleConnectCommand   = new RelayCommand(_ => ToggleConnect());
        AddParameterCommand    = new RelayCommand(_ => AddParameter());
        RemoveParameterCommand = new RelayCommand(p => RemoveParameter(p as OscParameter));
        SendAllCommand         = new RelayCommand(_ => SendAll(),                   _ => IsConnected);
        SendOneCommand         = new RelayCommand(p => SendOne(p as OscParameter),  _ => IsConnected);
        CopyAvatarIdCommand    = new RelayCommand(_ => Clipboard.SetText(AvatarId), _ => AvatarId != "—");
        ShowLogCommand         = new RelayCommand(_ => ShowLog());

        Log("Application started.");
        LoadSettings();
    }

    // ── Log window ────────────────────────────────────────────

    private void ShowLog()
    {
        var win = new LogWindow(LogHistory);
        win.Show();
    }

    // ── VRChat integration helpers ────────────────────────────

    /// <summary>
    /// Reads VRChat's latest log file to find the OSC port it's actually
    /// listening on. VRChat writes a line like:
    ///   "Advertising Service VRChat-Client-XXXXXX of type OSC on 9000"
    /// Falls back to defaults if not found.
    /// </summary>
    private (int sendPort, int listenPort) ReadVRChatOscPorts()
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "..", "LocalLow", "VRChat", "VRChat");

            if (!Directory.Exists(logDir))
            {
                Log("VRChat log directory not found — using default ports.");
                return (9000, 9001);
            }

            // Get the most recently written log file
            var logFile = Directory.GetFiles(logDir, "output_log_*.txt")
                                   .OrderByDescending(File.GetLastWriteTime)
                                   .FirstOrDefault();

            if (logFile is null)
            {
                Log("No VRChat log file found — using default ports.");
                return (9000, 9001);
            }

            Log($"Reading VRChat log: {Path.GetFileName(logFile)}");

            // Read with FileShare.ReadWrite so we can read while VRChat has it open
            int oscPort     = 9000;
            int oscOutPort  = 9001;
            bool foundIn    = false;
            bool foundOut   = false;

            using var stream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                // "Advertising Service VRChat-Client-XXXXX of type OSC on PORT"
                if (line.Contains("of type OSC on ") && !foundIn)
                {
                    var parts = line.Split("of type OSC on ");
                    if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out int p))
                    {
                        oscPort  = p;
                        foundIn  = true;
                        Log($"Found VRChat OSC receive port: {oscPort}");
                    }
                }
                // "Advertising Service VRChat-Client-XXXXX of type OSCQuery on PORT"
                if (line.Contains("of type OSCQuery on ") && !foundOut)
                {
                    var parts = line.Split("of type OSCQuery on ");
                    if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out int p))
                    {
                        oscOutPort = p;
                        foundOut   = true;
                        Log($"Found VRChat OSCQuery port: {oscOutPort}");
                    }
                }
            }

            return (oscPort, oscOutPort);
        }
        catch (Exception ex)
        {
            Log($"Could not read VRChat log: {ex.Message} — using defaults.");
            return (9000, 9001);
        }
    }

    /// <summary>
    /// Tries to find the current avatar ID from the VRChat OSC config folder.
    /// VRChat writes avatar config JSON files to:
    ///   %AppData%\..\LocalLow\VRChat\VRChat\OSC\[userID]\Avatars\[avatarID].json
    /// The most recently written file corresponds to the current avatar.
    /// </summary>
    private void TryFetchCurrentAvatar()
    {
        try
        {
            var oscDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "..", "LocalLow", "VRChat", "VRChat", "OSC");

            if (!Directory.Exists(oscDir))
            {
                Log("VRChat OSC config directory not found.");
                return;
            }

            // Find the most recently modified avatar JSON across all user folders
            var avatarFile = Directory.GetFiles(oscDir, "*.json", SearchOption.AllDirectories)
                                      .Where(f => f.Contains("Avatars"))
                                      .OrderByDescending(File.GetLastWriteTime)
                                      .FirstOrDefault();

            if (avatarFile is null)
            {
                Log("No avatar config files found — switch avatars once to populate.");
                return;
            }

            // The avatar ID is the filename without extension
            var avatarId = Path.GetFileNameWithoutExtension(avatarFile);
            Log($"Current avatar detected from OSC config: {avatarId}");

            Application.Current.Dispatcher.Invoke(() =>
            {
                AvatarId     = avatarId;
                AvatarStatus = "Detected  ✓";
            });
        }
        catch (Exception ex)
        {
            Log($"Could not detect current avatar: {ex.Message}");
        }
    }

    // ── Parameter management ──────────────────────────────────

    private void AddParameter()
    {
        var p = new OscParameter { Name = "/avatar/parameters/MyParam", Type = ParamType.Bool, BoolValue = true };
        Parameters.Add(p);
        Log($"Parameter added: '{p.Name}'");
    }

    private void RemoveParameter(OscParameter? p)
    {
        if (p is null) return;
        Parameters.Remove(p);
        Log($"Parameter removed: '{p.Name}'");
    }

    // ── Persistence ───────────────────────────────────────────

    public void SaveSettings()
    {
        try
        {
            var saved = new SavedSettings(
                Host, Port,
                Parameters.Select(p => new SavedParameter(
                    p.Name, p.Type.ToString(),
                    p.BoolValue, p.IntValue, p.FloatValue)).ToList());

            Directory.CreateDirectory(Path.GetDirectoryName(SavePath)!);
            File.WriteAllText(SavePath, JsonSerializer.Serialize(saved,
                new JsonSerializerOptions { WriteIndented = true }));

            Log($"Settings saved → {SavePath}  ({Parameters.Count} parameter(s))");
        }
        catch (Exception ex)
        {
            Log($"Save FAILED: {ex.Message}  path: {SavePath}");
            MessageBox.Show(
                $"Failed to save settings:\n{ex.Message}\n\nSave path: {SavePath}",
                "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(SavePath))
            {
                Log("No save file found — loading defaults.");
                Parameters.Add(new OscParameter { Name = "/avatar/eyeheight", Type = ParamType.Float, FloatValue = 1f });
                return;
            }

            var json  = File.ReadAllText(SavePath);
            var saved = JsonSerializer.Deserialize<SavedSettings>(json);
            if (saved is null)
            {
                Log("Save file was empty or invalid.");
                return;
            }

            Host = saved.Host;
            Port = saved.Port;

            foreach (var p in saved.Parameters)
            {
                var type = Enum.TryParse<ParamType>(p.Type, out var t) ? t : ParamType.Bool;
                Parameters.Add(new OscParameter
                {
                    Name       = p.Name,
                    Type       = type,
                    BoolValue  = p.BoolValue,
                    IntValue   = p.IntValue,
                    FloatValue = p.FloatValue
                });
            }

            Log($"Settings loaded from {SavePath}");
            StatusMessage = $"Loaded {Parameters.Count} parameter(s) from last session.";
        }
        catch (Exception ex)
        {
            Log($"Load FAILED: {ex.Message} — falling back to defaults.");
            Parameters.Clear();
            Parameters.Add(new OscParameter { Name = "/avatar/parameters/MyBool", Type = ParamType.Bool });
        }
    }

    // ── Connection ────────────────────────────────────────────

    private void ToggleConnect()
    {
        if (IsConnected)
        {
            _client?.Dispose();
            _client = null;
            _listener.Stop();
            IsConnected   = false;
            AvatarStatus  = "Not listening";
            StatusMessage = "Disconnected.";
            Log($"OSC client disconnected from {Host}:{Port}");
            Log($"OSC listener stopped on port {_listener.ListenPort}");
            return;
        }

        try
        {
            // Read actual ports from VRChat log file
            var (sendPort, listenPort) = ReadVRChatOscPorts();
            Port = sendPort;
            _listener.UpdatePort(listenPort);

            Log($"Connecting to {Host}:{Port}...");
            _client = new OscClient(Host, Port);
            Log($"OSC client connected → {Host}:{Port}");

            Log($"Starting OSC listener on port {_listener.ListenPort}...");
            _listener.Start();
            Log($"OSC listener active on port {_listener.ListenPort}");

            IsConnected   = true;
            AvatarStatus  = $"Listening on :{_listener.ListenPort}";
            StatusMessage = $"Connected → {Host}:{Port}  |  Listening on :{_listener.ListenPort}";

            // Immediately try to detect the current avatar from OSC config files
            Task.Run(TryFetchCurrentAvatar);
        }
        catch (Exception ex)
        {
            Log($"Connection FAILED: {ex.GetType().Name}: {ex.Message}");
            StatusMessage = $"Connection failed: {ex.Message}";
            MessageBox.Show(ex.Message, "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Sending ───────────────────────────────────────────────

    private void SendAll()
    {
        if (_client is null) return;
        Log($"Sending all {Parameters.Count} parameter(s)...");
        int sent = 0;
        var errors = new List<string>();
        foreach (var p in Parameters)
        {
            var err = TrySend(p);
            if (err is null) sent++; else errors.Add(err);
        }
        StatusMessage = errors.Count == 0
            ? $"Sent {sent} parameter(s)  ✓"
            : $"Sent {sent}, {errors.Count} error(s): {string.Join(", ", errors)}";
    }

    private void SendOne(OscParameter? p)
    {
        if (_client is null || p is null) return;
        var err = TrySend(p);
        StatusMessage = err is null ? $"Sent '{p.Name}'  ✓" : $"Error: {err}";
    }

    private string? TrySend(OscParameter p)
    {
        if (string.IsNullOrWhiteSpace(p.Name))
        {
            Log("Send skipped — parameter name is empty.");
            return "[empty name]";
        }

        try
        {
            switch (p.Type)
            {
                case ParamType.Bool:
                    _client!.SendBool(p.Name, p.BoolValue);
                    Log($"SEND  Bool   {p.Name} = {p.BoolValue}");
                    break;
                case ParamType.Int:
                    _client!.SendInt(p.Name, p.IntValue);
                    Log($"SEND  Int    {p.Name} = {p.IntValue}");
                    break;
                case ParamType.Float:
                    _client!.SendFloat(p.Name, p.FloatValue);
                    Log($"SEND  Float  {p.Name} = {p.FloatValue:F3}");
                    break;
            }
            return null;
        }
        catch (Exception ex)
        {
            Log($"SEND FAILED  {p.Name}: {ex.Message}");
            return $"'{p.Name}': {ex.Message}";
        }
    }

    // ── INotifyPropertyChanged ────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute    = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? p) => _canExecute?.Invoke(p) ?? true;
    public void Execute(object? p)    => _execute(p);

    public event EventHandler? CanExecuteChanged
    {
        add    => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}
