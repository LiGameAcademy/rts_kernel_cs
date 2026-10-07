# 导航查询与动态占地

导航基础对应 Issue #1/#2，PR #4/#5 已合并。地图读取和 WC3 格式转换由游戏宿主负责。

## 查询与所有权

`GridPathfinder.FindPath(grid, start, goal, maxExpandedNodes, clearanceCells)` 查询静态地图；`RtsMatch(config, seed, pathingGrid)` 启用该对局的导航，`match.FindPath(start, goal, clearanceCells, maxExpandedNodes)` 查询静态阻挡与当前帧动态占地。两种入口的参数顺序不同，建议使用命名参数。

`clearanceCells` 沿用游戏 `PathQuery.can_walk_cell_clear` 的 Chebyshev 方形邻域：中心及四周指定格数范围必须全部可走，越界视为阻挡。0 是点单位，1 要求至少 3×3 空间。它不是世界半径或圆形碰撞体；单位参数到格子净空的转换由后续迁移处理。负数抛参数异常，过大的正数返回非法端点。

路径含起终点、不做吸附或平滑，代价直线 1、斜线 1.4142135；对角线两侧也必须满足完整净空。搜索节点预算包括起点和终点。每次同步查询使用独立搜索状态；不支持在查询期间并发修改对局。

地图 `PathingGrid` 只读共享；每场对局各自保存占地计数和稳定障碍 ID。障碍 ID 为非零 ulong，与 EntityId 命名空间分离，ID 生命周期由宿主测试命令管理；建造系统迁移后再建立实体占地关联。成功查询返回只读路径，不暴露占地数组。

## 固定帧命令

```csharp
var match = new RtsMatch(MatchConfig.Default, seed: 7, pathingGrid: grid);
match.SubmitCommand(CommandEnvelope.SetObstacle(
    executeFrame: 1, playerId: 0, sequence: 0,
    obstacleId: 1, area: new GridArea(X: 10, Y: 20, Width: 3, Height: 2)));
match.Step();
var path = match.FindPath(start, goal, clearanceCells: 1);
match.SubmitCommand(CommandEnvelope.RemoveObstacle(
    executeFrame: 2, playerId: 0, sequence: 1, obstacleId: 1));
```

矩形范围左上角包含、右下角不包含。相同 ID 替换旧矩形，不同 ID 可重叠；移除一个障碍不会清掉其他障碍或静态阻挡。完整校验通过后才更新占地；越界替换保留原占地。

空矩形、非法 ID、错误命令载荷在提交时拒绝；越界、导航未启用或移除不存在 ID 在目标帧产生 `CommandRejected`。事件使用现有错误详情。这些是导航测试命令，暂不包含建造权限、资源支付或玩家占地所有权，不应当作已完成的联机建造接口。

命令沿用现有执行帧、玩家和序列排序；单对局单线程。修改只在 `Step` 中发生，不由渲染回调驱动。

## 导航快照身份

帧快照包含静态地图内容 SHA-256、按 ID 升序排列的动态矩形，以及未执行命令的障碍载荷。地图本身由宿主保管，不嵌入快照；恢复有导航的对局必须 `RtsMatch.Restore(snapshot, grid)`。宽高、格距、原点或任一阻挡标志不同均拒绝。

静态身份采用明确小端编码的宽高、double 格距与原点、行优先阻挡字节；它只覆盖当前 PathingGrid。高度场、移动参数及其他游戏内容身份将在实际移动与内容接入时扩展，不能把它当成完整游戏内容哈希。

快照校验障碍 ID 顺序、唯一性、矩形合法性和待执行载荷；恢复时再校验地图边界。失败发生在新实例构造过程中，不修改原对局。状态哈希覆盖导航与待执行命令，字段差异报告包含障碍字段和命令载荷。

导航状态最初在 v2 引入，当前格式随移动状态升级为 v3；v1/v2 不再接受，不提供迁移工具。没有地图的对局仍可通过原构造/恢复入口运行。最新快照与移动规则见 [移动说明](MOVEMENT.md)。

目标路径跟随与动态占地变化后的在途重规划已分批实现；转向、预约、避让及正式玩法接入仍待开发。300/500 基线与 Windows 导出仍暂缓。
