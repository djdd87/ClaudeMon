using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BurnRate.Services;

namespace BurnRate.ViewModels;

public class MetricListItem
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string Hint { get; init; }
}

public partial class MetricsManagerViewModel : ObservableObject
{
    private readonly MainViewModel _mainVm;

    public ObservableCollection<MetricListItem> EnabledMetrics { get; } = [];
    public ObservableCollection<MetricListItem> AvailableMetrics { get; } = [];

    public MetricsManagerViewModel(MainViewModel mainVm)
    {
        _mainVm = mainVm;

        var enabledIds = mainVm.EnabledMetricIds.ToHashSet();

        foreach (var id in mainVm.EnabledMetricIds)
        {
            var def = MetricRegistry.Find(id);
            if (def != null)
                EnabledMetrics.Add(new MetricListItem { Id = def.Id, Label = def.Label, Hint = def.Hint });
        }

        foreach (var def in MetricRegistry.All)
        {
            if (!enabledIds.Contains(def.Id))
                AvailableMetrics.Add(new MetricListItem { Id = def.Id, Label = def.Label, Hint = def.Hint });
        }
    }

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp(MetricListItem? item)
    {
        if (item == null) return;
        var idx = EnabledMetrics.IndexOf(item);
        EnabledMetrics.Move(idx, idx - 1);
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        Apply();
    }

    private bool CanMoveUp(MetricListItem? item) =>
        item != null && EnabledMetrics.IndexOf(item) > 0;

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown(MetricListItem? item)
    {
        if (item == null) return;
        var idx = EnabledMetrics.IndexOf(item);
        EnabledMetrics.Move(idx, idx + 1);
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        Apply();
    }

    private bool CanMoveDown(MetricListItem? item) =>
        item != null && EnabledMetrics.IndexOf(item) < EnabledMetrics.Count - 1;

    [RelayCommand]
    private void Remove(MetricListItem? item)
    {
        if (item == null) return;
        EnabledMetrics.Remove(item);

        // Re-insert into AvailableMetrics maintaining registry order
        var allIds = MetricRegistry.All.Select(d => d.Id).ToList();
        var itemIdx = allIds.IndexOf(item.Id);
        var insertAt = AvailableMetrics
            .TakeWhile(m => allIds.IndexOf(m.Id) < itemIdx)
            .Count();
        AvailableMetrics.Insert(insertAt, item);

        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        Apply();
    }

    [RelayCommand]
    private void Add(MetricListItem? item)
    {
        if (item == null) return;
        AvailableMetrics.Remove(item);
        EnabledMetrics.Add(item);
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        Apply();
    }

    private void Apply() => _mainVm.SetEnabledMetrics(EnabledMetrics.Select(m => m.Id));
}
