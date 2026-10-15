# 对局框架职责（0.0.1）

当前使用说明：正式数值已迁到32.32/Int128，快照v11；拒绝旧格式。以下各批关于v9与哈希不变的说明是当时结构重构记录，不表示当前仍使用旧数值契约。当前API与兼容边界见README及移动说明。


0.0.1 收口已落地的模块职责边界。本文说明当前内部状态所有者；完整移动验收、战斗与后续玩法不属于本版本。

## 角色分工

`RtsMatch` 保留公共入口、固定帧序、跨模块快照装配与恢复协调。可变业务状态由具体内部模块独占，不通过多个 partial 直接共享字典。

| 模块 | 独占状态 | `RtsMatch` 协调内容 |
| --- | --- | --- |
| `MatchEntities` | 实体字典、下一 ID、生成与状态提交、实体快照 | 生成前业务校验、事件发布 |
| `MatchCommandQueue` | 待执行命令、到达序号、按帧取出 | 命令执行规则 |
| `MatchCommandExecutor` | 无耐久状态；应用 Spawn / Obstacle / Velocity / Move / Stop 规则 | `GroupMove` 仍暂由对局 partial 处理（Issue #63 包 C） |
| `CrowdStepper` | 到达邻接缓存；拥挤预约、局部绕行、超时失败与到点让行 | 帧推进时传入导航与脏路径标志 |
| `MatchEventBuffer` | 未消费事件与事件序号 | 传入帧号、消费后继续推进序号 |
| `UnitOrderBook` | 当前/排队订单意图与活动路径 | Replace/Append/Stop、完成与失败清理 |
| `GroupPlanningQueue` | 计划身份、FIFO、取消与本帧列预算调度 | 就绪后调用落点提交；来源保护 |
| `GroupPlanningJob` | 匹配进度、候选准备、取消成员与就绪判断 | 由队列推进；提交入口在 `RtsMatch` |
| `MovementStepper` | 路段转向、坡度与按时间推进的运动提案 | 碰撞预约、诊断直接提交、失败后是否保留进度 |
| `MovePathPlanner` | 无状态；网格寻路 → 世界路点 | 脏路径重规划、排队激活、命令入队 |

导航动态占地仍由 `NavigationState` 拥有。随机流由对局内部持有，不向宿主暴露可变对象。

## Issue #12 验收要点

