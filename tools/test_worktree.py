"""tools/worktree.ps1 makes a git worktree ready for Unity and removes one safely.

A new worktree has no Library, so the editor would import every asset before it could compile. The script copies the Library of a donor
checkout that has the same Unity version and no editor open, and removes a worktree only when nothing would be lost (no uncommitted change,
every commit published). Here the real script runs against throwaway git repositories (each with a local bare `origin`) and an invented
Library: no Unity and no real project. How long the real Library takes to copy and how long the first compile takes are measured on the
real project and written in docs/engineering/building-and-shipping.md. The script needs Windows PowerShell 5.1, git and robocopy, so the
tests that run it are skipped elsewhere."""
import collections
import concurrent.futures
import json
import os
from pathlib import Path
import shutil
import stat
import subprocess
import tempfile
import time
import unittest

TOOLS = Path(__file__).resolve().parent
SCRIPT = TOOLS / "worktree.ps1"
PROJECT = "unity/PryingEyes"
ASSETS = f"{PROJECT}/Assets"
VERSION_FILE = f"{PROJECT}/ProjectSettings/ProjectVersion.txt"
EDITOR = b"m_EditorVersion: 6000.0.0f1\nm_EditorVersionWithRevision: 6000.0.0f1 (0123456789ab)\n"

COMMITTED = {
    ".gitignore": b"unity_logs/\nunity/PryingEyes/Library/\nunity/PryingEyes/Temp/\nunity/PryingEyes/Builds/\n",
    VERSION_FILE: EDITOR,
    f"{ASSETS}/Game.cs": b"class Game {}\n",
    f"{ASSETS}/Settings.asset": b"a: 1\r\nb: 2\r\n",         # Unity rewrites it with other line endings after a run, and changes nothing
    f"{ASSETS}/Wall.mat": b"m: 1\n",
    "docs/notes.md": b"notes\n",
}
# An invented Library: nested folders, a name with a space, an empty file, a big-ish file and a folder named Temp (robocopy leaves it out).
LIBRARY = {
    "ArtifactDB": b"\x01\x02" * 40000,
    "Bee/artifacts/Game.dll": bytes(range(256)) * 300,
    "metadata/ab/abcdef": b"meta",
    "PackageCache/com.example@1.0.0/a name with spaces.txt": b"a name with a space",
    "empty": b"",
    "DataStore/Temp/left-out.bin": b"in a folder named Temp",
}
COPIED = {name: content for name, content in LIBRARY.items() if "Temp" not in name.split("/")}

Run = collections.namedtuple("Run", "code data stdout stderr")


def write(repo, relative, content):
    path = Path(repo) / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(content)
    return path


def git(repo, env, *args):
    return subprocess.run(["git", "-C", str(repo), *args], check=True, capture_output=True, text=True, env=env).stdout.strip()


def git_code(repo, env, *args):
    return subprocess.run(["git", "-C", str(repo), *args], capture_output=True, text=True, env=env).returncode


def commit(repo, env, message):
    git(repo, env, "add", ".")
    git(repo, env, "-c", "user.name=worktree-test", "-c", "user.email=worktree-test@example.invalid", "commit", "-q", "-m", message)


def add_library(checkout, files=None):
    for name, content in (LIBRARY if files is None else files).items(): write(checkout, f"{PROJECT}/Library/{name}", content)


def make_main(root, env, name, autocrlf="false"):
    """A checkout shaped like the real one, committed and pushed to a local bare origin, with the script in it; no Library yet.
    The maintainer's git has core.autocrlf=true, which changes what `git status` and `git diff` say about a file that Unity rewrote."""
    repo = root / name
    for relative, content in COMMITTED.items(): write(repo, relative, content)
    shutil.copyfile(SCRIPT, write(repo, "tools/worktree.ps1", b""))
    git(repo, env, "init", "-q", "-b", "preview")
    git(repo, env, "config", "core.autocrlf", autocrlf)
    write(repo, ".git/info/exclude", b".claude/worktrees/\n")           # as in the real repository: the worktrees are not changes of the main checkout
    commit(repo, env, "fixture")
    origin = root / f"{name}-origin.git"
    git(root, env, "init", "-q", "--bare", "-b", "preview", str(origin))
    git(repo, env, "remote", "add", "origin", str(origin))
    git(repo, env, "push", "-q", "origin", "preview")
    return repo


def origin_of(repo):
    return repo.parent / f"{repo.name}-origin.git"


def add_checkout(main, env, name, library=True):
    """Another registered worktree of the same repository, made with plain git (what a donor looks like)."""
    path = main.parent / f"{main.name}-{name}"
    git(main, env, "worktree", "add", "-q", "--no-track", "-b", f"claude/{name}", str(path), "origin/preview")
    if library: add_library(path)
    return path


def clone(main, env, name):
    other = main.parent / f"{main.name}-{name}"
    subprocess.run(["git", "clone", "-q", str(origin_of(main)), str(other)], check=True, capture_output=True, text=True, env=env)
    git(other, env, "config", "core.autocrlf", "false")
    return other


