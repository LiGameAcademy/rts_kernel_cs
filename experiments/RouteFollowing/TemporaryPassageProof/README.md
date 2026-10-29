# Temporary passage proof — automatic24 pass, release pending, 2026-10-29

Refs #154 and draft PR #155. Author: Codex.

This is an isolated execution experiment awaiting black-box/release gates. It does not replace the normal
RouteFollowing Core, Godot lab, production movement or host. The normal experimental
snapshot remains8; only this candidate uses10. Production movement14 and combat1
are unchanged. Do not merge or interpret a generated `passed` field for an individual
requester as group acceptance.

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

## Requester connection follow-up — 2026-10-28

Search-local reuse follow-up: one RouteYieldObservation captures stable bodies and
lazy claims for Settled/Waiting/Temporary searches. It memoizes only the original
distance, terrain and static-straight results for identical raw inputs. At most two
helper projections are checked afresh in original body order; no projected-body
result or search context survives a candidate/search boundary. Candidate ordering,
budget, fixed rounding and collision predicates are unchanged.

Compared with the recovery-entry commitc8adbd1, every state/event transcript matches
for all three complete frozen inputs, including per-frame restore.442 still reaches
24/24 at655 plus90 stable;492 allocation falls from1952256 to about578000 bytes.
Its two final cold CLI peaks are11.8566/35.6695ms; the second remains over33.333ms.
369 retains24/24 at646 but its true first tick is47.0709ms;368 reaches827 with
23.9960ms peak. An earlier no-distance-cache candidate's36.9403ms is also retained.
88 recovery-entry,1418 requester and15 projection/map/restore assertions pass.
Functional frozen gates pass; performance and overall closeout still FAIL. No
aggregate from-start24, cold Godot,300/500 or host migration follows these failures.

Recovery-entry follow-up: a Following member with explicitly empty recorded contacts
and no pending recovery is filtered before scheduling a new round. Normal motion
first supplies the next actual rejection. Existing pending searches, Waiting members
and historical missing contacts retain their old eligibility. Counters, thresholds
and snapshot format10 are unchanged. This rule is isolated in the candidate's own
RouteRecovery override; ordinary Core8 and production are untouched.

The original442 world now completes24/24 at655 plus90 stable ticks:23 retains its
round at515, then starts24→11 at520 and completes that cooperation545. Two cold
CLI runs agree on every state/event but retain frame492 peaks41.5750/45.5744ms,
so30Hz and overall acceptance still FAIL. The368 world remains827 with its prior
state/event transcript;369 improves to646, also24/24 plus90 stable ticks. Their
cold peaks29.8609/31.6324ms do not erase the442 overruns. Requester command/restore
boundaries and `--recovery-entry-boundaries` pass. Performance work is a separate
next change; no full-from-start or host/Godot takeover is implied.

The442/22 admission forecast was not the executed movement:22 drifted while8/2
were clearing, then encountered2 again after opening. The old clearance test also
missed nonparticipant1 on the predicted tail. Admission now checks the predicted
trace against all projected final body positions. If the original32-progress
forecast is blocked, it may instead prove a complete connection to the root's
existing, vacated position within156. No new sampled target is introduced.

For this connection only, the requester retains its position/facing during outward
legs, then executes the admitted target without generic sidesteps. The derived
corridor protects it from other same-task members. Actual arrival releases the
helpers to their original tasks; interruption still cancels ownership. The new
flag is copied with the plan, persisted and hashed in candidate format10; runtime
load rejects9. Final claims, two helpers,2048 work,156 radius and90-tick deadline
are unchanged. Holding does not count as physical progress or extend that deadline.

|Complete frozen input|Current actual outcome|Retained cold wall peak|
|---|---|---|
|442/22, strict cold A/B|8→2 opens484;22 reaches handoff491; stop at623 when23 fails|41.3419/53.1761ms, both frame492|
|442/22, diagnostic tail|22/8/2 complete original tasks737, then90 stable;23 remains failed, only23/24|17.7465ms; exit1, diagnostic only|
|368/1, regression|24/24 finish827, then90 stable|26.6674ms|
|369/23, regression|24/24 finish704, then90 stable|31.4854ms|

The two strict442 runs agree on every state/event digest. All timed runs compare
per-frame restored state/events and check speed, body separation and final claims.
The369 regression improving does not erase23's failure in the different442 world.
Both strict overruns and all historical overruns remain failures of30Hz33.333ms.
No aggregate from-start, Godot or300/500 validation follows this failed gate.

