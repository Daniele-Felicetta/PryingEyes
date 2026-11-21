"""Census of the game's C# sources: how big each part is, who references whom, which flags the game reads, where the tests live.

Read-only, standard library only. It is the measuring instrument behind docs/engineering/architecture-audit.md: the numbers on
that page are this tool's output, so they can be taken again after every step of docs/engineering/modular-roadmap.md.

    python tools/code_census.py                    every section; the tests and the root Scripts folder are split into groups
    python tools/code_census.py --by-folder        the same with plain folders as groups
    python tools/code_census.py --section edges    one section: sizes, edges, cycles, partials, fan-in, pure, flags, tests, statics
    python tools/code_census.py --json out.json    the whole result as data

How it reads the code: comments, strings and characters are blanked, the types declared at namespace level are collected, and a
file references a type when the type's name appears as an identifier that is not a member access (`x.Name`, unless `x` is a
namespace), not the member of an enum, not the name of something being declared (`int Name;`) and not a type nested in that
same file. An assembly that cannot see another one (a package, or an assembly definition that does not reference it) never
references it. This is name matching, not a compiler: two types with one name are merged, and a use it cannot tell from a
type (`Name` alone as a value) counts as a reference. Good enough to rank coupling and to see it move; not a proof that a
dependency is absent.
"""
import argparse
import collections
import json
from pathlib import Path
import re
import sys
from xml.etree import ElementTree

ROOT = Path(__file__).resolve().parents[1]
GAME = "unity/PryingEyes/Assets/_PryingEyes"
PACKAGES = "unity/PryingEyes/Packages"
SECTIONS = ("sizes", "edges", "cycles", "partials", "fan-in", "pure", "flags", "tests", "statics")

DECLARATION = re.compile(r"\b(class|struct|enum|interface)\s+([A-Za-z_]\w*)")
NAMESPACE = re.compile(r"^\s*namespace\s+([\w.]+)\s*(;?)", re.M)
TOKEN = re.compile(r"[A-Za-z_]\w*|\.")
ENUM_BODY = re.compile(r"\benum\s+\w+[^{;]*\{[^}]*\}")
BEFORE_A_TYPE = frozenset("as is new return typeof case in out ref throw else await yield".split())      # words a type can follow
DECLARED_NAME_END = frozenset(";=,){")       # what follows the name of a field, property, parameter or local being declared
ENGINE =re.compile(r"\bUnity(Engine|Editor)\b")
FLAG = re.compile(r'"(-[a-z][a-z0-9]{2,})"')
ARGUMENT_HELPER = re.compile(r"\bstatic\s+(?:bool|string|int|float)\s+(?:HasArg|Arg|ArgInt|ArgStr|ArgFloat|IntArg|TestArg|Flag)\s*\(")
STATIC_FIELD = re.compile(r"^\s*(?:(?:public|private|internal|protected)\s+)?static\s+(?!readonly\b|void\b|class\b|partial\b|event\b|extern\b)"
                          r"[A-Za-z_][\w<>,\[\]\.? ]*?\s+[A-Za-z_]\w*\s*(?:=(?!=|>)|;|,)")
TEST_GROUP = "(tests)"
# A reading aid for the root Scripts folder, which holds a third of the files: a file goes to the group that lists the first
# part of its name. It is this tool's own grouping, not a structure of the project; edit it when files move.
ROOT_GROUPS = (
    ("Scripts:enemy", "Enemy EnemyFlinch EnemyMats EnemyRagdoll EnemyTemi AlienRig EyeRig VigilRig CrocAnim RelicRig ToxicCloud CorpseSink "
                      "WatchingEyes BodyLodLibrary PlayerAvoidance"),
    ("Scripts:weapon", "Weapon ArmRig TriggerFinger Knife Mine Equipment WallBuy Magic Shockwave"),
    ("Scripts:player", "PlayerController PlayerPerks Perks PerkIcons TouchControls PadControls Punti Medals Prove"),
    ("Scripts:ui", "GameMenu MenuEngraving MenuWidgets HUD GameSettings VideoSettings PadMenuInput FpsCounter"),
    ("Scripts:audio", "SoundGen SoundImpatti SoundKnife SoundLampadari SoundLampade SoundMedaglie SoundMenu SoundPerks SoundPower SoundRelics "
                      "SoundTemi SoundVarianti Synth Sfx Ambience MusicDirector MenuMusic MenuSound"),
    ("Scripts:city", "CityAccess CityDirector CityRoom CitySign CityWaves"),
    ("Scripts:core", "GameManager Elevator HotelNav TestPaths Flicker ProcTextures"),
)


