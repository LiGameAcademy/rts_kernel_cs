"""Add candidate access observations only to the existing disposable diagnostic copy."""
from pathlib import Path
import shutil

HERE = Path(__file__).resolve().parent


def replace_once(text, before, after):
    if text.count(before) != 1:
        raise ValueError(f"Candidate observation marker changed: {before[:100]}")
    return text.replace(before, after)


def add_fixture(destination):
    shutil.copy2(HERE / "CandidateUsageProbe.cs.source",
                 destination / "tests/Rts.Kernel.Tests/CandidateUsageProbe.cs")
    path = destination / "tests/Rts.Kernel.Tests/Program.cs"
    text = path.read_text(encoding="utf-8-sig")
    text = replace_once(text, "    PreparedBoundaryChecks.Run(Check);",
                        "    PreparedBoundaryChecks.Run(Check);\n    CandidateUsageProbe.Run(Check);")
    path.write_text(text, encoding="utf-8")
    path = destination / "tests/Rts.Kernel.Tests/AtomicSmallGroupChecks.cs"
    text = path.read_text(encoding="utf-8-sig")
    marker = '            "every adjusted tree-edge goal fits the complete body");'
    trace = '''
        var candidateTrace = match.ComputeStateHash() + "\\n" + JsonSerializer.Serialize(events)
            + "\\n" + edge.ComputeStateHash() + "\\n" + JsonSerializer.Serialize(outcomes);
        var candidateDigest = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(candidateTrace));
        Console.WriteLine("candidate_atomic=" + Convert.ToHexString(candidateDigest));'''
    text = replace_once(text, marker, marker + trace)
    path.write_text(text, encoding="utf-8")


def instrument(destination):
    kernel = destination / "src/Rts.Kernel"
    shutil.copy2(HERE / "CandidateUsage.cs.source", kernel / "CandidateUsage.cs")
    path = kernel / "ProfileCounters.cs"
    text = path.read_text(encoding="utf-8-sig")
    for before, after in [
        ("        foreach (var stats in Totals)", "        CandidateUsage.Reset();\n        foreach (var stats in Totals)"),
        ("        _enabled = true;", "        _enabled = true;\n        CandidateUsage.Enabled = true;"),
        ("        _enabled = false;", "        _enabled = false;\n        CandidateUsage.Enabled = false;"),
        ("        for (var phase = 0; phase < 2; phase++)", "        CandidateUsage.Report(count, run);\n        for (var phase = 0; phase < 2; phase++)")
    ]:
        text = replace_once(text, before, after)
    path.write_text(text, encoding="utf-8")
    path = kernel / "Navigation/PlacementCandidates.cs"
    text = path.read_text(encoding="utf-8-sig")
    for before, after in [
        ("    public int Count { get; }", "    internal int UsageId { get; }\n    public int Count { get; }"),
        ("        for (var y = top; y <= bottom; y++) Enqueue(y, 0);",
         "        for (var y = top; y <= bottom; y++) Enqueue(y, 0);\n        UsageId = CandidateUsage.Register(Count, bottom - top + 1, _columns.Length);"),
        ("            while (_ordered.Count <= index)", "            var cachedPrefix = _ordered.Count;\n            while (_ordered.Count <= index)"),
        ("            return _ordered[index];", "            CandidateUsage.Read(UsageId, index, cachedPrefix, _ordered.Count);\n            return _ordered[index];"),
        ("        for (var i = 0; i < Count; i++) yield return this[i];",
         "        CandidateUsage.Enumerate(UsageId);\n        for (var i = 0; i < Count; i++) yield return this[i];")
    ]:
        text = replace_once(text, before, after)
    path.write_text(text, encoding="utf-8")
    path = kernel / "Navigation/GroupPlacementPlanner.cs"
    text = path.read_text(encoding="utf-8-sig")
    before = """                    foreach (var cell in candidates[slot] ??= Candidates(navigation, ideals[slot]))
                    {"""
    after = """                {
                    var candidateList = candidates[slot] ??= Candidates(navigation, ideals[slot]);
                    var candidateAttempt = CandidateUsage.Begin(candidateList is PlacementCandidates lazy ? lazy.UsageId : 0,
                        member.Id.Value, slot, candidateList.Count);
                    foreach (var cell in candidateList)
                    {
                        candidateAttempt.Visit();"""
    text = replace_once(text, before, after)
    text = replace_once(text, "                        goal = center;",
                        "                        candidateAttempt.Accept();\n                        goal = center;")
    text = replace_once(text, "                if (goal is null) failed.Add(member.Id);",
                        "                }\n                if (goal is null) failed.Add(member.Id);")
    path.write_text(text, encoding="utf-8")
    path = destination / "tests/Rts.Kernel.Tests/CandidateUsageProbe.cs"
    text = path.read_text(encoding="utf-8-sig")
    replacements = {
        "// CANDIDATE_SCENARIO_RESET": "ProfileCounters.Reset();",
        "// CANDIDATE_SCENARIO_BEGIN": "ProfileCounters.BeginTick(commandFrame);",
        "// CANDIDATE_SCENARIO_END": "ProfileCounters.EndTick();",
        "// CANDIDATE_SCENARIO_REPORT": "ProfileCounters.Report(24, run);",
        "// CANDIDATE_ATOMIC_BEGIN": "CandidateUsage.SelfCheck(check);\n        ProfileCounters.Reset();\n        ProfileCounters.BeginTick(true);",
        "// CANDIDATE_ATOMIC_END": "ProfileCounters.EndTick();\n        ProfileCounters.Report(24, 2);"
    }
    for before, after in replacements.items():
        text = replace_once(text, before, after)
    path.write_text(text, encoding="utf-8")
