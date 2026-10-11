#!/usr/bin/env python3
"""Parse source syntax with prebuilt Tree-sitter grammars; never invokes a compiler."""
from pathlib import Path
from tree_sitter import Language, Parser
import tree_sitter_c_sharp
import tree_sitter_typescript

root = Path(__file__).resolve().parents[2]
parsers = {
    ".cs": Parser(Language(tree_sitter_c_sharp.language())),
    ".ts": Parser(Language(tree_sitter_typescript.language_typescript())),
    ".tsx": Parser(Language(tree_sitter_typescript.language_tsx())),
}
files = list((root / "Linux").rglob("*.cs"))
files += list((root / "Frontend/src/pages/linux").glob("*.ts"))
files += list((root / "Frontend/src/pages/linux").glob("*.tsx"))
errors = []
for path in files:
    tree = parsers[path.suffix].parse(path.read_bytes())
    nodes = [tree.root_node]
    while nodes:
        node = nodes.pop()
        if node.type == "ERROR" or node.is_missing:
            errors.append(f"{path.relative_to(root)}:{node.start_point.row + 1}: {node.type}")
        nodes.extend(node.children)
if errors:
    raise SystemExit("\n".join(errors))
print(f"PASS: syntax parsed for {len(files)} C#/TS/TSX files. No typechecking, compilation or runtime execution.")
