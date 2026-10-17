"""Join raw method lifetimes with indexed C stacks; keep unmapped frames and unknown scheduling visible."""
from collections import Counter
import json
from pathlib import Path
import re
import sys

root = Path(sys.argv[1])
evidence_root = Path(sys.argv[2]) if len(sys.argv) > 2 else root
summary = json.loads((root / "matrix-summary.json").read_text())
result = []
for row in summary["processes"]:
    first = row["first_300_command"]
    entry = dict(ordinal=row["ordinal"], condition=row["condition"], wall_ms=first["elapsed_ms"],
                 cycles=first["cpu_cycles"], alloc_bytes=first["alloc_bytes"], gc_ms=first["gc_pause_ms"],
                 jit_ms=first["jit_ms"], restore_ms=row["restore_command"]["elapsed_ms"],
                 nonspawn_over=sum(f["phase"] != "spawn" for f in row["over_budget"]),
                 spawn_over=sum(f["phase"] == "spawn" for f in row["over_budget"]))
    if row["condition"] == "A":
        entry["trace"] = None
        result.append(entry)
        continue
    prefix = Path(row["log"]).stem
    raw = json.loads((evidence_root / (prefix + "-evidence.json")).read_text())
    frame = row["first_300_etl"]
    timing = frame["timing"]
    leaves = Counter((s["method"]["Name"], s["method"]["Tier"], s["method"]["Size"])
                     for s in frame["cpu_samples"] if s["method"])
    entry["trace"] = dict(running_ms=timing["scheduled_running_ms"], ready_ms=timing["ready_ms"],
                          waiting_ms=timing["waiting_ms"], unknown_ms=timing["unknown_ms"],
                          interrupt_union_ms=frame["interrupt_union_ms"], samples=len(frame["cpu_samples"]),
                          mapped_leaves=sum(leaves.values()), leaves=[dict(name=k[0], tier=k[1], size=k[2], hits=v)
                                                                   for k, v in leaves.most_common()])
    if row["condition"] == "C":
        indexed = json.loads((root / (prefix + "-stacks.json")).read_text())
        if indexed["trace_events_lost"] or indexed["truncated"] or indexed["qpc_clock_error_ms"] > 0.01:
            raise ValueError("Indexed C trace incomplete or misaligned")
        inclusive = Counter()
        matched, unknown = 0, 0
        for sample in indexed["cpu_samples"]:
            if not first["qpc_start"] <= sample["qpc"] <= first["qpc_end"]:
                continue
            names = set()
            for text in sample["stack"]["Frames"]:
                if not text.startswith("rts.kernel!"):
                    continue
                address = int(re.search(r"\[0x([0-9a-f]+)\]$", text)[1], 16)
                version = next((m for m in reversed(raw["kernel_method_versions"])
                                if m["LoadQpc"] <= sample["qpc"] < m["EndQpc"]
                                and m["Start"] <= address < m["Start"] + m["Size"]), None)
                if version:
                    matched += 1
                    names.add(version["Name"])
                else:
                    unknown += 1
            inclusive.update(names)
        pairs = {(f["sample"]["scenario"], f["sample"]["count"], f["sample"]["run"], f["sample"]["tick"]): f
                 for f in indexed["frames"]}
        max_error = 0
        for raw_frame in raw["frames"]:
            item = raw_frame["timing"]["sample"]
            key = (item["scenario"], item["count"], item["run"], item["tick"])
            if key not in pairs:  # Spawn frames remain in raw evidence, not the stack export scope.
                continue
            for field in ["scheduled_running_ms", "ready_ms", "waiting_ms", "unknown_ms"]:
                max_error = max(max_error, abs(raw_frame["timing"][field] - pairs[key][field]))
        if max_error > 0.00011:
            raise ValueError("Raw/indexed scheduling reconstruction differs by more than one QPC tick")
        entry["trace"]["indexed_clock_error_ms"] = indexed["qpc_clock_error_ms"]
        entry["trace"]["raw_indexed_timing_error_ms"] = max_error
        entry["trace"]["stack_method_ranges_confirmed"] = matched
        entry["trace"]["stack_method_ranges_unconfirmed"] = unknown
        entry["trace"]["inclusive_methods"] = [dict(name=k, samples=v) for k, v in inclusive.most_common()]
    result.append(entry)
(root / "attribution-summary.json").write_text(json.dumps(dict(
    processes=result, diagnostics_complete=summary["diagnostics_complete"],
    partial_windows=len(summary["coverage_warnings"]),
    notes="Inclusive stack counts overlap and are sampling evidence, not exact per-method elapsed time. "
          "Only loaded runtime code ranges confirm indexed managed frames; unmatched ranges remain explicit. "
          "No average trace overhead is subtracted from release gate."), indent=2))
for entry in result:
    print(entry["ordinal"], entry["condition"], entry["wall_ms"],
          "running/ready/unknown", None if entry["trace"] is None else [entry["trace"][k] for k in ["running_ms", "ready_ms", "unknown_ms"]])
print("Attribution join complete; unknowns retained, no causal constant inferred.")
