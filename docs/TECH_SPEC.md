## 技术方案与架构设计

### 架构概览
- 客户端（C# .NET 8 WPF）：UI、规则管理、事件展示与操作入口。
- 核心监控模块：WinEventHook 监听窗口事件；UIA/Win32 收集窗口与进程信息。
- 提权执行器（可选 Windows 服务）：执行需要管理员权限的操作（强制结束受保护进程等）。
- 数据存储：本地 SQLite/JSON（MVP 可用 JSON），记录事件与规则。

### 关键流程
1) 事件监听：SetWinEventHook 订阅 EVENT_OBJECT_SHOW/CREATE/FOREGROUND。
2) 窗口→进程映射：GetWindowThreadProcessId → PID → QueryFullProcessImageName。
3) 进程信息扩展：
   - 文件元数据：公司名、产品名、版本（FileVersionInfo）。
   - 签名：WinVerifyTrust（P1/2）。
   - 父进程/命令行：WMI Win32_Process。
4) 过滤/规则：窗口类名、路径、签名白名单，黑名单策略。
5) 命令执行：
   - 优雅关闭：PostMessage WM_CLOSE。
   - 强制结束：TerminateProcess / taskkill /PID /T /F（需管理员权限时触发 UAC）。
   - 打开位置：ShellExecute explorer.exe /select,"path"。
6) 数据与日志：本地存储事件、用户动作；支持导出。

### 模块划分
- EventListener：封装 WinEventHook、窗口过滤。
- ProcessInspector：进程与签名/父子关系解析。
- ActionExecutor：关闭/结束/挂起/打开位置。
- RulesEngine：白/黑名单、屏蔽策略。
- Persistence：本地存储（JSON/SQLite）。
- UI：弹出卡片与历史面板（WPF MVVM）。

### 权限与安全
- 默认非提权运行；仅在需要强杀时请求管理员。
- 维护系统进程白名单（如 Windows、显卡厂商签名）。
- 对关键操作弹窗二次确认，并记录审计日志。

### 错误处理与稳定性
- 钩子失败自动重试与降级（轮询 EnumWindows）。
- 强杀失败报错提示与可行替代（WM_CLOSE、挂起）。
- 崩溃恢复：异常日志 + 下次启动上报。

### 性能考虑
- 事件去抖与合并；相同来源限速展示。
- 按需延迟加载签名与 WMI 查询。

### 开发栈与依赖
- .NET 8、WPF、P/Invoke、System.Management（WMI）、Windows SDK。


