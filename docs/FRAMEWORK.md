# 对局框架职责（0.0.1）

0.0.1 收口已落地的模块职责边界。本文说明当前内部状态所有者；完整移动验收、战斗与后续玩法不属于本版本。

## 角色分工

`RtsMatch` 保留公共入口、固定帧序、跨模块快照装配与恢复协调。可变业务状态由具体内部模块独占，不通过多个 partial 直接共享字典。

| 模块 | 独占状态 | `RtsMatch` 协调内容 |
| --- | --- | --- |
| `MatchEntities` | 实体字典、下一 ID、生成与状态提交、实体快照 | 生成前业务校验、事件发布 |
| `MatchCommandQueue` | 待执行命令、到达序号、按帧取出 | 命令执行规则 |
| `MatchEventBuffer` | 未消费事件与事件序号 | 传入帧号、消费后继续推进序号 |
| `UnitOrderBook` | 当前/排队订单意图与活动路径 | Replace/Append/Stop、完成与失败清理 |
| `GroupPlanningQueue` | 计划身份、FIFO、取消与本帧工作量 | 规划推进、落点提交、来源保护 |

导航动态占地仍由 `NavigationState` 拥有。随机流由对局内部持有，不向宿主暴露可变对象。

## Issue #12 验收要点

对应 [Issue #12](https://github.com/LiGameAcademy/rts_kernel_cs/issues/12)：订单与规划生命周期分别进入 `UnitOrderBook` / `GroupPlanningQueue`。

- Replace、Append、Stop、完成、失败、计划取消及恢复由订单/规划模块维护对应关系。
- 代表性合法输入保持逐帧哈希与事件顺序；快照仍为 v9，派生缓存恢复后重建。
- 不引入 ECS、DI 容器、通用调度或 Actor 框架。

验证入口：仓库根目录 `./Test.ps1`（构建零警告/错误，控制台断言及 CLI）。

## 0.0.1 范围边界

纳入：命令/事件/实体/订单/规划职责边界，非法命令拒绝、随机流所有权、快照恢复与 CLI 一致性。

不纳入：密集交通与 300/500 性能门、完整 M2 正式地图接入、战斗/经济/技能迁移、Windows 导出。完整移动跟踪 [Issue #3](https://github.com/LiGameAcademy/rts_kernel_cs/issues/3)；版本总验收见 [Issue #55](https://github.com/LiGameAcademy/rts_kernel_cs/issues/55)。
