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

## Task-owned goals

`RoutePlacements` is the sole runtime writer of initial and revised task goals.
Reservations are derived from active endpoints and queued preferences; there is no
second mutable goal store. Each task's claims have at least 64 units of separation.
The follow calculation reads the assigned endpoint and cannot independently change it.
Passing moving teammates do not invalidate goals. Terrain changes or external fixed
bodies can trigger a deterministic, task-wide repair within 156 units of the original
preference. Each accepted replacement immediately reserves its space for subsequent
members. A failed repair preserves the old claim and is not searched again until
the fixed-body/terrain context changes. Settled teammates do not yield in this step.

Experimental snapshots are **format 5**, saving placement context/check revisions
alongside corridor corners and physical progress. Formats 1–4 are rejected.
Production v14 is unchanged. Historical motion fixtures explicitly repair conflicting
goals for test setup only; their positions, phases and routes are retained. This is
neither an unchanged historical full trajectory nor a runtime migration.

| Case | Result | Cold CLI Release peak | Godot Debug peak |
| --- | --- | ---: | ---: |
| Two followers | 2/2 complete | 2.3288ms | 4.4661ms |
| Opposing units | 2/2 complete | 2.1984ms | 3.8132ms |
| Stop blocker | 1/1 complete; Stop stays fixed | 1.5383ms | 2.6440ms |
| 24-unit passage | 13 complete / 11 fail, ends at tick 670 | 28.5524ms | 49.4125ms |
| Five direction changes | 15 complete / 9 fail, ends at tick 743 | 8.2838ms | 25.0662ms |

Both processes retain all 2,160 timed ticks and every first tick; all five per-frame
state/event transcript digests and final hashes agree. Reservation, reference/progress,
contraction, Stop, replacement, queue and JSON checks pass. Each ended case has 90
additional stability-check ticks. All timed frames have zero overlapping task goals.
Direction-change endpoint edits fell from 134 to 6 (none during the first 125 ticks);
all six repairs follow external fixed-body occupancy, not a passing teammate.

Preparation took 2,742.0713ms (CLI) / 4,032.5162ms (Godot), without changing the real
world. CLI has no over-budget ticks in this sample; Godot's passage has two above the
unchanged 33.3333ms gate. Both suites return 1 because whole-group acceptance fails.
The direction-change completion count regressed from 17 to 15; stable claims alone
do not solve local passage. No production integration is approved by these checks.

Earlier format-4 passage peaks of 72.9730/49.1516ms, the development 271.9081ms
sample and previous format-2/3 results remain failures in their respective records.
Development placement runs and an interrupted run with an obsolete synthetic test
state are retained separately. No slow first tick is excluded. The current isolated
Godot import exited cleanly, and its runtime self-test completed all boundary checks.

Temporarily settled members still do not move to clear a teammate's route. No new
local passage algorithm or reference-retry ownership change is included here.

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
format. The fixed traffic fixtures explicitly adapt snapshot shape and conflicting goal claims
for test setup only; they are not a runtime migration. No flow field, generic idle yielding, formation speed lock or 500-unit spawn
work is included. Whole-group completion, the direction-change regression and performance still
need evidence before a production migration can be considered.
