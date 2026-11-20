"""Room variety report, read from the -roomcensus log of ProcCheck.

ProcCheck writes one CENSUS line per furnished room (see Census in RoomKits.cs):

    [PryingEyes] CENSUS tema=Albergo tipo=Normale stanza=Camera area=45 oggetti=3 copertura=0.080 quote=1 w=7.2 d=6.3 arredi=arr_letto:0.5:2.1:0|...

Each prop is name:x:z:facing, with x and z in metres from the inner corner of the room and the facing in degrees.
Lines written before the layout was logged have no arredi field, and a room with no prop at all (a lift cabin) has an
empty one: both count for coverage only.

For every room kind the report gives:
  comp     distinct sets of furniture names, and the share of the most common set ("top"): repetition by content;
  layout   distinct arrangements (positions to the metre, facing to the quarter turn) and the share of the most common;
  cover    mean share of the floor covered by solid props;
  aligned  share of props facing within 3 degrees of a wall axis: a piece that is off-axis reads as dropped at random.

--max-top X fails the run (exit 1) when a kind with at least --min-rooms rooms has a most common composition above X.

Usage: python tools/room_variety.py LOG [LOG ...] [--max-top 0.3] [--min-rooms 10]
"""

from __future__ import annotations

import argparse
import collections
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path

ALIGN_TOLERANCE_DEG = 3.0
DEFAULT_MIN_ROOMS = 10

_CENSUS = re.compile(r"CENSUS\s+(?P<body>.*)$")


@dataclass(frozen=True)
class Prop:
    name: str
    x: float
    z: float
    facing: float


@dataclass
class Room:
    kind: str
    coverage: float
    props: list[Prop] | None  # None: the line predates the layout field


@dataclass
class KindSummary:
    kind: str
    rooms: int = 0
    coverage_sum: float = 0.0
    with_layout: int = 0
    props: int = 0
    aligned: int = 0
    compositions: collections.Counter = field(default_factory=collections.Counter)
    layouts: collections.Counter = field(default_factory=collections.Counter)

    @property
    def mean_coverage(self) -> float:
        return self.coverage_sum / self.rooms

    @property
    def top_composition_share(self) -> float | None:
        if not self.with_layout:
            return None
        return self.compositions.most_common(1)[0][1] / self.with_layout

    @property
    def top_layout_share(self) -> float | None:
        if not self.with_layout:
            return None
        return self.layouts.most_common(1)[0][1] / self.with_layout

    @property
    def aligned_share(self) -> float | None:
        return self.aligned / self.props if self.props else None


def parse_props(text: str) -> list[Prop]:
    """The arredi field: name:x:z:facing entries separated by '|'. Raises ValueError on a malformed entry."""
    props = []
    for entry in filter(None, text.split("|")):
        name, x, z, facing = entry.split(":")
        props.append(Prop(name, float(x), float(z), float(facing)))
    return props


def parse_line(line: str) -> Room | None:
    """One CENSUS line as a Room; None for any other log line."""
    match = _CENSUS.search(line)
    if match is None:
        return None
    fields = dict(token.split("=", 1) for token in match.group("body").split() if "=" in token)
    if "stanza" not in fields or "copertura" not in fields:
        return None
    props = parse_props(fields["arredi"]) if "arredi" in fields else None
    return Room(kind=fields["stanza"], coverage=float(fields["copertura"]), props=props)


def composition(props: list[Prop]) -> tuple[str, ...]:
    """The furniture names of a room, as a set: the same pieces in any place."""
    return tuple(sorted(p.name for p in props))


def layout(props: list[Prop]) -> tuple:
    """Arrangement to the metre and the quarter turn: two rooms with the same layout look alike at a glance."""
    return tuple(sorted((p.name, round(p.x), round(p.z), (round(p.facing / 90.0) % 4) * 90) for p in props))


def is_aligned(facing: float) -> bool:
    off = facing % 90.0
    return min(off, 90.0 - off) <= ALIGN_TOLERANCE_DEG


def summarize(rooms: list[Room]) -> dict[str, KindSummary]:
    kinds: dict[str, KindSummary] = {}
    for room in rooms:
        summary = kinds.setdefault(room.kind, KindSummary(room.kind))
        summary.rooms += 1
        summary.coverage_sum += room.coverage
        if not room.props:      # no layout logged, or no furniture at all (lift cabins): nothing to compare
            continue
        summary.with_layout += 1
        summary.props += len(room.props)
        summary.aligned += sum(1 for p in room.props if is_aligned(p.facing))
        summary.compositions[composition(room.props)] += 1
        summary.layouts[layout(room.props)] += 1
    return kinds


def _pct(value: float | None) -> str:
    return "-" if value is None else f"{value:.0%}"


def format_report(kinds: dict[str, KindSummary]) -> str:
    header = f"{'kind':<14}{'rooms':>6}{'comp':>6}{'top':>6}{'layout':>8}{'top':>6}{'cover':>8}{'aligned':>9}"
    lines = [header, "-" * len(header)]
    for s in sorted(kinds.values(), key=lambda k: (-k.rooms, k.kind)):
        comp = len(s.compositions) if s.with_layout else "-"
        layouts = len(s.layouts) if s.with_layout else "-"
        lines.append(
            f"{s.kind:<14}{s.rooms:>6}{comp!s:>6}{_pct(s.top_composition_share):>6}"
            f"{layouts!s:>8}{_pct(s.top_layout_share):>6}{s.mean_coverage:>8.3f}{_pct(s.aligned_share):>9}"
        )
    lines.append("comp: distinct sets of furniture names; top: share of the most common set of its rooms.")
    lines.append("layout: distinct arrangements (metre, quarter turn); aligned: props within 3 degrees of a wall axis.")
    return "\n".join(lines)


def failing_kinds(kinds: dict[str, KindSummary], max_top: float, min_rooms: int) -> list[tuple[str, float]]:
    failing = []
    for s in kinds.values():
        share = s.top_composition_share
        if share is not None and s.with_layout >= min_rooms and share > max_top:
            failing.append((s.kind, share))
    return sorted(failing)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Room variety from ProcCheck -roomcensus logs.")
    parser.add_argument("logs", nargs="+", type=Path, help="log files written by ProcCheck -roomcensus")
    parser.add_argument("--max-top", type=float,
                        help="fail when the most common composition of a kind holds more than this share of its rooms")
    parser.add_argument("--min-rooms", type=int, default=DEFAULT_MIN_ROOMS,
                        help="kinds with fewer rooms never fail the gate (default %(default)s)")
    args = parser.parse_args(argv)

    rooms: list[Room] = []
    for path in args.logs:
        with path.open(encoding="utf-8", errors="replace") as handle:
            for number, line in enumerate(handle, 1):
                try:
                    room = parse_line(line)
                except ValueError as error:
                    print(f"{path}:{number}: malformed CENSUS line ({error})", file=sys.stderr)
                    return 2
                if room is not None:
                    rooms.append(room)
    if not rooms:
        print("no CENSUS lines found", file=sys.stderr)
        return 2

    kinds = summarize(rooms)
    print(f"{len(rooms)} rooms from {len(args.logs)} log(s)")
    print(format_report(kinds))
    if args.max_top is not None:
        failing = failing_kinds(kinds, args.max_top, args.min_rooms)
        if failing:
            for kind, share in failing:
                print(f"FAIL {kind}: the most common composition holds {share:.0%} of its rooms (limit {args.max_top:.0%})")
            return 1
        print(f"ok: no kind above {args.max_top:.0%}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
