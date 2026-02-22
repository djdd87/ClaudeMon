using BurnRate.Services;
using BurnRate.ViewModels;

namespace BurnRate.Tests.ViewModels;

public sealed class MetricsManagerViewModelTests : IDisposable
{
    private readonly ThemeService _themeService;
    private readonly MainViewModel _mainVm;

    public MetricsManagerViewModelTests()
    {
        _themeService = new ThemeService();
        // Seed with 3 known metrics in a specific order
        _themeService.SetEnabledMetrics(["TodaySessions", "WeeklyTokens", "DailyBurn"]);
        _mainVm = new MainViewModel(_themeService);
    }

    public void Dispose()
    {
        _mainVm.Dispose();
        _themeService.Dispose();
    }

    #region Constructor

    [Fact]
    public void Constructor_EnabledMetrics_MatchesMainViewModelOrder()
    {
        var vm = new MetricsManagerViewModel(_mainVm);

        Assert.Equal(3, vm.EnabledMetrics.Count);
        Assert.Equal("TodaySessions", vm.EnabledMetrics[0].Id);
        Assert.Equal("WeeklyTokens", vm.EnabledMetrics[1].Id);
        Assert.Equal("DailyBurn", vm.EnabledMetrics[2].Id);
    }

    [Fact]
    public void Constructor_AvailableMetrics_ExcludesEnabledIds()
    {
        var vm = new MetricsManagerViewModel(_mainVm);

        var enabledIds = new HashSet<string> { "TodaySessions", "WeeklyTokens", "DailyBurn" };
        Assert.All(vm.AvailableMetrics, m => Assert.DoesNotContain(m.Id, enabledIds));
    }

    [Fact]
    public void Constructor_TotalMetricCount_EqualsRegistry()
    {
        var vm = new MetricsManagerViewModel(_mainVm);

        Assert.Equal(MetricRegistry.All.Count, vm.EnabledMetrics.Count + vm.AvailableMetrics.Count);
    }

    [Fact]
    public void Constructor_AvailableMetrics_AreInRegistryOrder()
    {
        var vm = new MetricsManagerViewModel(_mainVm);

        var allIds = MetricRegistry.All.Select(d => d.Id).ToList();
        var availableIndices = vm.AvailableMetrics.Select(m => allIds.IndexOf(m.Id)).ToList();
        Assert.Equal(availableIndices.OrderBy(i => i).ToList(), availableIndices);
    }

    [Fact]
    public void Constructor_MetricListItem_HasCorrectLabel()
    {
        var vm = new MetricsManagerViewModel(_mainVm);

        var def = MetricRegistry.Find("TodaySessions")!;
        Assert.Equal(def.Label, vm.EnabledMetrics[0].Label);
    }

    [Fact]
    public void Constructor_MetricListItem_HasCorrectHint()
    {
        var vm = new MetricsManagerViewModel(_mainVm);

        var def = MetricRegistry.Find("TodaySessions")!;
        Assert.Equal(def.Hint, vm.EnabledMetrics[0].Hint);
    }

    #endregion

    #region MoveUp

    [Fact]
    public void MoveUp_FirstItem_CannotExecute()
    {
        var vm = new MetricsManagerViewModel(_mainVm);

        Assert.False(vm.MoveUpCommand.CanExecute(vm.EnabledMetrics[0]));
    }

    [Fact]
    public void MoveUp_SecondItem_CanExecute()
    {
        var vm = new MetricsManagerViewModel(_mainVm);

        Assert.True(vm.MoveUpCommand.CanExecute(vm.EnabledMetrics[1]));
    }

    [Fact]
    public void MoveUp_MovesItemToCorrectPosition()
    {
        var vm = new MetricsManagerViewModel(_mainVm);
        var secondItem = vm.EnabledMetrics[1]; // WeeklyTokens

        vm.MoveUpCommand.Execute(secondItem);

        Assert.Equal("WeeklyTokens", vm.EnabledMetrics[0].Id);
        Assert.Equal("TodaySessions", vm.EnabledMetrics[1].Id);
        Assert.Equal("DailyBurn", vm.EnabledMetrics[2].Id);
    }

    [Fact]
    public void MoveUp_PersistsNewOrder()
    {
        var vm = new MetricsManagerViewModel(_mainVm);
        var secondItem = vm.EnabledMetrics[1];

        vm.MoveUpCommand.Execute(secondItem);

        Assert.Equal(vm.EnabledMetrics.Select(m => m.Id).ToList(), _mainVm.EnabledMetricIds.ToList());
    }

    [Fact]
    public void MoveUp_AfterMove_FirstItem_CannotMoveUp()
    {
        var vm = new MetricsManagerViewModel(_mainVm);
        var secondItem = vm.EnabledMetrics[1];

        vm.MoveUpCommand.Execute(secondItem); // moves to index 0

        Assert.False(vm.MoveUpCommand.CanExecute(secondItem));
    }

    #endregion

    #region MoveDown

    [Fact]
    public void MoveDown_LastItem_CannotExecute()
    {
        var vm = new MetricsManagerViewModel(_mainVm);

        Assert.False(vm.MoveDownCommand.CanExecute(vm.EnabledMetrics[^1]));
    }

    [Fact]
    public void MoveDown_FirstItem_CanExecute()
    {
        var vm = new MetricsManagerViewModel(_mainVm);

        Assert.True(vm.MoveDownCommand.CanExecute(vm.EnabledMetrics[0]));
    }

    [Fact]
    public void MoveDown_MovesItemToCorrectPosition()
    {
        var vm = new MetricsManagerViewModel(_mainVm);
        var firstItem = vm.EnabledMetrics[0]; // TodaySessions

        vm.MoveDownCommand.Execute(firstItem);

        Assert.Equal("WeeklyTokens", vm.EnabledMetrics[0].Id);
        Assert.Equal("TodaySessions", vm.EnabledMetrics[1].Id);
        Assert.Equal("DailyBurn", vm.EnabledMetrics[2].Id);
    }

