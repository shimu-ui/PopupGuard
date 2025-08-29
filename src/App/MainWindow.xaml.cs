using System;
using System.Collections.ObjectModel;
using System.Windows;
using Core;
using System.IO;
using System.Windows.Forms;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Diagnostics;
using System.Windows.Threading;
using System.Collections.Generic;

namespace PopupGuard;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window, System.ComponentModel.INotifyPropertyChanged
{
    private readonly WinEventListener _listener = new();
    private readonly ObservableCollection<EventRow> _rows = new();
    private readonly RuleSet _rules = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PopupGuard", "rules.json"));
    private NotifyIcon? _tray;
    private System.Windows.Media.ImageSource? _appIconSource; // 窗口图标
    private System.Drawing.Icon? _appNotifyIcon; // 托盘图标
    public string LastPickedSummary { get; set; } = string.Empty;
    public Visibility LastPickedSummaryVisible => string.IsNullOrEmpty(LastPickedSummary) ? Visibility.Collapsed : Visibility.Visible;
    private int _currentBatchId = 0;
    private bool _onlyPicked;
    private int? _batchFilter;
    
    // 性能优化：UI更新节流
    private readonly DispatcherTimer _uiUpdateTimer;
    private readonly Queue<EventRow> _pendingRows = new();
    private int _uiUpdateIntervalMs = 200; // UI刷新间隔（可配置）
    private bool _enableIconCache = true;
    private int _maxRows = 500;

    // 性能优化：发布者信息缓存，避免重复签名解析
    private static readonly Dictionary<string, string> _publisherCache = new();
    private static string GetPublisherCached(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return string.Empty;
        if (_publisherCache.TryGetValue(path, out var cached)) return cached;
        var publisher = SignerInfo.TryGetPublisher(path) ?? string.Empty;
        if (_publisherCache.Count > 500) _publisherCache.Clear();
        _publisherCache[path] = publisher;
        return publisher;
    }
    
    public bool OnlyPicked { get => _onlyPicked; set { _onlyPicked = value; OnPropertyChanged(nameof(OnlyPicked)); _view?.Refresh(); } }
    public int? BatchFilter { get => _batchFilter; set { _batchFilter = value; OnPropertyChanged(nameof(BatchFilter)); _view?.Refresh(); } }
    public ObservableCollection<int> BatchIds { get; } = new();
    private ICollectionView? _view;

    public MainWindow()
    {
        InitializeComponent();
        EventsList.ItemsSource = _rows;
        _view = System.Windows.Data.CollectionViewSource.GetDefaultView(EventsList.ItemsSource);
        if (_view != null)
        {
            _view.Filter = FilterRow;
        }
        
        // 性能优化：初始化UI更新定时器
        _uiUpdateTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(_uiUpdateIntervalMs), DispatcherPriority.Background, OnUiUpdateTimer, Dispatcher);
        
        // 启动时加载性能设置
        TryLoadPerformanceSettings();

        // 生成并应用应用程序图标
        _appIconSource = CreateAppIconImageSource(32, 32);
        if (_appIconSource is BitmapSource bs)
        {
            this.Icon = bs;
        }
        // 确保生成多尺寸 .ico 到本地，便于快捷方式/安装包使用
        try { EnsureAppIcoFile(); } catch { }

        _listener.WindowShown += OnWindowShown;
        _rules.Load();
        _listener.Start();

        // Initialize tray icon
        _tray = new NotifyIcon
        {
            Text = "PopupGuard",
            Visible = true,
            Icon = (_appNotifyIcon = CreateAppNotifyIcon(32, 32)) ?? System.Drawing.SystemIcons.Application
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("显示", null, (_, _) => ShowAndActivate());
        menu.Items.Add("-"); // 分隔线
        menu.Items.Add("取窗工具", null, (_, _) => OnPickClickFromTray());
        menu.Items.Add("测试弹窗", null, (_, _) => OnTestClickFromTray());
        menu.Items.Add("-"); // 分隔线
        menu.Items.Add("设置", null, (_, _) => OnSettingsClickFromTray());
        menu.Items.Add("规则管理", null, (_, _) => OnRulesClickFromTray());
        menu.Items.Add("-"); // 分隔线
        menu.Items.Add("退出", null, (_, _) => { _tray!.Visible = false; Close(); });
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowAndActivate();
    }

