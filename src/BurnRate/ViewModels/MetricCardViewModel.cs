using CommunityToolkit.Mvvm.ComponentModel;
using BurnRate.Models;

namespace BurnRate.ViewModels;

/// <summary>
/// Lightweight ViewModel for a single configurable metric card in the dashboard.
/// Call Refresh() whenever the underlying UsageSummary changes to update the displayed value.
/// </summary>
public partial class MetricCardViewModel : ObservableObject
{
    private readonly Func<UsageSummary, string> _valueGetter;

    public string Label { get; }
    public string Hint { get; }

    [ObservableProperty]
    private string _formattedValue = "—";

    public MetricCardViewModel(string label, string hint, Func<UsageSummary, string> valueGetter)
    {
        Label = label;
        Hint = hint;
        _valueGetter = valueGetter;
    }

    public void Refresh(UsageSummary usage) => FormattedValue = _valueGetter(usage);
}
