# Movement closeout candidate — 2026-10-25

## Settled gathering repair — 2026-10-29 (74f3adc)

This supersedes the unresolved265 execution/admission result below, not the release
gate. The original265 full-world fixture completes its24-member party at564 and
stays stable90 frames. Its44.9547ms peak/10 overruns still FAIL30Hz. All first frames
and failed prototypes are retained.

Permanent goal yielding now checks32 net progress (or arrival) through the existing
local proposals before committing a static gathering placement. This read-only
bounded prediction applies only to Direct references with unplanned same-task peers
all Settled. Applying static reasoning to active peers regressed narrow14 and is
not retained. Existing sidestep directions may retry at the existing half speed.
No extra direction, search point, participant or pair budget; prediction adds work
and is not a guarantee of subsequent dynamic execution. Formats remain unchanged.

The original275-only admission regression now passes. Automatic265/275 Stop,
replacement, append, rejection, map and per-step-restore checks pass, together with
the existing1418 requester-boundary assertions.

Two CLI Release and two Godot Debug cold processes pass24 narrow659 (previous520,
slower by139), same-direction125, roundtrip248, each plus90 stable ticks. All four
1302-frame hash/event streams agree, including freshly restored continuations.
Total5208 frames, maximum12.8115ms, no overrun in these four samples. Narrow/return
trajectories intentionally change; golden output was not rewritten.

The20 original fixture still FAILS at434: a legal terrain reference is blocked by
settled11, and11/18 bounded recovery searches find no candidate while peers remain
active. No new300/500 complete matrix after this failure. Prior load failures and
production/host gates remain open; no interactive build replacement or merge/tag.
Evidence: internal V002_SETTLED_YIELD_2026_10_29.md. Reproduce
`--permanent-yield-admission`, `--settled-yield-boundaries`, `--requester-boundaries`,
`--gap-frozen <original-full-world.json> 265|20 <folder>` and `--full-24 <folder>`.

## Three-gap follow-up — 2026-10-29: partial repair, acceptance FAIL

This section supersedes the earlier all16-command limit and automatic24 PASS claims
for the current candidate. Production and the user's interactive build are unchanged.

-563a2a8: pending Move/Stop capacity and snapshot validation share32 entries. All21
 groups/500 members are accepted together, still applied one group per tick.
 Overflow and full-queue restore checks pass; no command splitting workaround.
-85efdad attempted to require requester clearance before permanent goal yielding.
 It rejected the old275-only plan but did not complete265, and regressed narrow24
 (member14 failed).365c762 reverts that guard. The explicit
 `--permanent-yield-admission` check remains an unresolved regression, not a passing
 CI assertion. Rejecting a useless plan alone did not create executable cooperation.
-5f09efa: a terrain-blocked current reference leg is rebuilt once from the actual
 body position using existing direct/independent routing, without retrying the same
 shared suffix. Goals, recovery counts and wait counters remain unchanged.
 The original20 fixture now gets a legal leg, but its party fails on member1 at450;
 actual party completion is NOT proved. Final265 fixture still fails265 at493.

The final12 cold processes cover CLI Release/Godot Debug, populations24/300/500,
 two runs each. Every recorded hash/event agrees across runtimes;24 additionally
 checks a freshly restored continuation each frame. Large cases compare an initial
 restored copy every frame. All16415 measured frames and767 overruns are retained.
 Preparation is separate and verified not to alter authority.

24 narrow completes520 (previously779), same-direction125, roundtrip248, each with90
 stable ticks. Both Godot runs and CLI runB pass; CLI runA has44.239ms at tick353 and
 stops after narrow. Its remaining scenarios are NOT_RUN, not inferred passing.
 Stop/new-command/queue/map/restore checks pass, including1418 requester-boundary
 assertions. These do not waive the unresolved permanent-yield admission test.