    private void TryLoadPerformanceSettings()
    {
        try
        {
            var settingsPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PopupGuard", "filter_settings.json");
            if (!File.Exists(settingsPath)) return;
            var json = File.ReadAllText(settingsPath);
            var perf = System.Text.Json.JsonSerializer.Deserialize<PopupGuard.PerfSettings>(json);
            if (perf == null) return;
            ProcessInspector.EnableDetailedInfo = perf.EnableDetailedInfo;
            _listener.MinEventIntervalMs = perf.MinEventIntervalMs;
            _uiUpdateIntervalMs = perf.UiUpdateIntervalMs < 50 ? 50 : perf.UiUpdateIntervalMs;
            _uiUpdateTimer.Interval = TimeSpan.FromMilliseconds(_uiUpdateIntervalMs);
            _enableIconCache = perf.EnableIconCache;
            _maxRows = Math.Max(100, perf.MaxRows);
        }
        catch { }
    }

    private void OnWindowShown(WindowEventArgs e)
    {
        // 性能优化：异步处理进程信息，避免阻塞UI线程
        _ = Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                var info = ProcessInspector.TryGetProcessInfo(e.ProcessId);
                if (info != null && _rules.IsBlocked(info.ExecutablePath))
                {
                    // Skip showing blocked origin
                    return;
                }
                
                var row = new EventRow
                {
                    Source = string.Empty,
                    Time = DateTime.Now.ToString("HH:mm:ss"),
                    Hwnd = e.Hwnd,
                    ProcessId = e.ProcessId,
                    ProcessName = e.ProcessName,
                    // 保底：即使无法读取详细信息，也显示进程名
                    DisplayName = string.IsNullOrWhiteSpace(info?.DisplayName) ? (string.IsNullOrWhiteSpace(e.ProcessName) ? $"PID {e.ProcessId}" : e.ProcessName) : info!.DisplayName,
                    // 窗口标题作为最终兜底
                    WindowTitle = string.IsNullOrWhiteSpace(e.WindowTitle) ? GetWindowTitleSafe(e.Hwnd) : e.WindowTitle,
                    CompanyName = info?.CompanyName ?? string.Empty,
                    ExecutablePath = info?.ExecutablePath ?? string.Empty,
                    Publisher = GetPublisherCached(info?.ExecutablePath),
                    IsTrusted = Whitelist.IsTrusted(info),
                    Icon = _enableIconCache ? TryGetIcon(info?.ExecutablePath) : null
                };
                
                // 性能优化：批量UI更新
                lock (_pendingRows)
                {
                    _pendingRows.Enqueue(row);
                }
                
                // 启动UI更新定时器
                if (!_uiUpdateTimer.IsEnabled)
                {
                    _uiUpdateTimer.Start();
                }
            }
            catch (Exception ex)
            {
                // 记录错误但不中断程序
                Debug.WriteLine($"Error processing window event: {ex.Message}");
            }
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// 性能优化：UI更新定时器回调
    /// </summary>
    private void OnUiUpdateTimer(object? sender, EventArgs e)
    {
        lock (_pendingRows)
        {
            if (_pendingRows.Count == 0)
            {
                _uiUpdateTimer.Stop();
                return;
            }
            
            // 批量处理待更新的行
            var rowsToAdd = new List<EventRow>();
            while (_pendingRows.Count > 0 && rowsToAdd.Count < 10) // 每次最多处理10行
            {
                rowsToAdd.Add(_pendingRows.Dequeue());
            }
            
            // 批量插入到UI
            foreach (var row in rowsToAdd)
            {
                InsertFiltered(row);
            }
            
            // 清理旧数据
            TrimRows();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is EventRow row)
        {
            var ok = ProcessActions.TryCloseWindow(row.Hwnd);
            var name = string.IsNullOrWhiteSpace(row.DisplayName) ? 
                (string.IsNullOrWhiteSpace(row.ProcessName) ? $"PID {row.ProcessId}" : row.ProcessName) : 
                row.DisplayName;
            if (ok)
            {
                System.Windows.MessageBox.Show(this, $"已向 {name} 发送关闭指令（如有未保存数据，应用可能弹出确认）。", "已发送关闭", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show(this, $"{name} 未响应关闭。可尝试“强制结束”。", "关闭未生效", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void OnKillClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is EventRow row)
        {
            var confirmationText = row.IsTrusted
                ? "该来源被标记为可信（系统/厂商）。仍要强制结束该进程吗？"
                : "将强制结束该进程。可能导致未保存数据丢失。是否继续？";
            var result = System.Windows.MessageBox.Show(this, confirmationText, "确认强制结束", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            // 首先尝试普通强制结束
            if (ProcessActions.TryTerminateProcess(row.ProcessId, out var err))
            {
                var displayName = string.IsNullOrWhiteSpace(row.DisplayName) ? row.ProcessName : row.DisplayName;
                System.Windows.MessageBox.Show(this, $"已成功结束进程 {displayName} (PID: {row.ProcessId})", "操作成功", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 如果普通强制结束失败，尝试管理员权限强制结束
            var forceKillResult = System.Windows.MessageBox.Show(this, 
                $"普通强制结束失败: {err}\n\n是否尝试以管理员权限强制结束（类似任务管理器）？", 
                "需要管理员权限", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (forceKillResult == MessageBoxResult.Yes)
            {
                if (ProcessActions.IsRunningAsAdministrator())
                {
                    // 已有管理员权限，直接强制结束
                    if (ProcessActions.TryForceKillProcess(row.ProcessId, out var forceErr))
                    {
                        var displayName = string.IsNullOrWhiteSpace(row.DisplayName) ? row.ProcessName : row.DisplayName;
                        System.Windows.MessageBox.Show(this, $"已成功强制结束进程 {displayName} (PID: {row.ProcessId})", "操作成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        System.Windows.MessageBox.Show(this, $"强制结束失败: {forceErr}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    // 没有管理员权限，请求重启应用
                    var restartResult = System.Windows.MessageBox.Show(this, 
                        "需要管理员权限才能强制结束此进程。\n\n是否重启应用并请求管理员权限？", 
                        "需要管理员权限", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    
                    if (restartResult == MessageBoxResult.Yes)
                    {
                        if (ProcessActions.RestartAsAdministrator())
                        {
                            Close(); // 关闭当前应用
                        }
                        else
                        {
                            System.Windows.MessageBox.Show(this, "无法重启应用。请手动以管理员身份运行。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
            }
        }
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is EventRow row)
        {
            var info = ProcessInspector.TryGetProcessInfo(row.ProcessId);
            if (info != null)
            {
                ProcessActions.TryOpenFileLocation(info.ExecutablePath);
            }
        }
    }

    private void OnBlockClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is EventRow row)
        {
            if (string.IsNullOrWhiteSpace(row.ExecutablePath))
            {
                System.Windows.MessageBox.Show(this, "无法屏蔽：未获取到应用程序路径信息。", "屏蔽失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var displayName = string.IsNullOrWhiteSpace(row.DisplayName) ? row.ProcessName : row.DisplayName;
            var companyName = string.IsNullOrWhiteSpace(row.CompanyName) ? "未知公司" : row.CompanyName;
            
            var confirmMessage = $"确定要屏蔽以下应用程序吗？\n\n" +
                               $"应用名称：{displayName}\n" +
                               $"公司：{companyName}\n" +
                               $"路径：{row.ExecutablePath}\n\n" +
                               $"屏蔽后，该程序的弹窗将不再显示在列表中。\n" +
                               $"可在\"规则管理\"中随时取消屏蔽。";
            
            var result = System.Windows.MessageBox.Show(this, confirmMessage, "确认屏蔽", 
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                if (_rules.AddBlock(row.ExecutablePath))
                {
                    _rules.Save();
                    System.Windows.MessageBox.Show(this, 
                        $"已成功屏蔽：{displayName}\n\n" +
                        $"该程序的弹窗将不再显示。\n" +
                        $"如需取消屏蔽，请在\"规则管理\"中操作。", 
                        "屏蔽成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    System.Windows.MessageBox.Show(this, 
                        $"屏蔽失败：该程序已在屏蔽列表中。", 
                        "屏蔽失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        // 性能优化：停止UI更新定时器
        _uiUpdateTimer?.Stop();
        
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
        if (_appNotifyIcon != null)
        {
            _appNotifyIcon.Dispose();
            _appNotifyIcon = null;
        }
        _listener.Dispose();
        
        // 性能优化：清理缓存
        ProcessInspector.CleanupCache();
        
        base.OnClosed(e);
    }

    private void OnRulesClick(object sender, RoutedEventArgs e)
    {
        var win = new RulesWindow(_rules)
        {
            Owner = this
        };
        win.ShowDialog();
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var settings = new SettingsWindow { Owner = this };
        settings.ShowDialog();
    }

    /// <summary>
    /// 更新过滤设置
    /// </summary>
    public void UpdateFilterSettings(bool filterOwnProcess, bool enableSmartFiltering)
    {
        _listener.FilterOwnProcess = filterOwnProcess;
        _listener.EnableSmartFiltering = enableSmartFiltering;
        
        // 显示设置更新提示
        System.Windows.MessageBox.Show(this, 
            $"过滤设置已更新：\n" +
            $"• 过滤自己的进程：{(filterOwnProcess ? "已启用" : "已禁用")}\n" +
            $"• 智能过滤：{(enableSmartFiltering ? "已启用" : "已禁用")}\n\n" +
            $"设置将在下次检测到新窗口时生效。", 
            "设置已更新", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnExportCsvClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出CSV",
            Filter = "CSV文件 (*.csv)|*.csv",
            DefaultExt = "csv",
            FileName = $"弹窗记录_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var csv = new System.Text.StringBuilder();
                csv.AppendLine("时间,进程ID,软件名称,公司,发布者,路径,标题,来源,批次");
                
                foreach (var row in _rows)
                {
                    csv.AppendLine($"{Escape(row.Time)},{row.ProcessId},{Escape(row.DisplayName)},{Escape(row.CompanyName)},{Escape(row.Publisher)},{Escape(row.ExecutablePath)},{Escape(row.WindowTitle)},{Escape(row.Source)},{row.BatchId}");
                }
                
                File.WriteAllText(dialog.FileName, csv.ToString(), System.Text.Encoding.UTF8);
                System.Windows.MessageBox.Show(this, $"已成功导出 {_rows.Count} 条记录到：\n{dialog.FileName}", "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, $"导出失败：{ex.Message}", "导出错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void OnTestClick(object sender, RoutedEventArgs e)
    {
        try
        {
            // 创建一个测试弹窗进程
            var testProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    Arguments = "这是一个测试弹窗，用于验证弹窗检测功能。",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Normal
                }
            };
            
            testProcess.Start();
            
            // 等待一下让窗口显示
            System.Threading.Thread.Sleep(500);
            
            // 尝试获取窗口句柄
            if (testProcess.MainWindowHandle != IntPtr.Zero)
            {
                System.Windows.MessageBox.Show(this, 
                    $"已启动测试弹窗：记事本\n\n" +
                    $"现在你可以：\n" +
                    $"1. 使用'取窗'功能选择这个记事本窗口\n" +
                    $"2. 或者等待自动检测到这个窗口\n" +
                    $"3. 然后测试关闭、强制结束等功能\n\n" +
                    $"测试完成后记得关闭记事本！", 
                    "测试弹窗已启动", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"启动测试弹窗失败：{ex.Message}", "测试失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnClearBatchFilterClick(object sender, RoutedEventArgs e)
    {
        BatchFilter = null;
        OnPropertyChanged(nameof(BatchFilter));
    }

    private void OnPickClick(object sender, RoutedEventArgs e)
    {
        var picker = new PickerWindow { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        picker.BatchStarted += () => { _currentBatchId++; if (!BatchIds.Contains(_currentBatchId)) BatchIds.Insert(0, _currentBatchId); };
        
        // 显示取窗提示
        LastPickedSummary = "取窗模式已激活 - 拖拽十字准星到目标窗口";
        OnPropertyChanged(nameof(LastPickedSummary));
        OnPropertyChanged(nameof(LastPickedSummaryVisible));
        
        picker.Picked += hwnd =>
        {
            // 暂停自动监听，避免干扰取窗结果
            _listener.Stop();
            
            Core.WinEventListener.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != 0)
            {
                try
                {
                    var info = ProcessInspector.TryGetProcessInfo((int)pid);
                    var row = new EventRow
                    {
                        Source = "取窗",
                        Time = DateTime.Now.ToString("HH:mm:ss"),
                        Hwnd = hwnd,
                        ProcessId = (int)pid,
                        ProcessName = info?.ProcessName ?? $"PID_{pid}",
                        DisplayName = info?.DisplayName ?? info?.ProcessName ?? $"PID_{pid}",
                        WindowTitle = GetWindowTitleSafe(hwnd),
                        CompanyName = info?.CompanyName ?? string.Empty,
                        ExecutablePath = info?.ExecutablePath ?? string.Empty,
                        Publisher = SignerInfo.TryGetPublisher(info?.ExecutablePath) ?? string.Empty,
                        IsTrusted = Whitelist.IsTrusted(info),
                        Icon = _enableIconCache ? TryGetIcon(info?.ExecutablePath) : null,
                        BatchId = _currentBatchId
                    };
                    row.BatchColor = GetBatchBrush(_currentBatchId);
                    
                    // 直接插入到列表顶部，确保取窗结果可见
                    _rows.Insert(0, row);
                    
                    LastPickedSummary = $"已选择窗口: {row.DisplayName} (PID: {row.ProcessId})";
                    OnPropertyChanged(nameof(LastPickedSummary));
                    OnPropertyChanged(nameof(LastPickedSummaryVisible));
                }
                catch (Exception ex)
                {
                    LastPickedSummary = $"取窗失败: {ex.Message}";
                    OnPropertyChanged(nameof(LastPickedSummary));
                    OnPropertyChanged(nameof(LastPickedSummaryVisible));
                }
            }
        };
        picker.Closed += (_, _) => 
        {
            ShowAndActivate();
            // 确保监听器重新启动
            if (!_listener.IsListening)
            {
                _listener.Start();
            }
        };
        Hide();
        picker.Show();
    }

    /// <summary>
    /// 性能优化：安全获取窗口标题
    /// </summary>
    private static string GetWindowTitleSafe(IntPtr hwnd)
    {
        try
        {
            var length = Core.WinEventListener.GetWindowTextLength(hwnd);
            if (length == 0) return string.Empty;
            var buffer = new System.Text.StringBuilder(length + 1);
            _ = Core.WinEventListener.GetWindowText(hwnd, buffer, buffer.Capacity);
            return buffer.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

    /// <summary>
    /// 从设置窗口应用性能设置
    /// </summary>
    public void UpdatePerformanceSettings(bool enableDetailedInfo, int minEventIntervalMs, int uiUpdateIntervalMs, bool enableIconCache, int maxRows)
    {
        // 应用到Core层
        ProcessInspector.EnableDetailedInfo = enableDetailedInfo;
        _listener.MinEventIntervalMs = minEventIntervalMs;

        // 更新UI刷新间隔
        if (uiUpdateIntervalMs < 50) uiUpdateIntervalMs = 50;
        _uiUpdateIntervalMs = uiUpdateIntervalMs;
        _uiUpdateTimer.Interval = TimeSpan.FromMilliseconds(_uiUpdateIntervalMs);

        // 应用图标缓存与最大行数
        _enableIconCache = enableIconCache;
        _maxRows = Math.Max(100, maxRows);

        System.Windows.MessageBox.Show(this, $"性能设置已应用：\n\n详细信息(WMI)：{(enableDetailedInfo ? "开启" : "关闭")}\n事件节流：{minEventIntervalMs} ms\nUI刷新：{_uiUpdateIntervalMs} ms", "性能设置已应用", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
    }

    private void ShowAndActivate()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private System.Windows.Media.Brush GetBatchBrush(int id)
    {
        var colors = new[] { "#3498DB", "#27AE60", "#8E44AD", "#E67E22", "#E74C3C" };
        var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colors[Math.Abs(id) % colors.Length]);
        return new System.Windows.Media.SolidColorBrush(c);
    }

    private void InsertFiltered(EventRow row)
    {
        _rows.Insert(0, row);
    }

    private void TrimRows()
    {
        while (_rows.Count > _maxRows)
        {
            _rows.RemoveAt(_rows.Count - 1);
        }
    }

    private static string Escape(string? s)
    {
        s ??= string.Empty;
        if (s.Contains('"') || s.Contains(','))
        {
            s = '"' + s.Replace("\"", "\"\"") + '"';
        }
        return s;
    }

    private bool FilterRow(object obj)
    {
        if (obj is not EventRow r) return true;
        if (OnlyPicked && r.Source != "取窗") return false;
        if (BatchFilter.HasValue && r.BatchId != BatchFilter) return false;
        return true;
    }

    /// <summary>
    /// 尝试从可执行文件提取图标（解码为16x16以降低CPU与内存）
    /// </summary>
    private static System.Windows.Media.ImageSource? TryGetIcon(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return null;
        }

        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(executablePath);
            if (icon != null)
            {
                using var bitmap = icon.ToBitmap();
                var handle = bitmap.GetHbitmap();
                try
                {
                    var source = Imaging.CreateBitmapSourceFromHBitmap(
                        handle, IntPtr.Zero, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(16, 16));
                    source.Freeze();
                    return source;
                }
                finally
                {
                    DeleteObject(handle);
                }
            }
        }
        catch
        {
            // 忽略图标提取错误
        }

        return null;
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    // 生成应用图标（红色圆环+十字），用于窗口与托盘
    private static System.Windows.Media.ImageSource? CreateAppIconImageSource(int width, int height)
    {
        try
        {
            var dv = new System.Windows.Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var bg = System.Windows.Media.Brushes.Transparent;
                dc.DrawRectangle(bg, null, new System.Windows.Rect(0, 0, width, height));

                var centerX = width / 2.0;
                var centerY = height / 2.0;
                var radius = Math.Min(width, height) / 2.3;
                var pen = new System.Windows.Media.Pen(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 85, 85)), Math.Max(2, width / 16.0));
                pen.Freeze();
                dc.DrawEllipse(null, pen, new System.Windows.Point(centerX, centerY), radius, radius);

                var crossPen = new System.Windows.Media.Pen(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 85, 85)), Math.Max(1.5, width / 20.0));
                crossPen.Freeze();
                dc.DrawLine(crossPen, new System.Windows.Point(centerX - radius * 0.6, centerY), new System.Windows.Point(centerX + radius * 0.6, centerY));
                dc.DrawLine(crossPen, new System.Windows.Point(centerX, centerY - radius * 0.6), new System.Windows.Point(centerX, centerY + radius * 0.6));
            }

            var bmp = new RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    private static Icon? CreateAppNotifyIcon(int width, int height)
    {
        try
        {
            var src = CreateAppIconImageSource(width, height) as BitmapSource;
            if (src == null) return null;
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(src));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            using var bmp = new Bitmap(ms);
            return System.Drawing.Icon.FromHandle(bmp.GetHicon());
        }
        catch { return null; }
    }

    // 生成多尺寸ICO文件（PNG压缩帧），写入到 %LocalAppData%\PopupGuard\PopupGuard.ico
    private static void EnsureAppIcoFile()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PopupGuard");
        Directory.CreateDirectory(dir);
        var icoPath = Path.Combine(dir, "PopupGuard.ico");
        if (File.Exists(icoPath)) return;

        var sizes = new[] { 16, 24, 32, 48, 64, 128 };
        var pngFrames = new List<byte[]>();
        foreach (var s in sizes)
        {
            var src = CreateAppIconImageSource(s, s) as BitmapSource;
            if (src == null) continue;
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(src));
            using var ms = new MemoryStream();
            enc.Save(ms);
            pngFrames.Add(ms.ToArray());
        }
        if (pngFrames.Count == 0) return;
        File.WriteAllBytes(icoPath, BuildIcoFromPngFrames(pngFrames, sizes[..pngFrames.Count]));
    }

    // 简单ICO打包：每个条目使用PNG数据
    private static byte[] BuildIcoFromPngFrames(IList<byte[]> pngs, int[] sizes)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((ushort)0); // reserved
        bw.Write((ushort)1); // type: icon
        bw.Write((ushort)pngs.Count); // count

        int headerSize = 6 + 16 * pngs.Count;
        int offset = headerSize;
        var entries = new List<(int w,int h,int size,int off)>();
        for (int i = 0; i < pngs.Count; i++)
        {
            var data = pngs[i];
            var sz = sizes[Math.Min(i, sizes.Length - 1)];
            entries.Add((sz, sz, data.Length, offset));
            offset += data.Length;
        }
        foreach (var e in entries)
        {
            bw.Write((byte)(e.w == 256 ? 0 : e.w)); // width (0 means 256)
            bw.Write((byte)(e.h == 256 ? 0 : e.h)); // height
            bw.Write((byte)0); // color count
            bw.Write((byte)0); // reserved
            bw.Write((ushort)1); // planes
            bw.Write((ushort)32); // bit count
            bw.Write(e.size); // size of data
            bw.Write(e.off); // offset
        }
        // write image data
        for (int i = 0; i < pngs.Count; i++) bw.Write(pngs[i]);
        bw.Flush();
        return ms.ToArray();
    }

    // 托盘菜单专用方法
    private void OnPickClickFromTray()
    {
        ShowAndActivate();
        OnPickClick(this, new RoutedEventArgs());
    }

    private void OnTestClickFromTray()
    {
        ShowAndActivate();
        OnTestClick(this, new RoutedEventArgs());
    }

    private void OnSettingsClickFromTray()
    {
        ShowAndActivate();
        OnSettingsClick(this, new RoutedEventArgs());
    }

    private void OnRulesClickFromTray()
    {
        ShowAndActivate();
        OnRulesClick(this, new RoutedEventArgs());
    }


}

public sealed class EventRow
{
    public string Source { get; set; } = string.Empty;
    public int BatchId { get; set; }
    public System.Windows.Media.Brush BatchColor { get; set; } = System.Windows.Media.Brushes.Transparent;
    public string Time { get; set; } = string.Empty;
    public IntPtr Hwnd { get; set; }
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public bool IsTrusted { get; set; }
    public System.Windows.Media.ImageSource? Icon { get; set; }
}