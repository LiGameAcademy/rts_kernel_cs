# RTS Kernel 0.0.1

Status: acceptance candidate for [Issue #55](https://github.com/LiGameAcademy/rts_kernel_cs/issues/55).  
Tag `v0.0.1` is created only after this version PR and the host pin PR are merged.

## Scope

First usable **framework** baseline:

- Match modules own entities, commands, events, order paths and group planning ([FRAMEWORK.md](FRAMEWORK.md)).
- Illegal command structure is rejected at submit and restore with the same contract.
- Match-owned RNG; hosts cannot consume the live stream through a public mutable object.
- Snapshot format **v9** (facing, motion, order queues, group-planning progress); v1–v8 rejected.
- CLI and Godot can drive the same kernel with matching content and input hashes.

## Out of scope

- Complete M2 movement acceptance (dense traffic, formal map/unit cutover, 300/500 performance gate).
- Combat, economy, abilities/GAS migration.
- Windows export (R00.5) and legacy 300/500 baseline capture (R00.4).
- Networking, fog of war, cross-platform determinism, cross-version saves.

## Closed framework work packages

`#12` `#13` `#14` `#16` `#29` `#30` `#33` (plus earlier `#10` `#11` `#15` `#17` on the integration line).

## Verification (this candidate)

Environment: Windows, .NET SDK 10, kernel `net8.0`, tests/CLI `net10.0`, Godot `4.7.2.stable.mono`.

| Check | Result |
| --- | --- |
| `./Test.ps1` (Release) | 0 warnings / 0 errors; **3224** assertions PASS |
| Default CLI hash | `dadc36f5f8436389122a88d72006575e0f8a0ed0ec2c9188377ca7776b305e58` (frame 31) |
| Godot default bridge | `selftest_rts_kernel_bridge: PASS (24 checks)`; CLI/Godot hashes match |
| Godot navigation bridge | `selftest_rts_kernel_navigation: PASS (15 checks)`; mid-move restore and CLI/Godot hashes match |
| Host playable boot | `godot_warcraft3` Issue #29: pin this commit, `Test-Apps.ps1 -App game`, normal startup to playable match |

Known non-blocking Godot exit noise: ObjectDB/resource leak reports and external-editor falling back to internal editor.

Dense 500-member fixture samples still show high failure/remaining counts; that does **not** block 0.0.1 and does **not** satisfy G2.

## Hosting notes

Pin the submodule to the tagged / merged 0.0.1 commit. Keep the current Godot gameplay simulation until later migration batches. Isolation probes and bridge tests exercise the kernel; they do not replace the main-game playable boot check.
