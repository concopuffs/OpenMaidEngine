#!/usr/bin/env python3
"""Pure tests for the data-table boot image tool (no game content)."""
from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path
from unittest import mock

import init_image


IMAGE = {
    "profile": "synthetic",
    "scripts": [
        {"script": "EBINIT.BIN", "ints": {"10": 1, "11": 2}, "strings": {"5": "a"}},
        {"script": "ITINIT.BIN", "ints": {"20": 7}, "strings": {}},
        {"script": "$1$EBINIT.BIN", "ints": {"11": 9, "12": 3}, "strings": {"6": "b"}},
        {"script": "$3$EBINIT.BIN", "ints": {"12": 4}, "strings": {"5": "c"}},
    ],
}


class InitImageTests(unittest.TestCase):
    def setUp(self) -> None:
        init_image.load.cache_clear()
        patcher = mock.patch.object(init_image, "load", return_value=IMAGE)
        patcher.start()
        self.addCleanup(patcher.stop)

    def test_layers_follow_boot_order_and_ignore_other_tables(self) -> None:
        layers = init_image.table_layers("ebinit.bin")
        self.assertEqual(
            ["EBINIT.BIN", "$1$EBINIT.BIN", "$3$EBINIT.BIN"], [s["script"] for s in layers]
        )

    def test_merged_applies_later_layers_over_earlier(self) -> None:
        ints, strings = init_image.merged("EBINIT")
        self.assertEqual({10: 1, 11: 9, 12: 4}, ints)
        self.assertEqual({5: "c", 6: "b"}, strings)

    def test_boot_sequence_is_base_then_ascending_append_autoruns(self) -> None:
        names = {0x4500: "INIT2.BIN", 0x2000000: "$2$AUTORUN.BIN", 0x1000000: "$1$AUTORUN.BIN"}
        calls = {
            "INIT2.BIN": ["SCINIT.BIN", "EBINIT.BIN"],
            "$1$AUTORUN.BIN": ["$1$EBINIT.BIN"],
            "$2$AUTORUN.BIN": ["$2$SCINIT.BIN"],
        }
        scripts = {name: Path(name) for name in calls}
        with mock.patch.object(init_image, "_callscript_names", return_value=names), \
                mock.patch.object(init_image.paths, "scripts", return_value=scripts), \
                mock.patch.object(init_image, "_called_tables",
                                  side_effect=lambda path, _: calls[path.name]):
            self.assertEqual(
                ["SCINIT.BIN", "EBINIT.BIN", "$1$EBINIT.BIN", "$2$SCINIT.BIN"],
                init_image.boot_sequence(),
            )


    def test_projection_uses_base_layout_and_records_layer_provenance(self) -> None:
        image = {"scripts": [
            {"script": "EBINIT.BIN", "ints": {"201": 5, "302": 7, "1007": 3}, "strings": {"101": "A"}},
            {"script": "$1$EBINIT.BIN", "ints": {"203": 6}, "strings": {"103": "C", "301": "x"}},
            {"script": "$2$EBINIT.BIN", "ints": {"201": 9}, "strings": {}},
        ]}
        layout = {
            "table": "EBINIT", "mode": "name", "name_array_base": "0x64", "record_span": 10,
            "desc_array_bases": {"desc": "0x12c"}, "field_columns": ["0xc8"],
            "record_field_columns": ["0x3e8/5/2"],
        }
        with tempfile.TemporaryDirectory() as directory:
            build = Path(directory)
            (build / "data").mkdir()
            (build / "data" / "EBINIT.json").write_text(json.dumps(layout), encoding="utf-8")
            with mock.patch.object(init_image, "load", return_value=image),                     mock.patch.object(init_image.paths, "BUILD", build):
                projected = init_image.project("EBINIT")
        records = {record["id"]: record for record in projected["records"]}
        self.assertEqual([1, 3], sorted(records))
        self.assertEqual({"0xc8": 9}, records[1]["fields"])
        self.assertEqual({"0x3e8/5/2": 3}, records[1]["record_fields"])
        self.assertEqual("x", records[1]["desc"])
        self.assertEqual("EBINIT.BIN", records[1]["origin"])
        self.assertEqual(["EBINIT.BIN", "$1$EBINIT.BIN", "$2$EBINIT.BIN"], records[1]["layers"])
        self.assertEqual("$1$EBINIT.BIN", records[3]["origin"])
        self.assertEqual({"0xc8": 6}, records[3]["fields"])

if __name__ == "__main__":
    unittest.main()
