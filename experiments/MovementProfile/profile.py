"""Run reproducible inclusive phase observations in a fresh temporary source copy."""
import argparse
import hashlib
import json
import platform
from pathlib import Path
import re
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]
SCOPES = [
    ("RtsMatch.cs", "Step", "Step"),
    ("RtsMatch.cs", "ExecuteCommandsForCurrentFrame", "Commands"),
    ("RtsMatchOrders.cs", "ActivateQueuedMoves", "Queued"),
    ("RtsMatchGroups.cs", "PrepareGroupMembers", "Group.Members"),
    ("RtsMatchGroups.cs", "GroupBodies", "Group.Bodies"),
    ("RtsMatchGroups.cs", "SubmitGroupGoals", "Group.Commit"),
    ("RtsMatchSmallGroupCommit.cs", "SubmitSmallGroupGoals", "Group.AtomicCommit"),
    ("Navigation/GroupPlacementPlanner.cs", "Plan", "Group.Layout"),
    ("Navigation/GroupPlacementPlanner.cs", "PlanAssigned", "Group.Place"),
    ("Navigation/SmallGroupAssignment.cs", "Assign", "Group.Assign"),
    ("Navigation/SmallGroupAssignment.cs", "Relax", "Group.Relax"),
    ("Navigation/PlacementCandidates.cs", "PlacementCandidates", "Group.Candidates"),
    ("Navigation/MovePathPlanner.cs", "TryPlan", "Path.Plan"),
    ("CrowdStepper.cs", "Advance", "Crowd"),
    ("CrowdStepper.cs", "Prepare", "Crowd.Prepare"),
    ("CrowdStepper.cs", "TryAlternative", "Crowd.Alternative"),
    ("Navigation/CrowdMotionResolver.cs", "Resolve", "Crowd.Resolve"),
    ("GroupYieldCoordinator.cs", "CompleteSettled", "Crowd.Settle"),
    ("LocalYieldPlanner.cs", "Plan", "Crowd.YieldPlan"),
    ("Navigation/MovementStep.cs", "ComputeTimed", "Motion.Compute"),
    ("Navigation/LocalDetour.cs", "Find", "Local.Detour"),
    ("Navigation/SoftGoalPath.cs", "Find", "Local.SoftGoal"),
    ("Navigation/NavigationComponents.cs", "Build", "Navigation.Build"),
    ("Navigation/CrowdReservations.cs", "FirstMotionConflict", "Collision.Query"),
    ("Navigation/TimedCrowdGeometry.cs", "Overlap", "Collision.Timed"),
    ("Navigation/TimedCrowdGeometry.cs", "SegmentOverlap", "Collision.Segment"),
]


def command(args, cwd, log):
    result = subprocess.run(args, cwd=cwd, stdout=subprocess.PIPE,
                            stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace", timeout=300)
    log.write(result.stdout)
    log.flush()
    if result.returncode:
        raise RuntimeError(f"Command failed ({result.returncode}): {args}; see {log.name}")
    return result.stdout


def copy_sources(destination):
    tracked = subprocess.check_output(["git", "ls-files", "-z"], cwd=ROOT).decode().split("\0")
    for relative in filter(None, tracked):
        source = ROOT / relative
        if not source.is_file():
            continue
        target = destination / relative
        if not target.resolve().is_relative_to(destination):
            raise ValueError(f"Outside diagnostic directory: {relative}")
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)


