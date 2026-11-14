"""The Prying Eyes catalog contract. The engine's own behaviour (jobs, locks, sources, queue) is in test_studio_engine.py."""
import copy
import json
from pathlib import Path
import tempfile
import unittest

from content_catalog import ROOT, read_catalog, validate_catalog


class StudioCheck(unittest.TestCase):
    def test_existing_ids_cannot_be_reordered(self):
        data = read_catalog()
        data["weapons"][0], data["weapons"][1] = data["weapons"][1], data["weapons"][0]
        data["weapons"][0]["index"], data["weapons"][1]["index"] = 0, 1
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "catalog.json"; path.write_text(json.dumps(data), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "WEAPON_STABLE_INDEX"): validate_catalog(path)

    def test_catalog_contract_and_invalid_variants(self):
        catalog = validate_catalog()
        prefix = json.loads((ROOT / "tools/fixtures/weapon-prefix-v1.json").read_text())
        self.assertEqual([e["definition"]["id"] for e in catalog["weapons"][:29]], [e["id"] for e in prefix["definitions"]])
        changes = [lambda c: c["weapons"][0]["definition"].pop("damage"),
                   lambda c: c["weapons"][0]["definition"].update(damage="25"),
                   lambda c: c["weapons"][0]["definition"].update(damage=float("inf")),
                   lambda c: c["weapons"][0]["definition"].update(modes=["Unknown"]),
                   lambda c: c["weapons"][0]["definition"].update(reserve=99999),
                   lambda c: c["weapons"][1]["definition"].update(id=c["weapons"][0]["definition"]["id"]),
                   lambda c: c["weapons"][0].update(index=3),
                   lambda c: c["weapons"][-1]["source"]["parameters"].update(barrelEnd=99)]
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "catalog.json"
            for change in changes:
                data = copy.deepcopy(catalog); change(data); path.write_text(json.dumps(data), encoding="utf-8")
                with self.assertRaises(ValueError): validate_catalog(path)


if __name__ == "__main__": unittest.main()
