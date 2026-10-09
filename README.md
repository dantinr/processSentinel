# 行为哨兵 · ProcessSentinel

Windows 程序行为监控工具，第一版使用 .NET 10 / WPF 和 Windows ETW。选择已运行的进程，或选择 `.exe` 从启动阶段开始监控，查看文件、网络、注册表、子进程和模块加载记录。

版本号统一维护在仓库根目录的 `VERSION` 文件中，构建时自动写入程序集和程序版本，界面标题及日志使用该版本。日常功能修改不自动递增版本号，正式发布前按需修改 `VERSION`。

选中任意进程后，默认向上识别所属程序的主进程，监控其相关父进程、兄弟及全部子孙进程。向上追溯止于 Explorer、终端和系统公共宿主；同一程序中的不同成员复用一个监控会话。界面标明原始所选进程与实际范围，进程名单区分所选、主进程和中间父进程。支持一个窗口同时监控 **4 个程序**，独立采集、计数、保存日志及单独/全部停止。

## 运行

日常构建的可运行程序位于固定目录 `artifacts/dev/ProcessSentinel-win-x64/`。正式发布目录为 `Releases/ProcessSentinel-<VERSION>-win-x64/`，同级保存对应 ZIP。双击 `ProcessSentinel.exe`，无需安装 .NET，也无需安装驱动。请保留同目录中的全部文件。

1. 从左侧选择进程，可按名称、PID、路径搜索。
2. 保持默认勾选“自动监控所属程序的完整进程树”，点击“添加监控”，允许 Windows UAC 提升采集器的权限。选中子进程时自动纳入其程序主进程及其他分支；已被监控的同程序成员会复用现有会话。继续选择其他程序并添加，无需停止之前的监控。
3. 或点击“选择 .exe 并启动监控”，填写可选参数。程序先挂起，采集器就绪后开始运行。
4. 顶部“查看程序”选择监控会话，标题显示程序主进程，下方显示原始所选 PID 与实际范围。该程序的“监控进程”页签区分“主进程”“父进程”“所选”“子进程”；所选即主进程时显示“所选/主”。可搜索名称、PID、父 PID 或路径，也可只看存活进程。点击进程查看完整信息；停止监控后显示最后记录的状态。
5. “行为记录”页签可按类别、关键词或“只看风险提示”筛选。点击记录查看完整路径、命令行和触发原因。
6. 点击“导出日志”保存当前会话的全部已收到事件，支持 JSONL 和 CSV；导出不受界面筛选影响。每个监控程序分别保存日志。
7. “停止当前”仅停止选中的会话，“全部停止”停止所有会话；关闭界面会清理所有采集器。已正常运行的目标程序继续运行。取消启动监控会结束尚未开始执行的挂起进程。
8. 已停止会话可以继续查看和导出，或点击“移除记录”释放界面记录；该操作不会删除磁盘上的日志。取消勾选自动程序树时，仅监控所选 PID；该选项只应用于新添加的会话。

顶部工具栏提供“设置”“关于”和“使用说明”。“设置”可保存默认的完整程序树范围及“只看风险提示”选项；保存后立即同步主窗口，下次启动自动恢复。范围只应用于新会话，风险筛选只影响显示。主窗口中的临时勾选不会改写默认设置，取消设置窗口也不会保存。设置保存在 `%LOCALAPPDATA%\ProcessSentinel\settings.json`。“关于”显示程序当前版本及项目地址。

界面按当前用户权限运行，只有采集器需要管理员权限。从普通权限的界面启动的目标保持普通权限。如果手动以管理员身份运行界面，新启动的目标也会继承该权限。需要管理员权限才能启动的目标，请先手动启动，再选择该进程监控。UAC 必须使用同一个 Windows 账户；换用其他管理员账户不支持本地管道连接。

推荐 Windows 10/11 x64，已在本机 Windows 11（26200）验证构建和界面。ARM64、x86 和受保护进程不在本次验证范围。

## 采集范围

| 行为 | 记录内容 |
|---|---|
| 文件 | 打开/创建、读取、写入、删除、重命名、查询属性、枚举目录、关闭、刷新；目标路径和请求字节 |
| 网络 | TCP 连接、接受连接、断开、收发；UDP 收发；IPv4 / IPv6 端点及传输字节 |
| 注册表 | 创建/打开/删除键、查询/设置/删除值、枚举键；键路径、值名称和可解析的 NTSTATUS |
| 进程 | 已有后代和新建后代的活动，新建子进程的命令行、父 PID、退出事件 |
| 模块 | 监控期间加载的 EXE/DLL 路径和大小 |

“打开/创建”不表示创建了新文件。文件记录默认来自操作请求，尚未关联操作完成事件；不能把写入或删除请求当成已经成功。重命名记录仅提供原路径。网络传输字节是 ETW 事件统计，不等于应用层有效数据量，不能从中判断上传了哪些内容。

