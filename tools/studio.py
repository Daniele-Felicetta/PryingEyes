"""Prying Eyes production studio: doctor, catalog list/affected, asset inspect/preview/build, test, report, gallery, queue, job.

This is only the entry point. The engine (jobs, locks, source guards, queue, command line) is tools/studio_engine.py and knows
nothing about this game; tools/studio_pryingeyes.py is the profile that gives it weapons, rooms, checks and the Unity/Blender pipeline.
"""
from studio_engine import main
from studio_pryingeyes import PRYING_EYES

if __name__ == "__main__":
    raise SystemExit(main(PRYING_EYES))