def worktree(repo, env, *args, cwd=None, normal_priority=False):
    """Runs repo/tools/worktree.ps1 the way an agent does and parses its one JSON document. A -New gets no free-space floor and, unless
    normal_priority, -FullSpeed: the default low disk priority makes a copy crawl on a PC that is busy, and the copies here are a few bytes."""
    flags = list(args)
    if "-New" in flags:
        if "-MinFreeGB" not in flags: flags += ["-MinFreeGB", "0"]
        if not normal_priority: flags += ["-FullSpeed"]
    command = ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(repo / "tools/worktree.ps1"), *flags]
    run = subprocess.run(command, capture_output=True, text=True, errors="replace", env=env, timeout=240, cwd=str(cwd or repo))
    try: data = json.loads(run.stdout)
    except ValueError: data = None
    return Run(run.returncode, data, run.stdout, run.stderr)


def new_worktree(main, env, name, *flags):
    run = worktree(main, env, "-New", name, *flags)
    assert run.code == 0 and run.data, f"-New {name} failed: {run.stdout} {run.stderr}"
    return Path(run.data["path"])


def plain_worktree(main, env, name, library=False):
    """A worktree the way the script leaves one (at the default place, on claude/<name> cut from origin/preview, no upstream), made with plain git."""
    path = main / ".claude/worktrees" / name
    git(main, env, "worktree", "add", "-q", "--no-track", "-b", f"claude/{name}", str(path), "origin/preview")
    if library: add_library(path)
    return path


def tree(root, skip=()):
    """Every file under root with its size and modification time: a worktree that was not touched has the same tree before and after."""
    found = {}
    for path in sorted(Path(root).rglob("*")):
        relative = path.relative_to(root).as_posix()
        if path.is_file() and relative.split("/")[0] not in skip:
            found[relative] = (path.stat().st_size, path.stat().st_mtime_ns)
    return found


def contents(root):
    return {path.relative_to(root).as_posix(): path.read_bytes() for path in sorted(Path(root).rglob("*")) if path.is_file()}


def same_path(a, b):
    return os.path.normcase(os.path.normpath(str(a))) == os.path.normcase(os.path.normpath(str(b)))


def force_remove(function, path, _):
    os.chmod(path, stat.S_IWRITE)
    function(path)


class WorktreeScriptSource(unittest.TestCase):
    def test_script_is_pure_ascii(self):
        """Windows PowerShell 5.1 reads a script without BOM in the ANSI code page: an accent corrupts it."""
        bad = [i for i, byte in enumerate(SCRIPT.read_bytes()) if byte > 127]
        self.assertEqual(bad, [], f"non-ASCII byte at offset {bad[:1]}")

    @unittest.skipUnless(os.name == "nt" and shutil.which("powershell"), "needs Windows PowerShell 5.1")
    def test_script_parses(self):
        command = ("$t=$null;$e=$null;[void][System.Management.Automation.Language.Parser]::ParseFile('" + str(SCRIPT) + "',[ref]$t,[ref]$e);"
                   "$e|ForEach-Object{$_.Message+' @'+$_.Extent.StartLineNumber};exit $e.Count")
        run = subprocess.run(["powershell", "-NoProfile", "-Command", command], capture_output=True, text=True, errors="replace", timeout=60)
        self.assertEqual(run.returncode, 0, run.stdout + run.stderr)


@unittest.skipUnless(os.name == "nt" and shutil.which("powershell") and shutil.which("git") and shutil.which("robocopy"),
                     "worktree.ps1 needs Windows PowerShell 5.1, git and robocopy")
