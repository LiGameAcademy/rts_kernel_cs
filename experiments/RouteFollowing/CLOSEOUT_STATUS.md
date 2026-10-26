# Movement closeout candidate — 2026-10-25

Refs [#154](https://github.com/LiGameAcademy/rts_kernel_cs/issues/154).
Base:4702c68. Step2:eebc410. Step3:8de83c4. Production kernelc1ce455, production
snapshot14 and stationary-combat snapshot1 are unchanged. Experiments only.

Member references can directly approach their own endpoint, reuse a legal shared
suffix or use existing terrain pathfinding. Local half-speed/six-direction sidestep
proposals do not write another long-term route. Task-owned placement and existing
bounded two-member cooperation remain authoritative.

## Full-history diagnosis and CPU evidence — 2026-10-26

This round changes diagnostics only. The complete narrow history reproduces21/24
(failed1/22/23) and the same final state/event transcript. Actual recovery roots
were not omitted or stale; pair work was0 or148 of2048 in the remaining cases.
The current candidate generator cannot admit the existing one/two-helper straight
relocations under placement/body/full-goal-clearance constraints. This is not a
proof that the world or every possible joint motion is unreachable.

Four cold CPU-instrumented runs retain all firsts. Frame430 belongs to member20
trying helper7: CLI wall9.2046/7.5364ms; Godot30.9618/45.8441ms. Godot threadCPU is
31.25ms (quantized), JIT0, GC pause0.195/0.243ms, about110M cycles and2.85MB allocated.
The old119.8594/38.0377ms samples remain unclassified; new data cannot backfill them.
All four first-frozen state/event transcripts agree with the prior build. One new
Godot run still fails30Hz; no release claim or dropped firsts.

Isolated progress-retention and turn-only counterfactuals both still finish21/24,
with different failed members; neither is deployed. The requested six-side geometry
also remains blocked at the representative frozen state. Reject further simple
parameter patches. The next functional step needs a bounded intermediate-passage
execution proof separate from final goal ownership; it is a proposal, not implemented.
The minimal performance candidate is per-search immutable admission observations,
without changing candidate order, collision predicates or cross-frame caching.

## Actual contacts and bounded recovery rounds — 2026-10-26

Experimental snapshot8 persists final timed collision contacts and a bounded recovery
cursor. Format7 is rejected at runtime; frozen fixture adaptation changes only shape.
Recovery tries the original contact first, then other real eligible contacts, one root
per tick sharing the old 2,048 pair cap. Candidate waiting does not consume a round.
Unchanged reference geometry retains version/cursor/physical progress and reports
no_recovery_candidate after an exhausted round. Limits remain32/90/3 and two helpers.

The exact frame387 world now automatically tries1/3 at388/389 and starts13→4 at390.
All24 finish at889, followed by90 stable ticks. Two cold CLI and two cold Godot
processes retain592 ticks each, including firsts, agree on every state/event and
per-tick restore. Peaks21.9764/19.8748/30.3711/22.1432ms all meet33.333ms in this gate.

Full-from-start narrow remains21/24;1/22/23 fail at559/560/625. Both final cold CLI
runs confirm it; direction changes remain24/24. Stop/new-order/queue/map/restore,
contact/probe ownership and old/corrupt snapshot checks pass. The existing timed
kernel checks pass24,382 assertions with the new read-only all-contact query.

Two cold Godot full-suite processes stop at the original21 frozen case because
frame430 costs119.8594/38.0377ms (arrival itself succeeds at442). Later full scenarios
were NOT run there. Keep these overruns and earlier37.314ms/intermediate failures;
no CPU/scheduling attribution is claimed for the untimed-CPU430 samples. No300/500
matrix, main-game takeover, merge, tag or new black-box handoff. Overall FAIL.

## Waiting-member cooperation — 2026-10-26

f077595 preserves settled-first planning, then admits same-task Waiting helpers
with no pending work. Following, Stop, ordinary Idle and other tasks remain excluded.
Both passes share the original 2,048 pair-check cap; no larger window or extra state.

A complete frame-356 world admits 18→7 cooperation for member 17 at 357. It finishes
at 383; the requester gains 48.994749 toward its original goal within 90 ticks.
Per-tick restore, 10 admission contracts and all original frozen/boundary gates pass.
Frame 267 remains a safe rejection case, not a demand for instant universal recovery.

Controlled CLI full-world completion improves 17/24 to 20/24; 1/2/22/23 still fail.
Direction changes remain 24/24 with 90 stable ticks. Unchanged-path retries fall
from 13 to 7, so ineffective recovery accounting is still unresolved. Global A*,
production/host and snapshot format 7 are unchanged. No new Godot/300/500 matrix,
merge, black-box handoff or completion claim. Driver mistakes and failed probes
are archived alongside controlled results; they are not substituted as passing runs.

## Remaining-helper corridor protection — 2026-10-25

Implementation d40233e; shared acceptance entry ba2d5be. Remaining active and future
helper corridors derive from the existing task plan and bodies each tick. Direct,
half-speed and side proposals check the same permission before timed safety;
clear body queries cannot resurrect denied entry. Existing occupants can move
outward. Control, map and deadline cancellation release the derived lease immediately.
No extra authoritative fields, directions, helpers or changed retries/timeouts.

All frozen executed gates now PASS: member 21 at frame 442, members 4/23 at 276/378,
13 at 339; 18→7 finishes at 488 within the original 504 cutoff. All 34 corridor
checks, 54 navigation checks and lifecycle, yield, placement, Stop/queue/restore
boundary checks pass.

Two cold CLI and two cold Godot processes agree on all nine state/event transcripts.
All 7,116 timed ticks and 36 firsts are retained. Direction changes complete 24/24,
followed by 90 settled ticks. The narrow scenario completes 17/24 with seven failures.
All seven are already beyond the terrain gap, failing during destination gathering:
three face active waiting peers, one a settled peer, three previously movement-failed
stationary peers. This does not prove 24 units cannot traverse the gap, or authorize
extending recovery until success.

Overall remains FAIL. CLI peaks are 14.3980/49.8566ms, Godot 46.1787/16.6785ms; two
narrow samples exceed 33.333ms, including CLI's first command. No CPU/scheduling
attribution is claimed. First editor import also returned nonzero and is retained,
although both runtime processes completed their checks normally. No production/host
takeover, 300/500 matrix, new black-box window, merge or completion tag.

Run the current member contract via CLI/Godot --member-acceptance; legacy --self-test
and centre-line goldens remain separate. RTS_CLOSEOUT_EVIDENCE saves failure inputs.

## Space-clearing proof and corridor reoccupation — 2026-10-25

External counterfactual only; runtime remains91d6f70. In the full504 world, a
geometry-derived retreat within half a cell reaches its target at510. Member1
advances, but1/7 wait again from512. At534,7 is91.515174 from its unchanged endpoint
(control85.329642); the original plan deadline remains535. Reject this candidate.
The30 extra diagnostic ticks do not extend or pass the already-failed60-tick gate.

An unmodified444→504 replay reproduces the original final hash. The future7-helper
corridor is initially clear of nonparticipants;5 enters at465, still occupies it
when7 activates at469, and1 enters at473. Both occupy it at504. Plan-time full-sweep
validation does not preserve that space during execution. Next work should first
prove bounded protection of the existing remaining helper corridor, not add retreat
angles or another priority order. No such protection is implemented or proven yet.

All120 actual diagnostic steps and four first ticks are kept, including unprepared
control first ticks66.2304/52.1612ms over33.333ms. Warm candidate timings are not cold
performance acceptance. A deferred-enumeration defect affected first-run member
rows; corrected replay retains matching per-frame hashes/events. Full evidence is
archived in internal documents. No production takeover, host launch or completion.

## Helper-priority A/B validation — 2026-10-25

Validation only; the algorithm in this branch remains91d6f70. Two external cold
CLI processes use the same source, fixtures and kernel DLLs; the sole candidate
change ranks Yielding before the existing waiting/ID keys. Both builds have zero
warnings and errors after fixing the external projects' initially shared NuGet
intermediate path. The initial failed build log is retained.

The control reproduces the previous pair's complete frame transcript and final
hash. The helper-first candidate starts7 at469 rather than470, but both still fail
the original60-tick18→7 gate. At504 the candidate also has1↔7 waiting and the plan
still atNext1.13's single-helper case finishes at339 in both variants.

240 measured steps and all four first ticks are retained. Control peaks are
1.2768/3.5763ms; candidate peaks1.9553/2.9205ms. Preparation1826.7172/2041.9168ms is
separate and does not mutate authority. One run per variant cannot establish a
performance improvement; functional acceptance fails regardless.

Diagnostic60-tick replays reproduce each variant's measured final hash. Read-only
queries of both504 worlds leave Save unchanged. With the other bodies held at their
snapshot positions, direct/half-speed/all six side steps for1 and7 each conflict
with bodies while all terrain checks pass. This explains why priority alone is
insufficient here; it does not prove all coordinated multi-body motion impossible.
For the candidate,7's right-side choices meet5/6 and its left choices meet1;1's
rightmost choice meets10. These are body constraints, not hidden terrain obstacles.

The candidate is rejected, not adopted. No extra directions, changed thresholds,
expanded search, new joint solver or production takeover. Next work needs an actual
executable passage/space-clearing proof, not another priority permutation. No
aggregate24/500 or cold-Godot acceptance was run. PR155 stays draft.

## Lifecycle repair follow-up — 2026-10-25

The two reproduced lifecycle defects are repaired: a direct clear tick retains a
nearby passage side, and a requester still settled in its task can retain an active
helper leg. Stop, new orders, queued takeover, map invalidation and the90-tick plan
deadline still cancel old control. Nearby means body contact range plus one existing
pathing cell (94 for the current experiment), an experimental geometric hysteresis,
not a Warcraft III constant or a newly approved final gameplay parameter.

Four lifecycle boundary groups pass; the54 navigation checks and original yield
boundary checks also pass. Production code and all golden expectations are unchanged.

The executed gate remains **FAIL**.21 reaches its endpoint at455 rather than328
(a127-tick regression, with6 rather than3 goal revisions);4/23 still reach at276/377,
and13 finishes cooperation at339.18 starts at445 and7 at470, but at504 the plan is
still on its second leg:7 waits for1,1 waits for7;7 remains85.329642 units from its
goal. The60-tick diagnostic replay preserves the original deadline and matches hash
`AC9FFD40455E29BD0CC11B7432C5F17FD9BED566D841A309BF8FBE5614064CA2`.
Requester settlement no longer cancels the plan. This is not evidence of permanent
deadlock, nor permission to extend the test until it passes.

544 actual Step records and all four first ticks are retained. CLI peaks are
3.9938/1.3706/0.5276/2.2647ms. These are frozen samples after explicit preparation,
not500-unit or cold-Godot acceptance. The existing production mover prioritizes
helpers; this experiment still sorts by waiting and ID. Whether prioritizing the
helper is sufficient has not been tested and is not claimed as a solution.

Stop at this failed gate. No aggregate24 regression,300/500 load, production takeover
or new test window. Preserve the old failure history below;PR155 remains draft.

## Actual verification

- Navigation/state boundary suite:54 assertions pass, including restore continuation,
  terrain/goal revisions, append, Stop, corrupt references and rejected format6.
- `--closeout-gates`:417 timed ticks retained, including all four first ticks.
  Preparation1677.6182ms is recorded separately and leaves the real world unchanged.
- Original before-151 full world:member21 reaches its endpoint at frame328;
  peak4.7741ms, zero over-budget ticks.
- Original before-259 full world:members4/23 reach their endpoints at276/377;
  peak1.8313ms, zero over-budget ticks. Both use the same input world.
- Frozen cooperation13 finishes at339; peak0.5902ms.
- Frozen18→7 cooperation **fails** the existing60-tick gate.18 starts at445,
  7 at473. Requester4 settles at477, cancelling the existing plan. At504,
  7 still waits for5,77.539848 units from its endpoint. Peak2.4645ms.
- Per-frame restored state and events agree in these runs. Diagnostic replay of
  exactly the failed60 ticks reproduces final hash
  `B79D558A41583D6AC9B9F2E4920AAF05F3C8F9BEF57CEED1544F78DCFF50C498`.
  It is not another performance sample and does not extend the deadline.

## Review findings and stop boundary

The new passage state clears whenever the direct proposal is safe for one tick.
That does not mean an encounter has finished:member7 records blocker5/side-1 at474,
no relation at475, then blocker5/side+1 at476. This violates the intended stable-side
contract. No claim of permanent deadlock follows from the60-tick observation.

Requester settlement cancelling a remaining yield plan is existing behavior;
the old assertion requires `yield_finished`. This interaction needs review alongside
actual helper completion. The assertion was not relaxed to claim success.

Stop here under the approved failure gate:no24-unit aggregate regression,
no300/500 load or cold-Godot acceptance, no production snapshot15, no host takeover
or new user test window. No completion tag or0.0.2 release. Priority:passage lifetime
under intermittent clearance and requester settlement during an executing helper leg.
Do not add samples, windows, retries, flow fields or generic Idle yielding.

## Reproduce

`dotnet run --project experiments/RouteFollowing/Checks/Checks.csproj -c Release -- --navigation-checks`

`dotnet run --project experiments/RouteFollowing/Checks/Checks.csproj -c Release -- --closeout-gates`

For diagnostic snapshots, set process-local `RTS_CLOSEOUT_EVIDENCE` to a disposable
output directory and run with `--closeout-failure-audit`. It replays only the
original60 ticks. Internal documents retain the first raw log, cancellation
snapshots, source input hashes and compressed evidence.

Author:Codex. Complex experimental change; PR remains draft and must not auto-merge.
