"""What a run of the player leaves behind, and how to read it: its log, and the result.json of the in-player harness.

Standard library only, nothing is run. Shared by the in-player suites of the studio (tools/studio_pryingeyes.py) and by
tools/check_navigation.py, so that "which exceptions count" is decided in one place.

A test of the game prints one line for each check and one result line, and quits with 0 (passed) or 1:

    [PryingEyes] survivaltest ok la candela si accende (3 candele)
    [PryingEyes] survivaltest FALLITO la leva costa 1000 (punti 1500)
    [PryingEyes] survivaltest RISULTATO FALLITA: 17/18

The co-op tests say which of the two processes speaks, after the suite and with no colon (the standard form, with `Role`: CoopTest and PistaCoopTest):

    [PryingEyes] cooptest ospite ok i due giocatori si vedono (2 pupazzi)
    [PryingEyes] pistacooptest ospite ok la gara parte (3 giri)
    [PryingEyes] pistacooptest ospite RISULTATO SUPERATA: 9/9

CoopTest printed another form until it came to the harness (T08): a colon after the process, the detail in square brackets and a tail to the result. It is still
read, so that the log of an older build keeps its verdict:

    [PryingEyes] cooptest ospite: ok       i due giocatori si vedono   [2 pupazzi]
    [PryingEyes] cooptest ospitato: RISULTATO SUPERATA: 12/12 verifiche ok

A test that uses the harness (docs/engineering/agent-tooling.md, "Results as data") also writes result.json into the folder given by
-testout, and that file is what a tool reads first; the lines are what it falls back on, so that a suite can be run before it is ported.
"""
import json
from pathlib import Path
import re

# The render-graph errors that -nographics always prints are not the test's exceptions.
NOISE = re.compile(r"UnityEngine\.Rendering\.(Universal|RenderGraph)")
EXCEPTION = re.compile(r"^\w*Exception: ")
ROLE = r"(?: (?P<role>ospite|ospitato):?)?"                       # the co-op tests: `cooptest ospite: ...`, `pistacooptest ospitato ...`
CHECK = re.compile(rf"^\[PryingEyes\] (?P<suite>\w+){ROLE} (?P<state>ok|FALLITO)\s+(?P<text>.*?)\s*$")
RESULT = re.compile(rf"^\[PryingEyes\] (?P<suite>\w+){ROLE} RISULTATO (?P<word>SUPERATA|FALLITA): (?P<passed>\d+)/(?P<total>\d+)(?: verifiche ok(?: \(interrotta\))?)?\s*$")
DETAIL = re.compile(r"(?P<name>.*) \((?P<detail>[^()]*)\)")
DETAIL_OF_COOP = re.compile(r"(?P<name>.*?)\s+\[(?P<detail>[^\[\]]*)\]")


def read_text(path):
    """The text of a log; empty when the file is not there (a run that never started leaves none). Unity writes UTF-8."""
    try:
        return Path(path).read_text(encoding="utf-8-sig", errors="replace")
    except OSError:
        return ""


def exceptions(text):
    """The exceptions of a Unity log, minus the render-graph errors that a run without a graphics card always prints:
    a line that starts with `...Exception: ` unless the noise is in it or in the three lines after it."""
    lines = text.splitlines()
    return [line for index, line in enumerate(lines) if EXCEPTION.match(line) and not NOISE.search("\n".join(lines[index:index + 4]))]


def check_lines(text, suite=None):
    """The check lines of the log, in order, as {suite, name, ok, detail}; those of `suite` only when it is given. The game prints
    `name (detail)`, and CoopTest `name   [detail]`: the detail is what is in the last pair, when it holds no other, otherwise
    the whole text is the name."""
    found = []
    for line in text.splitlines():
        match = CHECK.match(line)
        if match and suite in (None, match["suite"]):
            split = DETAIL.fullmatch(match["text"]) or (DETAIL_OF_COOP.fullmatch(match["text"]) if match["role"] else None)
            found.append({"suite": match["suite"], "name": split["name"] if split else match["text"],
                          "ok": match["state"] == "ok", "detail": split["detail"] if split else ""})
    return found


