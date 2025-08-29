using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Security.Principal;

namespace Core;

public static class ProcessActions
{
    public static bool TryCloseWindow(IntPtr hwnd)
    {
        try
        {
            var top = GetAncestor(hwnd, GA_ROOT);
            if (top == IntPtr.Zero)
            {
                top = hwnd;
            }

            // Try graceful system close
            _ = SendMessageTimeout(top, WM_SYSCOMMAND, new IntPtr(SC_CLOSE), IntPtr.Zero, SMTO_ABORTIFHUNG, 2000, out _);
            _ = PostMessage(top, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

            // Also attempt via process main window
            GetWindowThreadProcessId(top, out var pid);
            if (pid != 0)
            {
                try
                {
                    using var process = Process.GetProcessById((int)pid);
                    if (process.CloseMainWindow())
                    {
                        if (process.WaitForExit(2000)) return true;
                    }
                }
                catch { }
            }

            // Poll a bit to see if window goes away
            for (var i = 0; i < 10; i++)
            {
                if (!IsWindow(top)) return true;
                Thread.Sleep(100);
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryTerminateProcess(int processId, out string? error)
    {
        error = null;
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.CloseMainWindow())
            {
                if (process.WaitForExit(2000)) return true;
            }
        }
        catch { }

        try
        {
            var handle = OpenProcess(PROCESS_TERMINATE, false, (uint)processId);
            if (handle == IntPtr.Zero)
            {
                error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                return false;
            }
            try
            {
                if (!TerminateProcess(handle, 1))
                {
                    error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                    return false;
                }
                return true;
            }
            finally
            {
                CloseHandle(handle);
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// 强制结束进程（类似任务管理器，需要管理员权限）
    /// </summary>
    public static bool TryForceKillProcess(int processId, out string? error)
    {
        error = null;
        
        try
        {
            // 检查是否有管理员权限
            if (!IsRunningAsAdministrator())
            {
                error = "需要管理员权限才能强制结束此进程";
                return false;
            }

            using var process = Process.GetProcessById(processId);
            
            // 尝试优雅关闭
            if (process.CloseMainWindow())
            {
                if (process.WaitForExit(1000)) return true;
            }

            // 强制结束进程树
            try
            {
                process.Kill(true); // true = 结束整个进程树
                return true;
            }
            catch (Exception ex)
            {
                error = $"强制结束失败: {ex.Message}";
                return false;
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// 检查当前进程是否以管理员权限运行
    /// </summary>
    public static bool IsRunningAsAdministrator()
    {
        try
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 以管理员权限重启当前应用程序
    /// </summary>
    public static bool RestartAsAdministrator()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = true,
                FileName = Process.GetCurrentProcess().MainModule?.FileName,
                Verb = "runas" // 请求管理员权限
            };

            Process.Start(startInfo);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryOpenFileLocation(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private const int WM_CLOSE = 0x0010;
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_CLOSE = 0xF060;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint PROCESS_TERMINATE = 0x0001;

    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] private static extern IntPtr SendMessageTimeout(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
    private const uint GA_ROOT = 2;
}


