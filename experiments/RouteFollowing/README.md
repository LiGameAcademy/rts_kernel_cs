# Route-following experiment (not a production replacement)

A deterministic C# experiment for one reference route per task, per-member following,
current-frame temporal collision safety, explicit waiting and bounded recovery.
Godot is an input/rendering host only. The production `RtsMatch`, snapshot v14 and
recorded production traces are unchanged; the sole kernel code change grants this
experiment access to existing internal geometry/navigation primitives.

**Acceptance is FAIL. Do not merge this as the 0.0.2 movement baseline.**
Two-unit following, opposing movement and a stationary Stop blocker pass. The
24-unit narrow passage currently completes 9/24, with 15 failures; five successive
24-unit direction changes complete 19/24, with 5 failures. The narrow passage also
exceeds the unchanged 33.3333ms gate. These are recorded counterexamples, not waived
criteria. Queue, Stop, replacement and JSON continuation checks pass.

## Run

Requires .NET 10 for CLI checks and Godot 4.7.2 .NET with .NET 8 targeting support.
From the repository root:

```powershell
dotnet run --project experiments/RouteFollowing/Checks -c Release -p:NuGetAudit=false
dotnet build experiments/RouteFollowing/godot_probe -c Debug -p:NuGetAudit=false
# Open experiments/RouteFollowing/godot_probe/project.godot with Godot .NET.
```

The checks deliberately return a nonzero exit code while acceptance fails. Every
first simulation frame and over-budget sample is retained. Explicit preparation
runs disposable worlds; its full duration is printed and actual state preservation
is checked. Preparation does not turn a failed acceptance run into a pass.

## Interactive controls

- 1: two followers; 2: opposing units; 3: a Stop blocker; 4: narrow passage with 24;
  5: open ground with 24. Switching resets the experimental world.
- Left click or drag selects; A selects all; right click moves; Shift+right click appends.
- Space: Stop; F2: route visibility; F5/F9: experimental save/restore.
- R: replay five direction changes; P: pause; N: single tick while paused.
- Cyan lines are the shared reference, green crosses are preferences, orange units
  wait, gray units are stopped, red units failed. The selected body has a yellow ring.

Settings (32-unit net progress, 90 waiting ticks before retry, three recovery
attempts) are provisional experiment parameters. Saves have a separate experimental
format. No flow field, generic idle yielding, formation speed lock or 500-unit spawn
work is included. Current marching offsets and local reference attachment still
need a design correction before a production migration can be considered.
