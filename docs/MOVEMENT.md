# 目标移动与帧快照

Issue #3 的分步骤实现。内核计算路径、位置和朝向，宿主提交命令并消费状态与事件；Godot 示例由游戏仓库维护。

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

速度为世界单位/秒；目标和速度必须有限、速度为正，净空沿用导航格子规则。命令在目标帧执行并在同帧推进移动。所有权错误、无导航、不可达或地图外目标在执行时产生 CommandRejected。非法替换保留原移动；有效替换从当前坐标重新寻路。Stop 在移动积分前取消路径、清空待执行队列并清零速度；保留 Stop 订单，直到新的玩家或玩家 AI 移动替换。

路径按格子中心逐段移动，再到精确目标；不平滑、不吸附。跨多个路点也按本帧距离预算消费，抵达时精确停在目标、速度归零并只发一次 MoveCompleted。成功增删动态占地后，所有在途移动在本帧重新寻路；失败时停止并发 MoveFailed，不自动重试。单位被新障碍覆盖也会停止，暂不实现挤出。

实体移动、路径索引与障碍更新只发生在 Step。ReadMoveOrders 返回路径副本，不能修改对局。启用导航的对局拒绝 SetVelocity，避免绕过寻路；无地图的速度积分骨架仍可运行。出生仍是诊断命令，没有合法落点与生产规则。

## 追加与来源优先级

MoveTo 可指定 `mode: OrderMode.Append`，默认 Replace。当前有移动或等待衔接的队列时，追加只保存意图，不改变当前路径；空闲或停住时追加会立即启动新移动。成功替换清空待执行队列，非法替换/追加保留原状态。每个实体最多64个待执行订单，超限在执行帧返回 `order_queue_full`。

追加帧校验目标在地图内、当前可占用；轮到执行时从实际当前位置重新寻路，不冻结后续路径。完成/失败后，下一帧最多激活一个后续订单；没有在前一个订单之后重复授予整帧运动预算。动态障碍使某项不可达时发 MoveFailed 并保留剩余队列，后续帧继续；不自动重试失败项。

```csharp
match.SubmitCommand(CommandEnvelope.MoveTo(match.Frame + 1, 0, 2, new EntityId(1),
    new SimVector2(25, 15), 45, mode: OrderMode.Append));
```

`source` 默认 OrderSource.Player；PlayerAi 使用同一所有权校验和显式命令权限。UnitAi 不能替换、停止或追加到有效的玩家/玩家 AI 订单，也不能绕过已有玩家队列或持久 Stop。玩家命令可以替换单位 AI 行动。来源仍是可信宿主的诊断元数据，本批没有实现 AI 决策或联网身份认证。

同帧继续按执行帧、PlayerId、Sequence、到达顺序稳定执行；不按真实到达时间或来源重新排序。ReadUnitOrders 返回副本，ReadCurrentOrder 返回不可变当前意图，GetPendingOrderCount 返回待执行数量；等待下一帧衔接时当前意图为 null，队列仍存在。

## 转向与真实坡度

MoveTo 的可选 `motion` 参数启用 MotionParameters；默认 null 保持直接路径跟随、不主动更新朝向。带运动参数的命令以 EntityState.Facing 为权威，单位为弧度，范围 [-π, π)。TurnRate 使用圈/秒，诊断参数最小0.05，非法参数在提交时拒绝。

```csharp
var match = new RtsMatch(MatchConfig.Default, seed: 7, pathingGrid: grid, terrain: terrain);
match.SubmitCommand(CommandEnvelope.Spawn(1, 0, 0, new SimVector2(15, 15)));
match.Step();
match.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 1, new EntityId(1),
    new SimVector2(65, 15), 45, motion: new MotionParameters()));
match.Step();
var restored = RtsMatch.Restore(match.CaptureSnapshot(), grid, terrain);
```

