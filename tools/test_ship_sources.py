"""tools/ship.ps1 says what a build contains: the uncommitted sources, checked on throwaway git repositories.

Unity builds the working tree, not the commit. After committing, ship.ps1 lists what differs from HEAD under
unity/PryingEyes/{Assets,Packages,ProjectSettings}: the `build:` and `exe:` summary lines, an AVVISO, the marker and
Builds/build_info.json carry that list, and -RequireClean refuses to go on while it is not empty. Derived files (.mat,
Scenes/Torre.unity) and files that differ from HEAD only in their line endings do not count.

The build the maintainer plays is Builds/PryingEyes.exe and must always be the latest one: the exe step copies the verified build there,
leaves the sibling folders (Audit, Releases, ...) alone, and when the game is open it starts a hidden -RefreshOnly process that copies
as soon as the game closes.

Here the real script runs against temporary repositories shaped like this one, with no gate, no build and no push; the exe step runs
against a fake build folder. That needs Windows PowerShell 5.1, so those tests are skipped elsewhere."""
import concurrent.futures
import json
import os
from pathlib import Path
import re
import shutil
import stat
import subprocess
import tempfile
import time
import unittest

SCRIPT =Path(__file__).resolve().parent / "ship.ps1"
ASSETS = "unity/PryingEyes/Assets/_PryingEyes"
UNIX_EPOCH_TICKS = 621355968000000000        # .NET ticks (100 ns since 0001-01-01) at 1970-01-01

COMMITTED = {
    ".gitignore": b"Scratch/\nunity_logs/\nunity/PryingEyes/Builds/\n",
    "unity/PryingEyes/ProjectSettings/ProjectVersion.txt": b"m_EditorVersion: 6000.0.0f1\n",
    "unity/PryingEyes/Packages/manifest.json": b"{}\n",
    f"{ASSETS}/Scripts/Game.cs": b"class Game {}\n",
    f"{ASSETS}/Scripts/Gone.cs": b"class Gone {}\n",
    f"{ASSETS}/Scripts/EolCrlf.cs": b"a\r\nb\r\n",       # committed with CRLF, rewritten with LF
    f"{ASSETS}/Scripts/EolLf.cs": b"a\nb\n",             # committed with LF, rewritten with CRLF
    f"{ASSETS}/Scripts/Mixed.cs": b"a\r\nb\r\n",         # line endings change AND a line is added: that one counts
    f"{ASSETS}/Materials/Wall.mat": b"m: 1\n",
    f"{ASSETS}/Scenes/Torre.unity": b"scene: 1\n",
    "docs/notes.md": b"notes\n",
    "unity/LayoutDemo/Packages/manifest.json": b"{}\n",
}
# What the script must list once make_dirty() has run (status, path); everything else make_dirty() does must not be listed.
COUNTED = {
    ("M", f"{ASSETS}/Scripts/Game.cs"), ("D", f"{ASSETS}/Scripts/Gone.cs"), ("A", f"{ASSETS}/Scripts/Staged.cs"),
    ("??", f"{ASSETS}/Scripts/New.cs"), ("M", f"{ASSETS}/Scripts/Mixed.cs"),
    ("M", "unity/PryingEyes/Packages/manifest.json"), ("??", "unity/PryingEyes/ProjectSettings/NewSetting.asset"),
}
NOT_COUNTED = ("Wall.mat", "New.mat", "Torre.unity", "EolCrlf", "EolLf", "tmp.cs", "notes.md", "LayoutDemo")


def write(repo, relative, content):
    path = repo / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(content)
    return path


def git(repo, env, *args):
    return subprocess.run(["git", "-C", str(repo), *args], check=True, capture_output=True, text=True, env=env).stdout.strip()


def make_repo(root, env, name):
    """A repository shaped like the real one, with every kind of file the script tells apart already committed."""
    repo = root / name
    for relative, content in COMMITTED.items(): write(repo, relative, content)
    shutil.copyfile(SCRIPT, write(repo, "tools/ship.ps1", b""))
    git(repo, env, "init", "-q", "-b", "main")
    git(repo, env, "config", "core.autocrlf", "false")          # keep the CRLF blobs as committed
    git(repo, env, "add", ".")
    git(repo, env, "-c", "user.name=ship-test", "-c", "user.email=ship-test@example.invalid", "commit", "-q", "-m", "fixture")
    return repo