`--requester-boundaries` passes1418 assertions across actual Outward and Passing
states: requester/helper Stop and replacement, requester/helper append, rejected
out-of-map input, map changes, queued-command restore, old/malformed snapshots and
public-array isolation. These are functional checks, not cold performance samples.
The initial test at an exact current-position goal exposed an unchanged baseline
zero-heading exception in RoutePlacements.TryAssign. That separate defect remains
unfixed; normal replacement checks use an explicit nonzero goal. Raw failure is
retained, not counted as a pass.

The strict executable stops on any failed body. Optional `--diagnostic-tail` keeps
unrelated failures visible while observing requester/helpers to completion; it
still returns failure if any body failed. It cannot waive group acceptance.

No production or host takeover, merge or tag. Internal evidence:
V002_REQUESTER_PASSAGE_EXECUTION_2026_10_28.md.

## Original candidate contract — 2026-10-27

Final endpoints and task claims remain unchanged during temporary cooperation.
At most two existing same-task Waiting/Settled members without queued orders may
cooperate; Stop, ordinary Idle and other tasks remain excluded. The temporary
target search uses the existing156 radius around each participant's current body,
rather than around its distant final preference. Existing neighbouring body centres
can be a hand-off position after their owner moves; there are no new angular samples.

The normal settled-first and Waiting fallback searches remain first. The temporary
fallback shares their original2048 pair cap. It validates terrain, physical bodies
and sequential legs before writing a plan. Motion still goes through the existing
timed reservations and remaining-corridor protection.

The plan owns temporary targets/origins and the requester's initial reference.
Participants use Yielding/YieldHolding, never final Settled merely for reaching a
temporary target. After outward legs, release requires the requester to reach its
goal or gain32 signed physical progress. Participants then resume their original
tasks. The90-tick deadline and3 recovery rounds are unchanged. Temporary fields
are copied on Save/Load/public views and included in the candidate hash.

This does **not** establish that resuming original tasks is executable: the tests
below reject that assumption. Unmodified Core files are linked from the baseline;
the original seven files in this Core directory are explicit candidate overrides. Build and
results belong to this revision together; future baseline changes require revalidation.

## Preserved execution results — 2026-10-27

The three fixtures preserve the complete original worlds before frames368,369,442.
The loader only adapts the experimental format to9; no position, goal, blocker,
counter or participant is repaired. Input SHA256 is printed with every result.

|Input/requester|Actual outcome|Retained cold wall peak|
|---|---|---|
|368/1|5→23 temporary legs open387, release388; all24 finish769, then90 stable|41.1815ms in initial strict run;10.9805ms isolated replay|
|369/23|10→3 open390, release391; requester finishes1278, but helpers3/10 fail981/1278; only22/24|104.5788ms isolated replay|
|442/22|8→2 open472; requester becomes blocked by2 again; deadline cancels532|17.7216ms isolated replay|

Replay state/event digests match the corresponding earlier candidate recordings.
The strict executable rejects helper failures, so the second case exits1 even though
the requester eventually arrives. The third exits1 at cancellation. The first
isolated replay passing does not erase its earlier41.1815ms overrun. All first frames
and failures are retained.30Hz remains33.333ms and the overall result is FAIL.

An earlier lifecycle draft required complete straight returns to final goals before
release; all three plans expired. It was corrected to resume tasks after physical
passage, but the remaining failures above persist. Earlier logs that only assessed
the requester are diagnostic, not passing acceptance results. Neither lifecycle
variant is deployed.

At that revision, no full-from-start24-unit narrow/return regression, candidate Stop/new
command/queue/map boundary suite, Godot cold run,300/500 release matrix or black-box
handoff. Per-frame restore/state/event equality, final claim separation, physical
separation and speed were checked in the frozen runs; they do not substitute for
the missing boundary and functional gates.

## Reproduce

Build `experiments/RouteFollowing/TemporaryPassageProof/Checks/Checks.csproj` in
Release with .NET10. Invoke the resulting Checks.dll with an output directory and
optionally one requester ID (`1`, `23`, `22`). Without an ID, it stops at the first
failed gate. Each diagnostic input may be examined independently; that does not
waive a failed gate or authorize the aggregate regressions.

Example from repository root:

```powershell
dotnet run --project experiments/RouteFollowing/TemporaryPassageProof/Checks -c Release -- $env:TEMP/rts-passage-proof-23 23
```

The latest six frozen cold runs pass; the preserved older failing tables above
remain historical evidence, not current expected outcomes.
Use `Checks.dll --requester-boundaries` for the connection boundary checks.
Store each cold run separately. Historical evidence is
documented in V002_TEMPORARY_PASSAGE_PROOF_2026_10_27.md; it includes raw failures,
CPU observations, source snapshots and checksum manifests. No performance fix or
larger traffic algorithm was attempted in this batch.
