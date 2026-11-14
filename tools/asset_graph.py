"""Declared content dependencies plus local imports of model recipes; no execution of generators."""
import ast
from collections import deque
from pathlib import Path


class AssetGraph:
    def __init__(self, nodes, root):
        self.root = Path(root).resolve(); self.nodes = {}
        for node in nodes:
            if node["id"] in self.nodes: raise ValueError("DEPENDENCY_DUPLICATE_ID: " + node["id"])
            self.nodes[node["id"]] = node
        for node in nodes:
            for dependency in node.get("dependsOn", []):
                if dependency not in self.nodes: raise ValueError("DEPENDENCY_UNKNOWN: " + dependency)
            for source in node.get("sources", []): self.path(source["path"])
        # Cycles are errors in the declared asset graph, not silently truncated traversal.
        done, active = set(), set()
        def visit(identity):
            if identity in active: raise ValueError("DEPENDENCY_CYCLE: " + identity)
            if identity in done: return
            active.add(identity)
            for dependency in self.nodes[identity].get("dependsOn", []): visit(dependency)
            active.remove(identity); done.add(identity)
        for identity in self.nodes: visit(identity)

    def path(self, path):
        resolved = (self.root / path).resolve()
        if not resolved.is_relative_to(self.root): raise ValueError("DEPENDENCY_OUTSIDE_ROOT: " + str(path))
        return resolved

    def affected(self, path, pointer=None):
        if pointer is not None and pointer and not pointer.startswith("/"): raise ValueError("DEPENDENCY_POINTER_INVALID")
        path = self.path(path); found = {}; pending = deque()
        for identity, node in self.nodes.items():
            for source in node.get("sources", []):
                section = source.get("pointer", "")
                intersects = pointer is None or not section or pointer == section or pointer.startswith(section + "/") or section.startswith(pointer + "/")
                if self.path(source["path"]) == path and intersects:
                    found[identity] = {"id": identity, "type": node["type"], "name": node["name"], "via": [identity], "source": source}
                    pending.append(identity); break
        while pending:
            dependency = pending.popleft()
            for identity, node in self.nodes.items():
                if identity not in found and dependency in node.get("dependsOn", []):
                    found[identity] = {"id": identity, "type": node["type"], "name": node["name"],
                        "via": [*found[dependency]["via"], identity], "source": found[dependency]["source"]}
                    pending.append(identity)
        return sorted(found.values(), key=lambda item: (item["type"], item["id"]))


def recipe_sources(path, root):
    """Local recipe imports are resolved without importing bpy or running user code. Dispatcher registry is not expanded."""
    root = Path(root).resolve(); start = (root / path).resolve(); result = {}; pending = [(start, [])]
    if not start.is_relative_to(root): raise ValueError("DEPENDENCY_OUTSIDE_ROOT: " + str(path))
    while pending:
        current, via = pending.pop()
        if current in result or not current.is_relative_to(root): continue
        result[current] = via
        if current.suffix != ".py" or not current.is_file(): continue
        try: tree = ast.parse(current.read_text(encoding="utf-8-sig"), filename=str(current))
        except SyntaxError as error: raise ValueError("DEPENDENCY_SYNTAX: " + str(current) + ": " + str(error)) from error
        for node in ast.walk(tree):
            names = [alias.name for alias in node.names] if isinstance(node, ast.Import) else []
            if isinstance(node, ast.ImportFrom):
                if node.module: names.append(node.module)
                names.extend((node.module + "." if node.module else "") + alias.name for alias in node.names if alias.name != "*")
            for name in names:
                relative = Path(*name.split("."))
                base = current.parent
                if isinstance(node, ast.ImportFrom) and node.level:
                    for _ in range(node.level - 1): base = base.parent
                for search in (base, root / "tools"):
                    candidates = [search / relative.with_suffix(".py"), search / relative / "__init__.py"]
                    match = next((p.resolve() for p in candidates if p.is_file() and p.resolve().is_relative_to(root)), None)
                    if match:
                        pending.append((match, [*via, str(current.relative_to(root)).replace("\\", "/")])); break
    return [{"path": str(p.relative_to(root)).replace("\\", "/"), "reason": "recipe" if p == start else "local recipe import", "importVia": via}
            for p, via in sorted(result.items())]
