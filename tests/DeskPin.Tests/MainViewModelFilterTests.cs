using DeskPin.Models;
using DeskPin.Services;
using DeskPin.ViewModels;

namespace DeskPin.Tests;

public sealed class MainViewModelFilterTests
{
    private static readonly DesktopWindow Window = new(
        1,
        "季度报告 - 记事本",
        "notepad",
        1234,
        true,
        null);

    [Theory]
    [InlineData("季度", true)]
    [InlineData("NOTEPAD", true)]
    [InlineData("1234", false)]
    [InlineData("浏览器", false)]
    public void MatchesTitleAndProcessButNotPid(string search, bool expected)
    {
        Assert.Equal(expected, MainViewModel.Matches(Window, search, onlyTopmost: false));
    }

    [Fact]
    public void TopmostFilterRejectsNormalWindow()
    {
        var normal = Window with { IsTopmost = false };
        Assert.False(MainViewModel.Matches(normal, string.Empty, onlyTopmost: true));
    }

    [Fact]
    public void ViewModeDefaultsToCardsAndCanBeChanged()
    {
        using var manager = new FakeWindowManager([]);
        var viewModel = new MainViewModel(manager);

        Assert.True(viewModel.IsCardView);
        Assert.False(viewModel.IsListView);
        Assert.True(viewModel.SetViewMode(WindowViewMode.List));
        Assert.True(viewModel.IsListView);
        Assert.False(viewModel.SetViewMode(WindowViewMode.List));
    }

    [Fact]
    public void RefreshReusesUnchangedWindowsAndReconcilesChanges()
    {
        var first = new DesktopWindow(10, "Alpha", "app-a", 100, false, null);
        var second = new DesktopWindow(20, "Beta", "app-b", 200, false, null);
        using var manager = new FakeWindowManager([first, second]);
        var viewModel = new MainViewModel(manager);

        viewModel.ReconcileWindows([first, second]);
        var unchangedReference = viewModel.Windows[0];
        Assert.False(viewModel.ReconcileWindows(
        [
            new DesktopWindow(10, "Alpha", "app-a", 100, false, null),
            new DesktopWindow(20, "Beta", "app-b", 200, false, null),
        ]));

        Assert.Same(unchangedReference, viewModel.Windows[0]);

        viewModel.ReconcileWindows(
        [
            new DesktopWindow(20, "Beta updated", "app-b", 200, true, null),
            new DesktopWindow(30, "Gamma", "app-c", 300, false, null),
        ]);

        Assert.Equal([20L, 30L], viewModel.Windows.Select(window => window.Id));
        Assert.Equal("Beta updated", viewModel.Windows[0].Title);
        Assert.True(viewModel.Windows[0].IsTopmost);
        viewModel.OnlyTopmost = true;
        Assert.Equal(1, viewModel.VisibleWindowCount);
        viewModel.OnlyTopmost = false;
        Assert.Equal(2, viewModel.VisibleWindowCount);
        Assert.Equal(1, viewModel.PinnedWindowCount);
    }

    [Fact]
    public async Task RefreshDoesNotEnterOperatingStateAndSkipsOverlap()
    {
        using var manager = new FakeWindowManager([]);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        manager.GetWindowsOverride = () =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(5));
            return [];
        };
        var viewModel = new MainViewModel(manager);

        var firstRefresh = viewModel.RefreshAsync();
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(viewModel.IsOperating);
            await viewModel.RefreshAsync();
            Assert.Equal(1, manager.GetWindowsCallCount);
        }
        finally
        {
            release.Set();
        }

        await firstRefresh;
        Assert.False(viewModel.HasRefreshError, viewModel.RefreshError);
    }

    [Fact]
    public async Task RefreshErrorClearsAfterNextSuccessfulRefresh()
    {
        using var manager = new FakeWindowManager([])
        {
            GetWindowsOverride = () => throw new InvalidOperationException("测试错误"),
        };
        var viewModel = new MainViewModel(manager);

        await viewModel.RefreshAsync();

        Assert.True(viewModel.HasRefreshError);
        Assert.Contains("测试错误", viewModel.RefreshError);

        manager.GetWindowsOverride = () => [];
        await viewModel.RefreshAsync();

        Assert.False(viewModel.HasRefreshError, viewModel.RefreshError);
        Assert.Equal(string.Empty, viewModel.RefreshError);
        Assert.Empty(viewModel.Windows);
    }

    private sealed class FakeWindowManager(IReadOnlyList<DesktopWindow> windows) : IWindowManager
    {
        public IReadOnlyList<DesktopWindow> Windows { get; set; } = windows;
        public Func<IReadOnlyList<DesktopWindow>>? GetWindowsOverride { get; set; }
        public int GetWindowsCallCount { get; private set; }
        public long? LastEligibleWindowId => null;
        public IReadOnlyList<DesktopWindow> GetWindows()
        {
            GetWindowsCallCount++;
            return GetWindowsOverride?.Invoke() ?? Windows;
        }
        public WindowOperationResult ToggleTopmost(long windowId) => WindowOperationResult.Success(false);
        public WindowOperationResult ToggleLastEligibleWindow() => WindowOperationResult.Success(false);
        public WindowActionResult ShowWindow(long windowId) => WindowActionResult.Success("窗口已显示");
        public WindowActionResult CloseWindow(long windowId) => WindowActionResult.Success("已发送关闭请求");
        public int RestoreWindowsPinnedByDeskPin() => 0;
        public void Dispose()
        {
        }
    }
}