只记录开始监控后的事件，不追溯历史行为，也不会列出开始前的连接。已有子进程依靠进程快照发现；已退出父进程的历史后代、通过独立服务或代理执行的活动，可能无法归属。无法读取创建时间的进程不能作为监控根目标。PID 配合创建时间识别，结束后不会继续跟踪复用该 PID 的无关进程。

程序树范围依据当前 Windows 父子关系，从所选进程向上找到公共启动器之前的最高有效父进程，再包含它的全部子孙。公共边界采用明确名称列表，包括 Explorer、CMD/PowerShell、Windows Terminal、Bash/WSL、常见系统宿主及本工具。不同路径下的辅助程序也可属于同一树；不按文件名把系统中所有同名进程合并。父进程已退出、启动时间无法读取、PID 复用或父链异常时，在可确认的位置停止追溯，范围说明的提示可查看原因。

启动管理员采集器时会再次验证所选 PID、主进程及父链；授权期间进程树发生变化则提示重新选择，避免静默扩大范围。同一会话里的“所选”标记保留首次启动监控时的原始选择；随后选择该程序的其他成员只切换到已有会话。新启动 `.exe` 时，以新创建的进程为根跟踪其全部后代。

## 风险提示

| 线索 | 提示 |
|---|---|
| 读取浏览器密码库、Cookie 数据库、SSH 私钥 | 需复核 |
| 修改 Startup 目录、Run/RunOnce、hosts | 高关注 |
| 修改服务配置 | 需复核 |
| 修改 Defender 或 Image File Execution Options 配置 | 高关注 |
| 创建命令解释器或脚本宿主子进程 | 需复核 |
| PowerShell 编码指令、下载后执行模式 | 高关注 |
| 连接公网少数非常规端口 | 需复核，明确标注为弱线索 |

每条提示包含规则 ID 和解释。正常软件也可能触发这些规则；风险级别表示复核优先级，不是恶意软件概率。不联网查询信誉、不上传日志，也不自动结束、阻止、隔离目标。

普通联网或使用 HTTPS 不会直接触发风险提示。没有风险提示不能证明程序安全。此版本不读取文件内容、注册表值或网络内容，不解密 HTTPS，不捕获所有 API 调用，也不检测内存注入、屏幕/键盘采集、驱动层行为或 ETW 被篡改。要判断未知程序是否安全，仍需要额外证据。

ETW 有时只提供相对注册表键名，或不能及时解析已存在的文件/注册表对象；此时会显示“相对键名”或“路径未解析”，相关路径规则可能无法触发，不能把缺失提示理解为安全。

## 日志与性能

