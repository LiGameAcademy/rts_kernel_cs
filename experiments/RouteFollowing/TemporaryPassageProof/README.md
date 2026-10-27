# Temporary passage proof — rejected candidate, 2026-10-27

Refs #154 and draft PR #155. Author: Codex.

This is an isolated, failing execution experiment. It does not replace the normal
RouteFollowing Core, Godot lab, production movement or host. The normal experimental
snapshot remains8; only this candidate uses9. Production movement14 and combat1
are unchanged. Do not merge or interpret a generated `passed` field for an individual
requester as group acceptance.

## Candidate contract

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
the seven files in this Core directory are explicit candidate overrides. Build and
results belong to this revision together; future baseline changes require revalidation.

## Execution results

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

Stop here: no full-from-start24-unit narrow/return regression, candidate Stop/new
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

Expected exit1 for23 and22. Store each cold run separately. Internal evidence is
documented in V002_TEMPORARY_PASSAGE_PROOF_2026_10_27.md; it includes raw failures,
CPU observations, source snapshots and checksum manifests. No performance fix or
larger traffic algorithm was attempted in this batch.