def blank(code):
    """The code with comments, strings and characters replaced by spaces (same length, same line breaks)."""
    out, i, n = [], 0, len(code)

    def spaces(text):
        return "".join(ch if ch == "\n" else " " for ch in text)

    while i < n:
        c, two = code[i], code[i:i + 2]
        if two == "//":
            j = code.find("\n", i)
            j = n if j < 0 else j
        elif two == "/*":
            j = code.find("*/", i + 2)
            j = n if j < 0 else j + 2
        elif c == '"' or two in ('@"', '$"') or code[i:i + 3] in ('$@"', '@$"'):
            j = code.index('"', i)
            verbatim = "@" in code[i:j]
            j += 1
            while j < n:
                if verbatim:
                    if code[j] == '"':
                        if code[j + 1:j + 2] != '"': break
                        j += 1
                elif code[j] == "\\": j += 1
                elif code[j] in '"\n': break
                j += 1
            j = min(j + 1, n)
        elif c == "'":
            j = i + 1
            while j < n and code[j] not in "'\n": j += 2 if code[j] == "\\" else 1
            j = min(j + 1, n)
        else:
            out.append(c); i += 1
            continue
        out.append(spaces(code[i:j])); i = j
    return "".join(out)


def is_test(rel):
    name = Path(rel).stem
    return "Test" in name and not name.startswith("TestPaths")


def is_editor(rel):
    return "Editor" in Path(rel).parts[:-1]


def group_of(rel, by_folder):
    folder = str(Path(rel).parent).replace("\\", "/")
    if by_folder or is_editor(rel) or rel.startswith("Packages/"): return folder
    if is_test(rel): return TEST_GROUP
    if folder != "Scripts": return folder
    head = Path(rel).stem.split(".")[0]
    return next((group for group, heads in ROOT_GROUPS if head in heads.split()), "Scripts:other")


def read_sources(root):
    """Every C# file of the game and of its packages: text, blanked code, assembly."""
    files, assemblies = {}, {}
    for base, prefix in ((root / GAME, ""), (root / PACKAGES, "Packages/")):
        definitions = {}
        for path in sorted(base.rglob("*.asmdef")) if base.is_dir() else []:
            data = json.loads(path.read_text(encoding="utf-8-sig"))
            definitions[path.parent] = data["name"]
            assemblies[data["name"]] = {"references": list(data.get("references") or []), "engine": not data.get("noEngineReferences", False)}
        for path in sorted(base.rglob("*.cs")) if base.is_dir() else []:
            rel = prefix + str(path.relative_to(base)).replace("\\", "/")
            owner = next((definitions[parent] for parent in path.parents if parent in definitions), None)
            text = path.read_text(encoding="utf-8-sig", errors="replace")
            files[rel] = {"text": text, "code": blank(text), "lines": text.count("\n") + (0 if text.endswith("\n") or not text else 1),
                          "assembly": owner or ("Assembly-CSharp-Editor" if is_editor(rel) else "Assembly-CSharp")}
    return files, assemblies


def can_see(source, target, assemblies):
    """Whether code of one assembly can name a type of another (Unity's rules for predefined and defined assemblies)."""
    if source == target: return True
    if source in assemblies: return target in assemblies[source]["references"]
    if target == "Assembly-CSharp-Editor": return False
    return True       # the predefined assemblies see every auto-referenced definition, and the editor one sees the player one


