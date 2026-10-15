# RTS Kernel C#

[中文](README.zh.md)

An engine-independent C# RTS simulation kernel extracted from the [Godot Warcraft3 learning project](https://github.com/LiGameAcademy/godot_warcraft3). Development follows that game's tactical and control requirements.

**Early development:** APIs, data structures and snapshot formats may change incompatibly. This is not a complete RTS framework and does not promise production readiness, cross-version save compatibility or cross-platform determinism. Networking, complete movement rules, combat, economy and abilities remain future work.

## Available today

- Independent match instances, fixed simulation steps, entity IDs, queued commands and events.
- Framework ownership: entities, commands, events, order paths and group planning are owned by dedicated internal modules (see [framework notes](docs/FRAMEWORK.md), Chinese).
- Simple velocity integration; JSON frame snapshots, restoration, state hashes and field-level difference reports.
- Serializable random state, static pathing grids and bilinear height sampling.
- **0.0.1**: first usable framework baseline; see [VERSION_0.0.1.md](docs/VERSION_0.0.1.md). Complete M2 movement acceptance and later gameplay migration are out of scope for this version.
- Eight-neighbor pathfinding with static and match-owned dynamic obstacles, cell clearance and frame commands that add or remove footprints; navigation snapshots validate static map identity.
- Goal-based path following with stop, replace and append queues; dynamic replanning, continuation after a failed queued goal, player / unit-AI source priority and order snapshot restoration.
- Authoritative facing, per-frame turning with turn-rate slowdown and elevation-based slope scaling; restoration validates height-field identity.
- Group placement with four soft formations, fixed-work deferred planning, sweep reservation and local detours, and slot-ordered compact arrival.
- Continuous congestion for 3 seconds fails the current goal and continues the queue; stopped units are not pushed.
- Windows first; determinism checks apply only to matching code, input, content and controlled runtime environments.

The current implementation uses ordinary C# types and modular composition, focusing on correctness, state ownership and readability without an ECS migration. ECS is not permanently excluded: a mature third-party implementation may be evaluated later against real maintenance needs, project performance measurements and dependency costs. A general-purpose framework is not built in advance. Hosts own input, assets, rendering and networking; the kernel has no Godot reference or scene-tree dependency.

See [framework](docs/FRAMEWORK.md), [navigation](docs/NAVIGATION.md) and [movement](docs/MOVEMENT.md) for contracts (Chinese). Snapshot v11 stores facing, motion progress, order queues and group-planning progress, and validates supplied static grid and height field identities. Versions 1–10 are rejected; cross-version migration remains unsupported.

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


## Fixed-point simulation

`Rts.Kernel.Numerics` provides signed32.32 `SimScalar`, `FixedVector2` and separately
scaled128-bit `SquaredDistance`. Decimal inputs use `SimScalar.Parse`; arithmetic
truncates toward zero, JSON writes raw signed64-bit integer tokens, and overflow
throws. Floating conversion is an explicit presentation boundary. Squared values
remain wide until a checked length conversion; scalar/wide roots use exact integer
floor roots. The chosen radian period is exactly twice the quantized half-period.

Pinned MIT FixPointCS sources, license and source hashes live under
`third_party/FixPointCS`; the kernel has no dependency on experiments or Godot.
Match state, commands, movement, terrain and group planning now use these types.
Squared matching costs and signed dual potentials use Int128. Exact swept-circle
predicates compare wider products without floating conversion or BigInteger in the
hot path. The snapshot format is v11; older formats require the older kernel.
This is a numeric migration, not a claim of network-lockstep or full M2 acceptance.

## Integer path weights

A* scores, heuristic and queue ordering use Int64 units: an orthogonal step costs
10,000,000 and a diagonal step14,142,135, exactly retaining the previous1.4142135
metric. `PathResult.Cost` is a nullable `PathCost`; failed searches have no cost.
These are cell weights, not world distance. Floating conversion is presentation only.
Equal scores use cell index; this can select a different equally short path from
the old floating sum. The intermediate v10 identified integer path scoring;
v11 now stores fixed spatial state and128-bit matching checkpoints. Both older
snapshot formats are rejected; tags remain available for their matching kernels.

## Numeric boundaries and verification

Admitted coordinates/elevations are within +/-1,048,576 world units; movement
speed/radius are positive and at most65,536. Grid extents and command payloads
are checked before execution. These bounds also keep500-member squared-distance
matching well below signed128-bit limits; the prototype's128-radius limit is not
used here. Heading inputs normalize to[-Pi, Pi); stored headings are canonical.

Time remains integer ticks. Movement quantizes speed times the remaining frame
fraction divided by tick rate, rather than quantizing a reciprocal frame duration
first. Raw JSON values require an integer-preserving consumer; they are not decimal
world units. Use `SimScalar.Parse` for decimal authoring input and explicit
`ToPresentationDouble()` for display. Ideal trig geometry has a bounded approximation
error; final placement/body separation uses exact raw integer predicates.

`Test.ps1` includes independent arbitrary-precision geometry and exhaustive small
matching/path references. The experimental harness accepts `--kernel-checks` to
invoke actual production-kernel group/motion/snapshot scenarios in both CLI and
Godot. Synthetic parity is not actual-map, crowd quality or performance acceptance.
The current production group initialization still needs the separately measured
fixed-work scheduling port before large-crowd performance can pass.

Waypoint advancement computes from the original segment and rounds each coordinate
once. Snapshot segment validation uses a two-raw-unit bound, not the old floating
relative epsilon. A nonzero order that cannot produce a raw displacement in a full
frame and cannot turn reports `movement_below_resolution`; queues follow the usual
failure policy. Intentional zero-scale turning/holding remains valid.
