using System;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Collections.Generic;
using System.Text;

namespace Core;

public static class WindowPicker
{
    public static List<IntPtr> GetCandidatesAtPoint(int x, int y, int? excludePid = null)
    {
        var point = new POINT { X = x, Y = y };
        var candidates = new List<WindowCandidate>();

        // 枚举顶级窗口（Z 序）并收集命中窗口
        EnumWindows((hwnd, lParam) =>
        {
            if (IsGoodCandidate(hwnd))
            {
                var rect = new RECT();
                if (GetWindowRect(hwnd, ref rect))
                {
                    if (point.X >= rect.Left && point.X <= rect.Right &&
                        point.Y >= rect.Top && point.Y <= rect.Bottom)
                    {
                        var root = GetAncestor(hwnd, GA_ROOTOWNER);
                        var cand = CreateWindowCandidate(root, rect, point);
                        if (cand != null)
                        {
                            // 过滤指定进程
                            if (excludePid.HasValue && cand.Value.ProcessId == excludePid.Value) return true;
                            candidates.Add(cand.Value);
                        }
                    }
                }
            }
            return true;
        }, IntPtr.Zero);

        // 用 WindowFromPoint 再补一次，并加入 owner 链
        var topAtPoint = WindowFromPoint(point);
        if (topAtPoint != IntPtr.Zero)
        {
            var owner = GetAncestor(topAtPoint, GA_ROOTOWNER);
            if (owner != IntPtr.Zero && IsGoodCandidate(owner))
            {
                var rect = new RECT();
                if (GetWindowRect(owner, ref rect))
                {
                    var cand = CreateWindowCandidate(owner, rect, point);
                    if (cand != null)
                    {
                        if (!(excludePid.HasValue && cand.Value.ProcessId == excludePid.Value))
                        {
                            candidates.Add(cand.Value);
                        }
                    }
                }
            }
        }

        // 排序并去重（按 hwnd）
        candidates.Sort((a,b) => a.Priority.CompareTo(b.Priority));
        var list = new List<IntPtr>();
        var seen = new HashSet<IntPtr>();
        foreach (var c in candidates)
        {
            if (!seen.Contains(c.Hwnd))
            {
                seen.Add(c.Hwnd);
                list.Add(c.Hwnd);
            }
        }
        return list;
    }
    public static IntPtr GetWindowFromScreenPoint(int x, int y)
    {
        var point = new POINT { X = x, Y = y };
        
        // 使用更精确的窗口选择策略
        var targetHwnd = FindTargetWindowAtPoint(point);
        
        // 如果找到了目标窗口，返回它
        if (targetHwnd != IntPtr.Zero)
        {
            return targetHwnd;
        }
        
        // 如果没找到，使用传统的WindowFromPoint
        return WindowFromPoint(point);
    }

    public static IntPtr GetWindowFromCursor()
    {
        GetCursorPos(out var pt);
        return GetWindowFromScreenPoint(pt.X, pt.Y);
    }

    /// <summary>
    /// 在指定点查找目标窗口
    /// </summary>
    private static IntPtr FindTargetWindowAtPoint(POINT point)
    {
        var candidates = new List<WindowCandidate>();
        
        // 策略1: 枚举所有顶级窗口（只取可见、未最小化、未遮蔽的窗口）
        EnumWindows((hwnd, lParam) =>
        {
            if (IsGoodCandidate(hwnd))
            {
                var rect = new RECT();
                if (GetWindowRect(hwnd, ref rect))
                {
                    if (point.X >= rect.Left && point.X <= rect.Right && 
                        point.Y >= rect.Top && point.Y <= rect.Bottom)
                    {
                        var candidate = CreateWindowCandidate(GetAncestor(hwnd, GA_ROOTOWNER), rect, point);
                        if (candidate != null)
                        {
                            candidates.Add(candidate.Value);
                        }
                    }
                }
            }
            return true;
        }, IntPtr.Zero);

        // 策略2: 通过 WindowFromPoint 获取Z序最高的命中窗口，再提升为根属主
        var topAtPoint = WindowFromPoint(point);
        if (topAtPoint != IntPtr.Zero)
        {
            var owner = GetAncestor(topAtPoint, GA_ROOTOWNER);
            if (owner != IntPtr.Zero && IsGoodCandidate(owner))
            {
                var rect = new RECT();
                if (GetWindowRect(owner, ref rect))
                {
                    var candidate = CreateWindowCandidate(owner, rect, point);
                    if (candidate != null)
                    {
                        candidates.Add(candidate.Value);
                    }
                }
            }
        }
        
        // 按优先级排序候选窗口
        candidates.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        
        // 返回优先级最高的窗口
        return candidates.Count > 0 ? candidates[0].Hwnd : IntPtr.Zero;
    }