def compiled_by_labs(root):
    """Which lab under tools/ compiles which source file (the same list the boundary guard reads)."""
    result = collections.defaultdict(set)
    for project in sorted((root / "tools").rglob("*.csproj")) if (root / "tools").is_dir() else []:
        if any(part in ("bin", "obj") for part in project.parts): continue
        try: tree = ElementTree.parse(project)
        except ElementTree.ParseError: continue
        for item in tree.iter("Compile"):
            include = (item.get("Include") or "").replace("\\", "/")
            if not include or "$(" in include: continue
            folder, _, pattern = include.rpartition("/")
            target = (project.parent / folder).resolve()
            matches = sorted(target.glob(pattern)) if "*" in pattern and "**" not in include and target.is_dir() else [target / pattern]
            for match in matches: result[match.resolve()].add(project.relative_to(root / "tools").parts[0])
    return result


def census(root=ROOT, by_folder=False):
    root = Path(root).resolve()
    files, assemblies = read_sources(root)
    namespaces = {"System"}
    homes = collections.defaultdict(list)            # type name -> files that declare it at namespace level
    partial = set()
    for rel, info in files.items():
        code = info["code"]
        spaces = NAMESPACE.findall(code)
        for name, _ in spaces: namespaces.update(name.split("."))
        top = 1 if spaces and not any(mark for _, mark in spaces) else 0      # a file-scoped namespace adds no braces
        depth, depths = 0, []
        for ch in code:
            depths.append(depth)
            depth += ch == "{"; depth -= ch == "}"
        info["declared"], info["types"] = set(), set()
        for match in DECLARATION.finditer(code):
            line = code[code.rfind("\n", 0, match.start()) + 1:match.start()]
            if line.rstrip().endswith((":", ",")): continue          # a generic constraint: "where T : class"
            info["declared"].add(match[2])
            if depths[match.start()] != top: continue
            info["types"].add(match[2]); homes[match[2]].append(rel)
            if "partial" in line.split(): partial.add(match[2])
        info["group"] = group_of(rel, by_folder)
        info["engine"] = bool(ENGINE.search(code))

    def home_of(name):
        named = [rel for rel in homes[name] if Path(rel).name == name + ".cs"]
        return named[0] if named else sorted(homes[name], key=lambda rel: (-files[rel]["lines"], rel))[0]

    home = {name: home_of(name) for name in homes}
    group = {name: files[home[name]]["group"] for name in homes}
    edges = collections.defaultdict(lambda: {"types": set(), "files": set()})
    users = collections.defaultdict(set)             # type name -> files that name it and do not declare it
    for rel, info in files.items():
        code = ENUM_BODY.sub(lambda match: " " * len(match[0]), info["code"])      # the members of an enum are values, not types
        used, tokens = set(), list(TOKEN.finditer(code))
        for position, match in enumerate(tokens):
            if match[0] not in home: continue
            before = tokens[position - 1] if position else None
            if before is not None and before[0] == ".":
                if position < 2 or tokens[position - 2][0] not in namespaces: continue      # `x.Name`: a member, unless x is a namespace
            elif before is not None and before[0] not in BEFORE_A_TYPE and not code[before.end():match.start()].strip():
                if code[match.end():match.end() + 80].lstrip()[:1] in DECLARED_NAME_END: continue      # `int Name;`: something called Name
            used.add(match[0])
        info["uses"] = set()
        for name in used:
            if home[name] == rel or (name in info["declared"] and name not in info["types"]): continue
            if not can_see(info["assembly"], files[home[name]]["assembly"], assemblies): continue
            info["uses"].add(name)
            if rel not in homes[name]: users[name].add(rel)
            if group[name] == info["group"]: continue
            edge = edges[(info["group"], group[name])]
            edge["types"].add(name); edge["files"].add(rel)

    groups = collections.defaultdict(lambda: {"files": 0, "lines": 0, "types": 0, "engineFree": 0, "engineFreeLines": 0})
    sizes = collections.defaultdict(lambda: {"files": 0, "lines": 0})
    for rel, info in files.items():
        for row in (groups[info["group"]], sizes[info["assembly"]]):
            row["files"] += 1; row["lines"] += info["lines"]
        if not info["engine"]:
            groups[info["group"]]["engineFree"] += 1; groups[info["group"]]["engineFreeLines"] += info["lines"]
    for name in home: groups[group[name]]["types"] += 1

    graph = collections.defaultdict(set)
    for source, target in edges: graph[source].add(target)
    labs = compiled_by_labs(root)
    game = {rel: info for rel, info in files.items() if not rel.startswith("Packages/")}
    result = {
        "schemaVersion": 1, "root": GAME, "byFolder": by_folder,
        "assemblies": {name: {**row, "defined": name in assemblies, "engine": assemblies.get(name, {"engine": True})["engine"]}
                       for name, row in sorted(sizes.items())},
        "groups": dict(sorted(groups.items(), key=lambda item: -item[1]["lines"])),
        "edges": [{"from": source, "to": target, "types": sorted(edge["types"]), "files": len(edge["files"])}
                  for (source, target), edge in sorted(edges.items(), key=lambda item: (item[0][0], -len(item[1]["types"])))],
        "cycles": [sorted(component) for component in components(graph, list(groups)) if len(component) > 1],
        "partials": partials(partial, homes, files, group),
        "fanIn": [{"type": name, "group": group[name], "files": len(named)} for name, named in sorted(users.items(), key=lambda item: (-len(item[1]), item[0]))],
        "pure": [{"file": rel, "lines": info["lines"], "assembly": info["assembly"], "labs": sorted(labs.get((root / GAME / rel).resolve(), ())),
                  "engineBoundTypes": sorted(name for name in info["uses"] if files[home[name]]["engine"])}
                 for rel, info in sorted(game.items()) if not info["engine"]],
        "flags": flags(game), "tests": tests(game, home), "statics": statics(game),
    }
    result["mutual"] = sorted({tuple(sorted(pair)) for pair in edges if (pair[1], pair[0]) in edges})
    return result


