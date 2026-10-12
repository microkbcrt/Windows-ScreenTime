# Windows 屏幕使用时间 — 项目预案（已确认版）

> 对标 iOS / Android「屏幕使用时间」的 Windows 桌面端实现
> 交付形式：可直接运行的 exe（C# .NET 8 + WPF，单文件自包含发布）

## 一、功能范围（最终确认）

### 核心统计
- [ ] 今日 / 本周 / 本月屏幕使用总时长，含与昨日、上周同比
- [ ] 各应用使用时长排行（柱状图，Top 10）
- [ ] 应用分类统计（社交 / 游戏 / 办公 / 开发 / 娱乐 / 网页 / 其他，内置规则 + 可编辑）
- [ ] 24 小时逐时分布图 + 近 7 天趋势
- [ ] 「拿起次数」：锁屏解锁、亮屏事件计数
- [ ] 单个应用详情页：近 7 天趋势、日均、峰值时段
- [ ] 浏览器网站级统计：Chrome / Edge / Firefox 活动标签页标题与域名分别计时

### 管控
- [ ] 应用限额提醒：单应用每日上限，超额弹窗提醒（不做强制拦截）

### 其他
- [ ] 系统托盘常驻：悬浮显示今日时长、右键菜单快速查看
- [ ] 开机自启（注册表 Run 键，可开关）
- [ ] CSV 导出
- [ ] 中文界面

## 二、精确计时方案（三重判定）

秒级计时仅当以下三项同时成立，否则暂停并记录事件：

| 判定 | API | 说明 |
|---|---|---|
| 未锁屏 | `WTSRegisterSessionNotification` | 锁屏/解锁/远程会话事件 |
| 显示器点亮 | `GUID_CONSOLE_DISPLAY_STATE` 电源事件 | 熄屏、睡眠停止计时 |
| 无键鼠空闲 | `GetLastInputInfo` | 默认空闲 60s 判定离开（可设置 30/60/120s） |

前台应用识别：`GetForegroundWindow` → `GetWindowThreadProcessId` → `QueryFullProcessImageName`
- 1s 轮询 + `EVENT_SYSTEM_FOREGROUND` 钩子双保险，切换瞬间归属切换
- UWP 应用经 `GetPackageFullName` 取真实应用名
- 管理员权限窗口：降级取进程名（已知局限）
- 浏览器网站识别：UI Automation 读标签页标题，正则提取域名；Chrome/Edge 亦可用 DevTools 协议备用方案

## 三、技术架构

```
ScreenTimeAgent（后台采集，随 UI 同进程，托盘常驻）
 ├─ ForegroundMonitor    前台窗口轮询 + 窗口切换钩子
 ├─ SessionMonitor       锁屏/解锁、亮灭屏、睡眠唤醒
 ├─ IdleDetector         键鼠空闲
 └─ BrowserTracker       UIA 标签页标题抓取
ScreenTime.UI（WPF 仪表盘）
 ├─ 今日总览 / 应用排行 / 分类 / 小时分布 / 应用详情 / 设置
 └─ LiveChartsCore 图表
存储：SQLite（Microsoft.Data.Sqlite），分钟粒度记录表 + 事件表
```

## 四、已知局限
- Windows 通知中心无公开历史 API，「通知数量」不做（与手机端最大差异）
- 管理员权限程序仅统计进程名
- 多显示器任一屏点亮即计时（与手机逻辑一致）

## 五、里程碑
- M1 采集引擎 + SQLite（控制台验证计时准确）
- M2 WPF 仪表盘 + 图表
- M3 限额提醒 + 设置 + 托盘 + 自启
- M4 网站统计 + CSV 导出 + 单文件 exe 发布

## 六、已确认决策
| 决策项 | 结论 |
|---|---|
| 技术栈 | C# .NET 8 + WPF |
| 网站级统计 | 需要（Chrome / Edge / Firefox） |
| 限额/专注 | 仅限额提醒，不做拦截 |
| 交付 | 编译好的单文件 exe |
