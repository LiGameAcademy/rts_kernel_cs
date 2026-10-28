# Temporary passage proof — still failing overall, 2026-10-28

Refs #154 and draft PR #155. Author: Codex.

This is an isolated, failing execution experiment. It does not replace the normal
RouteFollowing Core, Godot lab, production movement or host. The normal experimental
snapshot remains8; only this candidate uses10. Production movement14 and combat1
are unchanged. Do not merge or interpret a generated `passed` field for an individual
requester as group acceptance.

## Requester connection follow-up — 2026-10-28

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

Current strict22 expects exit1. IDs1/23 passed their individual current frozen runs;
the preserved table above describes the old revision, not current expectations.
Use `Checks.dll --requester-boundaries` for the connection boundary checks.
Store each cold run separately. Historical evidence is
documented in V002_TEMPORARY_PASSAGE_PROOF_2026_10_27.md; it includes raw failures,
CPU observations, source snapshots and checksum manifests. No performance fix or
larger traffic algorithm was attempted in this batch.
