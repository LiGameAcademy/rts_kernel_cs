# Godot 运行时移动验收

使用Godot.NET.Sdk 4.7.2 / net8.0目标，直接引用当前内核及CLI的同一份多小组夹具和帧记录器。程序集名称沿用Rts.Kernel.Tests，以使用已有测试内部边界。

在仓库根执行：

```powershell
dotnet build experiments/MovementProfile/godot_probe/Rts.Kernel.Tests.csproj -c Release -p:NuGetAudit=false
python -c "import shutil; shutil.copytree('experiments/MovementProfile/godot_probe/.godot/mono/temp/bin/Release','experiments/MovementProfile/godot_probe/.godot/mono/temp/bin/Debug',dirs_exist_ok=True)"
& 'D:/GameMaker/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' --headless --path experiments/MovementProfile/godot_probe
```

Godot的工程启动器查找Debug缓存，因此复制当前Release程序集到该目录；实际测量仍使用优化后的Release程序集。入口打印引擎与实际.NET版本，net8.0目标不代表进程一定运行.NET8。

300/500各三轮，保留首轮、全部活动帧、逐帧状态/事件摘要及JSON续跑。frame_sample在每轮结束后统一输出；预算按全部Step的33.3ms峰值判断。进程GC暂停及当前线程JIT计数用于关联观测，不单独证明具体方法或调度原因。

这是无渲染Godot进程中的内核验证，不包含主游戏场景/资源加载/表现耗时，不切换游戏子模块。

显式准备验收：在上述引擎命令末尾加 `-- --prepared-multi-party`。每个独立冷进程都会记录准备总耗时及API工作耗时，保留出生首帧，验证读档/障碍增删替换后的准备与首次命令。普通启动参数保留未准备对照。主游戏桥接层的集成由对应宿主PR单独交付。
