# Windows屏幕使用时间

> 对标手机端「屏幕使用时间」的 Windows 桌面软件
> 作者：**kbcrt**　|　当前版本：**1.3.0**　|　技术栈：C# .NET 8 + WPF + WebView2 + SQLite
> 开源仓库：https://github.com/microkbcrt/Windows-ScreenTime

## 项目信息

| 项 | 值 |
|---|---|
| 产品名称 | Windows屏幕使用时间 |
| 作者 | kbcrt |
| 版本 | 1.0.0 |
| 解决方案 | ScreenTime.sln |
| 主程序 | src/ScreenTime.App（exe 名称：Windows屏幕使用时间.exe） |
| 采集核心 | src/ScreenTime.Core（前台监控 / 锁屏 / 熄屏 / 空闲 / UIA 网站识别） |
| 验证工具 | src/ScreenTime.Cli（控制台采集测试；`summary` 参数查看当日统计） |
| 数据库 | %LOCALAPPDATA%\ScreenTime\screentime.db（WAL 模式，分钟粒度） |
| 日志 | %LOCALAPPDATA%\ScreenTime\log.txt |
| 安装包脚本 | installer/setup.iss（Inno Setup 6） |

## 功能清单

- 秒级前台应用计时（三重判定：未锁屏 + 显示器点亮 + 无键鼠空闲）
- 今日/本周/本月总时长、拿起次数、24 小时分布、近 7 天趋势、分类占比
- 应用排行与单应用详情（14 天趋势、今日网站、限额设置）
- 浏览器网站级统计（Chrome / Edge / Firefox / Brave 等，UI Automation）
- 应用每日限额提醒（弹窗，不做强制拦截）
- 系统托盘常驻、开机自启（HKCU Run 键）、CSV 导出、数据备份/恢复

## 版本管理规则（每次更改必须遵守）

1. **任何**代码、资源、配置、安装脚本的更改都必须递增版本号，遵循 [SemVer 2.0.0](https://semver.org/lang/zh-CN/)：
   - **PATCH +1**（1.0.0 → 1.0.1）：bug 修复、文案/样式微调、构建脚本修正
   - **MINOR +1**（1.0.0 → 1.1.0）：新增功能、向下兼容的改进（如新增图表、新增导出格式）
   - **MAJOR +1**（1.0.0 → 2.0.0）：破坏性更改（数据库结构不兼容、移除功能、改变数据语义）
2. 版本号必须**三处同步**更新：
   - `src/ScreenTime.App/ScreenTime.App.csproj`（`<Version>` / `<FileVersion>` / `<AssemblyVersion>`）
   - `installer/setup.iss`（`#define MyAppVersion`，并同步更新 `OutputBaseFilename`）
   - 本文件头部「当前版本」及下方更新日志
3. 每次发版在【更新日志】最上方追加一行，格式：`- 版本号 (YYYY-MM-DD): 变更摘要`
4. 一个版本号一经发版不得复用；未发版的多次小改动可合并为一个版本号提交。

## 构建与发版

```powershell
# 调试运行（先启动任意一个实例即可）
dotnet build ScreenTime.sln -c Release

# 单文件发布（自包含，无需目标机装 .NET）
dotnet publish src/ScreenTime.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
  -o installer/publish

# 补拷 wwwroot（.NET publish 管道会遗漏 echarts.min.js，此步必须执行）
Copy-Item src/ScreenTime.App/wwwroot/* installer/publish/wwwroot -Recurse -Force

# 编译安装包（需 Inno Setup 6：winget install JRSoftware.InnoSetup）
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer\setup.iss
# 产物：dist\Windows屏幕使用时间_Setup_<版本>.exe

# 发版：GitHub Release（标签 vX.Y.Z，附上 Setup exe；文件名必须为 ASCII：Windows-ScreenTime_Setup_X.Y.Z.exe，中文文件名经 gh 上传会被破坏）
# gh release create vX.Y.Z "dist\Windows-ScreenTime_Setup_X.Y.Z.exe" --title "Windows屏幕使用时间 vX.Y.Z" --notes-file <说明文件>
# 注意：若 git push 被网络阻断，可用 GitHub API 建树提交（blob→tree→commit→PATCH refs/heads/main）
```

## 更新日志

- 1.3.0 (2026-10-12): 设置页新增「打开数据文件夹」按钮（资源管理器定位到数据库文件）。
- 1.2.0 (2026-10-12): 新增应用内自更新（启动时检查 GitHub Releases 最新版，发现新版弹窗提示，一键下载带进度并静默原地升级、自动重启，无需用户手动操作；设置页可手动检查更新）；项目发布至 GitHub 并接入 Releases 发版。
- 1.1.0 (2026-10-12): 新增应用分类手动设置（应用详情弹窗内下拉选择，自定义分类持久化到数据库并优先于内置规则）；重绘多尺寸应用图标（修复旧图标文件头损坏导致 exe 显示默认图标的问题）。
- 1.0.3 (2026-10-12): 修复 UIA 查询在个别页面上卡死导致网站级统计只记录第一个网站的问题（新增看门狗：查询超 8 秒自动重置缓存并重启 UIA 线程）；提高切换标签页时的归属准确性（新标题确认前不再沿用旧域名）。
- 1.0.2 (2026-10-12): 修复浏览器网站级统计不生效的问题（IsBrowser 进程名匹配误带 .exe 后缀导致永远无法命中浏览器名单）。
- 1.0.1 (2026-10-12): 修复安装包遗漏图表库（echarts）导致仪表盘图表空白的问题；修复浏览器网站级统计无法识别（UIA 改为专用 STA 线程执行，窗口标题变化立即重新解析）；新增前端 JS 错误与 RPC 诊断日志；完善发布流程补拷 wwwroot。
- 1.0.0 (2026-10-12): 首个版本。前台应用秒级计时、锁屏/熄屏/空闲三重判定、应用排行与分类、24 小时分布、7 天趋势、拿起次数、浏览器网站级统计（UIA）、应用限额提醒、托盘常驻、开机自启、CSV 导出、数据备份/导入、Inno Setup 安装包。