每个路径段先用当前朝向计算速度倍率，再按耗时转向、平移。多个路径段共享整帧时间，不能重复取得整帧转角；位置仍沿路径方向推进，不通过表现朝向反算移动。25/140度为全速/最低倍率阈值，最低0.12，使用 smoothstep 曲线。倍率为零时仍可转向；停止和抵达后保留朝向。

默认启用坡度：高度场必须覆盖完整导航网格；采样当前位置与下一路点的真实高度差，按高度差/平面距离取坡度。30度封顶，上坡最低0.6、下坡最低0.85，等高线/平地不降速。缺少高度时，执行帧返回 `motion_requires_height_field`，保留旧订单；非有限几何或高度计算失败停止并发 MoveFailed。禁用 ScaleSlopeSpeed 时无需高度场。运动参数仍是诊断入口，正式参数须来自权威单位定义。

## 快照 v6

保存实体朝向，以及每个在途实体的目标、速度、净空、运动参数、路径世界坐标和下一路点索引，并保存当前订单类型/来源、FIFO 待执行意图及未执行命令的追加模式/来源，状态哈希与差异报告覆盖这些字段。移动过程中每帧快照可恢复并继续相同模拟。静态地图不嵌入快照，恢复仍需提供身份完全一致的 PathingGrid，以及原局使用的 TerrainHeights；高度哈希包含尺寸、坐标和全部顶点高度。

恢复校验路径点有效性、相邻格、穿墙角与当前位置所在路径段；失败只丢弃新实例。v1/v2/v3/v4/v5 快照不再接受，未提供迁移。没有导航的快照也使用 v6。CLI 默认示例位置/帧号保持不变，哈希随新字段改变；验证宿主时从当前 CLI 取得期望值，不加载旧程序集或硬编码旧哈希。

```powershell
./Test.ps1 -Configuration Release
dotnet run --project samples/Rts.Kernel.Cli --no-build -c Release -- --navigation
dotnet run --project samples/Rts.Kernel.Cli --no-build -c Release -- --motion
dotnet run --project samples/Rts.Kernel.Cli --no-build -c Release -- --orders
```

## 本批边界

这一批包含直接路径跟随及转向/坡度隔离验证，尚非原游戏的完整移动替代。组队落点分配、预约、单位间避让、追击与真实游戏接入留在 Issue #3 的后续批次。速度/净空/运动参数目前随诊断命令传入，正式玩法必须由权威单位定义约束，不能直接信任联网客户端。未声明跨平台确定性；300/500 单位性能基线与 Windows 导出按计划暂缓。

## Group placement and formations

Create a match with frozen MovementDefinition entries and spawn members with their movementDefinitionId. Submit CommandEnvelope.MoveGroup with IDs, goal, FormationKind (Compact/Rectangle/Wedge/Circle), optional leader and heading. Commands do not supply member speeds or radii. Definitions are copied into a canonical catalog; snapshots require the identical content identity. Legacy diagnostic moves cannot override capabilities of profiled entities.

The kernel assigns stable ideal slots using deterministic minimum squared-distance matching, then checks clearance, connectivity, body separation, other known endpoints and stationary units. Local adjustment searches up to eight grid cells around each ideal slot. A failed member keeps its previous orders; other members may succeed. GroupMoveAssigned events carry actual goals and adjustment results. Append keeps current orders and persists resolved slots in the queue. Stop releases them.

Soft formations use individual paths and assemble at their assigned destinations. Open-ground group routes check every cell and corner before using the shortest octile route, otherwise A* applies. This is destination placement, not in-transit avoidance or strict formation following. Large group planning is synchronous; 500-member tests measure one command separately from frame simulation, and the 30 Hz performance gate is still pending.

v6 stores movement content identity, entity definition IDs, group counter, future group commands and current/pending slot metadata. Restore requires definitions as the final RtsMatch.Restore argument. Older snapshots are rejected. Run the CLI with --formation for a host parity example.
