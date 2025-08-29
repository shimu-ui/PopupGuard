using System;
using System.Windows;
using System.Windows.Input;
using Core;
using System.Collections.Generic;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Runtime.InteropServices;

namespace PopupGuard;

public partial class PickerWindow : Window
{
    public event Action<IntPtr>? Picked;
    public event Action? BatchStarted;
    public event Action? BatchEnded;

    private readonly List<IntPtr> _candidates = new();
    private int _candidateIndex = 0;
    private HighlightOverlayWindow? _overlay;
    private IntPtr _lockedHwnd = IntPtr.Zero;

    public PickerWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => BatchStarted?.Invoke();
        MouseLeftButtonDown += (_, _) => DragMove();
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseRightButtonUp += OnMouseRightButtonUp;
        
        // 添加鼠标移动事件来显示当前悬停的窗口信息
        MouseMove += OnMouseMove;
        MouseWheel += OnMouseWheel;

        // 透明点击穿透以便更像系统取色器（仍可拖动窗口区域）
        this.IsHitTestVisible = true;
        this.AllowsTransparency = true;
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var position = e.GetPosition(this);
        var screenPosition = PointToScreen(position);
        EnsureOverlay();

        // 如滚轮已确认锁定某个窗口，则只跟随锁定窗口显示边框与信息
        if (_lockedHwnd == IntPtr.Zero)
        {
            // 更新候选窗口集合：排除自身进程
            var currentPid = System.Diagnostics.Process.GetCurrentProcess().Id;
            _candidates.Clear();
            var list = Core.WindowPicker.GetCandidatesAtPoint((int)screenPosition.X, (int)screenPosition.Y, currentPid);
            if (list.Count > 0)
            {
                _candidates.AddRange(list);
            }
            _candidateIndex = Math.Min(_candidateIndex, Math.Max(0, _candidates.Count - 1));
        }

        var hwnd = _lockedHwnd != IntPtr.Zero ? _lockedHwnd : (_candidates.Count > 0 ? _candidates[_candidateIndex] : IntPtr.Zero);
        if (hwnd != IntPtr.Zero)
        {
            try
            {
                Core.WinEventListener.GetWindowThreadProcessId(hwnd, out var pid);
                if (pid != 0)
                {
                    var process = System.Diagnostics.Process.GetProcessById((int)pid);
                    var isOwnProcess = pid == System.Diagnostics.Process.GetCurrentProcess().Id;
                    
                    // 更新窗口标题显示当前悬停的窗口信息
                    var status = isOwnProcess ? " [自己的进程 - 建议选择其他窗口]" : "";
                    Title = _lockedHwnd == IntPtr.Zero
                        ? $"取窗工具 - 悬停: {process.ProcessName} (PID: {pid}){status}  [候选 {(_candidateIndex+1)}/{_candidates.Count}]"
                        : $"取窗工具 - 已锁定: {process.ProcessName} (PID: {pid}){status}";

                    // 显示绿色区域框
                    if (GetWindowRect(hwnd, out var rect))
                    {
                        var bounds = new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
                        _overlay!.ShowBounds(bounds, purple: false);
                    }
                    
                    // 添加调试信息到状态栏
                    if (isOwnProcess)
                    {
                        // 尝试找到其他窗口
                        var otherHwnd = WindowPicker.GetWindowFromScreenPoint((int)screenPosition.X, (int)screenPosition.Y);
                        if (otherHwnd != IntPtr.Zero && otherHwnd != hwnd)
                        {
                            try
                            {
                                Core.WinEventListener.GetWindowThreadProcessId(otherHwnd, out var otherPid);
                                if (otherPid != 0 && otherPid != pid)
                                {
                                    var otherProcess = System.Diagnostics.Process.GetProcessById((int)otherPid);
                                    Title += $" | 检测到其他窗口: {otherProcess.ProcessName}";
                                }
                            }
                            catch
                            {
                                // 忽略错误
                            }
                        }
                    }
                }
            }
            catch
            {
                Title = "取窗工具 - 无法识别窗口";
            }
        }
    }

    private void OnMouseWheel(object? sender, MouseWheelEventArgs e)
    {
        if (_candidates.Count == 0) return;
        _candidateIndex = (_candidateIndex + (e.Delta > 0 ? -1 : 1) + _candidates.Count) % _candidates.Count;

        var hwnd = _candidates[_candidateIndex];
        _lockedHwnd = hwnd; // 滚轮选择即锁定
        if (GetWindowRect(hwnd, out var rect))
        {
            _overlay?.ShowBounds(new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top), purple: true);
        }
        e.Handled = true;
    }

    // 已由 WindowPicker.GetCandidatesAtPoint 提供候选

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private void EnsureOverlay()
    {
        if (_overlay == null)
        {
            _overlay = new HighlightOverlayWindow();
            var origin = GetVirtualScreenOrigin();
            _overlay.Left = origin.X;
            _overlay.Top = origin.Y;
            _overlay.Width = SystemParameters.VirtualScreenWidth;
            _overlay.Height = SystemParameters.VirtualScreenHeight;
            _overlay.Show();
        }
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var position = e.GetPosition(this);
        var screenPosition = PointToScreen(position);
        var hwnd = _lockedHwnd != IntPtr.Zero
            ? _lockedHwnd
            : (_candidates.Count > 0 ? _candidates[_candidateIndex] : WindowPicker.GetWindowFromScreenPoint((int)screenPosition.X, (int)screenPosition.Y));
        if (hwnd != IntPtr.Zero)
        {
            // 显示选择的窗口信息
            try
            {
                Core.WinEventListener.GetWindowThreadProcessId(hwnd, out var pid);
                if (pid != 0)
                {
                    var process = System.Diagnostics.Process.GetProcessById((int)pid);
                    var isOwnProcess = pid == System.Diagnostics.Process.GetCurrentProcess().Id;
                    
                    var message = $"已选择窗口：{process.ProcessName} (PID: {pid})";
                    if (isOwnProcess)
                    {
                        message += "\n\n注意：这是PopupGuard自己的进程。\n建议选择其他应用程序的窗口。";
                    }
                    
                    var result = System.Windows.MessageBox.Show(message, "确认选择", 
                        isOwnProcess ? MessageBoxButton.YesNo : MessageBoxButton.OK, 
                        isOwnProcess ? MessageBoxImage.Warning : MessageBoxImage.Information);
                    
                    if (isOwnProcess && result == MessageBoxResult.No)
                    {
                        // 用户取消选择自己的进程
                        return;
                    }
                }
            }
            catch
            {
                // 忽略错误，继续处理
            }
            
            Picked?.Invoke(hwnd);
        }
        BatchEnded?.Invoke();
        _overlay?.HideBounds();
        _overlay = null;
        _lockedHwnd = IntPtr.Zero;
        Close();
    }

    private static System.Windows.Point GetVirtualScreenOrigin()
    {
        return new System.Windows.Point(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop);
    }

    private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        // 右键取消选择
        BatchEnded?.Invoke();
        _overlay?.HideBounds();
        _overlay = null;
        _lockedHwnd = IntPtr.Zero;
        Close();
    }
}