    /// <summary>
    /// 创建窗口候选对象
    /// </summary>
    private static WindowCandidate? CreateWindowCandidate(IntPtr hwnd, RECT rect, POINT point)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return null;
            
            var process = Process.GetProcessById((int)pid);
            var isOwnProcess = pid == Process.GetCurrentProcess().Id;
            
            // 计算窗口优先级
            var priority = CalculateWindowPriority(hwnd, process, isOwnProcess, rect, point);
            
            return new WindowCandidate
            {
                Hwnd = hwnd,
                ProcessId = (int)pid,
                ProcessName = process.ProcessName,
                IsOwnProcess = isOwnProcess,
                Priority = priority
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 计算窗口优先级（数值越小优先级越高）
    /// </summary>
    private static int CalculateWindowPriority(IntPtr hwnd, Process process, bool isOwnProcess, RECT rect, POINT point)
    {
        var priority = 1000; // 基础优先级
        
        // 优先选择非自己的进程
        if (isOwnProcess)
        {
            priority += 500;
        }
        
        // 优先选择有标题的窗口
        var title = GetWindowTitle(hwnd);
        if (string.IsNullOrWhiteSpace(title))
        {
            priority += 200;
        }
        
        // 优先选择较小的窗口（更可能是应用窗口而不是桌面）
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width > 1920 || height > 1080) // 全屏或超大窗口
        {
            priority += 300;
        }
        
        // 系统/宿主黑名单降级
        var systemProcesses = new[] { "explorer", "svchost", "dwm", "taskmgr", "TextInputHost", "ApplicationFrameHost", "shellexperiencehost" };
        if (Array.Exists(systemProcesses, p => string.Equals(p, process.ProcessName, StringComparison.OrdinalIgnoreCase)))
        {
            priority += 400;
        }

        // 工具窗口/透明窗口降级
        var exStyle = (uint)GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        if ((exStyle & WS_EX_TOOLWINDOW) != 0 || (exStyle & WS_EX_TRANSPARENT) != 0)
        {
            priority += 200;
        }
        
        // 优先选择有图标的窗口
        if (HasWindowIcon(hwnd))
        {
            priority -= 100;
        }

        // 鼠标命中点靠近窗口中心加分
        var cx = rect.Left + width / 2.0;
        var cy = rect.Top + height / 2.0;
        var dx = point.X - cx;
        var dy = point.Y - cy;
        var dist2 = dx * dx + dy * dy;
        if (dist2 < (width * width + height * height) / 16.0)
        {
            priority -= 50;
        }
        
        return priority;
    }

    /// <summary>
    /// 检查窗口是否有图标
    /// </summary>
    private static bool HasWindowIcon(IntPtr hwnd)
    {
        try
        {
            var icon = GetClassLong(hwnd, GCL_HICON);
            return icon != IntPtr.Zero;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsGoodCandidate(IntPtr hwnd)
    {
        if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) return false;
        if (IsWindowCloaked(hwnd)) return false;
        return true;
    }

    /// <summary>
    /// 获取窗口标题
    /// </summary>
    private static string GetWindowTitle(IntPtr hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length == 0) return string.Empty;
        var buffer = new StringBuilder(length + 1);
        _ = GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static bool IsWindowCloaked(IntPtr hwnd)
    {
        try
        {
            int cloaked = 0;
            var hr = DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, out cloaked, sizeof(int));
            return hr == 0 && cloaked != 0;
        }
        catch { return false; }
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, ref RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern IntPtr GetClassLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, DWMWINDOWATTRIBUTE dwAttribute, out int pvAttribute, int cbAttribute);

    private const int GCL_HICON = -14;
    private const uint GA_ROOTOWNER = 3;
    private const int GWL_EXSTYLE = -20;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_TRANSPARENT = 0x00000020;

    private enum DWMWINDOWATTRIBUTE
    {
        DWMWA_CLOAKED = 14
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private struct WindowCandidate
    {
        public IntPtr Hwnd;
        public int ProcessId;
        public string ProcessName;
        public bool IsOwnProcess;
        public int Priority;
    }
}


