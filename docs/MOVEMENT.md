# 目标移动与帧快照

Issue #3 的第一批实现。内核计算路径和位置，宿主提交命令并消费状态与事件；Godot 示例由游戏仓库维护。

```csharp
var match = new RtsMatch(MatchConfig.Default, seed: 7, pathingGrid: grid);
match.SubmitCommand(CommandEnvelope.Spawn(1, 0, 0, new SimVector2(15, 15)));
match.Step();
match.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 1, new EntityId(1),
    goal: new SimVector2(65, 15), speed: 45, clearanceCells: 0));
match.Step();
var moving = match.IsMoving(new EntityId(1));
var snapshot = match.CaptureSnapshot();
var restored = RtsMatch.Restore(snapshot, grid);
```

速度为世界单位/秒；目标和速度必须有限、速度为正，净空沿用导航格子规则。命令在目标帧执行并在同帧推进移动。所有权错误、无导航、不可达或地图外目标在执行时产生 CommandRejected。非法替换保留原移动；有效替换从当前坐标重新寻路。Stop 在移动积分前取消路径并清零速度。

路径按格子中心逐段移动，再到精确目标；不平滑、不吸附。跨多个路点也按本帧距离预算消费，抵达时精确停在目标、速度归零并只发一次 MoveCompleted。成功增删动态占地后，所有在途移动在本帧重新寻路；失败时停止并发 MoveFailed，不自动重试。单位被新障碍覆盖也会停止，暂不实现挤出。

实体移动、路径索引与障碍更新只发生在 Step。ReadMoveOrders 返回路径副本，不能修改对局。启用导航的对局拒绝 SetVelocity，避免绕过寻路；无地图的速度积分骨架仍可运行。出生仍是诊断命令，没有合法落点与生产规则。

## 快照 v3

保存每个在途实体的目标、速度、净空、路径世界坐标和下一路点索引，包含未执行的 MoveTo 载荷，状态哈希与差异报告覆盖这些字段。移动过程中每帧快照可恢复并继续相同模拟。静态地图不嵌入快照，恢复仍需提供身份完全一致的 PathingGrid。

恢复校验路径点有效性、相邻格、穿墙角与当前位置所在路径段；失败只丢弃新实例。v1/v2 快照不再接受，未提供迁移。没有导航的快照也使用 v3。CLI 默认示例位置/帧号保持不变，哈希随新字段改变；验证宿主时从当前 CLI 取得期望值，不加载旧程序集或硬编码旧哈希。

```powershell
./Test.ps1 -Configuration Release
dotnet run --project samples/Rts.Kernel.Cli --no-build -c Release -- --navigation
```

## 本批边界

这一批是直接路径跟随，尚非原游戏的完整移动替代。转向速度、坡度、命令排队、组队落点分配、预约、单位间避让、追击与真实游戏接入留在 Issue #3 的后续批次。速度/净空目前随诊断命令传入，正式玩法必须由权威单位定义约束，不能直接信任联网客户端。未声明跨平台确定性；300/500 单位性能基线与 Windows 导出按计划暂缓。