    [Fact]
    public void MoveDown_PersistsNewOrder()
    {
        var vm = new MetricsManagerViewModel(_mainVm);
        var firstItem = vm.EnabledMetrics[0];

        vm.MoveDownCommand.Execute(firstItem);

        Assert.Equal(vm.EnabledMetrics.Select(m => m.Id).ToList(), _mainVm.EnabledMetricIds.ToList());
    }

    [Fact]
    public void MoveDown_AfterMove_LastItem_CannotMoveDown()
    {
        var vm = new MetricsManagerViewModel(_mainVm);
        var secondToLast = vm.EnabledMetrics[^2]; // DailyBurn is last, so WeeklyTokens is 2nd to last

        vm.MoveDownCommand.Execute(secondToLast); // moves to last position

        Assert.False(vm.MoveDownCommand.CanExecute(secondToLast));
    }

    #endregion

    #region Remove

    [Fact]
    public void Remove_DecreasesEnabledMetricsCount()
    {
        var vm = new MetricsManagerViewModel(_mainVm);

        vm.RemoveCommand.Execute(vm.EnabledMetrics[0]);

        Assert.Equal(2, vm.EnabledMetrics.Count);
    }

    [Fact]
    public void Remove_IncreasesAvailableMetricsCount()
    {
        var vm = new MetricsManagerViewModel(_mainVm);
        var initialAvailableCount = vm.AvailableMetrics.Count;

        vm.RemoveCommand.Execute(vm.EnabledMetrics[0]);

        Assert.Equal(initialAvailableCount + 1, vm.AvailableMetrics.Count);
    }

    [Fact]
    public void Remove_ItemAppearsInAvailable()
    {
        var vm = new MetricsManagerViewModel(_mainVm);
        var item = vm.EnabledMetrics[1]; // WeeklyTokens

        vm.RemoveCommand.Execute(item);

        Assert.Contains(vm.AvailableMetrics, m => m.Id == "WeeklyTokens");
        Assert.DoesNotContain(vm.EnabledMetrics, m => m.Id == "WeeklyTokens");
    }

    [Fact]
    public void Remove_ReinsertsMaintainingRegistryOrder()
    {
        var vm = new MetricsManagerViewModel(_mainVm);
        // Initial available (registry order): TodayMessages(0), TodayTokens(1), Runway(5), ...
        // Removing WeeklyTokens (registry idx 3) should land it at position 2
        // (after TodayMessages and TodayTokens, before Runway)
        var weeklyTokens = vm.EnabledMetrics.First(m => m.Id == "WeeklyTokens");

        vm.RemoveCommand.Execute(weeklyTokens);

        Assert.Equal("WeeklyTokens", vm.AvailableMetrics[2].Id);
    }

    [Fact]
    public void Remove_PersistsChange()
    {
        var vm = new MetricsManagerViewModel(_mainVm);
        var item = vm.EnabledMetrics[0]; // TodaySessions

        vm.RemoveCommand.Execute(item);

        Assert.DoesNotContain(_mainVm.EnabledMetricIds, id => id == "TodaySessions");
    }

    #endregion

    #region Add

    [Fact]
    public void Add_IncreasesEnabledMetricsCount()
    {
        var vm = new MetricsManagerViewModel(_mainVm);

        vm.AddCommand.Execute(vm.AvailableMetrics[0]);

        Assert.Equal(4, vm.EnabledMetrics.Count);
    }

    [Fact]
    public void Add_DecreasesAvailableMetricsCount()
    {
        var vm = new MetricsManagerViewModel(_mainVm);
        var initialAvailableCount = vm.AvailableMetrics.Count;

        vm.AddCommand.Execute(vm.AvailableMetrics[0]);

        Assert.Equal(initialAvailableCount - 1, vm.AvailableMetrics.Count);
    }

    [Fact]
    public void Add_AppendsToEndOfEnabledMetrics()
    {
        var vm = new MetricsManagerViewModel(_mainVm);
        var item = vm.AvailableMetrics[0];

        vm.AddCommand.Execute(item);

        Assert.Equal(item.Id, vm.EnabledMetrics[^1].Id);
    }

    [Fact]
    public void Add_ItemDisappearsFromAvailable()
    {
        var vm = new MetricsManagerViewModel(_mainVm);
        var item = vm.AvailableMetrics[0];
        var itemId = item.Id;

        vm.AddCommand.Execute(item);

        Assert.DoesNotContain(vm.AvailableMetrics, m => m.Id == itemId);
    }

    [Fact]
    public void Add_PersistsChange()
    {
        var vm = new MetricsManagerViewModel(_mainVm);
        var item = vm.AvailableMetrics[0];

        vm.AddCommand.Execute(item);

        Assert.Contains(_mainVm.EnabledMetricIds, id => id == item.Id);
    }

    #endregion

    #region Round-trip

    [Fact]
    public void RemoveThenAdd_RestoresOriginalState()
    {
        var vm = new MetricsManagerViewModel(_mainVm);
        var item = vm.EnabledMetrics[1]; // WeeklyTokens

        vm.RemoveCommand.Execute(item);
        vm.AddCommand.Execute(item); // re-add — goes to end

        // WeeklyTokens is back in Enabled (at end), not in Available
        Assert.Contains(vm.EnabledMetrics, m => m.Id == "WeeklyTokens");
        Assert.DoesNotContain(vm.AvailableMetrics, m => m.Id == "WeeklyTokens");
    }

    #endregion
}