def instrument(destination, scopes):
    for relative, method, name in scopes:
        path = destination / "src/Rts.Kernel" / relative
        text = path.read_text(encoding="utf-8-sig")
        pattern = (r"(?m)^    (?:public|private|internal)[^\n]*\b" + re.escape(method)
                   + r"\([^;{}]*?\)\s*\n    \{")
        matches = list(re.finditer(pattern, text))
        if len(matches) != 1:
            raise ValueError(f"Expected one block method: {relative}:{method}, got {len(matches)}")
        position = matches[0].end()
        text = text[:position] + (
            '\n        using var observation = global::Rts.Kernel.ProfileCounters.Enter("'
            + name + '");') + text[position:]
        path.write_text(text, encoding="utf-8")
    helper = destination / "src/Rts.Kernel/ProfileCounters.cs"
    shutil.copy2(ROOT / "experiments/MovementProfile/ProfileCounters.cs.source", helper)
    path = destination / "tests/Rts.Kernel.Tests/MultiPartyCloseoutChecks.cs"
    text = path.read_text(encoding="utf-8-sig")
    replacements = {
        "        var match = new RtsMatch": "        ProfileCounters.Reset();\n        var match = new RtsMatch",
        "            var before = GC.GetAllocatedBytesForCurrentThread();":
            "            ProfileCounters.BeginTick(frame == 0);\n            var before = GC.GetAllocatedBytesForCurrentThread();",
        "            var end = Stopwatch.GetTimestamp();":
            "            var end = Stopwatch.GetTimestamp();\n            ProfileCounters.EndTick();",
        "        samples.Sort();": "        ProfileCounters.Report(count, run);\n        samples.Sort();",
    }
    for before, after in replacements.items():
        if text.count(before) != 1:
            raise ValueError(f"Probe marker changed: {before}")
        text = text.replace(before, after)
    path.write_text(text, encoding="utf-8")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--without-replay", action="store_true")
    parser.add_argument("--prepared", action="store_true", help="Use the existing explicit preparation acceptance entry")
    parser.add_argument("--commands-only", action="store_true", help="Observe command stages without per-collision probes")
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    destination = Path(tempfile.mkdtemp(prefix="rts-movement-profile-")).resolve()
    metadata = {
        "source_commit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT).decode().strip(),
        "source_directory": str(destination), "without_replay": args.without_replay,
        "prepared": args.prepared, "commands_only": args.commands_only,
        "platform": platform.platform(), "python": platform.python_version(),
        "helper_sha256": hashlib.sha256((ROOT / "experiments/MovementProfile/ProfileCounters.cs.source").read_bytes()).hexdigest(),
    }
    scopes = [scope for scope in SCOPES if not args.commands_only or scope[2].startswith("Group.")
              or scope[2] in {"Step", "Commands", "Path.Plan"}]
    metadata["scopes"] = scopes
    metadata["tool_sha256"] = hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
    (output / "metadata.json").write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
    copy_sources(destination)
    manifest = {str(path.relative_to(destination)): hashlib.sha256(path.read_bytes()).hexdigest()
                for path in sorted(destination.rglob("*")) if path.is_file()}
    (output / "source-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    if args.without_replay:
        probe = destination / "tests/Rts.Kernel.Tests/MultiPartyCloseoutChecks.cs"
        text = probe.read_text(encoding="utf-8-sig")
        if text.count("if (frame is 0 or 30)") != 1:
            raise ValueError("Replay marker changed")
        probe.write_text(text.replace("if (frame is 0 or 30)", "if (frame < 0)"), encoding="utf-8")
    # Build and observe an unmodified copy first, then add diagnostics only here.
    project = "tests/Rts.Kernel.Tests"
    probe = str(destination / project / "bin/Release/net10.0/Rts.Kernel.Tests.dll")
    probe_args = ["--prepared-multi-party" if args.prepared else "--multi-party"]
    with (output / "baseline.log").open("w", encoding="utf-8") as log:
        command(["dotnet", "build", project, "-c", "Release", "-m:1", "-p:NuGetAudit=false"], destination, log)
        baseline = command(["dotnet", probe, *probe_args], destination, log)
    instrument(destination, scopes)
    with (output / "profile.log").open("w", encoding="utf-8") as log:
        command(["dotnet", "build", project, "-c", "Release", "--no-restore", "-m:1"], destination, log)
        measured = command(["dotnet", probe, *probe_args], destination, log)
    digests = lambda text: re.findall(r"multi_party count=(\d+) .*?run=(\d+).*?trace=([A-F0-9]+) hash=([a-f0-9]+)", text)
    if digests(baseline) != digests(measured) or len(digests(baseline)) != 6:
        raise RuntimeError("Diagnostic copy changed observed outcomes")
    records = [json.loads(line.removeprefix("profile="))
               for line in measured.splitlines() if line.startswith("profile=")]
    (output / "profile.json").write_text(json.dumps(records, indent=2) + "\n", encoding="utf-8")
    print(f"PROFILE_COMPLETE source={destination} output={output} baseline_profile_traces_and_hashes=6/6")


if __name__ == "__main__":
    main()