def components(graph, nodes):
    """Strongly connected components (Tarjan), iteratively: groups that all depend on each other, directly or not."""
    index, low, on_stack, stack, found = {}, {}, set(), [], []
    for start in nodes:
        if start in index: continue
        work = [(start, iter(sorted(graph[start])))]
        index[start] = low[start] = len(index); stack.append(start); on_stack.add(start)
        while work:
            node, successors = work[-1]
            advanced = False
            for successor in successors:
                if successor not in index:
                    index[successor] = low[successor] = len(index); stack.append(successor); on_stack.add(successor)
                    work.append((successor, iter(sorted(graph[successor])))); advanced = True
                    break
                if successor in on_stack: low[node] = min(low[node], index[successor])
            if advanced: continue
            work.pop()
            if work: low[work[-1][0]] = min(low[work[-1][0]], low[node])
            if low[node] == index[node]:
                component = []
                while True:
                    member = stack.pop(); on_stack.discard(member); component.append(member)
                    if member == node: break
                found.append(component)
    return found


def partials(partial, homes, files, group):
    rows = []
    for name in sorted(partial):
        parts = homes[name]
        if len(parts) < 2: continue
        spread = collections.Counter(files[rel]["group"] for rel in parts)
        rows.append({"type": name, "home": group[name], "files": len(parts), "lines": sum(files[rel]["lines"] for rel in parts),
                     "groups": dict(spread.most_common())})
    return sorted(rows, key=lambda row: (-len(row["groups"]), -row["lines"]))


def flags(game):
    """The command-line flags the game and its editor tools read: literals such as "-seed" and who reads them."""
    readers = collections.defaultdict(set)
    calls = helpers = 0
    calling = set()
    for rel, info in game.items():
        for flag in FLAG.findall(info["text"]): readers[flag].add(rel)
        count = info["code"].count("GetCommandLineArgs")
        calls += count; helpers += len(ARGUMENT_HELPER.findall(info["code"]))
        if count: calling.add(rel)
    runtime = {flag for flag, users in readers.items() if any(not is_editor(rel) for rel in users)}
    production = collections.Counter(rel for flag, users in readers.items() for rel in users if not is_editor(rel) and not is_test(rel))
    return {"distinct": len(readers), "runtime": len(runtime), "editorOnly": len(readers) - len(runtime),
            "readInOneFile": sum(1 for users in readers.values() if len(users) == 1),
            "readInThreeOrMoreFiles": sum(1 for users in readers.values() if len(users) >= 3),
            "namedTest": sum(1 for flag in runtime if "test" in flag), "namedShots": sum(1 for flag in runtime if flag.endswith("shots")),
            "commandLineReads": calls, "filesReadingTheCommandLine": len(calling), "privateHelpers": helpers,
            "productionFiles": len(production),
            "productionFlags": len({flag for flag, users in readers.items() if any(not is_editor(rel) and not is_test(rel) for rel in users)}),
            "topProductionFiles": [{"file": rel, "flags": count} for rel, count in production.most_common(8)]}


