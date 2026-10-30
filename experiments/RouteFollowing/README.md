# Route-following experiment (not a production replacement)

**2026-10-25 closeout candidate:** member-owned terrain references replace the mandatory
shared centre line. Experimental format is now **7**; runtime loading rejects format6.
The earlier sections below describe the retained historical format6 experiment.
The four frozen executed gates now **PASS** with remaining-helper corridor protection
(d40233e); aggregate acceptance still **FAILS** at 17/24 in the narrow scenario
and retains over-budget samples. Do not integrate or merge this candidate.
See [current status](CLOSEOUT_STATUS.md).


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

## Historical acceptance at 2c569e2

The following measurements predate the range optimization below.

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

## Follow range optimization (2026-10-21)

Only the 9/192/200 range predicates in `RoutePlanning.Follow` now compare squared
integers. For nonnegative Raw radius R, the old floored Length <= R is exactly
D < (R + 1)^2, not D <= R^2. The checked Length overflow remains an overflow.
Nearest-point ordering, projection, path sampling, collision, recovery, yielding
and snapshot format 6 are unchanged.

813 comparisons against the original Length predicate and 15 actual follow-target
boundary cases pass, including one-Raw shells, negative deltas and overflow.
One optimized cold CLI Release and one control/optimized cold Godot Debug process
per version retain all 6,957 timed ticks and 21 first ticks. All seven final hashes
and timed state/event transcript digests match 2c569e2 in every process; Stop,
new orders, append, bounded yielding and JSON continuation checks pass.

| Cold Godot Debug, 24-unit direction changes | Control | Optimized |
| --- | ---: | ---: |
| First tick | 25.0255ms | 11.6408ms |
| P95, all 817 ticks | 10.7877ms | 2.7699ms |
| Maximum | 25.0255ms | 20.0255ms |
| Over 33.3333ms | 0 | 0 |

Full-case medians are 0.5008/0.5943ms; the first 125 ticks' medians are
8.6457/2.2424ms. These are one bounded pair, not a claim that every tick improved.
Both still complete 24/24 with the same eight cooperation goal edits. Optimized
CLI peak is 6.6067ms. Preparation costs are fully reported (CLI 1,728.9728ms;
control/optimized Godot 4,193.2057/2,904.5571ms), with authority unchanged.

**Overall acceptance remains FAIL:** the passage is still 13 complete / 11 fail;
optimized Godot retains two over-budget passage ticks, peak 44.2130ms. The
historical 229.2037ms remains a failure, not retroactively fixed by a faster run.
No WPR, priorities, JIT flags, traffic rules, budgets or host integration change.

## Short-connection broad phase (2026-10-21)

`RouteConnections.Clear` now computes one segment AABB per query, expanded by
both 31-unit radii. Only strictly outside body centres skip the original exact
capsule predicate. Box-boundary contact and all inside cases retain exact checks.
The body order, candidate order, 256-node budget, 8-cell window, route and recovery
rules are unchanged. No cross-frame cache or new snapshot state is introduced.

888 comparisons against the original exact geometry pass for points, horizontal,
vertical/reversed and diagonal segments, negative coordinates, contact and +/-one
Raw around the box boundary. Two format-6 snapshots before frames 150/251 have
30-frame expected state hashes/events generated using the unchanged 76203de DLL;
both optimized runtimes match every frame and JSON continuation.

One optimized CLI Release, one 76203de Godot Debug control and one optimized Godot
Debug process run serially with the existing explicit preparation. All seven
common state/event transcript digests and final hashes match in all processes.
The two optimized processes additionally retain 60 timed connection-fixture ticks
each: 7,077 timed ticks and 25 first ticks total, without removing slow samples.

| Cold Godot Debug passage | 76203de control | Filtered |
| --- | ---: | ---: |
| Frame 150 | 40.5145ms | 4.5282ms |
| Frame 251 | 39.7731ms | 4.0137ms |
| All 635 ticks maximum | 40.5145ms | 11.7660ms |
| Over 33.3333ms | 2 | 0 |

The optimized Godot's complete 2,379 timed ticks, including nine first ticks and
four frozen cases, peak at 11.7660ms with zero overruns. Its 24-unit direction
changes still complete 24/24, peak 11.7512ms. The optimized CLI's complete 2,379
ticks peak at 19.5046ms with no overruns. Preparation remains separate and state
preserving: CLI 2,195.4793ms; control/optimized Godot 2,585.4768/2,424.6314ms.
These are bounded cold-process observations, not a guarantee for every machine.

A separate development `--connection-checks` run bypassed the standard preparation
and retained a 165.8948ms first tick (FAIL), plus all remaining 59 ticks. It is not
erased or reclassified as passing by the later prepared runs; this direct entry
can incur cold initialization and is not the prepared match-start path.

**Overall acceptance remains FAIL:** passage completion is unchanged at 13/24.
Both complete suites still return failure code 1. This performance-only change
does not repair passage behavior, integrate the host, change 30Hz, or authorize
merging/tagging 0.0.2. Stop, new orders, append, yielding and restore regressions pass.

## Current member-reference acceptance

Use `--member-acceptance` with the CLI or Godot process. This runs the current
member-reference frozen gates, normal scenarios and command/restore boundaries.
`--corridor-checks` runs the CLI permission boundaries. The legacy centre-line
`--self-test` and golden expectations remain unchanged and separate. Production
is not replaced. Set `RTS_CLOSEOUT_EVIDENCE` to retain failure input/output snapshots.

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
