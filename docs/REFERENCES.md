## 参考资料清单

### Win32 / Windows API
- 窗口事件：`SetWinEventHook`（EVENT_OBJECT_CREATE/SHOW/FOREGROUND）
- 窗口句柄与进程：`GetForegroundWindow`、`EnumWindows`、`GetWindowThreadProcessId`
- 进程信息：`QueryFullProcessImageName`、`OpenProcess`、`TerminateProcess`
- 消息：`PostMessage` / `SendMessage`（`WM_CLOSE`）
- Shell：`ShellExecute`（打开文件位置）

### .NET 与系统
- `System.Diagnostics.Process`、`FileVersionInfo`
- WMI：`Win32_Process`（父进程、命令行）
- 代码签名：WinVerifyTrust / Authenticode

### UI Automation
- Microsoft UI Automation（UIA）概览与实践
- UWP/AUMID 映射与 ApplicationFrameHost 注意事项

### 安全基线
- Windows 受保护进程与 UAC 提升
- 常见系统进程与可信签名厂商清单