def make_dirty(repo, env, stage=True):
    write(repo, f"{ASSETS}/Scripts/Game.cs", b"class Game { int x; }\n")                    # M
    (repo / f"{ASSETS}/Scripts/Gone.cs").unlink()                                             # D
    write(repo, f"{ASSETS}/Scripts/Staged.cs", b"class Staged {}\n")                          # A (or ?? when not staged)
    if stage: git(repo, env, "add", f"{ASSETS}/Scripts/Staged.cs")
    write(repo, f"{ASSETS}/Scripts/New.cs", b"class New {}\n")                                # ??
    write(repo, "unity/PryingEyes/Packages/manifest.json", b'{"dependencies":{}}\n')          # M
    write(repo, "unity/PryingEyes/ProjectSettings/NewSetting.asset", b"s: 1\n")               # ??
    write(repo, f"{ASSETS}/Scripts/Mixed.cs", b"a\nb\nc\n")                                   # M: real change on top of the line endings
    # none of these is a source change
    write(repo, f"{ASSETS}/Scripts/EolCrlf.cs", b"a\nb\n")
    write(repo, f"{ASSETS}/Scripts/EolLf.cs", b"a\r\nb\r\n")
    write(repo, f"{ASSETS}/Materials/Wall.mat", b"m: 2\n")                                    # regenerated material
    write(repo, f"{ASSETS}/Materials/New.mat", b"m: 3\n")
    write(repo, f"{ASSETS}/Materials/New.mat.meta", b"guid: x\n")
    write(repo, f"{ASSETS}/Scenes/Torre.unity", b"scene: 2\n")                                # generated scene
    write(repo, f"{ASSETS}/Scratch/tmp.cs", b"class Tmp {}\n")                                # ignored by .gitignore
    write(repo, "docs/notes.md", b"more notes\n")                                             # outside the three roots
    write(repo, "unity/LayoutDemo/Packages/manifest.json", b'{"dependencies":{}}\n')


def fake_build(root, repo, head):
    """A folder that looks like a finished build plus the marker a previous ship.ps1 would have left for it."""
    build = root / "fake-build"
    for relative in ("PryingEyes.exe", "UnityPlayer.dll", "PryingEyes_Data/Managed/Assembly-CSharp.dll", "PryingEyes_Data/resources.assets",
                     "PryingEyes_Data/level0", "D3D12/d3d12.dll", "MonoBleedingEdge/mono.dll"):
        write(build, relative, relative.encode())
    ticks = (build / "PryingEyes_Data/resources.assets").stat().st_mtime_ns // 100 + UNIX_EPOCH_TICKS
    marker = {"out": str(build), "commit": head, "resTicks": str(ticks), "mode": "scripts", "when": "2026-01-01T00:00:00", "branch": "main",
              "uncommittedCount": 2, "uncommittedTruncated": False,
              "uncommitted": [{"status": "M", "path": f"{ASSETS}/Scripts/A.cs"}, {"status": "??", "path": f"{ASSETS}/Scripts/B.cs"}]}
    write(repo, "unity_logs/ship_built.json", json.dumps(marker).encode("utf-8"))
    write(repo, "unity/PryingEyes/Builds/build_info.json", b'{"commit": "stale"}')            # must be replaced, never kept
    write(repo, "unity/PryingEyes/Builds/Audit/keep.txt", b"workspace")                      # the exe step must leave its siblings alone
    write(repo, "unity/PryingEyes/Builds/Releases/old.zip", b"zip")
    return build


def ship(repo, env, *flags):
    command = ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(repo / "tools/ship.ps1"), "-NoGate", "-NoPush", *flags]
    run = subprocess.run(command, capture_output=True, text=True, errors="replace", env=env, timeout=240)
    return run.returncode, run.stdout + run.stderr


