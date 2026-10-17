"""Export matched WPR profiles; only stack switches may differ between B and C."""
import copy
import hashlib
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

source, destination = map(Path, sys.argv[1:])
destination.mkdir(parents=True, exist_ok=True)
root = ET.parse(source).getroot()
profile = root.find("Profiles/Profile")
profile.set("Id", "RtsOverhead.Verbose.Memory")
profile.set("Name", "RtsOverhead")
profile.set("Description", "Matched RTS command overhead diagnostic")
keywords = root.find("Profiles/SystemProvider/Keywords")
for keyword in ["DPC", "Interrupt"]:
    if not any(node.get("Value") == keyword for node in keywords):
        ET.SubElement(keywords, "Keyword", Value=keyword)

def remove_stacks(tree):
    for node in tree.iter():
        node.attrib.pop("Stack", None)
        for child in list(node):
            if child.tag == "Stacks":
                node.remove(child)

no_stacks = copy.deepcopy(root)
remove_stacks(no_stacks)
check = copy.deepcopy(root)
remove_stacks(check)
if ET.tostring(check) != ET.tostring(no_stacks):
    raise ValueError("Profiles differ beyond stack collection")
paths = {condition: destination / (condition + ".wprp") for condition in ["B", "C"]}
manifest_path = destination / "profile-verification.json"
for path in [*paths.values(), manifest_path]:
    if path.exists():
        raise FileExistsError(path)
for condition, tree in [("B", no_stacks), ("C", root)]:
    path = paths[condition]
    ET.indent(tree)
    ET.ElementTree(tree).write(path, encoding="utf-8", xml_declaration=True)
def shape(node):
    return (node.tag, tuple(sorted(node.attrib.items())), (node.text or "").strip(),
            tuple(shape(child) for child in node))

actual = {k: ET.parse(path).getroot() for k, path in paths.items()}
counts = {k: len(tree.findall(".//Stack")) + sum("Stack" in node.attrib for node in tree.iter())
          for k, tree in actual.items()}
for tree in actual.values():
    remove_stacks(tree)
if shape(actual["B"]) != shape(actual["C"]) or counts["B"] != 0 or counts["C"] == 0:
    raise ValueError("Serialized profiles do not differ solely by stack switches")
manifest_path.write_text(json.dumps(dict(
    otherwise_identical=True, stack_switches=counts,
    comparison="XML semantic tree, indentation whitespace excluded",
    source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),
    profiles={k: hashlib.sha256(path.read_bytes()).hexdigest() for k, path in paths.items()}
), indent=2), encoding="utf-8")
print("Matched B/C profiles generated and verified; only stack switches differ; DPC/ISR shared.")
