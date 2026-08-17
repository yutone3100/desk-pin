using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Shell;
using DeskPin.Models;
using DeskPin.Services;
using WpfGridView = System.Windows.Controls.GridView;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfBorder = System.Windows.Controls.Border;
using WpfListView = System.Windows.Controls.ListView;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace DeskPin.Tests;

public sealed class WindowSmokeTests
{
    [Fact]
    public async Task MainAndSettingsWindowsCanBeShown()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            App? application = null;
            MainWindow? mainWindow = null;
            SettingsWindow? settingsWindow = null;
            try
            {
                application = new App();
                application.InitializeComponent();
                using var manager = new FakeWindowManager();
                settingsWindow = new SettingsWindow(
                    new AppSettings(),
                    _ => Task.FromResult(SettingsApplyResult.Success()));
                mainWindow = new MainWindow(
                    manager,
                    () => { },
                    () => { },
                    WindowViewMode.Cards,
                    _ => Task.FromResult(SettingsApplyResult.Success()));
                _ = new WindowInteropHelper(mainWindow).EnsureHandle();
                Assert.False(mainWindow.IsContentActive);
                Assert.False(mainWindow.IsRefreshTimerEnabled);
                Assert.Null(mainWindow.FindContentElement<WpfTextBox>("SearchBox"));
                Assert.Equal(0, manager.GetWindowsCallCount);
                mainWindow.EnterBackgroundMode();
                Assert.True(mainWindow.IsMemoryReclamationScheduled);
                mainWindow.ActivateContent();
                Assert.False(mainWindow.IsMemoryReclamationScheduled);
                mainWindow.Show();
                settingsWindow.Owner = mainWindow;
                settingsWindow.Show();
                mainWindow.UpdateLayout();
                settingsWindow.UpdateLayout();

                Assert.True(mainWindow.ActualWidth >= mainWindow.MinWidth);
                Assert.True(settingsWindow.ActualWidth > 0);
                Assert.True(WindowChrome.GetWindowChrome(mainWindow)?.GlassFrameThickness.Left > 0);
                Assert.True(WindowChrome.GetWindowChrome(settingsWindow)?.GlassFrameThickness.Left > 0);
                Assert.False(mainWindow.AllowsTransparency);
                Assert.False(settingsWindow.AllowsTransparency);
                Assert.True(WindowShadowService.IsAttached(mainWindow));
                Assert.True(WindowShadowService.IsAttached(settingsWindow));
                Assert.True(HasShadowCapableFrame(mainWindow));
                Assert.True(HasShadowCapableFrame(settingsWindow));
                var searchBox = Assert.IsType<WpfTextBox>(mainWindow.FindContentElement<WpfTextBox>("SearchBox"));
                Assert.Equal(TextAlignment.Left, searchBox.TextAlignment);
                Assert.NotNull(mainWindow.FindContentElement<System.Windows.Controls.ItemsControl>("CardWindowList"));
                Assert.Null(mainWindow.FindContentElement<WpfListView>("WindowList"));
                Assert.Null(mainWindow.FindContentElement<System.Windows.Controls.Button>("RefreshButton"));

                var viewModel = Assert.IsType<DeskPin.ViewModels.MainViewModel>(mainWindow.DataContext);
                Assert.True(viewModel.SetViewMode(WindowViewMode.List));
                mainWindow.UpdateLayout();
                Assert.Null(mainWindow.FindContentElement<System.Windows.Controls.ItemsControl>("CardWindowList"));
                var windowList = Assert.IsType<WpfListView>(mainWindow.FindContentElement<WpfListView>("WindowList"));
                var gridView = Assert.IsType<WpfGridView>(windowList.View);
                Assert.DoesNotContain(gridView.Columns, column => Equals(column.Header, "PID"));

                var contextMenu = Assert.IsType<WpfContextMenu>(mainWindow.Resources["WindowContextMenu"]);
                Assert.False(contextMenu.HasDropShadow);
                Assert.True(contextMenu.OverridesDefaultStyle);
                Assert.NotNull(contextMenu.Template);
                var menuItems = contextMenu.Items.OfType<System.Windows.Controls.MenuItem>().ToArray();
                Assert.Equal("打开", menuItems.First().Header);
                Assert.Equal("删除", menuItems.Last().Header);
                Assert.All(menuItems, item =>
                {
                    Assert.Equal(System.Windows.HorizontalAlignment.Center, item.HorizontalContentAlignment);
                    Assert.Equal(
                        Assert.IsType<SolidColorBrush>(application.Resources["ContextMenuTextBrush"]).Color,
                        Assert.IsType<SolidColorBrush>(item.Foreground).Color);
                    Assert.NotNull(item.Template);
                });

                var hotkeyHeader = Assert.IsType<System.Windows.Controls.Grid>(settingsWindow.FindName("HotkeyHeader"));
                Assert.Empty(FindVisualChildren<System.Windows.Shapes.Path>(hotkeyHeader));

                var startupSwitch = Assert.IsType<WpfCheckBox>(settingsWindow.FindName("StartWithWindowsCheckBox"));
                startupSwitch.ApplyTemplate();
                var track = Assert.IsType<WpfBorder>(startupSwitch.Template.FindName("Track", startupSwitch));
                var initialWidth = track.ActualWidth;
                var initialPosition = track.TranslatePoint(new System.Windows.Point(), settingsWindow);
                startupSwitch.IsChecked = true;
                settingsWindow.UpdateLayout();
                Assert.Equal(initialWidth, track.ActualWidth);
                Assert.Equal(initialPosition, track.TranslatePoint(new System.Windows.Point(), settingsWindow));
                Assert.Null(startupSwitch.FocusVisualStyle);

                settingsWindow.Close();
                settingsWindow = null;
                mainWindow.Close();
                Assert.False(mainWindow.IsVisible);
                Assert.False(mainWindow.IsContentActive);
                Assert.False(mainWindow.IsRefreshTimerEnabled);
                Assert.Null(mainWindow.FindContentElement<WpfTextBox>("SearchBox"));
                Assert.Empty(Assert.IsType<DeskPin.ViewModels.MainViewModel>(mainWindow.DataContext).Windows);

                for (var cycle = 0; cycle < 20; cycle++)
                {
                    mainWindow.ActivateContent();
                    mainWindow.Show();
                    mainWindow.UpdateLayout();
                    Assert.True(mainWindow.IsContentActive);
                    Assert.True(mainWindow.IsRefreshTimerEnabled);
                    Assert.NotNull(mainWindow.FindContentElement<WpfTextBox>("SearchBox"));
                    mainWindow.Close();
                    Assert.False(mainWindow.IsContentActive);
                    Assert.False(mainWindow.IsRefreshTimerEnabled);
                    Assert.Null(mainWindow.FindContentElement<WpfTextBox>("SearchBox"));
                }
                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
            finally
            {
                settingsWindow?.Close();
                mainWindow?.AllowClose();
                mainWindow?.Close();
                application?.Shutdown();
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        thread.Join(TimeSpan.FromSeconds(5));
    }

    private sealed class FakeWindowManager : IWindowManager
    {
        public int GetWindowsCallCount { get; private set; }
        public long? LastEligibleWindowId => null;

        public IReadOnlyList<DesktopWindow> GetWindows()
        {
            GetWindowsCallCount++;
            return
            [
                new DesktopWindow(1, "季度报告 - 记事本", "notepad", 1234, false, null),
            ];
        }

        public WindowOperationResult ToggleTopmost(long windowId) => WindowOperationResult.Success(true);
        public WindowOperationResult ToggleLastEligibleWindow() => WindowOperationResult.Success(true);
        public WindowActionResult ShowWindow(long windowId) => WindowActionResult.Success("窗口已显示");
        public WindowActionResult CloseWindow(long windowId) => WindowActionResult.Success("已发送关闭请求");
        public int RestoreWindowsPinnedByDeskPin() => 0;
        public void Dispose()
        {
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static bool HasShadowCapableFrame(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlStyle).ToInt64();
        return (style & NativeMethods.WsThickFrame) != 0;
    }

}
