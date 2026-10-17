"""Verify complete probe logs against the existing 300/500 trajectory and event baselines."""
import json
import re
import sys
from pathlib import Path

root = Path(sys.argv[1])
expected = {
    300: ("E56EDE11DD0B2228A2430A52FC5A0B894ED6D2005E504BA27EAA057D3E43D799",
          "94af92f44e67f07edf2877a8a16bb666d49e347a912cc5cbec9243a718644e63"),
    500: ("5266A82ECA29575CAFBFB4BA1C62AD502A5940121F1B58D364EC0CA8D1CD79EC",
          "e6ae65771a374e5483b190e3cb7ff50673b0beaa8c94b69bddf70fd9f0400741"),
}
paths = sorted([*root.glob("*-control.log"), *root.glob("*-trace-*.log")])
results = []
for path in paths:
    lines = path.read_text(encoding="utf-8-sig").splitlines()
    if not any("PASS (512552 checks)" in line for line in lines):
        raise ValueError(f"Incomplete or failed probe: {path}")
    samples = [json.loads(line[13:]) for line in lines if line.startswith("frame_sample=")]
    if len(samples) != 901:
        raise ValueError(f"Missing frames: {path}: {len(samples)}")
    identity = {(s["process_id"], s["native_thread_id"], s["qpc_frequency"]) for s in samples}
    if len(identity) != 1:
        raise ValueError(f"Native thread/clock changed: {path}")
    for sample in samples:
        elapsed = (sample["qpc_end"] - sample["qpc_start"]) * 1000 / sample["qpc_frequency"]
        if abs(elapsed - sample["elapsed_ms"]) > 0.00011:
            raise ValueError(f"QPC/wall gate interval mismatch: {path}")
        if sample["over_budget"] != (sample["elapsed_ms"] > 1000 / 30):
            raise ValueError(f"Wall gate changed: {path}")
        if sample["thread_cpu_ms"] is None or sample["thread_cpu_ms"] < 0 or sample["cpu_cycles"] <= 0:
            raise ValueError(f"Missing native counters: {path}")
    digests = []
    for line in lines:
        if not line.startswith("multi_party "):
            continue
        fields = dict(re.findall(r"(\w+)=([^ ]+)", line))
        if (fields["trace"], fields["hash"]) != expected[int(fields["count"])]:
            raise ValueError(f"State/event trajectory differs: {path}")
        if fields["failed"] != "0" or fields["completed"] != fields["count"]:
            raise ValueError(f"Movement failed: {path}")
        digests.append(fields)
    if len(digests) != 6:
        raise ValueError(f"Missing match results: {path}")
    prep = [json.loads(line.split("=", 1)[1]) for line in lines if line.startswith("preparation=")]
    if not prep or not all(p["state_preserved"] for p in prep):
        raise ValueError(f"Preparation changed authority: {path}")
    row = dict(log=path.name, frames=len(samples), identity=sorted(identity), digests=digests,
               commands=[s for s in samples if s["phase"] == "command"],
               over=[s for s in samples if s["over_budget"]], preparation=prep)
    results.append(row)
    print(path.name, "901 frames, 6 state/event digests PASS; nonspawn OVER:",
          [(s["scenario"], s["count"], s["run"], s["tick"], round(s["elapsed_ms"], 3))
           for s in row["over"] if s["phase"] != "spawn"])
(root / "probe-summary.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
print(f"Verified {len(results)} cold processes, {len(results) * 901} frames, {len(results) * 6} digests")
