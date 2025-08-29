using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using Timers = System.Timers;

namespace Core;

public sealed class WinEventListener : IDisposable
{
    public event Action<WindowEventArgs>? WindowShown;

    private IntPtr _hookObjectCreate = IntPtr.Zero;
    private IntPtr _hookObjectShow = IntPtr.Zero;
    private IntPtr _hookForeground = IntPtr.Zero;
    private WinEventDelegate? _callback;
    private readonly int _currentProcessId;
    
    // 性能优化：缓存和节流
    private readonly Dictionary<IntPtr, long> _recentWindows = new();
    private readonly Dictionary<string, bool> _processFilterCache = new();
    private long _lastEventTime = 0;
    /// <summary>
    /// 事件最小触发间隔（毫秒），用于节流，默认100ms，可在设置中调整
    /// </summary>
    public int MinEventIntervalMs { get; set; } = 100;
    
    // 覆盖型补偿：定时轮询前台与枚举顶层窗口，避免漏报
    private Timers.Timer? _pollTimer;
    public bool EnableWatchdogScan { get; set; } = true;
    public int WatchdogIntervalMs { get; set; } = 1000; // 1s 轮询
    private const int MAX_RECENT_WINDOWS = 100; // 最大缓存窗口数
    
    // 可配置的过滤设置
    public bool EnableSmartFiltering { get; set; } = false; // 默认关闭智能过滤
    public bool FilterOwnProcess { get; set; } = true; // 默认过滤自己的进程
    
    /// <summary>
    /// 是否正在监听
    /// </summary>
    public bool IsListening => _callback != null;

    public WinEventListener()
    {
        _currentProcessId = Process.GetCurrentProcess().Id;
    }

    public void Start()
    {
        if (_callback != null) return;
        _callback = HandleWinEvent;
        _hookObjectCreate = SetWinEventHook(EVENT_OBJECT_CREATE, EVENT_OBJECT_CREATE, IntPtr.Zero, _callback, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        _hookObjectShow = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, _callback, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        _hookForeground = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _callback, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

        if (EnableWatchdogScan)
        {
            _pollTimer = new Timers.Timer(Math.Max(250, WatchdogIntervalMs));
            _pollTimer.Elapsed += (_, _) =>
            {
                try
                {
                    ScanForegroundAndTopLevelWindows();
                }
                catch { }
            };
            _pollTimer.AutoReset = true;
            _pollTimer.Start();
        }
    }

    public void Stop()
    {
        if (_hookObjectCreate != IntPtr.Zero) UnhookWinEvent(_hookObjectCreate);
        if (_hookObjectShow != IntPtr.Zero) UnhookWinEvent(_hookObjectShow);
        if (_hookForeground != IntPtr.Zero) UnhookWinEvent(_hookForeground);
        _hookObjectCreate = _hookObjectShow = _hookForeground = IntPtr.Zero;
        _callback = null;
        
        // 清理缓存
        _recentWindows.Clear();
        _processFilterCache.Clear();

        // 停止轮询
        if (_pollTimer != null)
        {
            _pollTimer.Stop();
            _pollTimer.Dispose();
            _pollTimer = null;
        }
    }

    private void HandleWinEvent(IntPtr hWinEventHook, uint @event, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (hwnd == IntPtr.Zero) return;
        if (idObject != OBJID_WINDOW) return;

        // 性能优化：事件节流
        var currentTime = Environment.TickCount64;
        if (currentTime - _lastEventTime < (uint)Math.Max(0, MinEventIntervalMs)) return;
        _lastEventTime = currentTime;

        // 性能优化：重复窗口检测
        if (_recentWindows.ContainsKey(hwnd)) return;
        
        try
        {
            // 统一提升为顶级窗口，避免对子/宿主窗口误判导致“未显示”
            var root = GetAncestor(hwnd, GA_ROOT);
            if (root != IntPtr.Zero) hwnd = root;

            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return;

            // 可选的自身进程过滤
            if (FilterOwnProcess && pid == _currentProcessId) return;

            // 性能优化：缓存进程过滤结果
            var processName = GetProcessNameCached((int)pid);
            if (string.IsNullOrEmpty(processName)) return;

            var title = GetWindowTextSafe(hwnd);
            
            // 可选的智能过滤
            if (EnableSmartFiltering && ShouldSkipWindowCached(processName, title)) return;

            // 添加到最近窗口缓存
            AddToRecentWindows(hwnd, currentTime);

            WindowShown?.Invoke(new WindowEventArgs(hwnd, (int)pid, processName, title));
        }
        catch
        {
            // Swallow per-event exceptions to not break the hook
        }
    }

    private void ScanForegroundAndTopLevelWindows()
    {
        // Foreground 首选
        var fg = GetForegroundWindow();
        if (fg != IntPtr.Zero)
        {
            TryEmitForWindow(fg);
        }

        // 枚举顶层窗口（轻量：仅标题非空且可见）
        EnumWindows((hwnd, lparam) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            var length = GetWindowTextLength(hwnd);
            if (length == 0) return true;
            // 跳过最近处理过的
            if (_recentWindows.ContainsKey(hwnd)) return true;
            TryEmitForWindow(hwnd);
            return true;
        }, IntPtr.Zero);
    }

