# 移动阶段诊断（仅隔离副本）

保持正式算法和30Hz发布门槛，定位300/500全场、每组最多24单位的现有热点。

```powershell
python experiments/MovementProfile/profile.py --output C:/Temp/rts-profile
python experiments/MovementProfile/profile.py --without-replay --output C:/Temp/rts-profile-no-replay
```

需要Python3.12+、现有.NET10 SDK和完整工作区依赖。脚本复制Git跟踪的工作区文件到新建临时目录，先运行原算法，再仅在副本插入阶段计时/当前线程分配观测。正式src/与宿主不加诊断字段或分支。临时源码保留供核对，路径在输出/metadata.json中。

产物：baseline.log、profile.log、profile.json、metadata.json。未插桩基线用于判断预算；插桩数据用于归因，包含观测开销，不能当作发布性能。首轮包含JIT等首次执行成本，非严格冷机实验。

- 阶段数据是包含子调用的inclusive值，父子时间和分配不可相加。
- gc0/1/2和gc_pause_ms是整个command/move阶段的统计，为便于读取重复在各行，不是每个方法独占的GC，也不能按行累加。
- 默认沿用已有快照对照；--without-replay只在副本关闭第1/31帧的读档验证，用于检查恢复验证产生的GC干扰。它不删除正式测试中的恢复覆盖。
- 两轮300/500各三次最终哈希必须6/6一致，夹具仍检查完成数、身体分离和终态静止；默认模式另有24帧live/JSON续跑对照。不宣称覆盖所有中间帧哈希。
- 稳定阶段时钟与分配只观察，不参与命令、逻辑预算、状态提交或哈希。
- 源码方法或探针标记变化时明确失败，不静默跳过；诊断结果须与原始日志及源码版本一起审阅。

本工具不实现缓存、碰撞裁剪、寻路预算或交通策略优化。

逐帧验收记录由tests/Rts.Kernel.Tests/FrameSpikeLog.cs提供，固定容量450，计时窗口外采集进程GC/当前线程JIT计数差并写入缓冲，每轮结束后输出全部frame_sample。CLI与godot_probe共用夹具；计数范围不等于线程调度归因，不剔除首轮和超预算帧。
