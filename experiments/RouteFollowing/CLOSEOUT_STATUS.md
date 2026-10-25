# Movement closeout candidate — 2026-10-25

Refs [#154](https://github.com/LiGameAcademy/rts_kernel_cs/issues/154).
Base:4702c68. Step2:eebc410. Step3:8de83c4. Production kernelc1ce455, production
snapshot14 and stationary-combat snapshot1 are unchanged. Experiments only.

Member references can directly approach their own endpoint, reuse a legal shared
suffix or use existing terrain pathfinding. Local half-speed/six-direction sidestep
proposals do not write another long-term route. Task-owned placement and existing
bounded two-member cooperation remain authoritative.

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