    private void TryEmitForWindow(IntPtr hwnd)
    {
        try
        {
            var root = GetAncestor(hwnd, GA_ROOT);
            if (root != IntPtr.Zero) hwnd = root;

            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return;
            if (FilterOwnProcess && pid == _currentProcessId) return;

            var processName = GetProcessNameCached((int)pid);
            if (string.IsNullOrEmpty(processName)) return;
            var title = GetWindowTextSafe(hwnd);
            if (EnableSmartFiltering && ShouldSkipWindowCached(processName, title)) return;
            AddToRecentWindows(hwnd, Environment.TickCount64);
            WindowShown?.Invoke(new WindowEventArgs(hwnd, (int)pid, processName, title));
        }
        catch { }
    }

    /// <summary>
    /// 性能优化：缓存进程名称
    /// </summary>
    private string GetProcessNameCached(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 性能优化：缓存过滤结果
    /// </summary>
    private bool ShouldSkipWindowCached(string processName, string windowTitle)
    {
        var cacheKey = $"{processName}|{windowTitle}";
        if (_processFilterCache.TryGetValue(cacheKey, out var cachedResult))
        {
            return cachedResult;
        }

        var result = ShouldSkipWindow(processName, windowTitle);
        _processFilterCache[cacheKey] = result;
        
        // 限制缓存大小
        if (_processFilterCache.Count > 200)
        {
            _processFilterCache.Clear();
        }
        
        return result;
    }

    /// <summary>
    /// 性能优化：添加到最近窗口缓存
    /// </summary>
    private void AddToRecentWindows(IntPtr hwnd, long timestamp)
    {
        _recentWindows[hwnd] = timestamp;
        
        // 限制缓存大小并清理过期项
        if (_recentWindows.Count > MAX_RECENT_WINDOWS)
        {
            var cutoff = timestamp - 5000; // 5秒前的窗口
            var keysToRemove = new List<IntPtr>();
            
            foreach (var kvp in _recentWindows)
            {
                if (kvp.Value < cutoff)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }
            
            foreach (var key in keysToRemove)
            {
                _recentWindows.Remove(key);
            }
        }
    }

    /// <summary>
    /// 判断是否应该跳过某些窗口（仅在启用智能过滤时使用）
    /// </summary>
    private static bool ShouldSkipWindow(string processName, string windowTitle)
    {
        // 跳过一些常见的系统进程
        var skipProcesses = new[] { "explorer", "shellexperiencehost", "applicationframehost", "audiodg", "svchost", "dwm", "taskmgr" };
        if (Array.Exists(skipProcesses, p => string.Equals(p, processName, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // 跳过一些技术性窗口标题
        var skipTitles = new[] { "", "Program Manager", "Default IME", "MSCTFIME UI" };
        if (Array.Exists(skipTitles, t => string.Equals(t, windowTitle, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    private static string GetWindowTextSafe(IntPtr hWnd)
    {
        var length = GetWindowTextLength(hWnd);
        if (length == 0) return string.Empty;
        var buffer = new System.Text.StringBuilder(length + 1);
        _ = GetWindowText(hWnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private const uint EVENT_OBJECT_CREATE = 0x8000;
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const int OBJID_WINDOW = 0;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    private const uint GA_ROOT = 2;

    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint @event, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc,
        uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);
}

public sealed record WindowEventArgs(IntPtr Hwnd, int ProcessId, string ProcessName, string WindowTitle);