原始日志自动保存到 `%LOCALAPPDATA%\ProcessSentinel\Sessions\`，JSONL 第一行包含会话元数据，后续保存事件和丢失统计。日志最多约每秒刷新到磁盘，正常停止会刷新；系统突然断电可能损失末尾尚未刷新数据。CSV 对可能被表格软件当作公式的字段进行了文本转义。

0.1.2 的 JSONL 在采集就绪、进程名单变化和正常结束时保存 `Processes` 快照，包含根目标、已有后代、新建后代及已退出进程。名单变化通常在一秒内显示；短暂运行后退出的子进程也保留在名单中。不勾选跟踪子进程时，只列出根目标。该名单由采集器提供，不依赖界面最近 5,000 条活动记录。

0.1.4 的会话元数据在 `Request.Root` 保存实际程序根，在 `Request.SelectedProcess` 保存自动程序树模式的原始选择；`Processes` 的 `IsSelected` / `IsAncestor` 保留原始所选和父链身份，PID 被复用时不会继承原选择标记。开始事件也记录程序范围和追溯边界。旧版本请求与日志仍可读取。

每个会话的界面保留最近 5,000 条，待显示队列上限 20,000 条；后台会话也持续更新自己的记录，不会因切换视图丢失已保存的证据。每个采集器队列上限 10,000 条，ETW 缓冲区 64 MB；发生 ETW 或采集队列丢失时会显示该会话的计数，此时证据不完整。多程序使用独立采集器和 ETW 会话，每个新会话请求一次管理员权限，同时上限设为 4 个根程序以控制资源开销；Windows 或其他工具已占用采集资源时，新会话可能无法启动，其他会话继续运行。

同一个子进程被多个根程序覆盖时，各自日志可能包含相同事件，跨日志统计时需要注意重复。没有合并所有程序的总计视图。高频系统事件仍可能带来 CPU 与磁盘开销，建议只在需要分析时开始监控。日志没有自动轮转或空间上限，长时间采集请自行管理磁盘空间。

日志包含本机路径、IP、进程命令行，分享前请检查其中是否包含个人信息或参数中的凭据。

## 开发与验证

需要 .NET 10 SDK 和 Windows。

```powershell
.\build.ps1
```

此脚本构建、运行非管理员基础和 WPF 界面检查，然后更新固定开发目录 `artifacts/dev/ProcessSentinel-win-x64/`，不创建版本目录或 ZIP。基础检查包含风险误报边界、PID 复用、日志恢复、CSV、真实进程枚举和挂起启动。

正式打包发布时，先按需更新 `VERSION`（三段数字，如 `0.1.4`），再执行：

```powershell
.\build.ps1 -Package
```

只有 `-Package` 模式创建 `Releases/ProcessSentinel-<VERSION>-win-x64/` 和 `Releases/ProcessSentinel-<VERSION>-win-x64.zip`。程序包包含运行时、版本文件和许可文件；再次打包同一版本会更新对应产物。可使用 `-Package -WhatIf` 预览发布路径而不生成产物。

开发程序正在使用时，可指定另一个开发输出目录继续构建：

```powershell
.\build.ps1 -OutputDirectory .\artifacts\dev-alternate\ProcessSentinel-win-x64
```

`-NoRestore` 使用已有还原结果；`-SkipTests` 跳过基础和界面检查。开发目录和发布产物均被 Git 忽略。后续功能开发默认使用日常构建流程，正式打包按发布指令执行。

构建时还会运行实际 WPF 界面更新回归检查；也可以单独使用保存的日志验证：

```powershell
.\artifacts\dev\ProcessSentinel-win-x64\ProcessSentinel.exe --verify-ui .\artifacts\ui-replay-results.txt C:\path\to\session.jsonl
```

此检查回放已保存的事件，不启动采集器，也不需要管理员权限。

多会话真实 WPF 验证：

```powershell
.\artifacts\dev\ProcessSentinel-win-x64\ProcessSentinel.exe --verify-multiple .\artifacts\multiple-live-results.txt .\artifacts\dev\ProcessSentinel-win-x64\ProcessSentinel.SelfTest.exe
```

该检查启动 4 个独立采集器，验证重复目标、并发上限、选择性停止、启动失败隔离、事件与日志隔离、全部停止，以及关闭窗口时清理全部会话。只运行自行创建的临时 IPv4 测试程序，不操作用户已有目标。

完整程序树真实验证：

```powershell
.\artifacts\dev\ProcessSentinel-win-x64\ProcessSentinel.exe --verify-family .\artifacts\family-live-results.txt .\artifacts\dev\ProcessSentinel-win-x64\ProcessSentinel.SelfTest.exe
```

该检查创建主进程 → 中间父进程 → 所选工作进程和兄弟分支，从最下层选择启动监控，验证各分支的真实文件、IPv4 网络、注册表和后续子进程活动、角色、边界、同程序会话复用及日志归属。

真实界面与采集器通信检查使用 `--verify-collector <报告路径> <SelfTest.exe 路径>`，会请求采集器管理员权限。它使用 IPv4 回环测试用例验证文件、网络、注册表、子进程以及初始/结束进程名单保存，并要求测试目标正常退出；完整 IPv6 采集仍由下面的 `--integration` 检查验证。

管理员终端内运行真实 ETW 集成测试：

```powershell
.\artifacts\dev\ProcessSentinel-win-x64\ProcessSentinel.SelfTest.exe --integration
```

测试仅创建临时文件、临时 HKCU 测试键、本机 IPv4/IPv6 回环连接和短暂 `cmd.exe` 子进程，成功后清理。测试证据输出为测试程序同目录的 `integration-events.jsonl`。

界面示例图可重新生成：

```powershell
.\artifacts\dev\ProcessSentinel-win-x64\ProcessSentinel.exe --render-preview .\artifacts\ui-preview.png
.\artifacts\dev\ProcessSentinel-win-x64\ProcessSentinel.exe --render-preview .\artifacts\process-preview.png --processes
.\artifacts\dev\ProcessSentinel-win-x64\ProcessSentinel.exe --render-preview .\artifacts\settings-preview.png --settings
.\artifacts\dev\ProcessSentinel-win-x64\ProcessSentinel.exe --render-preview .\artifacts\about-preview.png --about
```

示例图使用明确标记的示例数据，不是恶意软件检测结果。

项目结构：`src/ProcessSentinel.App` 为普通权限桌面界面，`src/ProcessSentinel.Collector` 为管理员采集器，`src/ProcessSentinel.Core` 包含 ETW、进程跟踪、风险规则和日志，`tests/ProcessSentinel.SelfTest` 为验证入口。

技术依据：[Windows ETW 文档](https://learn.microsoft.com/en-us/windows/win32/etw/event-tracing-portal)、[微软 TraceEvent 指南](https://github.com/microsoft/perfview/blob/main/documentation/TraceEvent/TraceEventProgrammersGuide.md)。TraceEvent 锁定为 3.2.8，针对该版本的注册表句柄和状态解析问题使用独立 KCB 映射，不依赖其错误的零句柄回退路径。
