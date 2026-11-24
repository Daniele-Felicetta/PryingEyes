"""The smallest project the production studio engine can run: one catalog of sprites, two checks.

It exists to prove the engine is reusable (nothing here is shared with the game profile except the engine) and to give the
engine's tests, and any new project, a working example of the Project / ContentType / Suite contract. The two suites show what a suite
can say about itself: `smoke` needs nothing, `careful` needs something and declares hazards, so it runs only when each is allowed.
"""
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from studio_engine import ContentType, Project, Suite  # noqa: E402


class Sprites(ContentType):
    """A catalog `sprites.json` of {"id", "name", "pixels"}; building one writes its pixels file through a recorded step."""
    name = "sprite"

    def __init__(self, root): self.catalog = Path(root) / "sprites.json"
    def load(self, strict=True):
        data = json.loads(self.catalog.read_text(encoding="utf-8"))
        if strict and not all(isinstance(entry.get("pixels"), int) and entry["pixels"] > 0 for entry in data["sprites"]):
            raise ValueError("SPRITE_PIXELS_INVALID")
        return data
    def entries(self, data): return data["sprites"]
    def entry_id(self, entry): return entry["id"]
    def summary(self, entry): return f"{entry['id']:10} {entry['name']} ({entry['pixels']} px)"
    def files(self): return [self.catalog]

    def build(self, ctx, entry):
        built = ctx.build_dir / (entry["id"] + ".pixels")
        ctx.build_dir.mkdir(parents=True, exist_ok=True)
        # a real step: its command, log and exit code land in the job report like any production tool's
        ctx.job.run("make", [sys.executable, "-c", "import sys; open(sys.argv[1], 'w').write('#' * int(sys.argv[2]))", str(built), str(entry["pixels"])])
        ctx.job.report["artifacts"].append(str(built))
        return built


class ToyProject(Project):
    name = "toy"
    description = "Toy project for the production studio engine"
    default_subject = "sprites"

    def __init__(self, root, output=None):
        self.root = Path(root).resolve()
        self.default_output = self.root / "studio"
        self.entry_script = Path(__file__).resolve()
        self.content_types = (Sprites(self.root),)
        self.suites = {"smoke": Suite(lambda ctx, runtime: ctx.job.run("smoke", [sys.executable, "-c", "print('ok')"]),
                                      kind="check", seconds=1, about="Starts the interpreter once"),
                       "careful": Suite(lambda ctx, runtime: ctx.job.run("careful", [sys.executable, "-c", "print('ok')"]),
                                        kind="demo", needs=("easel",), hazards=("noise", "paint"), seconds=2,
                                        about="Declares what it can disturb: it runs only when every hazard is allowed")}
        self.publish_dir = self.root / "published"
        super().__init__(output)

    def doctor(self, catalogs): return f"{len(catalogs['sprite']['sprites'])} sprites"

    def doctor_facts(self):
        """What `doctor --json` reports: facts of any shape, and `problems`, the one key the engine reads (the command exits 1 when it is not empty)."""
        try: count = len(json.loads(self.content_types[0].catalog.read_text(encoding="utf-8"))["sprites"])
        except (OSError, ValueError, KeyError): return {"sprites": None, "problems": ["SPRITES_UNREADABLE: sprites.json is missing or is not a catalog"]}
        return {"sprites": count, "problems": [] if count else ["SPRITES_EMPTY: sprites.json lists no sprite"]}


if __name__ == "__main__":
    # python toy_project.py ROOT <studio arguments>: the same command line the real project has
    from studio_engine import main
    raise SystemExit(main(ToyProject(sys.argv[1]), sys.argv[2:]))