class WorktreeScriptOnThrowawayRepositories(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        # resolve(): a runner's TEMP can be an 8.3 short path and git reports the long one; the repositories must be named the way git sees them
        cls.root = Path(tempfile.mkdtemp(prefix="worktree-test-")).resolve()
        cls.addClassCleanup(shutil.rmtree, cls.root, onerror=force_remove)
        empty = write(cls.root, "empty.gitconfig", b"")
        # No PSModulePath: started from pwsh 7 (as on the runner) the child would inherit its module path and lose modules of Windows PowerShell 5.1
        inherited = {key: value for key, value in os.environ.items() if key.upper() != "PSMODULEPATH"}
        cls.env = {**inherited, "GIT_CONFIG_GLOBAL": str(empty), "GIT_CONFIG_NOSYSTEM": "1", "GIT_TERMINAL_PROMPT": "0"}
        names = ("new", "pick", "lock", "version", "none", "dry", "exists", "fresh", "busy", "remove_clean", "remove_dirty", "remove_unpushed",
                 "remove_pushed", "remove_squash", "remove_pull_head", "remove_noise", "remove_noise_crlf", "remove_refused", "remove_long", "remove_held", "stuck",
                 "untouched")
        with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:
            cases = {name: pool.submit(getattr(cls, "case_" + name)) for name in names}
            cls.runs = {}
            for name, case in cases.items():
                try: cls.runs[name] = case.result()
                except Exception as error: cls.runs[name] = error          # only the tests that read this case fail, with the reason

    def case(self, name):
        found = self.runs[name]
        if isinstance(found, Exception): self.fail(f"the scenario '{name}' could not be set up: {found!r}")
        return found

    @classmethod
    def main(cls, name, library=True, autocrlf="false"):
        main = make_main(cls.root, cls.env, name, autocrlf)
        if library: add_library(main)
        return main

    @classmethod
    def listing(cls, main):
        return {"worktrees": git(main, cls.env, "worktree", "list", "--porcelain"), "branches": git(main, cls.env, "branch", "--list", "--format=%(refname:short)")}

    # -- a new worktree ---------------------------------------------------------------------------------------------------------
    @classmethod
    def case_new(cls):
        main = cls.main("new")
        before = tree(main / PROJECT / "Library")
        run = worktree(main, cls.env, "-New", "feature", normal_priority=True)
        return {"main": main, "run": run, "before": before, "after": tree(main / PROJECT / "Library"),
                "origin": git(main, cls.env, "rev-parse", "refs/remotes/origin/preview")}

    @classmethod
    def case_pick(cls):
        main = cls.main("pick", library=False)
        other = add_checkout(main, cls.env, "other")
        first = worktree(main, cls.env, "-New", "a")                         # the main checkout has no Library: the other worktree gives its own
        named = worktree(main, cls.env, "-New", "b", "-From", other.name)   # -From takes the name of a worktree...
        by_path = worktree(main, cls.env, "-New", "c", "-From", str(other))  # ...or a folder
        add_library(main)
        both = worktree(main, cls.env, "-New", "d")                          # now both qualify: the main checkout comes first
        return {"main": main, "other": other, "first": first, "named": named, "by_path": by_path, "both": both}

    @classmethod
    def case_lock(cls):
        main = cls.main("lock")
        write(main, f"{PROJECT}/Temp/UnityLockfile", b"")                                       # an editor has the donor open
        return {"main": main, "auto": worktree(main, cls.env, "-New", "x"),
                "explicit": worktree(main, cls.env, "-New", "y", "-From", str(main)), "state": cls.listing(main),
                "no_library": worktree(main, cls.env, "-New", "z", "-NoLibrary")}

    @classmethod
    def case_version(cls):
        main = cls.main("version")
        write(main, VERSION_FILE, b"m_EditorVersion: 6000.1.0f1\n")                             # the donor was last opened by another editor
        return {"main": main, "run": worktree(main, cls.env, "-New", "x"), "state": cls.listing(main)}

    @classmethod
    def case_none(cls):
        main = cls.main("none", library=False)
        return {"main": main, "run": worktree(main, cls.env, "-New", "x"), "state": cls.listing(main)}

    @classmethod
    def case_dry(cls):
        main = cls.main("dry")
        before = cls.listing(main)
        run = worktree(main, cls.env, "-New", "x", "-DryRun")
        return {"main": main, "run": run, "before": before, "after": cls.listing(main), "worktrees": sorted(p.name for p in (main / ".claude").rglob("*"))
                if (main / ".claude").exists() else []}

    @classmethod
    def case_exists(cls):
        main = cls.main("exists")
        keep = write(main, ".claude/worktrees/x/keep.txt", b"somebody's folder")
        taken = worktree(main, cls.env, "-New", "x")
        branch = worktree(main, cls.env, "-New", "y", "-Branch", "preview")
        bad = worktree(main, cls.env, "-New", "no/slash")
        return {"main": main, "taken": taken, "branch": branch, "bad": bad, "keep": keep.read_bytes(), "state": cls.listing(main)}

    @classmethod
    def case_fresh(cls):
        main = cls.main("fresh", library=False)
        write(main, "docs/local.md", b"a commit of the main checkout that is not on origin\n")
        commit(main, cls.env, "local only")
        other = clone(main, cls.env, "other")
        write(other, "docs/pushed.md", b"pushed by somebody else after the last fetch\n")
        commit(other, cls.env, "pushed by another clone")
        git(other, cls.env, "push", "-q", "origin", "preview")
        new_tip = git(other, cls.env, "rev-parse", "HEAD")
        run = worktree(main, cls.env, "-New", "x", "-NoLibrary")
        return {"main": main, "run": run, "new_tip": new_tip, "local_tip": git(main, cls.env, "rev-parse", "HEAD")}

    @classmethod
    def case_busy(cls):
        """An editor has the donor open, and a player runs from a worktree: processes that only Windows can show are faked with a copy of cmd.exe
        named Unity.exe or PryingEyes.exe, whose command line or folder is what the script looks at."""
        main = cls.main("busy")
        wt = plain_worktree(main, cls.env, "x")
        editor = cls.root / "busy-editor" / "Unity.exe"
        player = wt / PROJECT / "Builds/Audit/PryingEyes.exe"
        for fake in (editor, player):
            fake.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(Path(os.environ.get("SystemRoot", r"C:\Windows")) / "System32/cmd.exe", fake)
        started = []
        try:
            started.append(subprocess.Popen(f'"{editor}" /c "ping -n 90 127.0.0.1 >nul & rem -projectPath {main / PROJECT}"', stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL))
            started.append(subprocess.Popen(f'"{player}" /c "ping -n 90 127.0.0.1 >nul"', stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL))
            new = worktree(main, cls.env, "-New", "y")
            remove = worktree(main, cls.env, "-Remove", "x")
            state = cls.listing(main)
        finally:
            for process in started:
                process.kill()
                process.wait()
        for _ in range(3):                                                                       # the player is gone: nothing holds the folder
            after = worktree(main, cls.env, "-Remove", "x")                                      # (a scanner may still hold the exe for a moment)
            if after.code == 0: break
            time.sleep(3)
        return {"main": main, "wt": wt, "new": new, "remove": remove, "state": state, "after": after}

    @classmethod
    def case_stuck(cls):
        """On the real project Windows refused, once, to rename the finished copy (a scanner held a file in it). A tracked file named Library makes the
        rename fail every time: the donor is a plain folder, given with -From, so that it can have a Library of its own."""
        main = make_main(cls.root, cls.env, "stuck")
        donor = cls.root / "stuck-donor"
        write(donor, VERSION_FILE, EDITOR)
        add_library(donor)
        write(main, f"{PROJECT}/Library", b"a file where the folder should go")
        commit(main, cls.env, "a file named Library")
        git(main, cls.env, "push", "-q", "origin", "preview")
        run = worktree(main, cls.env, "-New", "x", "-From", str(donor), "-RenameTries", "2", "-RenameWait", "1")
        stage = main / ".claude/worktrees/x" / PROJECT / "Temp/Library.copy"
        return {"main": main, "run": run, "stage": contents(stage) if stage.is_dir() else None}

    # -- removing a worktree ----------------------------------------------------------------------------------------------------
    @classmethod
    def case_remove_clean(cls):
        main = cls.main("remove-clean")
        wt = plain_worktree(main, cls.env, "x", library=True)
        tip = git(wt, cls.env, "rev-parse", "HEAD")
        dry = worktree(main, cls.env, "-Remove", "x", "-DryRun")
        existed = wt.exists()
        run = worktree(main, cls.env, "-Remove", "x")
        return {"main": main, "wt": wt, "tip": tip, "dry": dry, "existed": existed, "run": run, "state": cls.listing(main)}

    @classmethod
    def case_remove_dirty(cls):
        main = cls.main("remove-dirty", library=False)
        wt = plain_worktree(main, cls.env, "x")
        write(wt, f"{ASSETS}/Game.cs", b"class Game { int x; }\n")
        write(wt, "docs/new.txt", b"not added to git\n")
        run = worktree(main, cls.env, "-Remove", "x")
        return {"main": main, "wt": wt, "run": run, "game": (wt / ASSETS / "Game.cs").read_bytes(), "new": (wt / "docs/new.txt").exists(), "state": cls.listing(main)}

    @classmethod
    def case_remove_unpushed(cls):
        main = cls.main("remove-unpushed", library=False)
        wt = plain_worktree(main, cls.env, "x")
        write(wt, "docs/local.md", b"work\n")
        commit(wt, cls.env, "a commit that was not pushed")
        return {"main": main, "wt": wt, "run": worktree(main, cls.env, "-Remove", "x"), "state": cls.listing(main)}

    @classmethod
    def case_remove_pushed(cls):
        main = cls.main("remove-pushed", library=False)
        wt = plain_worktree(main, cls.env, "x")
        write(wt, "docs/pushed.md", b"work\n")
        commit(wt, cls.env, "a commit that was pushed")
        git(wt, cls.env, "push", "-q", "origin", "claude/x")
        return {"main": main, "wt": wt, "run": worktree(main, cls.env, "-Remove", "x"), "state": cls.listing(main),
                "remote": git(main, cls.env, "ls-remote", "--heads", "origin", "claude/x")}

    @classmethod
    def squash_merged(cls, name, number, subject):
        """A pushed branch whose pull request GitHub squash-merged (or not) and whose branch was deleted: only refs/pull/N/head keeps its commits."""
        main = cls.main(name, library=False)
        wt = plain_worktree(main, cls.env, "x")
        write(wt, "docs/feature.md", b"the feature\n")
        commit(wt, cls.env, "the work of the branch")
        git(wt, cls.env, "push", "-q", "origin", "claude/x")
        tip = git(wt, cls.env, "rev-parse", "HEAD")
        git(origin_of(main), cls.env, "update-ref", f"refs/pull/{number}/head", tip)
        if subject:
            other = clone(main, cls.env, "other")
            write(other, "docs/feature.md", b"the feature\n")
            commit(other, cls.env, subject)                                                      # one squash commit with another hash
            git(other, cls.env, "push", "-q", "origin", "preview")
        git(main, cls.env, "push", "-q", "origin", "--delete", "claude/x")                       # the branch is deleted on origin and its tracking ref goes with it
        return main, wt, tip

    @classmethod
    def case_remove_squash(cls):
        main, wt, tip = cls.squash_merged("remove-squash", 7, "The feature (#7)")
        return {"main": main, "wt": wt, "tip": tip, "run": worktree(main, cls.env, "-Remove", "x"), "state": cls.listing(main)}

    @classmethod
    def case_remove_pull_head(cls):
        main, wt, tip = cls.squash_merged("remove-pull-head", 8, None)                          # a pull request that is still open
        return {"main": main, "wt": wt, "tip": tip, "run": worktree(main, cls.env, "-Remove", "x"), "state": cls.listing(main)}

    @classmethod
    def noise(cls, name, autocrlf):
        """Unity rewrites some assets with other line endings and the same text. With core.autocrlf=true (the maintainer's setting) `git status`
        lists such a file and `git diff HEAD` does not, and git worktree remove goes by `git status`."""
        main = cls.main(name, library=False, autocrlf=autocrlf)
        quiet = plain_worktree(main, cls.env, "quiet")
        write(quiet, f"{ASSETS}/Settings.asset", b"a: 1\nb: 2\n")                                # Unity rewrote it: the same text, other line endings
        busy = plain_worktree(main, cls.env, "busy")
        write(busy, f"{ASSETS}/Settings.asset", b"a: 1\nb: 2\n")
        write(busy, f"{ASSETS}/Game.cs", b"class Game { int y; }\n")                            # and a real change next to it
        return {"main": main, "quiet": quiet, "busy": busy, "status_quiet": git(quiet, cls.env, "status", "--porcelain"),
                "run_quiet": worktree(main, cls.env, "-Remove", "quiet"), "run_busy": worktree(main, cls.env, "-Remove", "busy"), "state": cls.listing(main)}

    @classmethod
    def case_remove_noise(cls):
        return cls.noise("remove-noise", "false")

    @classmethod
    def case_remove_noise_crlf(cls):
        return cls.noise("remove-noise-crlf", "true")

    @classmethod
    def case_remove_refused(cls):
        main = cls.main("remove-refused", library=False)
        wt = plain_worktree(main, cls.env, "x")
        locked = plain_worktree(main, cls.env, "locked")
        git(main, cls.env, "worktree", "lock", str(locked))
        return {"main": main, "wt": wt, "locked": locked,
                "main_checkout": worktree(main, cls.env, "-Remove", main.name),
                "unknown": worktree(main, cls.env, "-Remove", "no-such-worktree"),
                "locked_run": worktree(main, cls.env, "-Remove", "locked"),
                "inside_cwd": worktree(main, cls.env, "-Remove", "x", cwd=wt),
                "inside_script": worktree(wt, cls.env, "-Remove", "x"), "state": cls.listing(main)}

    @classmethod
    def case_remove_long(cls):
        """The deepest file of the real Library is 245 characters long and a worktree adds its own folder to that: git stops at 260 unless told otherwise."""
        main = cls.main("remove-long", library=False)
        wt = plain_worktree(main, cls.env, "x")
        deep = str(wt / PROJECT / "Library")
        for level in range(12): deep += "\\" + "d" * 24 + str(level)
        os.makedirs("\\\\?\\" + deep)                                                            # \\?\ lifts the limit for the test itself
        with open("\\\\?\\" + deep + "\\file.bin", "wb") as handle: handle.write(b"deep")
        return {"main": main, "wt": wt, "length": len(deep) + len("\\file.bin"), "run": worktree(main, cls.env, "-Remove", "x"), "state": cls.listing(main)}

    @classmethod
    def case_remove_held(cls):
        """A file that something holds open cannot be deleted on Windows: git fails half way, and the script has to say what is left."""
        main = cls.main("remove-held", library=False)
        wt = plain_worktree(main, cls.env, "x")
        held = write(wt, f"{PROJECT}/Library/held.bin", b"in use")
        with open(held, "rb"):
            run = worktree(main, cls.env, "-Remove", "x")
            left = {"folder": wt.is_dir(), "held": held.is_file()}
        return {"main": main, "wt": wt, "run": run, "state": cls.listing(main), **left}

    # -- no other worktree is touched -------------------------------------------------------------------------------------------
    @classmethod
    def case_untouched(cls):
        main = cls.main("untouched")
        quiet = add_checkout(main, cls.env, "quiet")
        busy = add_checkout(main, cls.env, "busy")
        write(busy, "docs/local.md", b"a commit of somebody else\n")
        commit(busy, cls.env, "somebody else's commit")
        write(busy, "docs/notes.md", b"an uncommitted change of somebody else\n")
        write(busy, "docs/untracked.md", b"an untracked file\n")

        def snapshot():
            return {"main": tree(main, skip=(".git", ".claude")), "quiet": tree(quiet), "busy": tree(busy),
                    "status": [git(w, cls.env, "status", "--porcelain") + git(w, cls.env, "rev-parse", "HEAD") for w in (main, quiet, busy)]}
        before = snapshot()
        created = new_worktree(main, cls.env, "mine")
        during = snapshot()
        removed = worktree(main, cls.env, "-Remove", "mine")
        return {"before": before, "during": during, "after": snapshot(), "removed": removed, "created": created}

    # -- tests: a new worktree ----------------------------------------------------------------------------------------------------
    def test_a_new_worktree_is_cut_from_origin_preview_on_a_new_branch_without_upstream(self):
        found = self.case("new")
        run, main = found["run"], found["main"]
        self.assertEqual(run.code, 0, run.stdout + run.stderr)
        data = run.data
        wt = Path(data["path"])
        self.assertTrue(same_path(wt, main / ".claude/worktrees/feature"), data["path"])
        self.assertEqual((data["ok"], data["action"], data["dryRun"], data["created"], data["problems"]), (True, "new", False, True, []))
        self.assertEqual((data["name"], data["branch"]), ("feature", "claude/feature"))
        self.assertEqual(data["base"]["commit"], found["origin"])
        self.assertEqual(git(wt, self.env, "rev-parse", "HEAD"), found["origin"])
        self.assertEqual(git(wt, self.env, "branch", "--show-current"), "claude/feature")
        self.assertNotEqual(git_code(wt, self.env, "rev-parse", "--abbrev-ref", "claude/feature@{upstream}"), 0, "the first push names the branch: no upstream")
        self.assertEqual(git(wt, self.env, "status", "--porcelain"), "", "the Library and Temp are ignored: the new worktree is clean")
        self.assertIn(str(wt).replace("\\", "/").lower(), git(main, self.env, "worktree", "list", "--porcelain").lower().replace("\\", "/"))

    def test_the_library_is_copied_whole_and_the_donor_is_left_alone(self):
        found = self.case("new")
        data = found["run"].data
        wt = Path(data["path"])
        self.assertEqual(contents(wt / PROJECT / "Library"), COPIED)
        library = data["library"]
        self.assertEqual((library["copied"], library["files"], library["bytes"]), (True, len(COPIED), sum(map(len, COPIED.values()))))
        self.assertTrue(same_path(library["donor"], found["main"]), library["donor"])
        self.assertGreaterEqual(library["seconds"], 0)
        self.assertEqual(data["unityVersion"], "6000.0.0f1")
        self.assertFalse((wt / PROJECT / "Library" / "DataStore" / "Temp").exists(), "folders named Temp are not copied")
        self.assertFalse((wt / PROJECT / "Temp").exists(), "the staging folder is gone once the copy is renamed")
        self.assertEqual(found["after"], found["before"], "the donor's Library was only read")
        self.assertIn("copying the Library", found["run"].stderr)

    def test_the_donor_is_the_main_checkout_first_and_otherwise_any_checkout_that_qualifies(self):
        found = self.case("pick")
        for key, name in (("first", "a"), ("named", "b"), ("by_path", "c"), ("both", "d")):
            self.assertEqual(found[key].code, 0, f"{key}: {found[key].stdout} {found[key].stderr}")
        self.assertTrue(same_path(found["first"].data["library"]["donor"], found["other"]))
        self.assertTrue(same_path(found["named"].data["library"]["donor"], found["other"]))
        self.assertTrue(same_path(found["by_path"].data["library"]["donor"], found["other"]))
        self.assertTrue(same_path(found["both"].data["library"]["donor"], found["main"]))
        for key in ("first", "named", "by_path", "both"):
            self.assertEqual(contents(Path(found[key].data["path"]) / PROJECT / "Library"), COPIED, key)

    # -- tests: refusals, and nothing is created ---------------------------------------------------------------------------------
    def test_a_donor_with_the_unity_lockfile_is_refused_and_nothing_is_created(self):
        found = self.case("lock")
        for key in ("auto", "explicit"):
            run = found[key]
            self.assertEqual(run.code, 1, run.stdout + run.stderr)
            self.assertFalse(run.data["ok"])
            self.assertIn("UnityLockfile", " ".join(run.data["problems"]), key)
        self.assertEqual(found["state"]["branches"], "preview")
        self.assertEqual(found["state"]["worktrees"].count("worktree "), 1)
        for name in ("x", "y"): self.assertFalse((found["main"] / ".claude/worktrees" / name).exists(), name)

    def test_without_a_library_a_plain_worktree_is_still_possible_with_no_library(self):
        found = self.case("lock")                                      # the only donor has the lockfile; -NoLibrary does not look at it
        run = found["no_library"]
        self.assertEqual(run.code, 0, run.stdout + run.stderr)
        wt = Path(run.data["path"])
        self.assertTrue(wt.is_dir())
        self.assertFalse((wt / PROJECT / "Library").exists())
        self.assertEqual(run.data["library"], {"copied": False, "reason": "-NoLibrary"})
        self.assertEqual(git(wt, self.env, "rev-parse", "HEAD"), git(found["main"], self.env, "rev-parse", "refs/remotes/origin/preview"))

    def test_a_donor_on_another_unity_version_is_refused(self):
        found = self.case("version")
        run = found["run"]
        self.assertEqual(run.code, 1, run.stdout + run.stderr)
        self.assertIn("Unity 6000.1.0f1, not 6000.0.0f1", " ".join(run.data["problems"]))
        self.assertEqual(found["state"]["branches"], "preview")
        self.assertEqual(found["state"]["worktrees"].count("worktree "), 1)

    def test_no_checkout_with_a_library_is_a_refusal_that_says_what_to_do(self):
        found = self.case("none")
        run = found["run"]
        self.assertEqual(run.code, 1, run.stdout + run.stderr)
        problem = " ".join(run.data["problems"])
        self.assertIn("none has a Library", problem)
        self.assertIn("-NoLibrary", problem)
        self.assertEqual(found["state"]["worktrees"].count("worktree "), 1)

    def test_a_name_that_is_taken_or_not_a_name_is_refused_and_what_exists_is_left_alone(self):
        found = self.case("exists")
        for key in ("taken", "branch", "bad"):
            self.assertEqual(found[key].code, 1, f"{key}: {found[key].stdout}")
            self.assertFalse(found[key].data["ok"])
        self.assertIn("already exists", " ".join(found["taken"].data["problems"]))
        self.assertIn("already exists", " ".join(found["branch"].data["problems"]))
        self.assertIn("letters, digits", " ".join(found["bad"].data["problems"]))
        self.assertEqual(found["keep"], b"somebody's folder")
        self.assertEqual(found["state"]["worktrees"].count("worktree "), 1)

    def test_dry_run_checks_everything_and_changes_nothing(self):
        found = self.case("dry")
        run = found["run"]
        self.assertEqual(run.code, 0, run.stdout + run.stderr)
        self.assertEqual((run.data["ok"], run.data["dryRun"]), (True, True))
        self.assertEqual((run.data["library"]["wouldCopy"], run.data["library"]["copied"]), (True, False))
        self.assertEqual((run.data["library"]["files"], run.data["library"]["bytes"]), (len(COPIED), sum(map(len, COPIED.values()))))
        self.assertFalse(run.data["base"]["fetched"])
        self.assertEqual(found["after"], found["before"])
        self.assertEqual(found["worktrees"], [], "no folder was created")

    def test_the_worktree_starts_from_what_origin_has_now_not_from_the_checkout_that_runs_the_script(self):
        found = self.case("fresh")
        run = found["run"]
        self.assertEqual(run.code, 0, run.stdout + run.stderr)
        self.assertTrue(run.data["base"]["fetched"])
        wt = Path(run.data["path"])
        self.assertEqual(git(wt, self.env, "rev-parse", "HEAD"), found["new_tip"])
        self.assertNotEqual(found["new_tip"], found["local_tip"])
        self.assertEqual(git_code(wt, self.env, "cat-file", "-e", f"{found['local_tip']}^{{commit}}"), 0, "the local commit exists in the shared repository")
        self.assertEqual(git_code(wt, self.env, "merge-base", "--is-ancestor", found["local_tip"], "HEAD"), 1, "but it is not in the new branch")

    def test_an_editor_on_the_donor_and_a_player_running_from_a_worktree_are_refused(self):
        found = self.case("busy")
        new, remove = found["new"], found["remove"]
        self.assertEqual(new.code, 1, new.stdout + new.stderr)
        self.assertRegex(" ".join(new.data["problems"]), r"Unity \(pid \d+\) is running on it")
        self.assertEqual(found["state"]["worktrees"].count("worktree "), 2, "nothing was created")
        self.assertEqual(remove.code, 1, remove.stdout + remove.stderr)
        self.assertRegex(" ".join(remove.data["problems"]), r"PryingEyes\.exe \(pid \d+\) is running from this worktree")
        self.assertEqual(found["after"].code, 0, found["after"].stdout + found["after"].stderr)
        self.assertFalse(found["wt"].exists(), "once the player is closed the worktree goes, with the build it ran from")

    def test_a_copy_that_arrived_whole_but_cannot_be_renamed_is_asked_again_and_then_kept_not_thrown_away(self):
        found = self.case("stuck")
        run = found["run"]
        self.assertEqual(run.code, 1, run.stdout + run.stderr)
        library = run.data["library"]
        self.assertEqual((library["copied"], library["renameTries"]), (False, 2))
        self.assertIn("arrived whole but cannot be renamed", " ".join(run.data["problems"]))
        self.assertIn("Temp\\Library.copy", " ".join(run.data["warnings"]))
        self.assertEqual(found["stage"], COPIED, "the copy is where it was made, whole")
        self.assertIn("try 1 of 2", run.stderr)

    # -- tests: removing a worktree ----------------------------------------------------------------------------------------------
    def test_a_clean_published_worktree_is_removed_with_its_library_and_its_merged_branch(self):
        found = self.case("remove_clean")
        self.assertTrue(found["existed"], "the dry run removes nothing")
        self.assertEqual((found["dry"].code, found["dry"].data["wouldRemove"], found["dry"].data["removed"]), (0, True, False), found["dry"].stdout)
        run = found["run"]
        self.assertEqual(run.code, 0, run.stdout + run.stderr)
        self.assertEqual((run.data["ok"], run.data["removed"], run.data["merged"]), (True, True, True))
        self.assertFalse(found["wt"].exists())
        self.assertEqual(run.data["branchDeleted"], {"name": "claude/x", "was": found["tip"]})
        self.assertEqual(found["state"]["branches"], "preview")
        self.assertEqual(found["state"]["worktrees"].count("worktree "), 1)

    def test_uncommitted_changes_are_listed_and_refuse_the_removal(self):
        found = self.case("remove_dirty")
        run = found["run"]
        self.assertEqual(run.code, 1, run.stdout + run.stderr)
        self.assertEqual(run.data["uncommitted"], ["?? docs/new.txt", f"M {ASSETS}/Game.cs"])
        self.assertFalse(run.data["removed"])
        self.assertTrue(found["wt"].is_dir())
        self.assertEqual(found["game"], b"class Game { int x; }\n")
        self.assertTrue(found["new"])
        self.assertIn("claude/x", found["state"]["branches"])
        self.assertEqual(found["state"]["worktrees"].count("worktree "), 2)

    def test_commits_that_are_on_no_branch_of_origin_are_listed_and_refuse_the_removal(self):
        found = self.case("remove_unpushed")
        run = found["run"]
        self.assertEqual(run.code, 1, run.stdout + run.stderr)
        self.assertEqual(len(run.data["unpushed"]), 1)
        self.assertIn("a commit that was not pushed", run.data["unpushed"][0])
        self.assertTrue(found["wt"].is_dir())
        self.assertIn("claude/x", found["state"]["branches"])

    def test_a_pushed_branch_that_is_not_merged_loses_its_worktree_but_keeps_its_branch(self):
        found = self.case("remove_pushed")
        run = found["run"]
        self.assertEqual(run.code, 0, run.stdout + run.stderr)
        self.assertEqual((run.data["removed"], run.data["merged"]), (True, False))
        self.assertFalse(found["wt"].exists())
        self.assertIn("claude/x", found["state"]["branches"])
        self.assertIn("not merged", run.data["branchKept"])
        self.assertIn("refs/heads/claude/x", found["remote"], "origin still has it")

    def test_a_squash_merged_branch_whose_remote_branch_is_gone_is_removed_and_its_branch_deleted(self):
        found = self.case("remove_squash")
        run = found["run"]
        self.assertEqual(run.code, 0, run.stdout + run.stderr)
        self.assertEqual(run.data["published"], "head of pull request #7")
        self.assertTrue(run.data["merged"])
        self.assertEqual(run.data["branchDeleted"], {"name": "claude/x", "was": found["tip"]})
        self.assertFalse(found["wt"].exists())
        self.assertEqual(found["state"]["branches"], "preview")

    def test_the_head_of_an_open_pull_request_is_published_even_when_its_branch_is_gone(self):
        found = self.case("remove_pull_head")
        run = found["run"]
        self.assertEqual(run.code, 0, run.stdout + run.stderr)
        self.assertEqual((run.data["published"], run.data["merged"]), ("head of pull request #8", False))
        self.assertFalse(found["wt"].exists())
        self.assertIn("claude/x", found["state"]["branches"], "not merged: the branch stays")

    def test_a_change_of_line_endings_only_is_not_work_but_a_real_change_next_to_it_is(self):
        for setting, name in (("autocrlf=false", "remove_noise"), ("autocrlf=true", "remove_noise_crlf")):
            with self.subTest(setting):
                found = self.case(name)
                quiet, busy = found["run_quiet"], found["run_busy"]
                self.assertEqual(found["status_quiet"], f"M {ASSETS}/Settings.asset", "git itself calls the file modified (git() strips the leading space)")
                self.assertEqual(quiet.code, 0, quiet.stdout + quiet.stderr)
                self.assertEqual(quiet.data["lineEndingsRestored"], 1)
                self.assertFalse(found["quiet"].exists())
                self.assertEqual(busy.code, 1, busy.stdout + busy.stderr)
                self.assertEqual(busy.data["uncommitted"], [f"M {ASSETS}/Game.cs"], "the file that only changed line endings is not listed")
                self.assertEqual((found["busy"] / ASSETS / "Settings.asset").read_bytes(), b"a: 1\nb: 2\n", "a refusal changes nothing in the worktree")
                self.assertEqual((found["busy"] / ASSETS / "Game.cs").read_bytes(), b"class Game { int y; }\n")

    def test_the_main_checkout_a_locked_worktree_an_unknown_name_and_the_folder_the_script_runs_from_are_refused(self):
        found = self.case("remove_refused")
        text = lambda run: " ".join(run.data["problems"])
        for key in ("main_checkout", "unknown", "locked_run", "inside_cwd", "inside_script"):
            self.assertEqual(found[key].code, 1, f"{key}: {found[key].stdout} {found[key].stderr}")
        self.assertIn("main checkout", text(found["main_checkout"]))
        self.assertIn("no worktree", text(found["unknown"]))
        self.assertIn("locked", text(found["locked_run"]))
        self.assertIn("inside the worktree", text(found["inside_cwd"]))
        self.assertIn("inside the worktree", text(found["inside_script"]))
        self.assertTrue(found["main"].is_dir() and found["wt"].is_dir() and found["locked"].is_dir())
        self.assertEqual(found["state"]["worktrees"].count("worktree "), 3)

    def test_a_worktree_with_a_path_over_260_characters_is_removed_whole(self):
        found = self.case("remove_long")
        run = found["run"]
        self.assertGreater(found["length"], 400, "the fixture is far past the 260 characters that git stops at by default")
        self.assertEqual(run.code, 0, run.stdout + run.stderr)
        self.assertEqual((run.data["removed"], run.data["problems"]), (True, []))
        self.assertFalse(found["wt"].exists(), "nothing is left behind")
        self.assertEqual(found["state"]["worktrees"].count("worktree "), 1)
        self.assertEqual(found["state"]["branches"], "preview")

    def test_a_folder_that_git_cannot_delete_is_reported_with_what_is_left_and_the_branch_stays(self):
        found = self.case("remove_held")
        run = found["run"]
        self.assertEqual(run.code, 1, run.stdout + run.stderr)
        self.assertFalse(run.data["removed"])
        self.assertTrue(found["folder"] and found["held"], "the file that is held open is still there")
        self.assertIn("claude/x", found["state"]["branches"], "a branch is deleted only after its worktree is gone")
        problem = " ".join(run.data["problems"])
        if "leftover" in run.data:                                                               # git drops its record first and its folder second
            self.assertTrue(same_path(run.data["leftover"], found["wt"]), run.data["leftover"])
            self.assertIn("record of the worktree", problem)
        else:                                                                                    # (a git that keeps the record until the folder is gone)
            self.assertIn("git worktree remove failed", problem)

    # -- tests: no other worktree is touched ----------------------------------------------------------------------------------------
    def test_creating_and_removing_a_worktree_touches_no_other_worktree_and_no_donor(self):
        found = self.case("untouched")
        self.assertEqual(found["removed"].code, 0, found["removed"].stdout + found["removed"].stderr)
        for stage in ("during", "after"):
            self.assertEqual(found[stage], found["before"], f"{stage}: the other worktrees, the donor's Library and the working copy of the main checkout are as they were")
        self.assertFalse(found["created"].exists())


if __name__ == "__main__":
    unittest.main()
