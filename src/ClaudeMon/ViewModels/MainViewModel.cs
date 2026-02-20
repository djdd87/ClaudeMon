using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeMon.Helpers;
using ClaudeMon.Models;
using ClaudeMon.Services;
using ClaudeMon.Views;

namespace ClaudeMon.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private MainWindow? _mainWindow;
    private bool _disposed;

    [ObservableProperty]
    private ObservableCollection<ProfileViewModel> _profiles = [];

    [ObservableProperty]
    private ProfileViewModel? _selectedProfile;

    [ObservableProperty]
    private bool _isWindowVisible;

    /// <summary>
    /// Convenience binding: the Usage of the currently selected profile.
    /// </summary>
    public UsageSummary? Usage => SelectedProfile?.Usage;

    public MainViewModel() { }

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
            profile.Initialize();
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

        _mainWindow?.Close();

        foreach (var profile in Profiles)
        {
            profile.TrayLeftClicked -= OnProfileTrayClicked;
            profile.Dispose();
        }
    }
}
