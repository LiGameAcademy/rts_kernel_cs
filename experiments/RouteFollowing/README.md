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

## Task-owned goals and bounded cooperation

`RoutePlacements` remains the sole goal writer. Active endpoints and queued
preferences derive the task's reservations, with at least 64 units of separation.
Passing teammates do not trigger goal repair. The original preference and its
156-unit adjustment window remain fixed.

A waiting member may ask an actually blocking, still-owned `Settled` teammate to
yield. A task holds at most one plan, with at most two settled participants moving
in sequence. The planner first tries one participant; a second can only be the
concrete body/claim obstructing a candidate. It does not search arbitrary groups.
New goals are validated together before any are committed. Motion still uses the
existing stepper and temporal collision checks, never teleportation.

Stop, ordinary Idle, other tasks and settled members with queued work are excluded.
Stop/new orders, queued work, map changes, requester release or a 90-tick deadline
cancel remaining control. Cancellation does not undo positions or overwrite a new
order. Each accepted plan consumes an existing recovery attempt; real progress
alone resets that history. The pair search has a 2,048-attempt cap, which is a work
limit, **not a guarantee of fitting a frame budget**.

Experimental snapshots are **format 6**, saving plan membership, stage and lifetime
alongside goal ownership. Formats 1–5 are rejected; production v14 is unchanged.
Public task views and snapshots copy plan membership arrays.

Both frozen gates pass automatically in CLI and Godot:

- Member 13 starts at frame 330 and finishes at 339.
- The pair planner selects 18 then 7: 18 moves at 445, 7 first moves at 466, and
  the plan finishes at 488. This is an executed sequence, not only a geometric witness.
- Frozen peaks: CLI 2.9169/2.7415ms; Godot 2.2156/9.0213ms. All 120 ticks and both
  first ticks are retained. Stop/Idle exclusion, takeover, append, map invalidation,
  corruption rejection and active-plan JSON continuation checks pass.

| Case | Result | Cold CLI Release peak | Godot Debug peak |
| --- | --- | ---: | ---: |
| Two followers | 2/2 complete | 3.7670ms | 3.9608ms |
| Opposing units | 2/2 complete | 5.2780ms | 5.4324ms |
| Stop blocker | 1/1 complete; Stop fixed | 1.3919ms | 11.5941ms |
| 24-unit passage | 13 complete / 11 fail, tick 635 | 39.8444ms | 148.4219ms |
| Five direction changes | **24/24 complete**, tick 817 | 16.4715ms | **229.2037ms** |

All seven per-frame state/event transcript digests and final hashes agree across
runtimes. Each retains 2,199 regular ticks plus 120 frozen ticks, all seven first
ticks and every over-budget tick; five ended cases add 450 stability-check ticks.
The direction-change case has zero overlapping task goals and eight same-task goal
edits from cooperation; it previously completed 15/24. Placement, reference/progress,
inward, queue and restoration checks pass.

**Overall acceptance remains FAIL.** Godot has 20 over-budget direction-change ticks
and 13 passage ticks. The passage still fails 11 members. Preparation took
3,093.0770ms (CLI) / 10,685.1244ms (Godot), without mutating the real world. The
33.3333ms gate is unchanged. The current performance regression is not diagnosed
as CPU computation versus scheduling; no timing sample is discarded to claim success.

An earlier CLI run overlapped a hung isolated editor import and recorded a 36.3570ms
direction-change peak. It is retained separately. After terminating only that owned
import, one serial CLI run and one Godot runtime run were recorded. The runtime
completed every check and returned the expected failure code 1; the import was
terminated with exit -1 and is not reported as a clean import. Historical failures
remain attached to their original code versions. No production integration, new
local passage algorithm, generic idle yielding or further tuning is included.

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
work is included. The passage and performance remain failed; the successful direction-change case alone
does not authorize production migration.
