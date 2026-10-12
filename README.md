# Windows屏幕使用时间

对标手机端「屏幕使用时间」的 Windows 桌面软件。

**作者**：kbcrt　|　**当前版本**：见 [Releases](https://github.com/microkbcrt/Windows-ScreenTime/releases)

## 功能

- 秒级前台应用计时（三重判定：未锁屏 + 显示器点亮 + 无键鼠空闲）
- 今日/本周/本月总时长、拿起次数、24 小时分布、近 7 天趋势、分类占比
- 应用排行、应用详情（14 天趋势、限额设置、分类手动设置）
- 浏览器网站级统计（Chrome / Edge / Firefox 等，UI Automation）
- 应用每日限额提醒（弹窗，不强制拦截）
- 数据备份/恢复、CSV 导出、开机自启、系统托盘常驻
- 应用内自更新：启动时自动检查新版本，一键原地升级

## 下载安装

前往 [Releases](https://github.com/microkbcrt/Windows-ScreenTime/releases) 下载 `Windows屏幕使用时间_Setup_x.y.z.exe`，双击安装（用户级安装，无需管理员权限）。数据保存在 `%LOCALAPPDATA%\ScreenTime`，卸载不会丢失。

## 技术栈

C# .NET 8 + WPF + WebView2（HTML 仪表盘 + ECharts）+ SQLite

## 构建

```powershell
dotnet build ScreenTime.sln -c Release
dotnet publish src/ScreenTime.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
Copy-Item src/ScreenTime.App/wwwroot/* installer/publish/wwwroot -Recurse -Force   # 必须执行
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer/setup.iss
```

## 目录结构

```
src/ScreenTime.Core   采集核心（前台监控/锁屏/熄屏/空闲/UIA 网站识别/自更新）
src/ScreenTime.App    WPF 主程序（WebView2 仪表盘/托盘/限额/设置）
src/ScreenTime.Cli    控制台验证工具（summary / sitetest / updatetest）
installer/            Inno Setup 打包脚本
```

## License

MIT
