"""tools/release.ps1 prepares a release: checks before anything is built, a search for this PC's paths in the files that will be shipped,
and the zip with its checksum, verified entry by entry.

Here the real script runs against throwaway git repositories (with a local bare `origin`) and fake build folders: no Unity and no real
build. The build step (Unity) and the smoke step (the packaged player) are acceptance checks on the real project, run by the script itself
(`tools/release.ps1 -Version x.y.z`, see docs/engineering/building-and-shipping.md). That needs Windows PowerShell 5.1 and git, so the tests
that run the script are skipped elsewhere."""
import concurrent.futures
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import stat
import subprocess
import sys
import tempfile
import unittest
import zipfile

TOOLS = Path(__file__).resolve().parent
SCRIPT = TOOLS / "release.ps1"
TEMPLATE = TOOLS / "release" / "LEGGIMI.txt"
VERSION = "9.9.9"
ASSETS = "unity/PryingEyes/Assets/_PryingEyes"
BUILD = f"unity/PryingEyes/Builds/Release-v{VERSION}"
ZIP_NAME = f"PryingEyes-v{VERSION}-Windows-x64.zip"

COMMITTED = {
    ".gitignore": b"unity_logs/\nunity/PryingEyes/Builds/\n",
    "unity/PryingEyes/ProjectSettings/ProjectVersion.txt": b"m_EditorVersion: 6000.0.0f1\n",
    "unity/PryingEyes/ProjectSettings/ProjectSettings.asset": f"PlayerSettings:\n  bundleVersion: {VERSION}\n  preloadedAssets: []\n".encode(),
    f"{ASSETS}/Scripts/Game.cs": b"class Game {}\n",
    f"{ASSETS}/Scripts/EolCrlf.cs": b"a\r\nb\r\n",         # committed with CRLF, rewritten with LF: not a source change
    f"{ASSETS}/Materials/Wall.mat": b"m: 1\n",
    f"{ASSETS}/Scenes/Torre.unity": b"scene: 1\n",
}
BUILD_FILES = {
    "PryingEyes.exe": b"MZ the player",
    "UnityPlayer.dll": b"player",
    "PryingEyes_Data/Managed/Assembly-CSharp.dll": b"assembly",
    "PryingEyes_Data/resources.assets": b"assets" * 5000,
    "PryingEyes_Data/level 0": b"a name with a space",
    "D3D12/d3d12.dll": b"d3d12",
    "MonoBleedingEdge/etc/mono/config": b"config",
}
NOT_SHIPPED = {
    "PryingEyes_BackUpThisFolder_ButDontShipItWithYourGame/x86_64/lib_burst_generated.txt": b"backup",
    "PryingEyes_BurstDebugInformation_DoNotShip/Data/Plugins/x86_64/lib.pdb": b"symbols",
}


def write(repo, relative, content):
    path = repo / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(content)
    return path


def git(repo, env, *args):
    return subprocess.run(["git", "-C", str(repo), *args], check=True, capture_output=True, text=True, env=env).stdout.strip()


def make_repo(root, env, name):
    """A repository shaped like the real one, committed and pushed to a local bare origin, so HEAD equals origin/preview."""
    repo = root / name
    for relative, content in COMMITTED.items(): write(repo, relative, content)
    shutil.copyfile(SCRIPT, write(repo, "tools/release.ps1", b""))
    shutil.copyfile(TEMPLATE, write(repo, "tools/release/LEGGIMI.txt", b""))
    git(repo, env, "init", "-q", "-b", "preview")
    git(repo, env, "config", "core.autocrlf", "false")
    git(repo, env, "add", ".")
    git(repo, env, "-c", "user.name=release-test", "-c", "user.email=release-test@example.invalid", "commit", "-q", "-m", "fixture")
    origin = root / f"{name}-origin.git"
    git(root, env, "init", "-q", "--bare", "-b", "preview", str(origin))
    git(repo, env, "remote", "add", "origin", str(origin))
    git(repo, env, "push", "-q", "origin", "preview")
    return repo


def commit_all(repo, env, message):
    git(repo, env, "add", ".")
    git(repo, env, "-c", "user.name=release-test", "-c", "user.email=release-test@example.invalid", "commit", "-q", "-m", message)


