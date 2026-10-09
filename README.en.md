# RTS Kernel C#

[中文](README.md)

An engine-independent C# RTS simulation kernel extracted from the [Godot Warcraft3 learning project](https://github.com/LiGameAcademy/godot_warcraft3). Development follows that game's tactical and control requirements.

**Early development:** APIs, data structures and snapshot formats may change incompatibly. This is not a complete RTS framework and does not promise production readiness, cross-version save compatibility or cross-platform determinism. Networking, complete movement rules, combat, economy and abilities remain future work.

## Available today

Independent match instances, fixed simulation steps, entity IDs, queued commands, events, simple velocity integration, JSON snapshots and restoration, state hashes, field-level differences, serializable random state, static pathing grids and bilinear height sampling.

Windows is the initial target. Determinism checks apply only to matching code, input, content and controlled runtime environments. The project will not adopt an ECS framework. Hosts own input, assets, rendering and networking; the kernel has no Godot reference or scene-tree dependency.

Navigation now supports eight-neighbor paths, cell clearance and match-owned dynamic obstacles edited through frame commands. Goal-based movement supports stop, replacement, FIFO append, dynamic replanning and continuation after a failed queued goal. Player and player-AI orders take precedence over unit AI; persistent Stop orders block unit AI until replaced. Orders and queued intents survive snapshot restoration. Motion orders support authoritative facing, bounded turning, turn speed reduction and elevation-based slope scaling. Snapshot v8 stores facing and motion progress, and validates supplied static grid and height field identities. Version 1/2/3/4 snapshots are rejected; cross-version migration remains unsupported. See [navigation contracts](docs/NAVIGATION.md) and [movement contracts](docs/MOVEMENT.md) for API details (Chinese).

The match owns its random stream. Hosts cannot consume it through a public mutable object. Snapshot `RngState` can be used for diagnostics or a detached stream; the early mutable `RtsMatch.Rng` property has been removed.

## Build and run

Install the .NET 10 SDK. The library targets `net8.0`; the test and CLI hosts target `net10.0`. Initial restore needs the net8 targeting pack; CI installs both .NET 8 and 10 SDKs.

```powershell
git clone https://github.com/LiGameAcademy/rts_kernel_cs.git
cd rts_kernel_cs
./Test.ps1
```

Equivalent commands:

```text
dotnet restore RtsKernel.sln -m:1
dotnet build RtsKernel.sln --no-restore -m:1 -c Release
dotnet run --project tests/Rts.Kernel.Tests --no-build -c Release
dotnet run --project samples/Rts.Kernel.Cli --no-build -c Release
```

Tests use a console assertion runner with a nonzero exit code on failure. Use the script above, not `dotnet test`, as the verification entry point. Windows CI runs the same script.

## Repository boundaries

- `src/Rts.Kernel`: simulation and terrain data contracts.
- `tests/Rts.Kernel.Tests`: kernel behavior checks.
- `samples/Rts.Kernel.Cli`: minimal headless example.

Godot adapters, WC3 JSON/SLK conversion, game assets and integration tests stay in the game repository. No game artwork or map assets are included here. Terrain coordinates and flag bits currently follow the source project's conventions, not a general-purpose map format.

## Host integration

```text
git submodule add https://github.com/LiGameAcademy/rts_kernel_cs.git external/rts_kernel
git submodule update --init --recursive
```

Reference `external/rts_kernel/src/Rts.Kernel/Rts.Kernel.csproj` from the host. Pin the submodule commit and upgrade explicitly. Commit and push kernel work first, then update, verify and commit the host's submodule pointer.

## History and license

Relevant path history was extracted from source commit `05cf2b0`, preserving authors, dates and messages. Commit hashes changed; see the [mapping](docs/history-map.txt). Historical commits retain pre-extraction project references; standalone builds are supported starting at the repository preparation commit.

MIT, Copyright (c) 2026 李维民. See [LICENSE](LICENSE). No NuGet package or formal release is published at this stage.
