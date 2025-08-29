## PopupGuard (Windows)

### 运行与构建
- 运行（Debug）：
  - `dotnet run --project .\src\App\App.csproj -c Debug`
- 构建：
  - `dotnet build .\PopupGuard.sln -c Release`

### 功能概览（MVP 已完成）
- 实时监听窗口事件并映射到进程
- 显示来源信息：进程名、公司、路径、发布者（占位）
- 操作：关闭窗口、强制结束（带确认）、打开文件位置、屏蔽同源
- 规则管理：查看/解除屏蔽；系统进程/厂商白名单（隐藏强杀）
- 托盘：最小化到托盘，托盘菜单（Show/Exit）
- 设置：开机自启（当前用户）

更多细节见 `docs/`。