def fake_build(repo, env, extra=None):
    """A finished build folder plus the marker the build step leaves for the others; returns (folder, commit)."""
    head = git(repo, env, "rev-parse", "HEAD")
    build = repo / BUILD
    for relative, content in {**BUILD_FILES, **NOT_SHIPPED, **(extra or {})}.items(): write(build, relative, content)
    marker = {"version": VERSION, "commit": head, "outDir": str(build), "builtAt": "2026-01-01T00:00:00", "buildLog": "none"}
    write(repo, f"unity_logs/release_v{VERSION}.json", json.dumps(marker).encode("utf-8"))
    return build, head


def release(repo, env, step, *flags):
    command = ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(repo / "tools/release.ps1"), "-Version", VERSION,
               "-Step", step, "-UnityExe", sys.executable, "-MinFreeGB", "0", *flags]
    run = subprocess.run(command, capture_output=True, text=True, errors="replace", env=env, timeout=240)
    return run.returncode, run.stdout + run.stderr


def listed_sources(output):
    return {(m[1], m[2]) for m in (re.match(r"^  (M|A|D|T|\?\?) (\S+)$", line) for line in output.splitlines()) if m}


def sha256(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def force_remove(function, path, _):
    os.chmod(path, stat.S_IWRITE)
    function(path)


class ReleaseScriptSource(unittest.TestCase):
    def test_script_and_template_are_pure_ascii(self):
        """Windows PowerShell 5.1 reads a script without BOM in the ANSI code page: an accent corrupts it."""
        for path in (SCRIPT, TEMPLATE):
            bad = [i for i, byte in enumerate(path.read_bytes()) if byte > 127]
            self.assertEqual(bad, [], f"non-ASCII byte at offset {bad[:1]} of {path.name}")

    def test_template_has_both_placeholders(self):
        text = TEMPLATE.read_text(encoding="ascii")
        self.assertIn("{VERSION}", text)
        self.assertIn("{COMMIT}", text)

    @unittest.skipUnless(os.name == "nt" and shutil.which("powershell"), "needs Windows PowerShell 5.1")
    def test_script_parses(self):
        command = ("$t=$null;$e=$null;[void][System.Management.Automation.Language.Parser]::ParseFile('" + str(SCRIPT) + "',[ref]$t,[ref]$e);"
                   "$e|ForEach-Object{$_.Message+' @'+$_.Extent.StartLineNumber};exit $e.Count")
        run = subprocess.run(["powershell", "-NoProfile", "-Command", command], capture_output=True, text=True, errors="replace", timeout=60)
        self.assertEqual(run.returncode, 0, run.stdout + run.stderr)


@unittest.skipUnless(os.name == "nt" and shutil.which("powershell") and shutil.which("git"), "release.ps1 needs Windows PowerShell 5.1 and git")
class ReleaseScriptOnThrowawayRepositories(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        # resolve(): a runner's TEMP can be an 8.3 short path and PowerShell expands it; the repository must be named the way the script sees it
        cls.root = Path(tempfile.mkdtemp(prefix="release-test-")).resolve()
        cls.addClassCleanup(shutil.rmtree, cls.root, onerror=force_remove)
        empty = write(cls.root, "empty.gitconfig", b"")
        # No PSModulePath: started from pwsh 7 (as on the runner) the child would inherit its module path and lose modules of Windows PowerShell 5.1
        inherited = {key: value for key, value in os.environ.items() if key.upper() != "PSMODULEPATH"}
        cls.env = {**inherited, "GIT_CONFIG_GLOBAL": str(empty), "GIT_CONFIG_NOSYSTEM": "1", "GIT_TERMINAL_PROMPT": "0"}
        with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
            cases = {name: pool.submit(getattr(cls, "case_" + name)) for name in
                     ("clean", "version", "unpushed", "dirty", "build_refused", "outdir", "no_template", "no_marker", "zip", "scan_hits", "scan_clean")}
            cls.runs = {name: case.result() for name, case in cases.items()}

    # -- the checks -------------------------------------------------------------------------------------------------------------
    @classmethod
    def case_clean(cls):
        return release(make_repo(cls.root, cls.env, "clean"), cls.env, "check")

    @classmethod
    def case_version(cls):
        repo = make_repo(cls.root, cls.env, "version")
        write(repo, "unity/PryingEyes/ProjectSettings/ProjectSettings.asset", b"PlayerSettings:\n  bundleVersion: 9.9.8\n")
        commit_all(repo, cls.env, "other version")
        git(repo, cls.env, "push", "-q", "origin", "preview")
        return release(repo, cls.env, "check")

    @classmethod
    def case_unpushed(cls):
        repo = make_repo(cls.root, cls.env, "unpushed")
        write(repo, "docs/later.md", b"a commit that was not pushed\n")
        commit_all(repo, cls.env, "not pushed")
        return release(repo, cls.env, "check")

    @classmethod
    def dirty_repo(cls, name):
        repo = make_repo(cls.root, cls.env, name)
        write(repo, f"{ASSETS}/Scripts/Game.cs", b"class Game { int x; }\n")                 # M
        write(repo, f"{ASSETS}/Scripts/New.cs", b"class New {}\n")                            # ??
        write(repo, f"{ASSETS}/Materials/Wall.mat", b"m: 2\n")                                # regenerated material: not counted
        write(repo, f"{ASSETS}/Scenes/Torre.unity", b"scene: 2\n")                            # generated scene: not counted
        write(repo, f"{ASSETS}/Scripts/EolCrlf.cs", b"a\nb\n")                                # line endings only: not counted
        return repo

    @classmethod
    def case_dirty(cls):
        return release(cls.dirty_repo("dirty"), cls.env, "check")

    @classmethod
    def case_build_refused(cls):
        return release(cls.dirty_repo("build-refused"), cls.env, "build")

    @classmethod
    def case_outdir(cls):
        repo = make_repo(cls.root, cls.env, "outdir")
        fake_build(repo, cls.env)
        return release(repo, cls.env, "check")

    @classmethod
    def case_no_template(cls):
        repo = make_repo(cls.root, cls.env, "no-template")
        (repo / "tools/release/LEGGIMI.txt").unlink()
        return release(repo, cls.env, "check")

    @classmethod
    def case_no_marker(cls):
        return release(make_repo(cls.root, cls.env, "no-marker"), cls.env, "zip")

    # -- the zip ------------------------------------------------------------------------------------------------------------------
    @classmethod
    def case_zip(cls):
        repo = make_repo(cls.root, cls.env, "zip")
        build, head = fake_build(repo, cls.env)
        first = release(repo, cls.env, "zip")
        zip_path = repo / "unity/PryingEyes/Builds/Releases" / ZIP_NAME
        before = sha256(zip_path) if zip_path.exists() else ""
        second = release(repo, cls.env, "zip")
        return repo, build, head, first, second, before

    # -- the search for this PC's paths -------------------------------------------------------------------------------------------
    @classmethod
    def case_scan_hits(cls):
        repo = make_repo(cls.root, cls.env, "scan-hits")
        path = str(repo)
        fake_build(repo, cls.env, {
            "PryingEyes_Data/Managed/Hit8.dll": b"xx " + path.encode("utf-8") + b" yy",
            "PryingEyes_Data/Hit16.assets": path.upper().encode("utf-16-le"),                 # UTF-16 and other letter case
            "PryingEyes_Data/hit_slash.json": path.replace("\\", "/").encode("utf-8"),        # forward slashes
            "PryingEyes_BackUpThisFolder_ButDontShipItWithYourGame/x86_64/hit_backup.txt": path.encode("utf-8"),   # not shipped: ignored
        })
        return release(repo, cls.env, "scan")

    @classmethod
    def case_scan_clean(cls):
        repo = make_repo(cls.root, cls.env, "scan-clean")
        fake_build(repo, cls.env)
        return release(repo, cls.env, "scan")

    # -- tests ----------------------------------------------------------------------------------------------------------------------
    def test_a_clean_pushed_tree_passes_the_checks(self):
        code, output = self.runs["clean"]
        self.assertEqual(code, 0, output)
        self.assertIn(f"bundleVersion = {VERSION}", output)
        self.assertIn("sorgenti non committati (derivati esclusi): 0", output)
        self.assertIn(f"RELEASE: OK (versione {VERSION}, passi: check)", output)

    def test_the_version_in_the_project_must_be_the_one_asked_for(self):
        code, output = self.runs["version"]
        self.assertEqual(code, 1, output)
        self.assertIn(f"RELEASE: FALLITO - bundleVersion (9.9.8) diverso dalla versione chiesta ({VERSION})", output)

    def test_a_commit_that_is_not_on_origin_is_refused(self):
        code, output = self.runs["unpushed"]
        self.assertEqual(code, 1, output)
        self.assertIn("RELEASE: FALLITO - HEAD diverso da origin/preview", output)

    def test_uncommitted_sources_are_refused_and_derived_files_do_not_count(self):
        code, output = self.runs["dirty"]
        self.assertEqual(code, 1, output)
        self.assertEqual(listed_sources(output), {("M", f"{ASSETS}/Scripts/Game.cs"), ("??", f"{ASSETS}/Scripts/New.cs")}, output)
        self.assertIn("RELEASE: FALLITO - ci sono 2 sorgenti non committati", output)
        for name in ("Wall.mat", "Torre.unity", "EolCrlf"): self.assertNotIn(name, output)

    def test_the_build_does_not_start_when_the_checks_fail(self):
        code, output = self.runs["build_refused"]
        self.assertEqual(code, 1, output)
        self.assertIn("RELEASE: FALLITO - ci sono 2 sorgenti non committati", output)
        self.assertNotIn("=== build ===", output)
        self.assertNotIn("unity:", output)

    def test_an_existing_output_folder_is_not_touched(self):
        code, output = self.runs["outdir"]
        self.assertEqual(code, 1, output)
        self.assertIn("esiste gia': non lo tocco", output)

    def test_a_missing_readme_template_is_caught_before_the_build(self):
        code, output = self.runs["no_template"]
        self.assertEqual(code, 1, output)
        self.assertIn("RELEASE: FALLITO - manca il modello del LEGGIMI", output)

    def test_the_later_steps_need_the_marker_of_the_build_step(self):
        code, output = self.runs["no_marker"]
        self.assertEqual(code, 1, output)
        self.assertIn(f"RELEASE: FALLITO - manca", output)
        self.assertIn(f"release_v{VERSION}.json", output)

    def test_the_zip_has_the_files_at_its_root_and_leaves_out_what_is_not_shipped(self):
        repo, build, head, first, _, _ = self.runs["zip"]
        code, output = first
        self.assertEqual(code, 0, output)
        self.assertIn("diverse: 0, mancanti: 0", output)
        zip_path = repo / "unity/PryingEyes/Builds/Releases" / ZIP_NAME
        with zipfile.ZipFile(zip_path) as archive:
            self.assertEqual(set(archive.namelist()), set(BUILD_FILES) | {"LEGGIMI.txt"})
            for name, content in BUILD_FILES.items(): self.assertEqual(archive.read(name), content, name)
            self.assertEqual(archive.testzip(), None)

    def test_the_readme_in_the_zip_names_the_version_and_the_built_commit(self):
        repo, _, head, first, _, _ = self.runs["zip"]
        with zipfile.ZipFile(repo / "unity/PryingEyes/Builds/Releases" / ZIP_NAME) as archive:
            text = archive.read("LEGGIMI.txt").decode("utf-8")
        self.assertTrue(text.startswith(f"Prying Eyes {VERSION} - Windows x64\r\nCommit: {head[:7]}\r\n"), text[:80])
        self.assertNotRegex(text, r"\{[A-Z]+\}")
        self.assertNotIn("\n", text.replace("\r\n", ""), "the readme is written with Windows line endings")

    def test_the_checksum_file_has_the_hash_of_the_zip(self):
        repo, _, _, _, _, _ = self.runs["zip"]
        releases = repo / "unity/PryingEyes/Builds/Releases"
        sums = (releases / f"v{VERSION}" / "SHA256SUMS.txt").read_bytes()
        self.assertEqual(sums, f"{sha256(releases / ZIP_NAME)}  {ZIP_NAME}\n".encode("ascii"))

    def test_an_existing_zip_is_never_overwritten(self):
        repo, _, _, _, second, before = self.runs["zip"]
        code, output = second
        self.assertEqual(code, 1, output)
        self.assertIn("esiste gia': non lo sovrascrivo", output)
        self.assertEqual(sha256(repo / "unity/PryingEyes/Builds/Releases" / ZIP_NAME), before)

    def test_the_search_finds_this_pcs_paths_in_every_encoding_and_only_in_shipped_files(self):
        code, output = self.runs["scan_hits"]
        self.assertEqual(code, 1, output)
        files = {match[1].replace("\\", "/") for match in (re.match(r"^  (\S.*?): .+ \[(utf8|utf16)\] x\d+$", line) for line in output.splitlines()) if match}
        self.assertEqual(files, {"PryingEyes_Data/Managed/Hit8.dll", "PryingEyes_Data/Hit16.assets", "PryingEyes_Data/hit_slash.json"}, output)
        self.assertIn("RELEASE: FALLITO - nei file da spedire ci sono percorsi di questo PC", output)

    def test_a_build_without_the_paths_passes_the_search(self):
        code, output = self.runs["scan_clean"]
        self.assertEqual(code, 0, output)
        self.assertIn("occorrenze: 0", output)
        self.assertIn("scan: ok", output)


if __name__ == "__main__":
    unittest.main()