300 fresh completes300 at713, but restore replacement fails265/493 and obstacle
 replacement fails217/468.500 fresh fails433/496, restore265/473, obstacle217/466.
 Tests stop at first failure; partial completion counts are not final arrival ratios.
 Worst recorded peaks: CLI30063.7054ms,50049.2128ms; Godot30095.7898ms,500199.211ms.
 The500199.211ms sample has171.875ms threadCPU and0.799ms GC pause; this cannot be
 dismissed as scheduling alone. Counters do not establish an exact function hotspot.

No new traffic algorithm, larger search window/budget, performance waiver, discarded
 first round, production migration or same-frame500 spawning work. Draft PR155 stays
 open. Internal evidence:V002_THREE_GAPS_2026_10_29.md and three_gaps_2026_10_29.
 Reproduce `Checks.dll --command-capacity`, `--reference-leg`, `--requester-boundaries`,
 `--gap-frozen <full-world.json> <subject> <folder>`, `--full-24 <folder>` or
 `--multi-group 300|500 <folder>`. Dedicated headless load_probe_godot accepts
 `--full24 <folder>` or the same `--multi-group` args, without rebuilding the UI lab.

Refs [#154](https://github.com/LiGameAcademy/rts_kernel_cs/issues/154).
Base:4702c68. Step2:eebc410. Step3:8de83c4. Production kernelc1ce455, production
snapshot14 and stationary-combat snapshot1 are unchanged. Experiments only.

Member references can directly approach their own endpoint, reuse a legal shared
suffix or use existing terrain pathfinding. Local half-speed/six-direction sidestep
proposals do not write another long-term route. Task-owned placement and existing
bounded two-member cooperation remain authoritative.

## 300/500 multi-group gate —2026-10-29: FAIL (no algorithm change)

A separate headless Godot load probe leaves the interactive24-unit lab untouched.
CLI Release and Godot Debug each ran two cold processes per population.300 bodies
form13 groups of at most24 on an existing128x128 map;500 bodies require21 groups.
All commands are submitted together; the candidate still applies one group per tick.

-300 fresh: all300 finish at frame713 and remain stable90 ticks. CLI peaks58.6834/
  89.8979ms; Godot173.9437/216.9268ms. The30Hz gate FAILS.
-300 moving snapshot at frame30, then same-goal replacement: first failure is265
  at493. With one real obstacle edit before replacement, first failure is20 at444.
  Checks stop at the first failure;35/12 completions are not final arrival ratios.
  Live worlds without restoring reproduce both failures and every intervening hash/event.
-500: all four cold runs reject groups17–21 because Move/Stop and snapshot validation
  cap pending commands at16. The admitted prefix is preserved but not stepped.
  No500 movement/performance PASS and no split-across-frames workaround.
-All6720 measured300-unit ticks,383 overruns and four500 admission observations
  are retained. Four300-unit runs agree on every hash/event. Setup, preparation,
  restore and obstacle edit costs are separate. Timing includes submissions on tick0.

Minimum gaps: command queue capacity/validation; permanent goal-yield admission
must prove actual requester clearance (moving275 left270 blocking265); a current
shared-reference leg can become terrain-inaccessible while Follow retains it
(20 before retries127/243/351). CPU evidence also includes real computation costs,
not only GC/scheduling. No movement rules, budgets, snapshots, host or thresholds
changed; performance causes are not fully attributed by thread counters alone.

Reproduce: `Checks.dll --multi-group 300|500 <new-folder>`. Build the dedicated
`TemporaryPassageProof/load_probe_godot/LoadProbe.csproj` in Debug; run Godot4.7.2
mono headless with process-local DOTNET_ROLL_FORWARD=LatestMajor and the same args.
`Checks.dll --multi-audit <cli-300-a-folder> <new-folder>` reproduces live/restore
failures and records diagnostic phase costs, not replacement performance samples.
Evidence: internal V002_MULTIGROUP_FAILURE_2026_10_29.md. Draft PR155/#154 stay open;
no production migration, release, merge or tag.500 same-frame spawning stays deferred.

## Full24 CLI and Godot acceptance — 2026-10-29: automatic local PASS

The latest isolated candidate is now tested from frame0, not only from frozen
failures. New Full24Checks is shared by CLI and an explicitly selected Godot build.
All three cases contain24 units. Narrow passage completes at779, open same-direction
at125, and four target reversals at31/62/93/124 complete at248. Each then remains
stable90 ticks. Commands, bodies, reserved goals and per-frame restore are checked.

Two cold CLI Release processes peak12.4802/22.1881ms; two cold Godot Debug processes
peak12.0182/11.6237ms. Every state hash and event agrees across all1422 ticks per
process (5688 total), all first ticks retained, no33.333ms overrun. Preparation is
separate and authority-unchanged:2143.1239/1904.0554ms CLI and2218.6993/1573.3464ms
Godot. Existing placement/command/map/queue boundaries pass in both runtimes;
1418 requester,88 recovery,15 observation and10104 geometry assertions also pass.

The first two Godot launches failed before any logic tick because the new diagnostic
assumed Assembly.Location was a file path. Those failures and sandbox certificate
errors are retained. The probe now reports the loaded module MVID, matched against
the built candidate; the two measured runs use normal user permissions and clean
stderr. This fixes the test entry only; simulation code and preparation did not change.

Default Godot builds still use Core8. Select the candidate explicitly:
`dotnet build experiments/RouteFollowing/godot_probe/RouteFollowing.Probe.csproj -c Debug -p:UseTemporaryPassageProof=true`
Then use process-local DOTNET_ROLL_FORWARD=LatestMajor and Godot user arguments
`--full-24 <evidence-folder>` for automation, or no user arguments for the interactive
lab. The candidate prints snapshot10 and module identity; default builds reject the
full24 switch rather than silently running the older core. CLI uses
`Checks.dll --full-24 <evidence-folder>`. Failed cases stop the suite and return1.

The interactive controls remain4=narrow24,5=open24,R=repeated target changes,
Space=Stop,Shift+right-click=append,F2=route overlay,F5/F9=save/restore. Black-box
acceptance is pending. No production/host migration,300/500 matrix,merge or tag;
movement/0.0.2 is not fully closed. Internal evidence:
V002_FULL24_CLI_GODOT_ACCEPTANCE_2026_10_29.md.

## Search geometry fast path, step2 — 2026-10-28

Search-only capsule calls now reject strictly disjoint expanded bounding boxes
using Int128 additions, otherwise invoking the original exact CrowdGeometry.Overlap.
Touching boxes, search ordering/budget and final movement safety remain unchanged;
there is no cross-frame cache or new serialized state. The helper is27 lines.

The geometry suite passes10104 assertions; a standalone friend-assembly comparison
passes40010 original-exact-versus-prefilter cases without widening a public API.
Existing1418 requester,88 recovery-entry and15 observation assertions pass again.
Two independent cold CLI processes per full frozen input retain all first ticks:
442 peaks13.1254/16.1560ms,369 peaks12.4116/11.8867ms,368 peaks8.8325/7.8931ms.
All six complete24/24 plus90 stable ticks, every frame hash/event equalsfa3348f
and step1, including per-frame restore. No overruns; frozen local gates pass.
Twelve current cold samples across both changes do not erase prior failures or
replace Godot/full-from-start24/300/500 validation. Overall closeout remains pending;
production/host are untouched and PR155 remains draft. No merge/tag or new lab window.

## Search geometry fast path, step1 — 2026-10-28

The isolated candidate now compares raw squared distance for the seven search-local
strict Length<64 goal/body checks. floor(sqrt(S))<R iff S<R*R for the integer raw
threshold R=64*2^32; exact64 is still legal.156-range, candidate order, budgets,
movement collision, state writes and snapshot10 are unchanged.

10031 numerical assertions and the existing1418 requester,88 recovery-entry and15
observation assertions pass. Two independent cold CLI processes per frozen input
retain every first tick:442 peaks11.1928/13.8318ms,369 peaks15.3547/9.2373ms,
368 peaks13.0607/12.5484ms. All six complete24/24 plus90 stable ticks, with every
frame hash/event identical tofa3348f, including per-frame restore. These frozen
local gates pass; prior overruns remain historical failures. Full-from-start24,
Godot,300/500 and production/host migration are still unvalidated. PR155 stays draft;
overall movement closeout is NOT achieved. Step2 AABB filtering is not applied yet.

## Requester admission/execution repair — 2026-10-28: local pass, overall FAIL

Per-search reuse is now implemented only in the isolated candidate. Stable bodies,
lazy task claims and distance/terrain/straight observations are reused within one
search; projected helper-body predicates remain fresh, with original ordering and
budgets. Three frozen state/event transcripts equal the recovery-entry baseline.
All complete24/24 plus90 stable:442at655,369at646,368at827.492allocation drops from
1952256 to about578000bytes, but two final442 cold peaks11.8566/35.6695ms and369's
actual first47.0709ms retain30Hz failure. The earlier36.9403ms candidate is retained.
88+1418+15 boundary assertions pass. Snapshot10 and stable/production/host unchanged.
Stop at this performance gate; no further algorithm patch or aggregate takeover.

Recovery-entry follow-up: normal Following motion with explicit empty contacts and
no pending search no longer spends a fresh round. The original442 world now reaches
24/24 at655 plus90 stable;23 starts actual24→11 cooperation520 instead of wasting
its round515. Two cold CLI state/event transcripts agree; frame49241.5750/45.5744ms
remain30Hz failures.368 remains24/24 at827 with unchanged transcript;36924/24 at646.
Stop/commands/append/map/restore and new scheduler boundaries pass. Only isolated
candidate RouteRecovery changes, format10 unchanged. No aggregate/host takeover;
the separate per-search reuse optimization has not yet been applied here.

Only isolated TemporaryPassageProof changes, now candidate format10. In the442
world,22 moved during helper clearance and invalidated its forecast; the forecast
also missed nonparticipant1. Full-body admission plus an executable connection to
the existing root origin holds the requester until clearance, then reaches the
actual handoff491. Helpers8/2 retain their final claims and resume original tasks.
They and22 finish737 plus90 stable ticks in an explicitly failing diagnostic tail.

Strict complete-world checks stop at623 when23 fails; both cold runs reproduce the
same state/event digest and retain frame492 overruns41.3419/53.1761ms. The tail
keeps23 failed and exits1, not repaired to pass. Other frozen regression worlds
finish24/24:368→827 (26.6674ms),369→704 (31.4854ms), each plus90 stable ticks.
These different input worlds do not erase the442 failure or historical overruns.

1418 requester-connection boundary assertions pass, including actual Outward and
Passing Stop/new commands/append/reject/map/restore and public-copy isolation.
Exact current-position replacement exposed a pre-existing zero-heading exception;
it is recorded, not fixed or counted as passing. Default Core8, production14,
combat1 and host remain unchanged. Stop before from-start24, Godot,300/500 release
matrix or a new black-box handoff. See TemporaryPassageProof/README.md and the
internal2026-10-28 execution report. PR155 remains draft; no movement closeout tag.

## Temporary passage execution proof — 2026-10-27: FAIL

The isolated TemporaryPassageProof candidate separates temporary targets from final
claims, with the same two helpers/156 radius/2048 pair work/32 progress/90 ticks/3 rounds.
The368 world now actually completes24/24 at769 plus90 stable ticks, but retains a
41.1815ms cold overrun. In the369 world requester23 passes, while helpers3/10 later
fail, leaving22/24; the442 world expires without passage. A new104.5788ms cold first
frame is retained. Neither requester-only success nor a later fast replay is release
acceptance. Candidate snapshot9 is isolated; ordinary experimental8, production14
and combat1 are unchanged. Stop before aggregate/boundary/Godot/300/500 regressions,
host takeover or a new black-box handoff. See TemporaryPassageProof/README.md.

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
