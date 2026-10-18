"""Summarize candidate observations without launching or replaying a match."""
import argparse
from collections import Counter
import json
from pathlib import Path


def summarize(label, lists, attempts):
    accepted = [item for item in attempts if item["Accepted"]]
    used = [item for item in lists if item["Reads"]]
    first = sum(item["Visited"] == 1 for item in accepted)
    first_only = sum(item["Generated"] == 1 for item in used)
    reads = sum(item["Reads"] for item in lists)
    visited = sum(item["Visited"] for item in attempts)
    if reads != visited or any(item["ListId"] == 0 for item in attempts):
        raise ValueError("This fixture must observe every candidate visit through its lazy list")
    return {
        "label": label, "constructed_lists": len(lists), "used_lists": len(used),
        "attempts": len(attempts), "accepted_candidates": len(accepted),
        "failed_attempts": len(attempts) - len(accepted), "first_hits": first,
        "first_hit_percent_of_success": 100 * first / len(accepted) if accepted else None,
        "first_only_lists": first_only,
        "first_only_percent_of_used_lists": 100 * first_only / len(used) if used else None,
        "visits": visited, "mean_visits": visited / len(attempts) if attempts else None,
        "max_visits": max((item["Visited"] for item in attempts), default=0),
        "generated_prefix_cells": sum(item["Generated"] for item in lists),
        "possible_cells": sum(item["Capacity"] for item in lists),
        "cached_reads": sum(item["CachedReads"] for item in lists),
        "enumerations": sum(item["Enumerations"] for item in lists),
        "visit_histogram": dict(sorted(Counter(item["Visited"] for item in attempts).items())),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    root = args.directory.resolve()
    raw = json.loads((root / "candidate-usage.json").read_text(encoding="utf-8"))
    rows = []
    for sample in raw:
        label = f'{sample["count"]}/run-{sample["run"]}'
        rows.append(summarize(label, sample["lists"], sample["attempts"]))
        if sample["count"] in (300, 500):
            if len(sample["lists"]) != sample["count"] or len(sample["attempts"]) != sample["count"]:
                raise ValueError("Unexpected main-fixture candidate count")
        if sample["count"] == 24 and sample["run"] == 2:
            # The fixed AtomicSmallGroupChecks source runs split-24 before tree-edge-24.
            if [item["Id"] for item in sample["lists"]] != list(range(1, 49)):
                raise ValueError("Atomic fixture construction order changed")
            for name, lower, upper in [("split-24", 1, 24), ("tree-edge-24", 25, 48)]:
                rows.append(summarize(name,
                    [item for item in sample["lists"] if lower <= item["Id"] <= upper],
                    [item for item in sample["attempts"] if lower <= item["ListId"] <= upper]))
    timing = []
    for name in ("baseline.log", "profile.log"):
        lines = (root / name).read_text(encoding="utf-8").splitlines()
        frames = [json.loads(line.split("=", 1)[1]) for line in lines if line.startswith("frame_sample=")]
        if len(frames) != 901:
            raise ValueError("Missing standard frame samples; do not discard first frames")
        timing.append({
            "log": name, "frames": len(frames), "pid": frames[0]["process_id"],
            "first_command_ms": next(item["elapsed_ms"] for item in frames
                if item["count"] == 300 and item["run"] == 0 and item["phase"] == "command"),
            "max_command_ms": max(item["elapsed_ms"] for item in frames if item["phase"] == "command"),
            "max_move_ms": max(item["elapsed_ms"] for item in frames if item["phase"] == "move"),
            "over": [item for item in frames if item["over_budget"]],
        })
    output = {"samples": rows, "timing": timing,
        "notes": ["Accepted means local placement validation, not atomic command acceptance.",
                  "Three runs repeat the same inputs; do not treat them as independent layouts.",
                  "Only baseline wall times assess the unchanged 30Hz gate; profile has observation overhead."]}
    (root / "candidate-summary.json").write_text(json.dumps(output, indent=2) + "\n", encoding="utf-8")
    for row in rows:
        print(row["label"], "attempts", row["attempts"], "mean", round(row["mean_visits"], 3),
            "first-hit", round(row["first_hit_percent_of_success"], 2),
            "first-only-lists", row["first_only_lists"], "/", row["used_lists"])


if __name__ == "__main__":
    main()
