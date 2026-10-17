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
