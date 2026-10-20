# Route-following experiment (not a production replacement)

A deterministic C# experiment for one reference route per task, per-member following,
current-frame temporal collision safety, explicit waiting and bounded recovery.
Godot is an input/rendering host only. The production `RtsMatch`, snapshot v14 and
recorded production traces are unchanged; the sole kernel code change grants this
experiment access to existing internal geometry/navigation primitives.

**Acceptance is FAIL. Do not merge this as the 0.0.2 movement baseline.**
Two-unit following, opposing movement and a stationary Stop blocker pass. The
same-reference inward-step fixtures and Stop, replacement, queue and JSON
continuation checks pass. Only an accepted safe movement commits a smaller
transit-offset cap; it cannot expand again during that task. New tasks and Stop
reset it. Bounded connections carry the selected absolute quarter-step cap too;
they cannot create an extra fractional level or restore an outward offset. Experimental snapshots are now **format 3**; formats 1 and 2 are rejected, while
production v14 is unchanged.

Full 24-unit acceptance still fails. At tick 1200 the narrow passage has 10 failed
members, 11 temporarily settled and 3 still moving/waiting, with no group completion
events. Five successive direction changes complete 20/24, with 4 failures.
The final cold CLI narrow-passage peak was 22.6360ms; normal-permission Godot Debug
peaked at 42.5231ms (earlier format-2 samples, including a sandbox 110.7003ms peak, are also retained). The unchanged
33.3333ms gate is not met. First ticks and all over-budget samples are retained.
These results do not establish that the whole movement algorithm improved.

## Run

Requires .NET 10 for CLI checks and Godot 4.7.2 .NET with .NET 8 targeting support.
From the repository root:

```powershell
dotnet run --project experiments/RouteFollowing/Checks -c Release
dotnet build experiments/RouteFollowing/godot_probe -c Debug
# Open experiments/RouteFollowing/godot_probe/project.godot with Godot .NET.
```

The checks deliberately return a nonzero exit code while acceptance fails. Every
first simulation frame and over-budget sample is retained. Explicit preparation
runs disposable worlds; its full duration is printed and actual state preservation
is checked. Preparation does not turn a failed acceptance run into a pass.

## Interactive controls

- 1: two followers; 2: opposing units; 3: a Stop blocker; 4: narrow passage with 24;
  5: open ground with 24. Switching resets the experimental world; startup opens case 4.
- Left click or drag selects; A selects all; right click moves; Shift+right click appends.
- Space: Stop; F2: route visibility; F5/F9: experimental save/restore.
- R: replay five direction changes; P: pause; N: single tick while paused.
- Cyan lines are the shared reference, green crosses are preferences, orange units
  wait, gray units are stopped, red units failed. The selected body has a yellow ring.

Settings (32-unit net progress, 90 waiting ticks before retry, three recovery
attempts) are provisional experiment parameters. Saves have a separate experimental
format. The fixed traffic fixtures keep historical inputs and explicitly convert
their snapshot shape for test setup only; they are not a runtime migration. No flow field, generic idle yielding, formation speed lock or 500-unit spawn
work is included. Current marching offsets and local reference attachment still
need a design correction before a production migration can be considered.
