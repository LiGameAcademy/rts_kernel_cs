# Route-following experiment (not a production replacement)

A deterministic C# experiment for one reference route per task, per-member following,
current-frame temporal collision safety, explicit waiting and bounded recovery.
Godot is an input/rendering host only. The production `RtsMatch`, snapshot v14 and
recorded production traces are unchanged; the sole kernel code change grants this
experiment access to existing internal geometry/navigation primitives.

**Acceptance is FAIL. Do not merge this as the 0.0.2 movement baseline.**

The experiment distinguishes real corridor bends from quantized straight samples.
Passed straight samples advance without forcing a reversal. Physical progress is
signed actual displacement projected onto the current real reference segment;
lookahead/connection cursor changes earn nothing. A reference-version change
restarts that baseline while keeping wait/recovery history. A terminally failed
member cannot publish another shared route or retry event.

Safe inward steps still commit only after temporal collision checks. Their absolute
quarter-step offset cap never expands within a task; Stop/new tasks reset it.
Experimental snapshots are **format 4**, including corridor corners and physical
progress state; formats 1, 2 and 3 are rejected. Production v14 is unchanged.

| Case | Result | Cold CLI Release peak | Godot Debug peak |
| --- | --- | ---: | ---: |
| Two followers | 2/2 complete | 2.6600ms | 5.5603ms |
| Opposing units | 2/2 complete | 3.2957ms | 3.6538ms |
| Stop blocker | 1/1 complete; Stop stays fixed | 1.7914ms | 2.6631ms |
| 24-unit passage | 12 complete / 12 fail, ends at tick 807 | 72.9730ms | 49.1516ms |
| Five direction changes | 17 complete / 7 fail, ends at tick 897 | 25.2783ms | 34.0352ms |

Both processes executed all 2,451 timed ticks, retaining every first tick and every
over-budget sample. All five per-frame state/event transcript digests and final
hashes agree. Reference/progress, contraction, Stop, replacement, queue and JSON
continuation checks pass; each ended case has 90 additional stability-check ticks.
The unchanged 33.3333ms gate still fails. Explicit preparation took 2,759.7560ms
(CLI) / 4,294.0393ms (Godot) and did not mutate the real world.

Previous format-3 results remain part of the record: the passage was still active
at 1,200 ticks (10 failed, 11 settled, 3 executing), while direction changes
completed 20/24. The new direction-change result is a regression, not acceptance.
A development CLI run also recorded a 271.9081ms direction-change peak; earlier
format-2 and sandbox slow samples are retained separately. No slow sample was
removed to make the performance gate pass. A separate editor-import process
again exited with a native error; the final Godot runtime self-test completed its
checks and returned the expected failure code 1.

Temporarily settled members still do not move to clear a teammate's route. That
separate design step is deliberately not implemented here. These changes do not
establish whole-group convergence or production readiness.

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
work is included. Whole-group completion, the direction-change regression and performance still
need evidence before a production migration can be considered.