def result_lines(text, suite=None):
    """The result lines of the log, in order, as {suite, ok, passed, total}; those of `suite` only when it is given."""
    found = []
    for line in text.splitlines():
        match = RESULT.match(line)
        if match and suite in (None, match["suite"]):
            found.append({"suite": match["suite"], "ok": match["word"] == "SUPERATA", "passed": int(match["passed"]), "total": int(match["total"])})
    return found


def read_result(path, suite):
    """The result.json of the harness at `path`, as {ok, checks: [{name, ok, detail}], exceptions}; None when there is no such file.
    A file that is there but cannot be trusted (not JSON, another version, another suite, another shape) is a ValueError that says why:
    the caller must not fall back to the log lines, which would hide a bug of the harness."""
    path = Path(path)
    if not path.is_file():
        return None
    try:
        data = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError) as error:
        raise ValueError(f"{path.name} is not JSON ({error})") from error
    if not isinstance(data, dict):
        raise ValueError(f"{path.name} is not an object")
    if data.get("schemaVersion") != 1:
        raise ValueError(f"{path.name} has schemaVersion {data.get('schemaVersion')!r}, this reader knows 1")
    if data.get("suite") != suite:
        raise ValueError(f"{path.name} is the result of {data.get('suite')!r}, not of {suite!r}")
    checks = data.get("checks")
    if not isinstance(data.get("ok"), bool) or not isinstance(checks, list) or not all(
            isinstance(check, dict) and isinstance(check.get("name"), str) and isinstance(check.get("ok"), bool) for check in checks):
        raise ValueError(f"{path.name} lacks ok (true or false) or checks (a list of {{name, ok}})")
    return {"ok": data["ok"], "exceptions": data.get("exceptions", 0),
            "checks": [{"name": check["name"], "ok": check["ok"], "detail": str(check.get("detail", ""))} for check in checks]}


def verdict(suite, log_text, result_path=None):
    """What a run says about itself, from result.json when it exists and from the log otherwise:

        {source: "result.json" | "log-line" | "none", ok, total, passed, checks: [{name, ok, detail}], exceptions: [lines of the log]}

    `total` is the number of checks the run ran. With the lines it is the one on the result line, which can be more than the lines
    of checks when a test prints only its failures; the lines of checks are all that is known of each. `ok` is False when there is
    no result at all. A ValueError is a result.json that cannot be trusted (read_result)."""
    found = {"source": "none", "ok": False, "total": 0, "passed": 0, "checks": [], "exceptions": exceptions(log_text)}
    harness = read_result(result_path, suite) if result_path is not None else None
    if harness is not None:
        passed = sum(check["ok"] for check in harness["checks"])
        found.update(source="result.json", ok=harness["ok"] and passed == len(harness["checks"]), total=len(harness["checks"]), passed=passed, checks=harness["checks"])
        return found
    lines = result_lines(log_text, suite)
    if lines:
        last, checks = lines[-1], check_lines(log_text, suite)
        found.update(source="log-line", total=last["total"], passed=last["passed"], checks=checks,
                     ok=last["ok"] and last["passed"] == last["total"] and all(check["ok"] for check in checks))
    return found


def combine(parts):
    """The verdict of a run made of several processes (a co-op scenario, one log each). One process alone is returned as it is. Of several,
    those that left a result count: they must all say they passed, and their checks add up; the exceptions of every log are kept. A process
    that left no result is not an error here (a scenario kills one on purpose): the script that started them owns that."""
    parts = list(parts)
    if len(parts) == 1:
        return parts[0]
    heard = [part for part in parts if part["source"] != "none"]
    return {"source": "log-line" if heard else "none", "ok": bool(heard) and all(part["ok"] for part in heard),
            "total": sum(part["total"] for part in heard), "passed": sum(part["passed"] for part in heard),
            "checks": [check for part in heard for check in part["checks"]], "exceptions": [line for part in parts for line in part["exceptions"]]}
