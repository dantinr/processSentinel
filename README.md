# 行为哨兵 · ProcessSentinel

Windows 程序行为监控工具，第一版使用 .NET 10 / WPF 和 Windows ETW。选择已运行的进程，或选择 `.exe` 从启动阶段开始监控，查看文件、网络、注册表、子进程和模块加载记录。

当前版本 **0.1.2**：新增“监控进程”页签，列出目标及全部被跟踪子进程的名称、PID、父 PID、启动时间、完整路径和运行状态。没有行为事件的进程也会显示；新进程自动加入，退出进程保留记录，PID 复用按启动时间区分。包含 0.1.1 的实时列表和窗口关闭修复。

## 运行

发布版位于 `Releases/ProcessSentinel-win-x64/`。双击 `ProcessSentinel.exe`，无需安装 .NET，也无需安装驱动。请保留同目录中的全部文件。

1. 从左侧选择进程，可按名称、PID、路径搜索。
2. 点击“开始监控”，允许 Windows UAC 提升采集器的权限。
3. 或点击“选择 .exe 并启动监控”，填写可选参数。程序先挂起，采集器就绪后开始运行。
4. “监控进程”页签显示目标及全部被跟踪子进程，可搜索名称、PID、父 PID 或路径，也可只看存活进程。点击进程查看完整信息；停止监控后显示最后记录的状态。
5. “行为记录”页签可按类别、关键词或“只看风险提示”筛选。点击记录查看完整路径、命令行和触发原因。
6. 点击“导出日志”保存全部已收到事件，支持 JSONL 和 CSV；导出不受界面筛选影响。
7. 停止或关闭界面会停止采集器，已正常运行的目标程序继续运行。取消启动监控会结束尚未开始执行的挂起进程。

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

界面保留最近 5,000 条，待显示队列上限 20,000 条。界面省略不会删除已保存证据。采集器队列上限 10,000 条，ETW 缓冲区 64 MB；发生 ETW 或采集队列丢失时会显示计数，此时证据不完整。高频系统事件仍可能带来 CPU 与磁盘开销，建议只在需要分析时开始监控。日志没有自动轮转或空间上限，长时间采集请自行管理磁盘空间。

日志包含本机路径、IP、进程命令行，分享前请检查其中是否包含个人信息或参数中的凭据。

## 开发与验证

需要 .NET 10 SDK 和 Windows。

```powershell
.\build.ps1
```

若旧发布目录中的程序正在运行，可输出到另一目录而不中断现有会话：

```powershell
.\build.ps1 -OutputDirectory .\Releases\ProcessSentinel-0.1.2-win-x64
```

此脚本构建、运行非管理员基础检查，并发布包含运行时的便携目录与 ZIP。基础检查包含风险误报边界、PID 复用、日志恢复、CSV、真实进程枚举和挂起启动。

构建时还会运行实际 WPF 界面更新回归检查；也可以单独使用保存的日志验证：

```powershell
.\Releases\ProcessSentinel-win-x64\ProcessSentinel.exe --verify-ui .\artifacts\ui-replay-results.txt C:\path\to\session.jsonl
```

此检查回放已保存的事件，不启动采集器，也不需要管理员权限。

真实界面与采集器通信检查使用 `--verify-collector <报告路径> <SelfTest.exe 路径>`，会请求采集器管理员权限。它使用 IPv4 回环测试用例验证文件、网络、注册表、子进程以及初始/结束进程名单保存，并要求测试目标正常退出；完整 IPv6 采集仍由下面的 `--integration` 检查验证。

管理员终端内运行真实 ETW 集成测试：

```powershell
.\Releases\ProcessSentinel-win-x64\ProcessSentinel.SelfTest.exe --integration
```

测试仅创建临时文件、临时 HKCU 测试键、本机 IPv4/IPv6 回环连接和短暂 `cmd.exe` 子进程，成功后清理。测试证据输出为测试程序同目录的 `integration-events.jsonl`。

界面示例图可重新生成：

```powershell
.\Releases\ProcessSentinel-win-x64\ProcessSentinel.exe --render-preview .\artifacts\ui-preview.png
.\Releases\ProcessSentinel-win-x64\ProcessSentinel.exe --render-preview .\artifacts\process-preview.png --processes
```

示例图使用明确标记的示例数据，不是恶意软件检测结果。

项目结构：`src/ProcessSentinel.App` 为普通权限桌面界面，`src/ProcessSentinel.Collector` 为管理员采集器，`src/ProcessSentinel.Core` 包含 ETW、进程跟踪、风险规则和日志，`tests/ProcessSentinel.SelfTest` 为验证入口。

技术依据：[Windows ETW 文档](https://learn.microsoft.com/en-us/windows/win32/etw/event-tracing-portal)、[微软 TraceEvent 指南](https://github.com/microsoft/perfview/blob/main/documentation/TraceEvent/TraceEventProgrammersGuide.md)。TraceEvent 锁定为 3.2.8，针对该版本的注册表句柄和状态解析问题使用独立 KCB 映射，不依赖其错误的零句柄回退路径。