def tests(game, home):
    """The checks compiled into the player: files named *Test*, what keeps them out of a release, who names them."""
    found = sorted(rel for rel in game if is_test(rel) and not is_editor(rel))
    types = {name for name, rel in home.items() if rel in found}
    named_by = {rel: sorted(info["uses"] & types) for rel, info in game.items() if rel not in found and not is_editor(rel) and info["uses"] & types}
    return {"files": len(found), "lines": sum(game[rel]["lines"] for rel in found),
            "selfInstalling": sum(1 for rel in found if "RuntimeInitializeOnLoadMethod" in game[rel]["code"]),
            "ownCheckHelper": sum(1 for rel in found if re.search(r"\b(?:void|bool)\s+Check\s*\(", game[rel]["code"])),
            "notExcludedFromDistribution": [rel for rel in found if "PE_DISTRIBUTION_BUILD" not in game[rel]["text"]],
            "namedByProductionCode": named_by,
            "testOutputLiterals": sum(info["text"].count('"unity_logs') for rel, info in game.items() if not is_editor(rel)),
            "testPathsCalls": sum(info["code"].count("TestPaths.Out") for info in game.values())}


def statics(game):
    """Fields that are static and can change: an approximate count (one declaration line each), tests and editor tools left out."""
    rows = collections.Counter()
    for rel, info in game.items():
        if is_editor(rel) or is_test(rel): continue
        for line in info["code"].splitlines():
            if "(" in line.split("=")[0] or " const " in line or "readonly" in line: continue
            rows[rel] += bool(STATIC_FIELD.match(line))
    rows = +rows
    singletons = sum(len(re.findall(r"\bpublic\s+static\s+\w+\s+(?:I|Instance)\b", info["code"])) for rel, info in game.items()
                     if not is_editor(rel) and not is_test(rel))
    return {"declarations": sum(rows.values()), "files": len(rows), "singletons": singletons,
            "top": [{"file": rel, "declarations": count} for rel, count in rows.most_common(8)]}


