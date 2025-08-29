using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace Core;

public static class ProcessInspector
{
    // 性能优化：缓存进程信息
    private static readonly Dictionary<int, ProcessInfo> _processCache = new();
    private static readonly Dictionary<int, long> _cacheTimestamps = new();
    private const int CACHE_TTL_MS = 30000; // 缓存30秒
    private const int MAX_CACHE_SIZE = 200; // 最大缓存200个进程

    public static ProcessInfo? TryGetProcessInfo(int processId)
    {
        // 性能优化：检查缓存
        if (_processCache.TryGetValue(processId, out var cachedInfo))
        {
            var currentTime = Environment.TickCount64;
            if (currentTime - _cacheTimestamps[processId] < CACHE_TTL_MS)
            {
                return cachedInfo;
            }
            // 缓存过期，移除
            _processCache.Remove(processId);
            _cacheTimestamps.Remove(processId);
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            var path = TryGetProcessPath(process);
            var fvi = path != null && File.Exists(path) ? FileVersionInfo.GetVersionInfo(path) : null;
            
            // 性能优化：延迟加载父进程信息
            var (parentPid, commandLine) = (0, (string?)null);
            if (EnableDetailedInfo)
            {
                (parentPid, commandLine) = TryGetParentAndCmd(processId);
            }

            var info = new ProcessInfo
            {
                ProcessId = processId,
                ProcessName = process.ProcessName,
                ExecutablePath = path ?? string.Empty,
                CompanyName = fvi?.CompanyName ?? string.Empty,
                ProductName = fvi?.ProductName ?? string.Empty,
                FileVersion = fvi?.FileVersion ?? string.Empty,
                ParentProcessId = parentPid,
                CommandLine = commandLine ?? string.Empty,
                DisplayName = GetDisplayName(fvi, path),
                IconPath = path
            };

            // 添加到缓存
            AddToCache(processId, info);
            
            return info;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 性能优化：是否启用详细信息（WMI查询）
    /// </summary>
    public static bool EnableDetailedInfo { get; set; } = false; // 默认关闭以减少CPU占用

    /// <summary>
    /// 性能优化：添加到缓存
    /// </summary>
    private static void AddToCache(int processId, ProcessInfo info)
    {
        // 限制缓存大小
        if (_processCache.Count >= MAX_CACHE_SIZE)
        {
            var oldestTime = long.MaxValue;
            var oldestKey = -1;
            
            foreach (var kvp in _cacheTimestamps)
            {
                if (kvp.Value < oldestTime)
                {
                    oldestTime = kvp.Value;
                    oldestKey = kvp.Key;
                }
            }
            
            if (oldestKey != -1)
            {
                _processCache.Remove(oldestKey);
                _cacheTimestamps.Remove(oldestKey);
            }
        }
        
        _processCache[processId] = info;
        _cacheTimestamps[processId] = Environment.TickCount64;
    }

    /// <summary>
    /// 性能优化：清理过期缓存
    /// </summary>
    public static void CleanupCache()
    {
        var currentTime = Environment.TickCount64;
        var keysToRemove = new List<int>();
        
        foreach (var kvp in _cacheTimestamps)
        {
            if (currentTime - kvp.Value > CACHE_TTL_MS)
            {
                keysToRemove.Add(kvp.Key);
            }
        }
        
        foreach (var key in keysToRemove)
        {
            _processCache.Remove(key);
            _cacheTimestamps.Remove(key);
        }
    }

    /// <summary>
    /// 获取应用程序的显示名称
    /// </summary>
    private static string GetDisplayName(FileVersionInfo? fvi, string? path)
    {
        // 优先使用产品名称
        if (!string.IsNullOrWhiteSpace(fvi?.ProductName))
        {
            return fvi.ProductName;
        }

        // 如果没有产品名称，使用文件描述
        if (!string.IsNullOrWhiteSpace(fvi?.FileDescription))
        {
            return fvi.FileDescription;
        }

        // 最后使用文件名（不含扩展名）
        if (!string.IsNullOrWhiteSpace(path))
        {
            return Path.GetFileNameWithoutExtension(path);
        }

        return string.Empty;
    }

    private static (int parentPid, string? cmd) TryGetParentAndCmd(int pid)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT ParentProcessId, CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
            foreach (ManagementObject obj in searcher.Get())
            {
                var parent = Convert.ToInt32(obj["ParentProcessId"]);
                var cmd = obj["CommandLine"]?.ToString();
                return (parent, cmd);
            }
        }
        catch { }
        return (0, null);
    }

    private static string? TryGetProcessPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Win32Exception)
        {
            // 尝试使用 OpenProcess + QueryFullProcessImageName
            var viaOpen = QueryImageWithOpenProcess(process.Id);
            if (!string.IsNullOrEmpty(viaOpen)) return viaOpen;
            // 回退到使用现有句柄的 QueryFullProcessImageName
            var viaHandle = QueryFullProcessImageName(process);
            if (!string.IsNullOrEmpty(viaHandle)) return viaHandle;
            // 最后尝试 WMI 的 ExecutablePath
            var (parent, cmd) = TryGetParentAndCmd(process.Id);
            if (!string.IsNullOrWhiteSpace(cmd))
            {
                try
                {
                    using var searcher = new ManagementObjectSearcher($"SELECT ExecutablePath FROM Win32_Process WHERE ProcessId = {process.Id}");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        var exec = obj["ExecutablePath"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(exec)) return exec;
                    }
                }
                catch { }
            }
            // 仍然失败，尝试 Psapi GetModuleFileNameEx（可能部分情况下可用）
            var psapi = GetModuleFileNameExFallback(process.Id);
            return string.IsNullOrWhiteSpace(psapi) ? null : psapi;
        }
        catch
        {
            return null;
        }
    }

    private static string? QueryFullProcessImageName(Process process)
    {
        try
        {
            var buffer = new char[1024];
            var size = (uint)buffer.Length;
            if (QueryFullProcessImageName(process.Handle, 0, buffer, ref size))
            {
                return new string(buffer, 0, (int)size);
            }
        }
        catch { }
        return null;
    }

    private static string? QueryImageWithOpenProcess(int pid)
    {
        IntPtr h = IntPtr.Zero;
        try
        {
            h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
            if (h == IntPtr.Zero) return null;
            var buffer = new char[1024];
            var size = (uint)buffer.Length;
            if (QueryFullProcessImageName(h, 0, buffer, ref size))
            {
                return new string(buffer, 0, (int)size);
            }
        }
        catch { }
        finally
        {
            if (h != IntPtr.Zero) CloseHandle(h);
        }
        return null;
    }

    private static string? GetModuleFileNameExFallback(int pid)
    {
        IntPtr h = IntPtr.Zero;
        try
        {
            h = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, (uint)pid);
            if (h == IntPtr.Zero) return null;
            var sb = new System.Text.StringBuilder(1024);
            if (GetModuleFileNameEx(h, IntPtr.Zero, sb, sb.Capacity) > 0)
            {
                return sb.ToString();
            }
        }
        catch { }
        finally
        {
            if (h != IntPtr.Zero) CloseHandle(h);
        }
        return null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, [Out] char[] lpExeName, ref uint lpdwSize);

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint PROCESS_VM_READ = 0x0010;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("psapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetModuleFileNameEx(IntPtr hProcess, IntPtr hModule, System.Text.StringBuilder lpFilename, int nSize);
}

public sealed class ProcessInfo
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string FileVersion { get; set; } = string.Empty;
    public int ParentProcessId { get; set; }
    public string CommandLine { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? IconPath { get; set; }
}


