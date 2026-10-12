; Windows屏幕使用时间 - Inno Setup 简体中文语言包（自写精简版，未覆盖的文案回退到 Default.isl）
[LangOptions]
LanguageName=Chinese (Simplified)
LanguageID=$0804
LanguageCodePage=65001

[Messages]
; ---- 标题 ----
SetupAppTitle=安装 - %1
SetupWindowTitle=安装 - %1
UninstallAppTitle=卸载 %1
UninstallAppFullTitle=%1 卸载

; ---- 按钮 ----
ButtonBack=< 上一步(&B)
ButtonNext=下一步(&N) >
ButtonInstall=安装(&I)
ButtonOK=确定
ButtonCancel=取消
ButtonYes=是(&Y)
ButtonYesToAll=全部是(&A)
ButtonNo=否(&N)
ButtonNoToAll=全部否(&O)
ButtonFinish=完成(&F)
ButtonBrowse=浏览(&R)...
ButtonWizardBrowse=浏览(&R)...
ButtonNewFolder=新建文件夹(&M)

; ---- 向导页标题 ----
WizardLicense=许可协议
WizardInfoBefore=重要信息
WizardSelectDir=选择安装位置
WizardSelectComponents=选择组件
WizardSelectProgramGroup=选择开始菜单文件夹
WizardSelectTasks=选择附加任务
WizardReady=准备安装
WizardPreparing=正在准备安装
WizardInstalling=正在安装
WizardFinished=安装完成
WizardUninstall=卸载

; ---- 选择目录 ----
SelectDirDesc=想把 [name] 安装到哪里？
SelectDirLabel3=安装程序将把 [name] 安装到下面指定的文件夹。
SelectDirBrowseLabel=若要继续，请点击「下一步」。若想选择其他文件夹，请点击「浏览」。
NeedSpaceLabel=至少需要 %1 的空闲磁盘空间。
InvalidPath=您必须输入完整的盘符路径，例如：C:\App，或 UNC 路径，例如：\\server\share。
DirDoesntExist=文件夹 %n%n%1 不存在。是否创建该文件夹？
DirExistsTitle=文件夹已存在
DirExistsQuestion=文件夹 %n%n%1 已存在。仍要安装到该文件夹吗？
DiskSpaceWarning=安装至少需要 %1 的空闲磁盘空间，但当前该驱动器仅有 %2。%n%n是否仍要继续？

; ---- 选择开始菜单 ----
SelectStartMenuFolderDesc=想把程序的快捷方式放到哪里？
SelectStartMenuFolderLabel3=安装程序将在下面的「开始菜单」文件夹中创建程序的快捷方式。
SelectStartMenuFolderBrowseLabel=若要继续，请点击「下一步」。若想选择其他文件夹，请点击「浏览」。
MustNotExist=文件夹 %n%n%1 %n%n已经存在。请输入其他名称或点击「浏览」选择现有文件夹。

; ---- 附加任务 ----
SelectTasksDesc=要执行哪些附加任务？
SelectTasksLabel2=选择安装 [name] 时要执行的附加任务，然后点击「下一步」。

; ---- 准备安装 ----
ReadyLabel1=安装程序已准备好将 [name] 安装到您的电脑。
ReadyLabel2a=安装程序已准备好将 [name] 安装到您的电脑。%n%n点击「安装」开始安装。
ReadyLabel2b=点击「安装」开始安装。
ReadyMemoDir=安装位置:
ReadyMemoTasks=附加任务:

; ---- 准备 / 安装中 ----
PreparingDesc=安装程序正在准备将 [name] 安装到您的电脑。
PreparingLabel=正在准备……
InstallingLabel=正在安装 [name]，请稍候……

; ---- 完成 ----
FinishedHeadingLabel=[name] 安装完成
FinishedLabelNoIcons=[name] 已成功安装到您的电脑。
FinishedLabel=[name] 已成功安装到您的电脑。
ClickFinish=点击「完成」退出安装程序。

; ---- 卸载 ----
UninstallStatusLabel=正在卸载 [name]，请稍候……
UninstalledAll=[name] 已成功从您的电脑中卸载。
UninstalledMost=[name] 卸载完成。%n%n部分元素无法删除，可手动删除。
UninstalledAppRequiresReboot=部分文件需要重启电脑后才能完成删除。
UninstallAppRunLabel=运行
UninstallExplorer=%1%n%n无法卸载，请先关闭 [name] 后重试。
UninstallOnlyOnWin64=此安装程序只能在 64 位 Windows 上运行。
UninstallUnknownEntry=未知条目 (%1)
ConfirmDeleteSharedFile=系统显示以下共享文件已不再被任何程序使用。是否删除这些共享文件？%n%n%1%n%n如果其他程序仍在使用这些文件，删除后可能导致它们无法运行。如不确定，请选择「否」。

; ---- 关闭应用 ----
CloseApplications=检测到以下程序正在使用需要更新的文件。%n%n%1%n%n点击「确定」让安装程序自动关闭这些程序并继续，或点击「取消」手动关闭后重试。
CloseApplicationsTitle=关闭程序？
StatusClosingApplications=正在关闭应用程序……
AppCannotBeUninstalled=%1%n%n正在运行，必须先关闭它才能继续卸载。请关闭该程序后点击「重试」，或点击「取消」中止卸载。

; ---- 其他 ----
ExitSetupTitle=退出安装
ExitSetupMessage=安装尚未完成。若现在退出，可稍后重新运行安装程序完成安装。%n%n现在退出吗？
ClickNext=点击「下一步」继续；点击「取消」退出安装程序。
ClickInstall=点击「安装」开始安装；点击「上一步」重新检查或修改设置。
ClickUninstall=点击「卸载」开始卸载。
StatusExtracting=正在解压文件……
StatusRollback=正在回退更改……
ErrorTitle=错误
ErrorFolderMustNotExist=文件夹 %n%n%1 %n%n已经存在。请输入其他名称或点击「浏览」选择现有文件夹。
InvalidParameter=命令行参数无效: %1
SetupAlreadyRunning=安装程序已在运行。%n%n是否关闭已运行的安装程序？
UninstallAlreadyRunning=卸载程序已在运行。%n%n是否关闭已运行的卸载程序？
