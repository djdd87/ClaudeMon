using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;
using BurnRate.Models;

namespace BurnRate.Services;

public enum AppThemeMode { Dark, Light, System, Custom }

public sealed class ThemeService : IDisposable
{
    private AppThemeMode _currentMode = AppThemeMode.Dark;
    private AppThemeMode _effectiveTheme = AppThemeMode.Dark;
    private CustomTheme? _activeCustomTheme;
    private List<string> _enabledMetricIds = [];
    private bool _disposed;

    public event Action<AppThemeMode>? ThemeChanged;

    public AppThemeMode CurrentMode => _currentMode;
    public AppThemeMode EffectiveTheme => _effectiveTheme;
    public CustomTheme? ActiveCustomTheme => _activeCustomTheme;
    public IReadOnlyList<string> EnabledMetricIds => _enabledMetricIds;

    public void Initialize(IReadOnlyList<CustomTheme> available)
    {
        var settings = LoadSettings();
        _currentMode = settings.Mode;
        _enabledMetricIds = settings.EnabledMetrics.ToList();

        if (settings.Mode == AppThemeMode.Custom && settings.CustomThemeId != null)
            _activeCustomTheme = available.FirstOrDefault(t => t.Id == settings.CustomThemeId);

        ApplyTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public void SetTheme(AppThemeMode mode)
    {
        if (mode != AppThemeMode.Custom)
            _activeCustomTheme = null;
        _currentMode = mode;
        SaveSetting();
        ApplyTheme();
    }

    public void SetCustomTheme(CustomTheme theme)
    {
        _activeCustomTheme = theme;
        _currentMode = AppThemeMode.Custom;
        SaveSetting();
        ApplyTheme();
    }

    public void SetEnabledMetrics(IEnumerable<string> ids)
    {
        _enabledMetricIds = ids.ToList();
        SaveSetting();
    }

    private void ApplyTheme()
    {
        AppThemeMode resolved = _currentMode == AppThemeMode.System
            ? ResolveSystemTheme()
            : _currentMode;

        _effectiveTheme = resolved;

        Uri uri;
        if (resolved == AppThemeMode.Custom && _activeCustomTheme?.ColorsXamlPath != null)
            uri = new Uri(_activeCustomTheme.ColorsXamlPath, UriKind.Absolute);
        else if (resolved == AppThemeMode.Custom)
            uri = new Uri("pack://application:,,,/Themes/Colors_Dark.xaml");
        else
            uri = resolved switch
            {
                AppThemeMode.Light => new Uri("pack://application:,,,/Themes/Colors_Light.xaml"),
                _                  => new Uri("pack://application:,,,/Themes/Colors_Dark.xaml"),
            };

        var dict = new System.Windows.ResourceDictionary { Source = uri };
        System.Windows.Application.Current.Resources.MergedDictionaries[0] = dict;

        ThemeChanged?.Invoke(resolved);
    }

    private static AppThemeMode ResolveSystemTheme()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                1);
            return value is int v && v == 0 ? AppThemeMode.Dark : AppThemeMode.Light;
        }
        catch
        {
            return AppThemeMode.Dark;
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_currentMode != AppThemeMode.System) return;
        System.Windows.Application.Current.Dispatcher.Invoke(ApplyTheme);
    }

    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BurnRate",
            "settings.json");

    private sealed class PersistedSettings
    {
        public string Theme { get; set; } = "Dark";
        public string? CustomTheme { get; set; }
        public string[]? EnabledMetrics { get; set; }
    }

    private sealed record LoadedSettings(AppThemeMode Mode, string? CustomThemeId, IReadOnlyList<string> EnabledMetrics);

    private static LoadedSettings LoadSettings()
    {
        try
        {
            var path = SettingsPath;
            if (!File.Exists(path)) return new(AppThemeMode.Dark, null, MetricRegistry.DefaultEnabled);

            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Theme
            AppThemeMode mode = AppThemeMode.Dark;
            string? customId = null;

            if (root.TryGetProperty("Theme", out var themeProp))
            {
                var themeStr = themeProp.GetString();

                // Legacy migration: "Doom" was a built-in enum value, now it's a custom theme
                if (themeStr == "Doom")
                {
                    mode = AppThemeMode.Custom;
                    customId = "Doom";
                }
                else if (Enum.TryParse<AppThemeMode>(themeStr, out var parsedMode))
                {
                    mode = parsedMode;
                    if (mode == AppThemeMode.Custom && root.TryGetProperty("CustomTheme", out var ctProp))
                        customId = ctProp.GetString();
                }
            }

            // Metrics
            List<string> metrics = [];
            if (root.TryGetProperty("EnabledMetrics", out var metricsProp) &&
                metricsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in metricsProp.EnumerateArray())
                {
                    var id = item.GetString();
                    if (id != null) metrics.Add(id);
                }
            }

            return new(mode, customId, metrics.Count > 0 ? metrics : MetricRegistry.DefaultEnabled);
        }
        catch
        {
            return new(AppThemeMode.Dark, null, MetricRegistry.DefaultEnabled);
        }
    }

    private void SaveSetting()
    {
        try
        {
            var path = SettingsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var settings = new PersistedSettings
            {
                Theme = _currentMode.ToString(),
                CustomTheme = _currentMode == AppThemeMode.Custom ? _activeCustomTheme?.Id : null,
                EnabledMetrics = _enabledMetricIds.ToArray()
            };

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
            File.WriteAllText(path, json);
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