def report(result, sections, top):
    lines = []
    if "sizes" in sections:
        lines.append("== sizes: assemblies ==")
        for name, row in result["assemblies"].items():
            kind = ("defined, engine-free" if not row["engine"] else "defined") if row["defined"] else "predefined"
            lines.append(f"{name:34} {row['files']:4} files {row['lines']:7} lines  ({kind})")
        lines.append("== sizes: groups (files, lines, types, engine-free files and their lines) ==")
        for name, row in result["groups"].items():
            lines.append(f"{name:42} {row['files']:4} {row['lines']:7} {row['types']:4} {row['engineFree']:4} {row['engineFreeLines']:6}")
        lines.append(f"{'TOTAL':42} {sum(r['files'] for r in result['groups'].values()):4} {sum(r['lines'] for r in result['groups'].values()):7}")
    if "edges" in sections:
        lines.append("== edges: A -> B, types of B named in A, files of A that name them ==")
        for edge in result["edges"]:
            lines.append(f"{edge['from']:40} -> {edge['to']:40} {len(edge['types']):3} types {edge['files']:3} files")
    if "cycles" in sections:
        lines.append(f"== cycles: {len(result['mutual'])} pairs of groups depend on each other; groups that all reach each other ==")
        for component in result["cycles"]: lines.append(f"{len(component)} groups: " + ", ".join(component))
        cyclic = {name for component in result["cycles"] for name in component}
        lines.append("outside every cycle: " + ", ".join(sorted(set(result["groups"]) - cyclic)))
    if "partials" in sections:
        lines.append("== partials: types written in several files (those spread over several groups first) ==")
        for row in result["partials"][:top]:
            spread = ", ".join(f"{name}={count}" for name, count in row["groups"].items())
            lines.append(f"{row['type']:20} {row['files']:3} files {row['lines']:6} lines  {spread}")
    if "fan-in" in sections:
        lines.append("== fan-in: types named by the most other files ==")
        for row in result["fanIn"][:top]: lines.append(f"{row['type']:26} {row['files']:4} files  ({row['group']})")
    if "pure" in sections:
        pure = result["pure"]
        player = [row for row in pure if row["assembly"] == "Assembly-CSharp"]
        proven = [row for row in player if row["labs"]]
        lines.append(f"== pure: {len(pure)} game files that name neither UnityEngine nor UnityEditor, {sum(row['lines'] for row in pure)} lines ==")
        lines.append(f"{len(player)} of them ({sum(row['lines'] for row in player)} lines) are in Assembly-CSharp; "
                     f"{len(proven)} of those ({sum(row['lines'] for row in proven)} lines) are compiled by a lab without the engine")
        for row in pure:
            proof = "labs: " + ", ".join(row["labs"]) if row["labs"] else "no lab compiles it"
            bound = f"; names engine-bound types: {', '.join(row['engineBoundTypes'][:6])}" if row["engineBoundTypes"] and not row["labs"] else ""
            where = "" if row["assembly"] == "Assembly-CSharp" else f"  [{row['assembly']}]"
            lines.append(f"{row['file']:52} {row['lines']:5} lines  {proof}{bound}{where}")
    if "flags" in sections:
        data = result["flags"]
        lines.append("== flags ==")
        lines.append(f"{data['distinct']} distinct flag literals: {data['runtime']} read by the player, {data['editorOnly']} only by editor tools; "
                     f"{data['namedTest']} of the player's contain 'test', {data['namedShots']} end in 'shots'")
        lines.append(f"{data['readInOneFile']} are read in one file only, {data['readInThreeOrMoreFiles']} in three or more")
        lines.append(f"the command line is read {data['commandLineReads']} times in {data['filesReadingTheCommandLine']} files, through {data['privateHelpers']} private helpers")
        lines.append(f"{data['productionFiles']} files that are neither tests nor editor tools read {data['productionFlags']} flags:")
        for row in data["topProductionFiles"]: lines.append(f"  {row['flags']:3} {row['file']}")
    if "tests" in sections:
        data = result["tests"]
        lines.append("== tests ==")
        lines.append(f"{data['files']} test files in the player assembly, {data['lines']} lines; {data['selfInstalling']} install themselves at load, "
                     f"{data['ownCheckHelper']} define their own Check")
        lines.append(f"{len(data['notExcludedFromDistribution'])} are not wrapped in PE_DISTRIBUTION_BUILD: " + ", ".join(data["notExcludedFromDistribution"]))
        lines.append(f"{len(data['namedByProductionCode'])} production files name a test type: "
                     + "; ".join(f"{rel} ({', '.join(names)})" for rel, names in sorted(data["namedByProductionCode"].items())))
        lines.append(f"output: {data['testPathsCalls']} TestPaths.Out calls, {data['testOutputLiterals']} \"unity_logs...\" literals relative to the working directory")
    if "statics" in sections:
        data = result["statics"]
        lines.append("== statics ==")
        lines.append(f"about {data['declarations']} mutable static fields in {data['files']} files (tests and editor left out); {data['singletons']} singletons named I or Instance")
        for row in data["top"]: lines.append(f"  {row['declarations']:3} {row['file']}")
    return "\n".join(lines)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--root", type=Path, default=ROOT)
    parser.add_argument("--by-folder", action="store_true", help="plain folders as groups (no test group, root folder not split)")
    parser.add_argument("--section", action="append", choices=SECTIONS, help="print only this section (repeatable)")
    parser.add_argument("--top", type=int, default=30, help="rows of the partials and fan-in sections")
    parser.add_argument("--json", type=Path, help="also write the whole result to this file")
    args = parser.parse_args(argv)
    result = census(args.root, args.by_folder)
    if args.json: args.json.write_text(json.dumps(result, indent=1, ensure_ascii=False), encoding="utf-8")
    print(report(result, args.section or SECTIONS, args.top))
    return 0


if __name__ == "__main__":
    sys.exit(main())