对应 [Issue #12](https://github.com/LiGameAcademy/rts_kernel_cs/issues/12)：订单与规划生命周期分别进入 `UnitOrderBook` / `GroupPlanningQueue`。

- Replace、Append、Stop、完成、失败、计划取消及恢复由订单/规划模块维护对应关系。
- 代表性合法输入保持逐帧哈希与事件顺序；快照仍为 v9，派生缓存恢复后重建。
- 不引入 ECS、DI 容器、通用调度或 Actor 框架。

## Issue #13 验收要点

对应 [Issue #13](https://github.com/LiGameAcademy/rts_kernel_cs/issues/13)：诊断与正式移动共享 `MovementStepper` 路段数学，分别提交。

- 正式单位走 `Compute`，返回轨迹供扫掠预约与局部绕行；诊断单位走 `ComputeDiagnostic`，无碰撞轨迹。
- 无 `Motion` 的诊断路径保留按距离扣减并保持朝向；有 `Motion` 时与正式路径共用转向/坡度时间步进。
- 零长度路点、失败时部分进度由调用方决定是否提交；不新增 Strategy 框架。
- 逐帧哈希/事件与转向、坡度、拐点、恢复检查保持一致；详见 [移动说明](MOVEMENT.md)。

## Issue #14 验收要点

对应 [Issue #14](https://github.com/LiGameAcademy/rts_kernel_cs/issues/14)：群体规划计算阶段与提交生命周期分离。

- `GroupPlanningJob` 拥有匹配与候选准备进度、`Ready` 判断及成员取消；候选可与匹配交替推进，不是互斥线性状态机。
- `GroupPlanningQueue.AdvanceNext` 独占固定列预算调度；`RtsMatch.AdvanceGroupPlanning` 仅在就绪后求解落点并经明确入口提交，每帧至多一次完整提交。
- 取消/替换/追加与进行中快照恢复仍走同一所有者；保留 v9、整数预算与 64/65/500 规模检查语义。
- 不引入通用状态机、DI 或 Actor；详见 [移动说明](MOVEMENT.md) 群体规划段落。

## Issue #16 验收要点

对应 [Issue #16](https://github.com/LiGameAcademy/rts_kernel_cs/issues/16)：核心生命周期与算法分支恢复可读代码块。

- 避让提交、群体任务推进与 Hungarian 匹配不再把赋值/事件/`continue` 压进同一行；保留简单表达式成员与合理数学变量（如 `u`/`v` 势）。
- `SlotMatchingWork` 说明 1..n 索引、列 0 增广根、势函数、平局顺序与按列计费不变量；碰撞预约与提交顺序有注释。
- 纯可读性整理：不改预算、迭代顺序、算法结果或 v9；改动文件不超过 500 物理行。

## Issue #29 验收要点

对应 [Issue #29](https://github.com/LiGameAcademy/rts_kernel_cs/issues/29)：命令队列从 `RtsMatch` 拆出为独立状态模块。

- `MatchCommandQueue` 独占待执行命令与到达序号；负责结构校验、冻结、按帧取出、快照导出与恢复。
- `RtsMatch.SubmitCommand` 委托接收；固定帧内执行规则仍由对局协调，不向模块传入整个对局。
- 排序保持 ExecuteFrame → PlayerId → Sequence → ArrivalOrder；拒绝不消耗到达序号；可变群体输入在入队时冻结。
- v9、公开 API、固定帧顺序及逐帧哈希/事件/恢复基线不变。

## Issue #30 验收要点

对应 [Issue #30](https://github.com/LiGameAcademy/rts_kernel_cs/issues/30)：事件缓冲统一管理发布与序号。

- `MatchEventBuffer` 独占未消费事件与下一序号；普通/群体事件共用 `Publish`，由 `RtsMatch` 显式传入帧号。
- `Drain` 返回独立结果并清空缓冲，序号不因清空重置；恢复只接管已校验序号。
- v9 只保存下一事件序号，不保存未消费事件，恢复不重放历史输出。
- 不引入事件总线、订阅系统或异步。

## Issue #33 验收要点

对应 [Issue #33](https://github.com/LiGameAcademy/rts_kernel_cs/issues/33)：实体身份、状态提交与快照转换独立模块。

- `MatchEntities` 独占实体字典、下一 ID、生成、已有状态提交及实体快照转换。
- 提供按 ID 读取、稳定 live 集合、detached `ReadAll`；订单/规划恢复只消费只读 `ById` 视图。
- `Update` 对缺失实体抛实现异常，不隐式新增；拒绝生成不消耗 ID。
- 快照装配仍由 `RtsMatch` 协调，不新建接收全部模块的快照服务；v9 与逐帧基线不变。

验证入口：仓库根目录 `./Test.ps1`（构建零警告/错误，控制台断言及 CLI）。

## Issue #63 验收要点（包 A）

对应 [Issue #63](https://github.com/LiGameAcademy/rts_kernel_cs/issues/63) 工作包 A：命令执行规则离开 `RtsMatch` partial。

- `MatchCommandExecutor` 应用 Spawn / Obstacle / Velocity / Move / Stop；依赖为实体、事件、订单、规划、移动定义与按次传入的导航，不持有对局引用。
- `MovePathPlanner` 集中网格→世界路点；`MovementDefinitions.ClearanceCells` 统一净空换算。
- `GroupMove` 仍由 `RtsMatch` partial 处理，经执行器提交单单位 Move；拥挤推进（包 B）与群体策略整体迁出（包 C）另开。
- v9、公开 API、固定帧顺序及逐帧哈希/事件/恢复基线不变。

## Issue #63 验收要点（包 B）

对应 [Issue #63](https://github.com/LiGameAcademy/rts_kernel_cs/issues/63) 工作包 B：拥挤推进离开 `RtsMatch` partial。

- `CrowdStepper` 负责地面单位预约、局部绕行、拥挤超时、到点让行及恢复时占地重叠校验；仅独占到达邻接缓存。
- 实体提交仍经 `MatchEntities`，路径状态仍经 `UnitOrderBook`，导航占地仍归 `NavigationState`；不向步进器传入整个对局。
- `RtsMatch.CrowdBlockedSeconds` / `LocalRetryFrames` 等公开常量保持别名，快照校验语义不变。
- 群体 `GroupMove` 策略仍在对局 partial（包 C）；v9 与逐帧哈希/事件基线不变。

## 0.0.1 范围边界

纳入：命令/事件/实体/订单/规划职责边界，运动计算与提交分离，规划生命周期边界，核心算法可读性整理，非法命令拒绝、随机流所有权、快照恢复与 CLI 一致性。清单工作包 #12/#13/#14/#16/#29/#30/#33 均已有验收说明。

不纳入：密集交通与 300/500 性能门、完整 M2 正式地图接入、战斗/经济/技能迁移、Windows 导出。完整移动跟踪 [Issue #3](https://github.com/LiGameAcademy/rts_kernel_cs/issues/3)；版本总验收见 [Issue #55](https://github.com/LiGameAcademy/rts_kernel_cs/issues/55)。框架补刀见 [Issue #63](https://github.com/LiGameAcademy/rts_kernel_cs/issues/63)。

## Issue #66：快照诊断职责整理

待执行命令的差异比较集中到 `PendingCommandSnapshotDiff`。`SnapshotDiff` 独立于数据契约文件，负责稳定的跨域比较顺序；导航、运动及群体状态比较器不再读取命令队列。保持所有既有路径、值格式与首差异优先级，公共 API、v9、模拟状态和事件顺序不变。

验证：Release 构建0警告/0错误，3,242项检查通过，其中新增18项命令诊断/优先级/恢复检查；既有逐帧哈希与事件基线通过，CLI哈希仍为 `dadc36f5f8436389122a88d72006575e0f8a0ed0ec2c9188377ca7776b305e58`。未修改宿主或子模块指针，本项未执行主游戏验收。

审查确认：实体、命令、事件、订单、规划状态已有独立所有者；群体策略仍在 RtsMatch partial，继续由 Issue #63 工作包 C 跟踪。当前工作区的执行器/寻路调整不属于本项；不得由本批质量整理宣称移动性能或完整 M2 验收完成。0.0.1 总验收 Issue #55 已关闭，本项属于其后的框架整理，不改动已发布版本。
