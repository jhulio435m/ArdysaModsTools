#!/usr/bin/env python3
"""Structural checks only: does not restore packages, invoke compilers, or execute the app."""
from pathlib import Path
import json
import re
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
checks = 0

def require(condition, message):
    global checks
    if not condition:
        raise SystemExit(message)
    checks += 1

for project in (ROOT / "Linux").rglob("*.csproj"):
    xml = ET.parse(project).getroot()
    require(xml.findtext("PropertyGroup/TargetFramework") == "net8.0", f"Unexpected target: {project}")
    for item in xml.iter():
        if item.tag in ("ProjectReference", "Compile") and "Include" in item.attrib:
            require((project.parent / item.attrib["Include"]).is_file(), f"Missing reference in {project}: {item.attrib}")

windows = ET.parse(ROOT / "ArdysaModsTools.csproj").getroot()
require(any(item.get("Remove") == "Linux\\**" for item in windows.iter("Compile")), "Windows source glob must exclude Linux.")
for path in (ROOT / "Frontend/src/pages/linux").glob("*.tsx"):
    source = path.read_text()
    for name in re.findall(r'(?:from\s+|import\s+)["\'](\.[^"\']+)["\']', source):
        target = path.parent / name
        require(any(Path(str(target) + suffix).is_file() for suffix in ("", ".ts", ".tsx", ".css")), f"Missing frontend import: {name}")
css = ROOT / "Frontend/src/pages/linux/linux.css"
for ref in re.findall(r'url\("([^"]+)"\)', css.read_text()):
    require((css.parent / ref).is_file(), f"Missing UI asset: {ref}")
json.loads((ROOT / "Frontend/package.json").read_text())
json.loads((ROOT / "Linux/recipes/example.json").read_text())
for script in (ROOT / "Linux/scripts").glob("*.sh"):
    subprocess.run(["bash", "-n", str(script)], check=True)
tracked = subprocess.check_output(["git", "ls-files", "-z", "Linux"], cwd=ROOT).decode().split("\0")
for name in filter(None, tracked):
    require(Path(name).suffix not in (".dll", ".exe"), f"Unexpected tracked compiled artifact: {name}")
require(not any(name.startswith("Linux/Host/wwwroot/") for name in tracked), "Generated frontend artifacts must not be committed.")
subprocess.run(["git", "-c", "core.whitespace=blank-at-eol,blank-at-eof,space-before-tab,cr-at-eol", "diff", "--check"], cwd=ROOT, check=True)
print(f"PASS: {checks} structural checks; shell syntax, JSON, XML, references, assets and diff whitespace. No compilation or runtime verification.")
