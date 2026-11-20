#!/usr/bin/env python3
"""Pure tests for the table layout inference of tools/init_arterial.py (synthetic records, no game content)."""
from __future__ import annotations

import unittest
from unittest import mock

import init_arterial
from init_arterial import Leader


def table(leader: Leader, fields: list[tuple[str, int, int, list[int]]], ids: list[int]):
    """Records for `ids`: each writes the leader cell, then every field's columns at base + stride * id + column."""
    records, cells = [], {}
    for rid in ids:
        writes = []
        cells[(leader.bank, leader.base + leader.stride * rid)] = (0, rid)
        for bank, base, stride, columns in fields:
            for column in columns:
                writes.append((bank, base + stride * rid + column))
                cells[(bank, base + stride * rid + column)] = (0, rid * 10 + column)
        records.append((0, rid, writes))
    return records, cells


class InferArraysTests(unittest.TestCase):
    def test_parallel_arrays_and_row_tables_are_found_at_their_strides(self) -> None:
        leader = Leader("GS", 0x1000, 100)
        fields = [("G", 0x5000, 1, [0]), ("G", 0x5064, 1, [0]), ("G", 0x6000, 3, [0, 1, 2]), ("GS", 0x1064, 2, [0, 1])]
        records, cells = table(leader, fields, [1, 2, 5, 9, 40])
        arrays, loose = init_arterial.infer_arrays(leader, records, cells, {})
        self.assertEqual([], loose)
        self.assertEqual([("G", 0x5000, 1), ("G", 0x5064, 1), ("G", 0x6000, 3), ("GS", 0x1000, 1), ("GS", 0x1064, 2)],
                         [(a.bank, a.base, a.stride) for a in arrays])
        row = next(a for a in arrays if a.base == 0x6000)
        self.assertEqual({0, 1, 2}, set(row.columns))
        self.assertEqual([1, 2, 5, 9, 40], row.columns[1]["ids"])
        self.assertEqual([11, 21, 51, 91, 401], row.columns[1]["values"])

    def test_a_layer_that_edits_a_record_without_its_leader_lands_in_the_same_arrays(self) -> None:
        leader = Leader("GS", 0x1000, 100)
        records, cells = table(leader, [("G", 0x5000, 1, [0]), ("G", 0x6000, 2, [0, 1])], [1, 2, 3])
        cells[("G", 0x5000 + 2)] = (4, 777)        # layer 4 rewrites record 2's field
        cells[("G", 0x6000 + 2 * 7 + 1)] = (4, 5)  # and gives record 7 a cell no record wrote
        arrays, loose = init_arterial.infer_arrays(leader, records, cells, {})
        self.assertEqual([], loose)
        scalar = next(a for a in arrays if a.base == 0x5000)
        self.assertEqual({0: 2, 4: 1}, dict(scalar.columns[0]["layers"]))
        self.assertIn(7, next(a for a in arrays if a.base == 0x6000).columns[1]["ids"])

    def test_a_column_zero_nobody_writes_takes_the_base_the_scripts_index(self) -> None:
        leader = Leader("GS", 0x1000, 100)
        records, cells = table(leader, [("G", 0x6000, 4, [1, 2])], [1, 2, 3])
        arrays, _ = init_arterial.infer_arrays(leader, records, cells, {})
        self.assertEqual(0x6001, next(a for a in arrays if a.bank == "G").base)   # the lowest column written
        arrays, _ = init_arterial.infer_arrays(leader, records, cells, {("G", 0x6000): {}})
        found = next(a for a in arrays if a.bank == "G")
        self.assertEqual((0x6000, {1, 2}), (found.base, set(found.columns)))

    def test_a_cell_outside_every_array_is_listed(self) -> None:
        leader = Leader("GS", 0x1000, 10)
        records, cells = table(leader, [("G", 0x5000, 1, [0])], [1, 2])
        cells[("G", 0x9000)] = (0, 1)
        _, loose = init_arterial.infer_arrays(leader, records, cells, {})
        self.assertEqual([("G", 0x9000)], loose)

    def test_a_leader_with_a_stride_keeps_one_record_through_its_cells(self) -> None:
        leader = Leader("G", 0x100, 10, stride=5)
        order = [("G", 0x100 + 5 * 1 + 1), ("G", 0x100 + 5 * 1 + 2), ("G", 0x900 + 1), ("G", 0x100 + 5 * 2 + 2),
                 ("G", 0x900 + 2)]
        with mock.patch.object(init_arterial, "write_order",
                               side_effect=lambda table, layer: order if layer == 0 else []):
            records, before = init_arterial.records_of("T", leader)
        self.assertEqual([(0, 1, [("G", 0x901)]), (0, 2, [("G", 0x902)])], records)
        self.assertEqual({}, before)

    def test_a_field_written_before_the_leader_belongs_to_the_record_that_follows(self) -> None:
        leader = Leader("GS", 0x1000, 100)
        ids = [3, 4, 5, 9, 10]
        stamp, field_ = (lambda rid: ("G", 0x7000 + rid)), (lambda rid: ("G", 0x5000 + rid))
        records, cells = [], {}
        for at, rid in enumerate(ids):   # stamp, leader, field: the stamp of the next record ends this one's writes
            records.append((1, rid, [field_(rid)] + ([stamp(ids[at + 1])] if at + 1 < len(ids) else [])))
            cells[("GS", 0x1000 + rid)] = (1, "name")
            cells[stamp(rid)] = (1, 1)
            cells[field_(rid)] = (1, rid)
        arrays, loose = init_arterial.infer_arrays(leader, records, cells, {}, {1: [stamp(ids[0])]})
        self.assertEqual([], loose)
        self.assertEqual([("G", 0x5000), ("G", 0x7000), ("GS", 0x1000)], [(a.bank, a.base) for a in arrays])
        self.assertEqual(ids, next(a for a in arrays if a.base == 0x7000).columns[0]["ids"])

    def test_records_of_several_kinds_each_start_at_their_own_leading_array(self) -> None:
        leader = Leader("G", 0x100, 10, also=(0x10a,))
        order = [("G", 0x101), ("G", 0x900 + 1), ("G", 0x10a + 4), ("G", 0x114 + 4), ("G", 0x900 + 4)]
        with mock.patch.object(init_arterial, "write_order",
                               side_effect=lambda table, layer: order if layer == 0 else []):
            records, _ = init_arterial.records_of("T", leader)
        self.assertEqual([(0, 1, [("G", 0x901)]), (0, 4, [("G", 0x118), ("G", 0x904)])], records)
        cells = {address: (0, 1) for address in order}
        arrays, loose = init_arterial.infer_arrays(leader, records + [(0, 5, [("G", 0x119), ("G", 0x905)])],
                                                   cells | {("G", 0x10f): (0, 1), ("G", 0x119): (0, 1),
                                                            ("G", 0x905): (0, 1)}, {})
        self.assertEqual([], loose)
        self.assertEqual([0x100, 0x10a, 0x114, 0x900], [a.base for a in arrays])

    def test_an_array_that_does_not_line_up_with_the_others_is_off_the_grid(self) -> None:
        leader = Leader("GS", 0x1000, 100)
        fields = [("G", 0x5000, 1, [0]), ("G", 0x5064, 1, [0]), ("G", 0x50c8, 2, [0, 1]), ("G", 0x5199, 1, [0])]
        records, cells = table(leader, fields, [1, 2, 5, 9, 40])
        arrays, _ = init_arterial.infer_arrays(leader, records, cells, {})
        self.assertEqual({0x5000: True, 0x5064: True, 0x50c8: True, 0x5199: False, 0x1000: True},
                         {a.base: a.on_grid for a in arrays})

    def test_every_leader_names_a_boot_table_a_bank_and_a_capacity(self) -> None:
        for name, leader in init_arterial.LEADERS.items():
            self.assertRegex(name, r"^[A-Z0-9]+INIT$")
            self.assertIn(leader.bank, ("G", "GS"))
            self.assertGreater(leader.capacity, 0)


if __name__ == "__main__":
    unittest.main()
