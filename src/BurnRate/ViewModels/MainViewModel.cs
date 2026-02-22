using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BurnRate.Helpers;
using BurnRate.Models;
using BurnRate.Services;
using BurnRate.Views;

namespace BurnRate.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ThemeService _themeService;
    private MainWindow? _mainWindow;
    private MetricsManagerWindow? _metricsManagerWindow;
    private bool _disposed;

    [ObservableProperty]
    private ObservableCollection<ProfileViewModel> _profiles = [];

    [ObservableProperty]
    private ProfileViewModel? _selectedProfile;

    [ObservableProperty]
    private bool _isWindowVisible;

    [ObservableProperty]
    private AppThemeMode _themeMode;

    [ObservableProperty]
    private AppThemeMode _effectiveTheme;

    [ObservableProperty]
    private ObservableCollection<CustomTheme> _availableCustomThemes = [];

    [ObservableProperty]
    private CustomTheme? _activeCustomTheme;

    /// <summary>
    /// Convenience binding: the Usage of the currently selected profile.
    /// </summary>
    public UsageSummary? Usage => SelectedProfile?.Usage;

    /// <summary>
    /// The ordered list of metric IDs currently enabled for display on the dashboard.
    /// Persisted to settings.json via ThemeService.
    /// </summary>
    public IReadOnlyList<string> EnabledMetricIds => _themeService.EnabledMetricIds;

    public MainViewModel(ThemeService themeService, IReadOnlyList<CustomTheme>? customThemes = null)
    {
        _themeService = themeService;
        _themeMode = themeService.CurrentMode;
        _effectiveTheme = themeService.EffectiveTheme;
        _activeCustomTheme = themeService.ActiveCustomTheme;
        _themeService.ThemeChanged += OnThemeServiceChanged;

        if (customThemes != null)
            foreach (var theme in customThemes)
                _availableCustomThemes.Add(theme);
    }

    partial void OnThemeModeChanged(AppThemeMode value)
    {
        if (value != AppThemeMode.Custom)
        {
            _themeService.SetTheme(value);
            ActiveCustomTheme = null;
        }
        EffectiveTheme = _themeService.EffectiveTheme;
    }

    private void OnThemeServiceChanged(AppThemeMode effectiveTheme)
    {
        EffectiveTheme = effectiveTheme;
    }

    [RelayCommand]
    private void ToggleMetric(string id)
    {
        var current = _themeService.EnabledMetricIds.ToList();
        if (current.Contains(id))
            current.Remove(id);
        else
            current.Add(id);
        _themeService.SetEnabledMetrics(current);
        OnPropertyChanged(nameof(EnabledMetricIds));
    }

    public void SetEnabledMetrics(IEnumerable<string> orderedIds)
    {
        _themeService.SetEnabledMetrics(orderedIds);
        OnPropertyChanged(nameof(EnabledMetricIds));
    }

    [RelayCommand]
    private void OpenMetricsManager()
    {
        if (_metricsManagerWindow?.IsVisible == true)
        {
            _metricsManagerWindow.Activate();
            return;
        }
        _metricsManagerWindow = new MetricsManagerWindow
        {
            DataContext = new MetricsManagerViewModel(this)
        };
        _metricsManagerWindow.Closed += (_, _) => _metricsManagerWindow = null;
        _metricsManagerWindow.Show();
        _metricsManagerWindow.UpdateLayout();
        WindowPositioner.PositionNearTray(_metricsManagerWindow);
        _metricsManagerWindow.Activate();
    }

    [RelayCommand]
    private void SelectCustomTheme(CustomTheme theme)
    {
        _themeService.SetCustomTheme(theme);
        // Set backing fields directly to skip generated callbacks that would fight each other.
        // MVVMTK0034 is suppressed intentionally here.
#pragma warning disable MVVMTK0034
        _activeCustomTheme = theme;
        OnPropertyChanged(nameof(ActiveCustomTheme));
        _themeMode = AppThemeMode.Custom;
        OnPropertyChanged(nameof(ThemeMode));
#pragma warning restore MVVMTK0034
        foreach (var p in Profiles)
            p.RefreshTrayIcon();
    }

    public void AddProfile(ProfileViewModel profile)
    {
        profile.TrayLeftClicked += OnProfileTrayClicked;
        Profiles.Add(profile);
        SelectedProfile ??= profile;
    }

    public void InitializeAll()
    {
        // Initialize in reverse so the first profile's tray icon is created last
        // and appears closest to the clock (most visible position).
        foreach (var profile in Profiles.Reverse())
            profile.Initialize(this);
    }

    partial void OnSelectedProfileChanged(ProfileViewModel? oldValue, ProfileViewModel? newValue)
    {
        if (oldValue != null)
            oldValue.PropertyChanged -= OnSelectedProfilePropertyChanged;

        if (newValue != null)
            newValue.PropertyChanged += OnSelectedProfilePropertyChanged;

        OnPropertyChanged(nameof(Usage));
    }

    private void OnSelectedProfilePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ProfileViewModel.Usage))
            OnPropertyChanged(nameof(Usage));
    }

    private void OnProfileTrayClicked(ProfileViewModel profile)
    {
        SelectedProfile = profile;

        // Check the window's actual visibility rather than IsWindowVisible, which can
        // fall out of sync when the window hides itself via Window_Deactivated.
        if (_mainWindow?.IsVisible == true)
            HideWindow();
        else
            ShowWindow();
    }

    [RelayCommand]
    private void ShowWindow()
    {
        if (_mainWindow == null || !_mainWindow.IsLoaded)
        {
            _mainWindow = new MainWindow { DataContext = this };
            _mainWindow.Closed += (_, _) =>
            {
                IsWindowVisible = false;
                _mainWindow = null;
            };
            _mainWindow.SizeChanged += (_, _) => WindowPositioner.PositionNearTray(_mainWindow);
        }

        _mainWindow.DataContext = this;
        _mainWindow.Show();
        _mainWindow.UpdateLayout();
        WindowPositioner.PositionNearTray(_mainWindow);
        _mainWindow.Activate();
        IsWindowVisible = true;
    }

    [RelayCommand]
    private void HideWindow()
    {
        _mainWindow?.Hide();
        IsWindowVisible = false;
    }

    [RelayCommand]
    private void ExitApplication()
    {
        Dispose();
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _themeService.ThemeChanged -= OnThemeServiceChanged;
        _mainWindow?.Close();

        foreach (var profile in Profiles)
        {
            profile.TrayLeftClicked -= OnProfileTrayClicked;
            profile.Dispose();
        }
    }
}
