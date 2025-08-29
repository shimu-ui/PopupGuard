<div align="center">
  <img src="src/App/PopupGuard.ico" alt="PopupGuard" width="96" height="96" />

  <h2>PopupGuard</h2>
  <p>Windows 弹窗来源识别与一键治理</p>

  <p>
    <img alt="platform" src="https://img.shields.io/badge/platform-Windows_10/11-blue" />
    <img alt=".NET" src="https://img.shields.io/badge/.NET-8.0-512BD4" />
    <img alt="license" src="https://img.shields.io/badge/license-MIT-green" />
  </p>
</div>

PopupGuard 是一个面向 Windows 10/11 的桌面工具，用于识别屏幕弹窗来自哪个软件（进程/签名/路径/父进程等），并提供关闭、强制结束、屏蔽同源弹窗、打开文件位置等一键操作，帮助快速定位并处理“流氓弹窗”。

### 功能特性
- 实时监听：新窗口出现即解析其来源信息
- 来源信息：软件名、公司、发布者、可执行文件路径、父进程、窗口标题
- 一键操作：关闭、强制结束（带确认与提权）、屏蔽同源、打开文件位置
- 规则与白名单：可管理屏蔽规则；对系统/可信签名进程默认隐藏“强杀”
- 事件列表：过滤、着色（可信/不可信）、图标与软件名、CSV 导出
- 取窗工具：拖拽十字准星、实时绿色框、滚轮切换候选（紫色）、左键选择/右键取消
- 托盘与自启动：最小化到托盘，支持随系统启动
- 性能优化：事件节流、UI 批量刷新、图标缓存、最大行数；性能预设（高性能/均衡/细节/自定义）
- 本地化：默认简体中文

### 环境需求
- Windows 10/11 x64
- .NET 8 SDK

### 构建与运行
```powershell
# 还原与构建
 dotnet restore
 dotnet build .\PopupGuard.sln -c Release

# 运行（调试）
 dotnet run --project .\src\App\App.csproj -c Debug
```

### 使用说明
- 主界面：自动显示最新弹窗来源；可对任意行执行“关闭/强杀/屏蔽/打开位置”等操作
- 屏蔽：点击“屏蔽”会弹出确认提示，可在“规则管理”中随时取消
- 取窗：点击“取窗”，移动鼠标看到绿色框；滚轮在候选窗口间切换（紫色框并锁定），左键选择、右键取消
- 导出：点击“导出CSV”可保存当前事件列表

### 设置项
- 随系统启动
- 过滤：过滤自身进程、启用智能过滤（系统/技术窗口）
- 性能：启用详细信息（WMI）、事件节流间隔、UI 刷新间隔、图标缓存、列表最大行数、性能预设

### 权限说明
- 默认以普通权限运行；当需要强制结束受保护进程时，会请求管理员权限（UAC）

### 许可证
- MIT（可根据需要调整）

---
如需提交反馈或改进建议，欢迎发起 Issue 或 PR。

### 预览与截图（可选）
- 后续可在此处放置应用运行截图或动图演示
- 资源建议放在 `assets/` 目录（例如：`assets/screenshot-main.png`）


