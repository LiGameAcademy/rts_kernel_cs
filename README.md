# RTS Kernel C#

[English](README.en.md)

不依赖游戏引擎的纯 C# RTS 模拟内核。从 [Godot Warcraft3 学习项目](https://github.com/LiGameAcademy/godot_warcraft3)中提取，围绕该项目的战术与操作需求逐步开发。

**项目处于早期开发阶段。API、数据结构和快照格式可能发生不兼容变化；尚不承诺生产可用、跨版本存档兼容或跨平台确定性。** 当前是可运行的模拟骨架，不是完整 RTS 框架；网络通信、完整移动规则、战斗、经济、技能等仍有待开发。

## 当前能力

- 独立对局实例、固定逻辑帧、实体 ID、命令队列与事件。
- 简单速度积分示例；JSON 帧快照、恢复、状态哈希和字段级差异报告。
- 可保存的随机流、静态网格阻挡查询与高度场双线性采样。
- 静态与对局动态障碍的八方向寻路、格子净空、帧命令增删占地；导航快照校验静态地图身份。
- 目标路径跟随、停止、替换和追加队列；动态重规划、失败后继续排队、玩家/单位 AI 来源优先级及订单快照恢复。
- 权威朝向、按帧转向与转向减速、基于真实高度差的坡度倍率；恢复校验高度场身份。
- Windows 优先；确定性验证仅限相同代码、输入、内容和受控运行环境。

永久不引入 ECS 框架。设计以实际游戏需求为依据，不预先构建通用引擎。宿主负责输入、资源读取、渲染和网络；内核不引用 Godot，也不依赖场景树或节点生命周期。

导航 API 见 [导航说明](docs/NAVIGATION.md)，目标移动与快照 v6 见 [移动说明](docs/MOVEMENT.md)。v1/v2/v3/v4/v5 快照不再接受；本项目尚不支持跨版本存档迁移。

## 构建与运行

安装 .NET 10 SDK（用于构建、测试和 CLI）。核心类库目标为 `net8.0`，测试及 CLI 目标为 `net10.0`；首次恢复需取得 net8 targeting pack，CI 同时安装 .NET 8/10 SDK。

```powershell
git clone https://github.com/LiGameAcademy/rts_kernel_cs.git
cd rts_kernel_cs
./Test.ps1
```

也可以使用标准命令：

```text
dotnet restore RtsKernel.sln -m:1
dotnet build RtsKernel.sln --no-restore -m:1 -c Release
dotnet run --project tests/Rts.Kernel.Tests --no-build -c Release
dotnet run --project samples/Rts.Kernel.Cli --no-build -c Release
```

测试采用无第三方测试框架的控制台检查器，失败时返回非零退出码；`dotnet test` 不是本仓库的验收入口。Windows CI 执行同一脚本。

## 仓库边界

- `src/Rts.Kernel`：纯模拟与数学数据契约。
- `tests/Rts.Kernel.Tests`：内核行为检查。
- `samples/Rts.Kernel.Cli`：无窗口最小示例。
- Godot 桥接、WC3 JSON/SLK 转换、地图资源及游戏集成测试继续留在游戏仓库。

内核不包含游戏美术或地图资源。静态地形 API 的坐标及阻挡位目前继承源项目约定，尚未抽象为通用地图格式。

## 作为子模块使用

```text
git submodule add https://github.com/LiGameAcademy/rts_kernel_cs.git external/rts_kernel
git submodule update --init --recursive
```

宿主通过 ProjectReference 引用 `external/rts_kernel/src/Rts.Kernel/Rts.Kernel.csproj`。宿主固定子模块提交，升级时显式更新引用。开发顺序为：内核提交并推送 → 宿主更新子模块提交并验证 → 提交宿主。

## 历史与许可证

从源仓库 `05cf2b0` 提取相关路径历史，保留原作者、时间及提交说明；提取后哈希发生变化，见 [提交映射](docs/history-map.txt)。早期提交保留了拆分前的项目引用，独立构建入口从拆分整理提交开始可用。

MIT，Copyright (c) 2026 李维民。见 [LICENSE](LICENSE)。暂不发布 NuGet 包或正式 Release。
