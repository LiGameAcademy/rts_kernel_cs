# Temporary passage proof — automatic24 pass, release pending, 2026-10-29

## Fixed-body recovery reference — 2026-10-29 (2ad0cea)

This supersedes the unresolved original20 functional result below, not the release
status. Existing eight-neighbour A* is retained. Recovery can plan around currently
settled same-task bodies; independent lookahead uses the same fixed-body predicate.
When a safe direct leg opens, the obsolete detour is released. Moving peers still
use timed motion resolution. The exact goal-cell-to-endpoint leg is also checked.
No new authority fields, snapshot format, cooperation size or search budget.

Original full-world20 and265 parties now finish24/24 at1117/564, plus90 stable ticks.
Final frozen-run peaks168.4501/129.9896ms still FAIL30Hz; earlier faster samples are
retained rather than substituted. The intermediate variant without direct release
failed member7 at817. Geometry success alone did not establish execution success.

Twelve cold processes (CLI Release/Godot Debug;24/300/500; two each) retain20712
frames/1580 overruns. All corresponding per-frame state/event streams agree.24
narrow577, same125, return248 finish and stay stable90; maximum13.5907ms. Stop,
replacement, rejection, append, map, restore, settled-yield and requester boundaries
pass. New `--fixed-body-reference` covers detour visibility and exact final legs.

300 fresh/restore both complete300, but obstacle fails1 at661.500 fresh/restore/
obstacle fail409/217/1 at500/695/667. Tests stop at first failure, so partial counts
are not final arrival ratios. Worst500 Godot sample976.7496ms includes250ms thread
CPU,38,271,456 allocated bytes and11.405ms GC pause; JIT0. Real work is present,
but counters alone do not attribute all remaining wall time to scheduling or a
specific function. All first rounds remain in the gate. No500 same-frame spawn work.

Full-world1 before recovery has contacts12/17; existing bounded cooperation rejects
both entries, and the fixed-body route query also fails. This does not prove all
continuous or multi-body solutions impossible. Preserve those inputs before choosing
the next minimal change. Production, host and the interactive black-box build are
unchanged; PR155 remains draft. Godot probe stderr has sandbox log/certificate
errors, retained separately; this is not a clean-log host launch acceptance.
Internal evidence: V002_FIXED_REFERENCE_2026_10_29.md / fixed_reference_2026_10_29.

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

### 2026-10-29 永久协作请求者等待（#154）

永久计划与临时连接共用请求者等待入口：助手顺序出发期间保留请求者位置、方向和通过侧，实际全部到位后才放行。没有新增权威字段，不改快照格式、恢复次数或90帧计划期限。

完整500单位原冻结世界217/frame595与409/frame393实际执行，其24人小组分别646/670帧完成并稳定90帧；初始读档副本逐帧状态/事件一致。与诊断仅针对单个请求者的653/844不同，本实现对所有永久计划一致生效。settled-yield-boundaries新增等待姿态及计数断言，原Stop/接管/非法替换/追加/地图/逐帧读档和requester-boundaries通过。构建零警告零错误，自审完成。

性能仍FAIL：217峰233.7899ms/11超时；409峰83.1860ms/7超时，首轮保留。只完成第一项功能修复，不能据此接管生产或宣布收口。下一批处理恢复后的有界可达换点及搜索复用。