def sources_step(output):
    """The lines the script logged under its 'sorgenti' step."""
    lines = output.splitlines()
    start = next(i for i, line in enumerate(lines) if line.startswith("--- sorgenti"))
    end = next((i for i in range(start + 1, len(lines)) if lines[i].startswith("--- ")), len(lines))
    return lines[start + 1:end]


def listed(output):
    found = (re.match(r"^  (M|A|D|T|\?\?) (\S+)$", line) for line in sources_step(output))
    return {(m[1], m[2]) for m in found if m}


def summary(output, key):
    """The summary line that starts with `key:`; empty when the script never got that far."""
    return next((line for line in output.splitlines() if line.startswith(key + ":")), "")


def force_remove(function, path, _):
    os.chmod(path, stat.S_IWRITE)
    function(path)


class ShipScriptSource(unittest.TestCase):
    def test_script_is_pure_ascii(self):
        """Windows PowerShell 5.1 reads a script without BOM in the ANSI code page: an accent corrupts it."""
        data = SCRIPT.read_bytes()
        bad = [i for i, byte in enumerate(data) if byte > 127]
        self.assertEqual(bad, [], f"non-ASCII byte at offset {bad[:1]} of tools/ship.ps1")


@unittest.skipUnless(os.name == "nt" and shutil.which("powershell") and shutil.which("git"), "ship.ps1 needs Windows PowerShell 5.1 and git")
class ShipDeclaresWhatTheBuildContains(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        # resolve(): a runner's TEMP can be an 8.3 short path (C:\Users\RUNNER~1\...) and PowerShell 5.1 expands it, so the fake
        # marker has to name the build folder the way the script will, or the exe step finds it "not the one this ship built".
        cls.root = Path(tempfile.mkdtemp(prefix="ship-test-")).resolve()
        cls.addClassCleanup(shutil.rmtree, cls.root, onerror=force_remove)
        empty = write(cls.root, "empty.gitconfig", b"")
        # No PSModulePath: when the tests are started from pwsh 7 (as on the runner) the child would inherit its module path and Windows
        # PowerShell 5.1 loses Get-FileHash, which the exe step uses; without the variable it builds its own default.
        inherited = {key: value for key, value in os.environ.items() if key.upper() != "PSMODULEPATH"}
        cls.env = {**inherited, "GIT_CONFIG_GLOBAL": str(empty), "GIT_CONFIG_NOSYSTEM": "1", "GIT_TERMINAL_PROMPT": "0"}
        with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:      # four PowerShell starts in parallel: a few seconds in all
            cases = {"default": pool.submit(cls.dirty_run), "refused": pool.submit(cls.dirty_run, "-RequireClean"),
                     "clean": pool.submit(cls.clean_run), "dry": pool.submit(cls.dry_run)}
            cls.runs = {name: case.result() for name, case in cases.items()}

    @classmethod
    def dirty_run(cls, *flags):
        repo = make_repo(cls.root, cls.env, "dirty" + "".join(flags).replace("-", "_"))
        make_dirty(repo, cls.env)
        return repo, git(repo, cls.env, "rev-parse", "HEAD"), *ship(repo, cls.env, "-NoBuild", "-NoExe", *flags)

    @classmethod
    def clean_run(cls):
        repo = make_repo(cls.root, cls.env, "clean")
        head = git(repo, cls.env, "rev-parse", "HEAD")
        build = fake_build(cls.root, repo, head)
        return repo, head, *ship(repo, cls.env, "-NoBuild", "-OutDir", str(build))

    @classmethod
    def dry_run(cls):
        repo = make_repo(cls.root, cls.env, "dry")
        make_dirty(repo, cls.env, stage=False)         # a staged file would stop the commit step: the index must start empty
        message = write(cls.root, "message.txt", b"test\n")
        paths = f"{ASSETS}/Scripts/Game.cs,unity/PryingEyes/Packages"       # a file and a folder, both would be committed
        return repo, git(repo, cls.env, "rev-parse", "HEAD"), *ship(repo, cls.env, "-NoBuild", "-NoExe", "-DryRun", "-Paths", paths, "-MessageFile", str(message))

    def test_a_dirty_tree_does_not_block_and_is_declared(self):
        _, head, code, output = self.runs["default"]
        self.assertEqual(code, 0, output)
        self.assertIn("SHIP: OK", output)
        self.assertEqual(listed(output), COUNTED, output)
        self.assertIn(f"HEAD {head[:7]} + {len(COUNTED)} sorgenti non committati", summary(output, "build"), output)
        self.assertTrue(any(line.startswith("AVVISO") and f"{len(COUNTED)} sorgenti non committati" in line for line in output.splitlines()), output)

    def test_derived_files_and_line_ending_only_changes_do_not_count(self):
        _, _, _, output = self.runs["default"]
        block = "\n".join(sources_step(output))
        for name in NOT_COUNTED: self.assertNotIn(name, block, output)

    def test_require_clean_refuses_before_the_build(self):
        _, _, code, output = self.runs["refused"]
        self.assertEqual(code, 1, output)
        self.assertIn(f"SHIP: FALLITO - sorgenti: {len(COUNTED)} sorgenti non committati", output)
        self.assertEqual(listed(output), COUNTED, output)
        self.assertEqual(summary(output, "build"), "", "the refusal must come before the build step")

    def test_a_clean_tree_says_so(self):
        _, head, code, output = self.runs["clean"]
        self.assertEqual(code, 0, output)
        self.assertEqual(listed(output), set(), output)
        self.assertIn(f"HEAD {head[:7]}, nessun sorgente non committato", summary(output, "build"), output)

    def test_exe_step_leaves_build_info_next_to_the_exe(self):
        repo, head, code, output = self.runs["clean"]
        self.assertEqual(code, 0, output)
        self.assertRegex(summary(output, "exe"), r"^exe:\s+aggiornato in ", output)
        self.assertIn(f"contiene HEAD {head[:7]} + 2 sorgenti non committati", summary(output, "exe"), output)
        builds = repo / "unity/PryingEyes/Builds"
        self.assertTrue((builds / "PryingEyes.exe").is_file(), output)
        self.assertEqual((builds / "PryingEyes_Data/resources.assets").read_bytes(), b"PryingEyes_Data/resources.assets", output)
        self.assertFalse((builds / "Prova").exists(), "nothing may be published to the retired Prova folder")
        self.assertEqual((builds / "Audit/keep.txt").read_bytes(), b"workspace", "the exe step must not touch the build workspace")
        self.assertEqual((builds / "Releases/old.zip").read_bytes(), b"zip", "the exe step must not touch the releases")
        info = json.loads((builds / "build_info.json").read_text(encoding="utf-8"))
        self.assertEqual((info["commit"], info["branch"], info["mode"], info["builtAt"]), (head, "main", "scripts", "2026-01-01T00:00:00"))
        self.assertEqual((info["uncommittedCount"], info["uncommittedTruncated"]), (2, False))
        self.assertEqual([(e["status"], e["path"]) for e in info["uncommitted"]], [("M", f"{ASSETS}/Scripts/A.cs"), ("??", f"{ASSETS}/Scripts/B.cs")])

    def test_dry_run_does_not_count_what_it_would_commit_and_changes_nothing(self):
        repo, head, code, output = self.runs["dry"]
        self.assertEqual(code, 0, output)
        expected = {("D", f"{ASSETS}/Scripts/Gone.cs"), ("??", f"{ASSETS}/Scripts/Staged.cs"), ("??", f"{ASSETS}/Scripts/New.cs"),
                    ("M", f"{ASSETS}/Scripts/Mixed.cs"), ("??", "unity/PryingEyes/ProjectSettings/NewSetting.asset")}
        self.assertEqual(listed(output), expected, output)
        self.assertEqual(git(repo, self.env, "rev-parse", "HEAD"), head)
        self.assertEqual(git(repo, self.env, "diff", "--cached", "--name-only"), "")


def kill_tree(pid):
    subprocess.run(["taskkill", "/F", "/T", "/PID", str(pid)], capture_output=True)


@unittest.skipUnless(os.name == "nt" and shutil.which("powershell") and shutil.which("git"), "ship.ps1 needs Windows PowerShell 5.1 and git")
class ShipKeepsTheGameCurrentEvenWhenItIsOpen(unittest.TestCase):
    """While the game is open the exe step cannot copy over it. Instead of leaving the build stale, ship.ps1 starts a hidden -RefreshOnly
    process that waits for the game to close and copies then. The stand-in game is a copy of ping.exe named PryingEyes.exe and started from
    the folder the script treats as the game's; it holds its files locked, like the real player, until the test lets it go."""

    @classmethod
    def setUpClass(cls):
        cls.root = Path(tempfile.mkdtemp(prefix="ship-defer-")).resolve()
        cls.addClassCleanup(shutil.rmtree, cls.root, onerror=force_remove)
        empty = write(cls.root, "empty.gitconfig", b"")
        inherited = {key: value for key, value in os.environ.items() if key.upper() != "PSMODULEPATH"}
        cls.env = {**inherited, "GIT_CONFIG_GLOBAL": str(empty), "GIT_CONFIG_NOSYSTEM": "1", "GIT_TERMINAL_PROMPT": "0"}
        cls.repo = make_repo(cls.root, cls.env, "defer")
        cls.head = git(cls.repo, cls.env, "rev-parse", "HEAD")
        build = fake_build(cls.root, cls.repo, cls.head)
        cls.builds = cls.repo / "unity/PryingEyes/Builds"
        cls.status = cls.repo / "unity_logs/ship_refresh.status"
        game = cls.builds / "PryingEyes.exe"
        shutil.copyfile(Path(os.environ.get("SystemRoot", r"C:\Windows")) / "System32" / "PING.EXE", game)
        cls.fake_game = subprocess.Popen([str(game), "-t", "127.0.0.1"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        cls.addClassCleanup(cls.stop_everything)       # registered last, so it runs first: processes die before the folder is removed
        cls.code, cls.output = ship(cls.repo, cls.env, "-NoBuild", "-OutDir", str(build), "-DeferExeMin", "3")
        cls.game_while_open = game.read_bytes()
        cls.fake_game.kill()
        cls.fake_game.wait()
        cls.final_status = cls.wait_for_refresh()

    @classmethod
    def stop_everything(cls):
        if cls.fake_game.poll() is None:
            cls.fake_game.kill()
            cls.fake_game.wait()
        if cls.status.exists():
            match = re.search(r"pid=(\d+)", cls.status.read_text(encoding="utf-8-sig", errors="replace"))
            if match: kill_tree(match[1])

    @classmethod
    def wait_for_refresh(cls, timeout=120):
        deadline = time.monotonic() + timeout
        text = ""
        while time.monotonic() < deadline:
            text = cls.status.read_text(encoding="utf-8-sig", errors="replace").strip() if cls.status.exists() else ""
            if text.startswith("FINITO"): break
            time.sleep(1)
        return text

    def test_ship_does_not_wait_and_leaves_the_open_game_alone(self):
        self.assertEqual(self.code, 0, self.output)
        line = summary(self.output, "exe")
        self.assertRegex(line, r"^exe:\s+NON aggiornato: il gioco dell'utente e' aperto", self.output)
        self.assertIn("aggiornamento differito avviato", line, self.output)
        self.assertEqual(self.game_while_open[:2], b"MZ", "the open game must be exactly what it was: the stand-in executable")

    def test_the_hidden_refresh_copies_the_build_once_the_game_closes(self):
        self.assertTrue(self.final_status.startswith("FINITO: SHIP: OK"), self.final_status)
        self.assertEqual((self.builds / "PryingEyes.exe").read_bytes(), b"PryingEyes.exe")
        self.assertEqual((self.builds / "PryingEyes_Data/resources.assets").read_bytes(), b"PryingEyes_Data/resources.assets")
        info = json.loads((self.builds / "build_info.json").read_text(encoding="utf-8"))
        self.assertEqual((info["commit"], info["mode"]), (self.head, "scripts"))
        self.assertEqual((self.builds / "Audit/keep.txt").read_bytes(), b"workspace")


if __name__ == "__main__":
    unittest.main()
