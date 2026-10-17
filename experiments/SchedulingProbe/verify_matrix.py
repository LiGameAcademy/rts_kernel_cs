"""Validate exactly the approved six processes and summarize original (never corrected) wall times."""
from collections import Counter
import hashlib
import json
from pathlib import Path
import statistics
import sys

root = Path(sys.argv[1])
evidence_root = Path(sys.argv[2]) if len(sys.argv) > 2 else root
manifest = json.loads((root / "matrix.json").read_text(encoding="utf-8-sig"))
processes = json.loads((root / "processes.json").read_text(encoding="utf-8-sig"))
validated = json.loads((root / "probe-summary.json").read_text(encoding="utf-8"))
order = ["A", "B", "C", "C", "B", "A"]
if manifest["error"] or not manifest["admin"] or manifest["attempts"] != 6:
    raise ValueError("Matrix did not complete as six same-admin processes")
if [p["condition"] for p in processes] != order or [p["ordinal"] for p in processes] != list(range(1, 7)):
    raise ValueError("Matrix order changed")
if any(p["error"] or p["probe_exit"] != 0 for p in processes):
    raise ValueError("Failed process retained; matrix cannot be declared complete")
for binary in manifest["binaries"]:
    if hashlib.sha256(Path(binary["path"]).read_bytes()).hexdigest().upper() != binary["sha256"]:
        raise ValueError("Frozen binary changed during matrix")
by_log = {row["log"]: row for row in validated}
if set(by_log) != {p["prefix"] + ".log" for p in processes}:
    raise ValueError("Baseline verification must include exactly these six logs")
identities = set()
intervals = set()
rows = []
coverage_warnings = []
for process in processes:
    prefix = process["prefix"]
    lines = (root / (prefix + ".log")).read_text(encoding="utf-8-sig").splitlines()
    frames = [json.loads(line[13:]) for line in lines if line.startswith("frame_sample=")]
    observers = [json.loads(line[15:]) for line in lines if line.startswith("frame_observer=")]
    identity = {(f["process_id"], f["native_thread_id"]) for f in frames}
    if len(identity) != 1 or len({(f["scenario"], f["count"], f["run"], f["tick"]) for f in frames}) != 901:
        raise ValueError("Native identity or complete unique frame set changed")
    identities.update(identity)
    if {(int(d["count"]), int(d["run"])) for d in by_log[prefix + ".log"]["digests"]} != {
        (count, run) for count in [300, 500] for run in range(3)
    }:
        raise ValueError("Missing first/repeated scenario result")
    if {(o["runtime"], o["architecture"]) for o in observers} != {(".NET 10.0.9", "X64")}:
        raise ValueError("Runtime context differs from calibrated target")
    intervals.add((root / (prefix + "-profile-interval.txt")).read_text(encoding="utf-8-sig").strip())
    def one(scenario, count, run, tick):
        matches = [f for f in frames if (f["scenario"], f["count"], f["run"], f["tick"]) == (scenario, count, run, tick)]
        if len(matches) != 1:
            raise ValueError("Missing/duplicate first frame")
        return matches[0]
    first = one("multi-party", 300, 0, 0)
    spawn = one("multi-party", 300, 0, -1)
    restore = one("restore", 300, 0, 0)
    commands = [f for f in frames if f["phase"] == "command"]
    if len(commands) != 10:
        raise ValueError("Missing command/restore/edit first records")
    evidence_path = evidence_root / (prefix + "-evidence.json")
    evidence = None
    if process["condition"] != "A":
        evidence = json.loads(evidence_path.read_text(encoding="utf-8"))
        if evidence["process_id"] != first["process_id"] or evidence["events_lost"] != 0:
            raise ValueError("ETL identity/loss check failed")
        first_etl = next(f for f in evidence["frames"] if f["timing"]["sample"] == first)
        # Unknown scheduling states stay explicit; they are never assigned to running or waiting.
        if not any(s["method"] and s["method"]["Name"].startswith("Rts.Kernel.") for s in first_etl["cpu_samples"]):
            raise ValueError("First command has no mapped kernel samples")
    elif (root / (prefix + ".etl")).exists():
        raise ValueError("A must not enable this experiment's WPR recording")
    row = dict(ordinal=process["ordinal"], condition=process["condition"], log=prefix + ".log",
               identity=list(next(iter(identity))), frames=901, state_event_digests=6,
               first_300_command=first, first_300_spawn=spawn, first_500_command=one("multi-party", 500, 0, 0),
               restore_command=restore, commands=commands,
               max_move_ms=max(f["elapsed_ms"] for f in frames if f["phase"] == "move"),
               over_budget=[f for f in frames if f["over_budget"]],
               preparation=by_log[prefix + ".log"]["preparation"])
    if evidence:
        for frame in evidence["frames"]:
            timing = frame["timing"]
            if not timing["state_coverage_complete"]:
                coverage_warnings.append(dict(log=prefix, sample=timing["sample"], unknown_ms=timing["unknown_ms"]))
            if frame["interrupt_union_ms"] > timing["scheduled_running_ms"] + 0.000001:
                raise ValueError("Interrupt overlap exceeds scheduled interval")
        row["first_300_etl"] = first_etl
        row["runtime_events"] = evidence["runtime_events"]
        row["method_loads"] = evidence["method_loads"]
    rows.append(row)
if len(identities) != 6 or len(intervals) != 1:
    raise ValueError("Cold process identities or sampling interval not matched")
by_condition = {}
for condition in ["A", "B", "C"]:
    subset = [r for r in rows if r["condition"] == condition]
    values = [r["first_300_command"]["elapsed_ms"] for r in subset]
    by_condition[condition] = dict(n=2, first_300_ms=values, mean_ms=statistics.mean(values),
                                   all_over_budget=sum(len(r["over_budget"]) for r in subset))
result = dict(validation_passed=True, diagnostics_complete=not coverage_warnings, coverage_warnings=coverage_warnings, order=order, cold_processes=6, frames=5406, state_event_digests=36,
              first_frames_retained=True, sampling_interval_identical=True, evidence_directory=str(evidence_root),
              profiles=manifest["profile_verification"], conditions=by_condition, processes=rows,
              notes="n=2 per condition; means/differences are descriptive, not a causal constant or corrected wall-clock gate. "
                    "Version tier/size is not proof of byte-identical machine code; A trace fields remain unavailable.")
(root / "matrix-summary.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
for row in rows:
    first = row["first_300_command"]
    print(row["ordinal"], row["condition"], "first300", first["elapsed_ms"], "cycles", first["cpu_cycles"],
          "restore", row["restore_command"]["elapsed_ms"], "over", len(row["over_budget"]))
print("Matrix verification PASS: 6 cold processes, 5406 unique frames, 36 baseline state/event digests; all first/over records retained.")
print("Partial scheduling windows (unknown retained):", len(coverage_warnings))
