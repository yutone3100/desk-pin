using System.Windows;
using System.Windows.Media;
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
                    WindowViewMode.Cards,
                    _ => Task.FromResult(SettingsApplyResult.Success()));
                mainWindow.Show();
                settingsWindow.Owner = mainWindow;
                settingsWindow.Show();
                mainWindow.UpdateLayout();
                settingsWindow.UpdateLayout();

                Assert.True(mainWindow.ActualWidth >= mainWindow.MinWidth);
                Assert.True(settingsWindow.ActualWidth > 0);
                Assert.Equal(new Thickness(0), WindowChrome.GetWindowChrome(mainWindow)?.GlassFrameThickness);
                Assert.Equal(new Thickness(0), WindowChrome.GetWindowChrome(settingsWindow)?.GlassFrameThickness);
                Assert.False(mainWindow.AllowsTransparency);
                Assert.False(settingsWindow.AllowsTransparency);
                Assert.True(WindowShadowService.IsAttached(mainWindow));
                Assert.True(WindowShadowService.IsAttached(settingsWindow));
                var searchBox = Assert.IsType<WpfTextBox>(mainWindow.FindName("SearchBox"));
                Assert.Equal(TextAlignment.Left, searchBox.TextAlignment);
                var windowList = Assert.IsType<WpfListView>(mainWindow.FindName("WindowList"));
                var gridView = Assert.IsType<WpfGridView>(windowList.View);
                Assert.DoesNotContain(gridView.Columns, column => Equals(column.Header, "PID"));
                Assert.Null(mainWindow.FindName("RefreshButton"));

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
        public long? LastEligibleWindowId => null;

        public IReadOnlyList<DesktopWindow> GetWindows() =>
        [
            new DesktopWindow(1, "季度报告 - 记事本", "notepad", 1234, false, null),
        ];

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
}
