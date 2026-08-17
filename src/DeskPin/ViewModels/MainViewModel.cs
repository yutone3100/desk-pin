using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using DeskPin.Models;
using DeskPin.Services;

namespace DeskPin.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IWindowManager _windowManager;
    private readonly object _refreshSync = new();
    private Task _currentRefresh = Task.CompletedTask;
    private string _searchText = string.Empty;
    private bool _onlyTopmost;
    private bool _isOperating;
    private bool _hasVisibleWindows;
    private int _visibleWindowCount;
    private int _pinnedWindowCount;
    private WindowViewMode _viewMode;
    private string _viewPreferenceError = string.Empty;
    private string _refreshError = string.Empty;
    private int _operating;
    private int _refreshGeneration;
    private volatile bool _isActive = true;

    public MainViewModel(IWindowManager windowManager, WindowViewMode initialViewMode = WindowViewMode.Cards)
    {
        _windowManager = windowManager;
        _viewMode = initialViewMode;
        WindowsView = CollectionViewSource.GetDefaultView(Windows);
        WindowsView.Filter = FilterWindow;
    }

    public ObservableCollection<DesktopWindow> Windows { get; } = [];
    public ICollectionView WindowsView { get; }

    public WindowViewMode ViewMode
    {
        get => _viewMode;
        private set
        {
            if (!SetField(ref _viewMode, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsCardView));
            OnPropertyChanged(nameof(IsListView));
        }
    }

    public bool IsCardView => ViewMode == WindowViewMode.Cards;
    public bool IsListView => ViewMode == WindowViewMode.List;

    public bool HasVisibleWindows
    {
        get => _hasVisibleWindows;
        private set => SetField(ref _hasVisibleWindows, value);
    }

    public int VisibleWindowCount
    {
        get => _visibleWindowCount;
        private set => SetField(ref _visibleWindowCount, value);
    }

    public int PinnedWindowCount
    {
        get => _pinnedWindowCount;
        private set => SetField(ref _pinnedWindowCount, value);
    }

    public string ViewPreferenceError
    {
        get => _viewPreferenceError;
        private set
        {
            if (SetField(ref _viewPreferenceError, value))
            {
                OnPropertyChanged(nameof(HasViewPreferenceError));
            }
        }
    }

    public bool HasViewPreferenceError => !string.IsNullOrWhiteSpace(ViewPreferenceError);

    public string RefreshError
    {
        get => _refreshError;
        private set
        {
            if (SetField(ref _refreshError, value))
            {
                OnPropertyChanged(nameof(HasRefreshError));
            }
        }
    }

    public bool HasRefreshError => !string.IsNullOrWhiteSpace(RefreshError);

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value))
            {
                WindowsView.Refresh();
                UpdateStatusCount();
            }
        }
    }

    public bool OnlyTopmost
    {
        get => _onlyTopmost;
        set
        {
            if (SetField(ref _onlyTopmost, value))
            {
                WindowsView.Refresh();
                UpdateStatusCount();
            }
        }
    }

    public bool IsOperating
    {
        get => _isOperating;
        private set => SetField(ref _isOperating, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool SetViewMode(WindowViewMode viewMode)
    {
        if (ViewMode == viewMode)
        {
            return false;
        }

        ViewMode = viewMode;
        ViewPreferenceError = string.Empty;
        return true;
    }

    public void SetViewPreferenceError(string message) => ViewPreferenceError = message;

    public Task RefreshAsync()
    {
        lock (_refreshSync)
        {
            if (!_isActive || !_currentRefresh.IsCompleted)
            {
                return Task.CompletedTask;
            }

            _currentRefresh = RefreshCoreAsync(Volatile.Read(ref _refreshGeneration));
            return _currentRefresh;
        }
    }

    internal void Resume()
    {
        Interlocked.Increment(ref _refreshGeneration);
        _isActive = true;
    }

    internal void Suspend()
    {
        _isActive = false;
        Interlocked.Increment(ref _refreshGeneration);
        Windows.Clear();
        WindowsView.Refresh();
        UpdateStatusCount();
        (_windowManager as Win32WindowManager)?.ClearEnumerationCache();
    }

    private async Task RefreshCoreAsync(int generation)
    {
        try
        {
            var windows = await Task.Run(_windowManager.GetWindows);
            if (!_isActive || generation != Volatile.Read(ref _refreshGeneration))
            {
                (_windowManager as Win32WindowManager)?.ClearEnumerationCache();
                return;
            }

            ReconcileWindows(windows);
            UpdateStatusCount();
            RefreshError = string.Empty;
        }
        catch (Exception exception)
        {
            RefreshError = $"刷新失败：{exception.Message}";
        }
    }

    public async Task<WindowOperationResult> ToggleAsync(DesktopWindow window)
    {
        if (Interlocked.Exchange(ref _operating, 1) != 0)
        {
            return WindowOperationResult.Failure(WindowOperationError.NativeFailure, "正在处理另一个窗口操作，请稍候");
        }

        IsOperating = true;
        try
        {
            var result = await Task.Run(() => _windowManager.ToggleTopmost(window.Id));
            if (result.Succeeded && result.IsTopmost is bool isTopmost)
            {
                ApplyTopmostResult(window.Id, isTopmost);
            }

            await RefreshAsync();
            return result;
        }
        finally
        {
            IsOperating = false;
            Interlocked.Exchange(ref _operating, 0);
        }
    }

    public WindowActionResult ShowWindow(DesktopWindow window) =>
        _windowManager.ShowWindow(window.Id);

    public WindowActionResult CloseWindow(DesktopWindow window) =>
        _windowManager.CloseWindow(window.Id);

    internal static bool Matches(DesktopWindow window, string searchText, bool onlyTopmost)
    {
        if (onlyTopmost && !window.IsTopmost)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        var term = searchText.Trim();
        return window.Title.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
            window.ProcessName.Contains(term, StringComparison.CurrentCultureIgnoreCase);
    }

    private bool FilterWindow(object item) =>
        item is DesktopWindow window && Matches(window, SearchText, OnlyTopmost);

    internal bool ReconcileWindows(IReadOnlyList<DesktopWindow> latest)
    {
        var latestIds = latest.Select(window => window.Id).ToHashSet();
        var changed = false;

        for (var index = Windows.Count - 1; index >= 0; index--)
        {
            if (latestIds.Contains(Windows[index].Id))
            {
                continue;
            }

            Windows.RemoveAt(index);
            changed = true;
        }

        for (var targetIndex = 0; targetIndex < latest.Count; targetIndex++)
        {
            var incoming = latest[targetIndex];
            var currentIndex = IndexOfWindow(incoming.Id);
            if (currentIndex < 0)
            {
                Windows.Insert(targetIndex, incoming);
                changed = true;
                continue;
            }

            var current = Windows[currentIndex];
            var replacement = HasSameDisplayData(current, incoming)
                ? current
                : incoming with { Icon = incoming.Icon ?? current.Icon };

            if (currentIndex != targetIndex)
            {
                Windows.Move(currentIndex, targetIndex);
                changed = true;
            }

            if (!ReferenceEquals(replacement, current))
            {
                Windows[targetIndex] = replacement;
                changed = true;
            }
        }

        return changed;
    }

    private void ApplyTopmostResult(long windowId, bool isTopmost)
    {
        var index = IndexOfWindow(windowId);
        if (index < 0 || Windows[index].IsTopmost == isTopmost)
        {
            return;
        }

        Windows[index] = Windows[index] with { IsTopmost = isTopmost };
        WindowsView.Refresh();
        UpdateStatusCount();
    }

    private int IndexOfWindow(long id)
    {
        for (var index = 0; index < Windows.Count; index++)
        {
            if (Windows[index].Id == id)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool HasSameDisplayData(DesktopWindow current, DesktopWindow incoming) =>
        current.Id == incoming.Id &&
        current.Title == incoming.Title &&
        current.ProcessName == incoming.ProcessName &&
        current.ProcessId == incoming.ProcessId &&
        current.IsTopmost == incoming.IsTopmost &&
        (current.Icon is not null || incoming.Icon is null);

    private void UpdateStatusCount()
    {
        var visibleCount = WindowsView.Cast<object>().Count();
        var pinnedCount = Windows.Count(window => window.IsTopmost);
        VisibleWindowCount = visibleCount;
        PinnedWindowCount = pinnedCount;
        HasVisibleWindows = visibleCount > 0;
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
