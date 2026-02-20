using System.Diagnostics;
using System.IO;

namespace ClaudeMon.Services;

/// <summary>
/// Watches the ~/.claude/ directory for file changes and raises
/// <see cref="DataChanged"/> when relevant JSON files are modified.
/// Uses <see cref="FileSystemWatcher"/> for real-time notifications
/// with a fallback polling timer to ensure freshness.
/// </summary>
public sealed class FileWatcherService : IDisposable
{
    private const int DebounceMs = 500;

    private FileSystemWatcher? _watcher;
    private Timer? _pollTimer;
    private readonly string _watchPath;
    private readonly int _pollIntervalMs;
    private DateTime _lastNotify = DateTime.MinValue;
    private readonly object _debounceLock = new();
    private readonly object _watcherLock = new();
    private SynchronizationContext? _syncContext;
    private bool _disposed;

    /// <summary>
    /// Raised when Claude data files have changed on disk (debounced).
    /// When a <see cref="SynchronizationContext"/> was captured at <see cref="Start"/>,
    /// the event is marshalled to the UI thread.
    /// </summary>
    public event Action? DataChanged;

    public FileWatcherService(string claudePath, int pollIntervalMs = 60_000)
    {
        _watchPath = Environment.ExpandEnvironmentVariables(claudePath);
        _pollIntervalMs = pollIntervalMs;
    }

    /// <summary>
    /// Begins watching for file changes and starts the fallback poll timer.
    /// Call from the UI thread so that <see cref="SynchronizationContext.Current"/>
    /// can be captured for marshalling events.
    /// </summary>
    public void Start()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FileWatcherService));

        _syncContext = SynchronizationContext.Current;

        StartFileWatcher();
        StartPollTimer();
    }

    // ────────────────────────────────────────────────────────
    // FileSystemWatcher setup
    // ────────────────────────────────────────────────────────

    private void StartFileWatcher()
    {
        if (!Directory.Exists(_watchPath))
        {
            Debug.WriteLine($"FileWatcherService: Watch path does not exist: {_watchPath}");
            return;
        }

        try
        {
            _watcher = new FileSystemWatcher(_watchPath, "*.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };

            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.Error += OnWatcherError;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FileWatcherService: Failed to create FileSystemWatcher: {ex.Message}");
            // Polling timer will still provide updates.
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        RaiseDebounced();
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        Debug.WriteLine($"FileWatcherService: Watcher error: {e.GetException().Message}");

        // Restart under lock to prevent concurrent error handlers creating multiple watchers.
        lock (_watcherLock)
        {
            DisposeWatcher();
            if (!_disposed)
                StartFileWatcher();
        }
    }

    // ────────────────────────────────────────────────────────
    // Fallback poll timer
    // ────────────────────────────────────────────────────────

    private void StartPollTimer()
    {
        _pollTimer = new Timer(
            _ => RaiseDebounced(),
            state: null,
            dueTime: _pollIntervalMs,
            period: _pollIntervalMs);
    }

    // ────────────────────────────────────────────────────────
    // Debounce + marshalling
    // ────────────────────────────────────────────────────────

    private void RaiseDebounced()
    {
        lock (_debounceLock)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastNotify).TotalMilliseconds < DebounceMs)
                return;

            _lastNotify = now;
        }

        if (_syncContext is not null)
        {
            _syncContext.Post(_ => DataChanged?.Invoke(), null);
        }
        else
        {
            DataChanged?.Invoke();
        }
    }

    // ────────────────────────────────────────────────────────
    // Cleanup
    // ────────────────────────────────────────────────────────

    private void DisposeWatcher()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnFileChanged;
            _watcher.Created -= OnFileChanged;
            _watcher.Error -= OnWatcherError;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        DisposeWatcher();

        _pollTimer?.Dispose();
        _pollTimer = null;

        GC.SuppressFinalize(this);
    }
}
