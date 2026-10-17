# 命令及读档首帧调度取证（Windows，测试工具）

不修改内核、预算或玩法，30Hz墙钟门不变。FrameSpikeLog以同一QPC起止区间给出elapsed_ms，记录PID/原生TID/频率；CPU和GC/JIT计数稍宽，仍只用于解释。所有首轮、出生、活动及边界帧均保留。

1. 构建Release CLI与MovementProfile/godot_probe；Godot按该目录README加载Release输出。
2. 不开启WPR，先分别运行`--prepared-multi-party`冷CLI和Godot对照并保存完整日志。
3. 管理员终端运行（只启动新进程，不重用编辑器）：

```powershell
./experiments/SchedulingProbe/Capture.ps1 -Runtime cli -Round 1 -OutputDirectory C:/Temp/rts-scheduling
./experiments/SchedulingProbe/Capture.ps1 -Runtime godot -Round 1 -OutputDirectory C:/Temp/rts-scheduling -GodotPath D:/tools/Godot_mono_console.exe
```

WPR CPU.Verbose.Memory包含CSwitch/ReadyThread及其栈。脚本拒绝覆盖已有日志，检查全局录制状态，用唯一instancename，只清理本次成功创建的会话。Windows策略/权限不足时失败并保存原始报错，不修改策略或跳过采集声称成功。ETL含系统级事件，留在本地内部证据目录，不提交到公开仓库。

离线导出复用已有、兼容的Microsoft.Diagnostics.Tracing.TraceEvent及FastSerialization组件，不下载包或为内核加依赖。传入它们所在工具目录；本机可复用dotnet-counters工具依赖目录。

```powershell
dotnet build experiments/SchedulingProbe -c Release -p:TraceEventDirectory=C:/path/to/existing/tool/dependencies
dotnet experiments/SchedulingProbe/bin/Release/net10.0/SchedulingProbe.dll --self-test
dotnet experiments/SchedulingProbe/bin/Release/net10.0/SchedulingProbe.dll C:/Temp/rts-scheduling/cli-trace-1.etl C:/Temp/rts-scheduling/cli-trace-1.log C:/Temp/rts-scheduling/cli-trace-1-scheduling.json
```

导出器拒绝覆盖已有ETLX索引；可显式传入已生成的.etlx路径读取，仍核验PID/TID和QPC。它验证探针/ETL时钟跨度、线程与频率，保留事件丢失及未知状态。仅导出命令和非出生超时窗口，原始日志仍含全部帧；target_transitions包含该原生线程的全部切换，便于判定窗口初态。保留栈地址和已能解析的模块/方法，不自动下载符号。

`TraceClock`是唯一使用TraceEvent原始TimeStampQPC的边界；库将其标为“Not Obsolete but Discouraged”，常规分析应使用相对毫秒。本工具要与进程外记录的绝对QPC对齐，故只在这一行明确抑制CS0618，不反射私有时钟或全局关闭警告。

scheduled_running_ms是被调度在CPU上的区间，可能含本配置未细分的DPC/ISR，不等于精确执行CPU。Ready是可运行但未运行，Waiting需结合切出/唤醒原因和栈解释；枚举或进程名本身不是具体阻塞对象的证明。事件丢失、缺栈或未能辨认的切出状态均保留限制，不能将残差自动算成锁等待。

采集会扰动性能；ETL用于归因，未录制对照继续以33.3333ms逐帧墙钟判定。未复现最多每运行时三份冷采集，不制造额外负载、不绑核/提优先级/关GC/调电源、不挑最好的一轮。不处理500同帧出生，不扩大预算或交通范围。

复核完整日志：`python experiments/SchedulingProbe/verify.py C:/Temp/rts-scheduling`。原有固定夹具的哈希/事件摘要、901帧、原生身份、QPC计时门及准备状态均核验；不是只比终态。

## 采集扰动对照的前置校准

在同一CLI Release构建中，用独立小进程验证已知方法的装载范围与CPU样本地址；这不是对真实对局新增预热。

```powershell
wpr -exportprofile CPU.Verbose C:/Temp/rts-overhead/original.wprp
python experiments/SchedulingProbe/profiles.py C:/Temp/rts-overhead/original.wprp C:/Temp/rts-overhead/profiles
```

B/C共用事件providers、关键字、缓冲及频率，并加入相同DPC/ISR；B仅移除全部内核/用户栈。不能以CPU.Light/Verbose直接相减推断纯栈开销。生成器拒绝覆盖原文件；WPR是否接受配置仍需profiledetails核验。

在正常管理员PowerShell中执行一次文件模式校准（输出目录必须不存在）：

```powershell
./experiments/SchedulingProbe/Calibrate.ps1 -ToolPath ./experiments/SchedulingProbe/bin/Release/net10.0/SchedulingProbe.dll -ProfilePath C:/Temp/rts-overhead/profiles/C.wprp -OutputDirectory C:/Temp/rts-overhead/file-calibration
```

脚本从输入C配置派生独立File配置，保留providers、关键词、级别和栈开关；不会修改原B/C配置。文件模式用于保留早期方法装载事件，不将循环缓冲的EventsLost=0视为首段完整的证明。这只是元数据校准配置，不自动确定后续六轮的统一记录模式。

fixture采用有界握手：ready报告实际PID/原生TID、coreclr路径/版本及相关配置；控制端读取目标provider原生注册与启用信息后发送run。工作完成再取一次快照，目标等待ETL保存成功，之后才接受release退出。错误命令、EOF或超时失败，不能借此给真实对局预热。

`--providers PID session-token output.json`只读调用EnumerateTraceGuidsEx和ControlTraceW(QUERY)，记录Runtime/Rundown实例、Flags、LoggerId、Level、MatchAny/MatchAll、会话模式和缓冲。只认可本次唯一会话的目标运行时注册，PRE_ENABLE及其他会话不算通过；本配置须启用Verbose及JIT/Loader。没有START/UPDATE/STOP provider调用；只清理脚本自己成功创建的WPR会话和fixture子进程。Windows x64之外明确拒绝，不猜原生结构布局。

必须同时有目标CLR方法装载事件、KnownWork有效代码范围、装载时间之后的范围内CPU样本和零丢事件才通过；不能用保存时才产生的Rundown地址反推先前运行版本。读取原始ETL，另计原始provider事件，不能以其他进程事件冒充目标。元数据缺失写passed=false、返回1；配置/IO等异常仍传播。

不重新录制时，可显式离线检查已有证据：

```powershell
dotnet experiments/SchedulingProbe/bin/Release/net10.0/SchedulingProbe.dll --calibrate C:/Temp/rts-overhead/file-calibration/calibration.etl C:/Temp/rts-overhead/file-calibration/fixture.log C:/Temp/rts-overhead/recheck.json
```

校准不是30Hz性能验收，不产生真实对局哈希/事件对照。**校准通过后仍须另行确定六轮A-B-C-C-B-A的统一配置，不能自动启动。** 30Hz墙钟、首轮保留及500同帧出生暂缓不变，工具不修改权威内核规则。
