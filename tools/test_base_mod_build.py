"""Plain tests for base_mod_build.py and the registry's aliases: synthesized registries and profile manifests, no
game content."""
import json
import sys
import tempfile
from pathlib import Path

import base_mod_build as build
import globals_build

FAILS = []


def check(condition, message):
    (FAILS.append(message) or print("FAIL:", message)) if not condition else print("ok:", message)


REGISTRY = '''
[[global]]
address = "0x10"
name = "current_unit_id"
category = "index-pointer"
type = "int"
source = "investigation"
confidence = "high"

[[global]]
address = "0x100"
name = "unit_current_levels"
category = "data-table"
type = "int[100]"
source = "investigation"
confidence = "high"

[[global]]
address = "0x200"
name = "unit_condition_levels"
category = "data-table"
type = "int[unit][30]"
columns = { "6" = "charm", "7" = "confusion" }
source = "investigation"
confidence = "high"

[[global]]
address = "0x0"
name = "unit_names"
category = "string-table"
type = "string[1000x2]"
source = "investigation"
confidence = "high"

[[global]]
address = "0x900"
category = "unknown"
type = "int"
source = "inference"
confidence = "low"
'''


# The saved prefix of each bank: G cells below 0x50, GS below 0x10 and GF below 1 are kept by a save.
PROFILE = {"persistence": {"bankDimensions": {"integerGlobals": 0x50, "floatGlobals": 1, "stringGlobals": 0x10,
                                              "pointerGlobals": 1, "pointerStrings": 1, "localPointerScratch": 1}}}


def workspace(directory: Path, registry: str = REGISTRY, version: str = "1.0.0",
              profile: dict | None = None) -> tuple[Path, Path]:
    mods = directory / "mods"
    (mods / "test-base").mkdir(parents=True, exist_ok=True)
    (mods / "test-base" / "manifest.toml").write_text(f'version = "{version}"\n', encoding="utf-8")
    source = directory / "globals-test.toml"
    source.write_text(registry, encoding="utf-8")
    (directory / "test.json").write_text(json.dumps(PROFILE if profile is None else profile), encoding="utf-8")
    return mods, source


def generate(mods: Path, source: Path):
    return build.generate("test", mods=mods, registry=source, points_registry=source.parent / "none.toml",
                          manifest=source.parent / "test.json")


def write(files: dict):
    for path, text in files.items():
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")


def layout_of(files: dict) -> dict:
    return {value["name"]: value for value in json.loads(next(text for path, text in files.items()
                                                              if path.name == "globals.json"))["globals"]}


def test_shapes():
    check(build.dimensions("int") == ("int", []), "a scalar has no dimensions")
    check(build.dimensions("string[210]") == ("string", [210]), "a 1-D array")
    check(build.dimensions("int[1000][14]") == ("int", [1000, 14]), "a 2-D array")
    check(build.dimensions("int[1000x14]") == ("int", [1000, 14]), "the RxC form is a 2-D array")
    check(build.dimensions("int[][27]") == ("int", [None, 27]), "an open first dimension")
    check(build.dimensions("int[unit][30]") == ("int", [None, 30]), "a symbolic first dimension is open")
    check(build.dimensions("bool") is None and build.dimensions("int[1.5]") is None, "anything else is no shape")


def test_layout():
    with tempfile.TemporaryDirectory() as directory:
        mods, source = workspace(Path(directory))
        files, errors = generate(mods, source)
        check(not errors, f"a well-formed registry generates ({errors})")
        layout = layout_of(files)
        check(list(layout) == sorted(layout), "globals are ordered by name")
        check("0x900" not in json.dumps(layout), "an unnamed global is not exported")
        check(layout["unit_names"] == {"name": "unit_names", "bank": "GS", "address": "0x0", "dims": [1000, 2]},
              "a string global is in the string bank, with its shape")
        check(layout["unit_condition_levels"]["dims"] == [None, 30]
              and layout["unit_condition_levels"]["columns"] == {"charm": 6, "confusion": 7},
              "columns map a name to its index in the last dimension")
        exports = next(text for path, text in files.items() if path.name == "exports.txt")
        check(build.parse_exports(exports) == (1, {"current_unit_id", "unit_current_levels", "unit_condition_levels",
                                                   "unit_names"}), "exports.txt lists every name and the major version")
        points = json.loads(next(text for path, text in files.items() if path.name == "points.json"))
        check(points["points"] == [] and points["profile"] == "test", "a profile without points gets an empty copy")


def test_shape_errors():
    with tempfile.TemporaryDirectory() as directory:
        bad = REGISTRY + '''
[[global]]
address = "0x300"
name = "bad_columns"
category = "data-table"
type = "int[4]"
columns = { "4" = "past_the_end" }
source = "investigation"
confidence = "high"

[[global]]
address = "0x400"
name = "bad_shape"
category = "data-table"
type = "int[4][]"
source = "investigation"
confidence = "high"
'''
        mods, source = workspace(Path(directory), bad)
        _, errors = generate(mods, source)
        check(any("bad_columns: column 4 (past_the_end) is outside" in error for error in errors),
              "a column past the last dimension is an error")
        check(any("bad_shape" in error and "only the first dimension may be open" in error for error in errors),
              "an open inner dimension is an error")


def test_rename_rule():
    with tempfile.TemporaryDirectory() as directory:
        mods, source = workspace(Path(directory))
        files, _ = generate(mods, source)
        write(files)
        renamed = REGISTRY.replace('name = "unit_current_levels"', 'name = "unit_levels"')
        source.write_text(renamed, encoding="utf-8")
        _, errors = generate(mods, source)
        check(any("exports 'unit_current_levels', which the registry no longer has" in error for error in errors),
              "a rename without an alias in the same major version is refused")
        source.write_text(renamed.replace('name = "unit_levels"', 'name = "unit_levels"\naliases = ["unit_current_levels"]'),
                          encoding="utf-8")
        files, errors = generate(mods, source)
        check(not errors, f"a rename that keeps the old name as an alias passes ({errors})")
        exports = next(text for path, text in files.items() if path.name == "exports.txt")
        check("unit_current_levels = unit_levels" in exports.splitlines(), "exports.txt lists an alias and its global")
        check(layout_of(files)["unit_levels"]["aliases"] == ["unit_current_levels"], "the layout carries the alias")
        source.write_text(renamed, encoding="utf-8")
        (mods / "test-base" / "manifest.toml").write_text('version = "2.0.0"\n', encoding="utf-8")
        files, errors = generate(mods, source)
        check(not errors and build.parse_exports(next(text for path, text in files.items()
                                                      if path.name == "exports.txt"))[0] == 2,
              "a rename with a raised major version passes and starts the new list")
        write(files)
        (mods / "test-base" / "manifest.toml").write_text('version = "1.4.0"\n', encoding="utf-8")
        _, errors = generate(mods, source)
        check(any("lower than the released 2" in error for error in errors), "the major version cannot go down")


def entry(address: str, name: str, type_: str, extra: str = "") -> str:
    return (f'\n[[global]]\naddress = "{address}"\nname = "{name}"\ncategory = "data-table"\ntype = "{type_}"\n'
            f'{extra}source = "investigation"\nconfidence = "high"\n')


# hero: a field that ends at the G prefix (state), one that begins at it (not state), a row with named columns, a
# string field at the GS prefix, a float field, and a field named like a Lua keyword.
HEROES = REGISTRY + (entry("0x4c", "hero_seen_flags", "int[4]") + entry("0x50", "hero_levels", "int[4]")
                     + entry("0x60", "hero_stats", "int[4][3]", 'columns = { "2" = "end", "0" = "hp" }\n')
                     + entry("0x10", "hero_names", "string[4]") + entry("0x1", "hero_speeds", "float[4]")) + '''
[[table]]
name = "hero"
records = 4
built_by = "boot"

[table.fields]
seen_flag = "hero_seen_flags"
level = "hero_levels"
stats = "hero_stats"
name = "hero_names"
speed = "hero_speeds"
end = "hero_levels"
'''


def record_map(files: dict) -> str:
    return next(text for path, text in files.items() if path.as_posix().endswith("lua/generated/tables.lua"))


def test_record_map():
    with tempfile.TemporaryDirectory() as directory:
        mods, source = workspace(Path(directory), HEROES)
        files, errors = generate(mods, source)
        check(not errors, f"a registry with a record table generates ({errors})")
        text = record_map(files)
        lines = text.splitlines()
        check(lines[0].startswith("-- DO NOT EDIT -- generated from vm-map/globals-test.toml and ")
              and "base_mod_build.py --build" in lines[0], "the record map says it is generated, and from what")
        check("    format = 1," in lines and "        hero = {" in lines and "            records = 4," in lines
              and '            built_by = "boot",' in lines, "a table has its record count and what builds it")
        fields = [line.strip() for line in lines if line.startswith("                ")]
        check(fields == [
            '["end"] = { global = "hero_levels", kind = "integer" },',
            'level = { global = "hero_levels", kind = "integer" },',
            'name = { global = "hero_names", kind = "string" },',
            'seen_flag = { global = "hero_seen_flags", kind = "integer", state = true },',
            'speed = { global = "hero_speeds", kind = "float" },',
            'stats = { global = "hero_stats", kind = "integer", width = 3, columns = { hp = 0, ["end"] = 2 } },',
        ], f"fields are sorted, with their kind, a row's width and columns by index, and state at the prefix ({fields})")
        check(text.endswith("    },\n}\n") and "\r" not in text, "the module ends with its table and a newline")
        again, _ = generate(mods, source)
        check(record_map(again) == text, "the record map is the same each time it is generated")


def test_record_map_without_tables():
    with tempfile.TemporaryDirectory() as directory:
        mods, source = workspace(Path(directory), profile={})
        files, errors = generate(mods, source)
        check(not errors, f"a registry without tables needs no saved prefix ({errors})")
        check("    tables = {\n    },\n}\n" in record_map(files), "its record map has no table")


def test_record_map_errors():
    table = '''
[[table]]
name = "hero"
records = 4
built_by = "boot"

[table.fields]
'''
    cases = [
        (entry("0x4e", "hero_split", "int[4]"), "split = \"hero_split\"\n", PROFILE,
         "hero_split (G 0x4e to 0x52) straddles the saved prefix 0x50", "an array across the saved prefix"),
        (entry("0x80", "hero_flags", "int[4]", 'columns = { "1" = "second" }\n'), "flags = \"hero_flags\"\n", PROFILE,
         "hero_flags has one cell for each record, so its column names would name records",
         "a 1-D field with column names"),
        (entry("0x80", "hero_flags", "int[4]"), "flags = \"hero_flags\"\n", {"persistence": {}},
         "has no persistence.bankDimensions", "a profile without its banks' saved prefixes"),
        (entry("0x80", "hero_flags", "int[4]"), "flags = \"hero_flags\"\n",
         {"persistence": {"bankDimensions": {"integerGlobals": 0x50, "floatGlobals": 1}}},
         "persistence.bankDimensions.stringGlobals is None", "a profile without a bank's prefix"),
    ]
    for globals_, fields, profile, needle, what in cases:
        with tempfile.TemporaryDirectory() as directory:
            mods, source = workspace(Path(directory), REGISTRY + globals_ + table + fields, profile=profile)
            _, errors = generate(mods, source)
            check(any(needle in error for error in errors), f"the record map refuses {what} ({errors})")
    with tempfile.TemporaryDirectory() as directory:
        mods, source = workspace(Path(directory), REGISTRY + entry("0x80", "hero_flags", "int[4]")
                                 + table.replace('built_by = "boot"\n', "") + "flags = \"hero_flags\"\n")
        _, errors = generate(mods, source)
        check(any("table 'hero': built_by must be a lowercase identifier" in error for error in errors),
              f"a table without built_by blocks the build ({errors})")


def test_alias_lint():
    registry = REGISTRY.replace('name = "unit_names"', 'name = "unit_names"\naliases = ["current_unit_id", "Bad"]')
    with tempfile.TemporaryDirectory() as directory:
        path = Path(directory) / "globals.toml"
        path.write_text(registry, encoding="utf-8")
        entries, _ = globals_build.load_toml(path)
        errors = globals_build.lint_aliases(entries)
        check(any("alias current_unit_id is also the name of a global" in error for error in errors),
              "an alias may not be another global's name")
        check(any("alias 'Bad' must be a lowercase identifier" in error for error in errors),
              "an alias is a lowercase identifier")
        twice = REGISTRY.replace('name = "unit_names"', 'name = "unit_names"\naliases = ["old"]').replace(
            'name = "unit_current_levels"', 'name = "unit_current_levels"\naliases = ["old"]')
        path.write_text(twice, encoding="utf-8")
        entries, _ = globals_build.load_toml(path)
        check(any("alias old is also an alias of" in error for error in globals_build.lint_aliases(entries)),
              "one alias names one global")


def exports_of(files: dict) -> list[str]:
    return [line for line in next(text for path, text in files.items() if path.name == "exports.txt").splitlines()
            if not line.startswith("#")]


# Old names (data patches plan §9.1): the table hero was heroes, and its field level was lvl.
OLD_NAMES = HEROES.replace('built_by = "boot"\n', 'built_by = "boot"\naliases = ["heroes"]\n') + '''
[table.field_aliases]
lvl = "level"
'''


def test_old_names():
    with tempfile.TemporaryDirectory() as directory:
        mods, source = workspace(Path(directory), OLD_NAMES)
        files, errors = generate(mods, source)
        check(not errors, f"a registry with old names of a table and a field generates ({errors})")
        exports = exports_of(files)
        check(exports[:2] == ["major = 1", "current_unit_id"], "exports.txt starts with the major version and the globals")
        tables_at = exports.index("table hero")
        check(exports[tables_at:tables_at + 3] == ["table hero", "table heroes = hero", "field hero.end"],
              f"the tables follow the globals, with an old name as OLD = NAME, then the fields ({exports[tables_at:]})")
        check("field hero.lvl = hero.level" in exports and "field hero.level" in exports,
              "a field's old name is listed with its table and its current name")
        check(all(not line.startswith(("table ", "field ")) for line in exports[:tables_at]),
              "no table or field is among the globals")
        lines = record_map(files).splitlines()
        check('            old_fields = { lvl = "level" },' in lines, "the record map gives a table its old field names")
        check(lines[-2] == '    old_tables = { heroes = "hero" },',
              "the record map lists the old table names beside its tables")
        with tempfile.TemporaryDirectory() as other:
            mods_without, source_without = workspace(Path(other), HEROES)
            plain, _ = generate(mods_without, source_without)
            check("old_fields =" not in record_map(plain) and "old_tables =" not in record_map(plain),
                  "a map without old names has neither list")
        merged = globals_build.merge(globals_build.load_toml(source)[0], {})
        reference = globals_build.emit_reference_md(merged, globals_build.load_tables(source))
        check("Formerly `heroes`." in reference and "| level (formerly lvl) | hero_levels |" in reference,
              "the reference names the old names")
        table_json = json.loads(globals_build.emit_json(merged, {}, globals_build.load_tables(source)))["tables"][0]
        check(table_json["aliases"] == ["heroes"] and table_json["field_aliases"] == {"lvl": "level"},
              "the registry's JSON carries the old names")


def test_old_names_lint():
    cases = [
        ('aliases = ["hero"]', "alias hero is also the name of a table", "a table's alias is not a table's name"),
        ('aliases = ["unit_names"]', "alias unit_names is also the name of a global", "nor a global's name"),
        ('aliases = ["Heroes"]', "alias 'Heroes' must be a lowercase identifier", "an alias is a lowercase identifier"),
        ('aliases = "heroes"', "aliases must be a list of names", "aliases is a list"),
    ]
    for line, needle, what in cases:
        registry = HEROES.replace('built_by = "boot"\n', f'built_by = "boot"\n{line}\n')
        with tempfile.TemporaryDirectory() as directory:
            mods, source = workspace(Path(directory), registry)
            _, errors = generate(mods, source)
            check(any(needle in error for error in errors), f"{what} ({errors})")
    second = '''
[[table]]
name = "villain"
records = 4
built_by = "boot"
aliases = ["heroes"]

[table.fields]
level = "hero_levels"
'''
    with tempfile.TemporaryDirectory() as directory:
        mods, source = workspace(Path(directory), OLD_NAMES + second)
        _, errors = generate(mods, source)
        check(any("table 'villain': alias heroes is also an alias of table 'hero'" in error for error in errors),
              f"one old name names one table ({errors})")
    fields = [
        ('level = "level"', "old field name level is also the name of a field", "an old field name is no field's name"),
        ('lvl = "levels"', "old field name lvl names 'levels', which is not a field of the table",
         "an old field name names a field of the table"),
        ('Lvl = "level"', "old field name 'Lvl' must be a lowercase identifier", "an old field name is an identifier"),
    ]
    for line, needle, what in fields:
        with tempfile.TemporaryDirectory() as directory:
            mods, source = workspace(Path(directory), HEROES + f"\n[table.field_aliases]\n{line}\n")
            _, errors = generate(mods, source)
            check(any(needle in error for error in errors), f"{what} ({errors})")


def test_old_names_rename_rule():
    with tempfile.TemporaryDirectory() as directory:
        mods, source = workspace(Path(directory), HEROES)
        files, _ = generate(mods, source)
        write(files)
        # The table renamed in the same major version, without its old name, is refused.
        renamed = HEROES.replace('name = "hero"', 'name = "champion"')
        source.write_text(renamed, encoding="utf-8")
        _, errors = generate(mods, source)
        check(len(errors) == 1 and "exports the table 'hero', which the registry no longer has" in errors[0],
              f"a table renamed without an old name is refused, and its error stands for its fields ({errors})")
        source.write_text(renamed.replace('built_by = "boot"\n', 'built_by = "boot"\naliases = ["hero"]\n'),
                          encoding="utf-8")
        _, errors = generate(mods, source)
        check(not errors, f"a table renamed with its old name keeps its fields exported under it ({errors})")
        # A field dropped in the same major version.
        dropped = HEROES.replace('level = "hero_levels"\n', 'rank = "hero_levels"\n')
        source.write_text(dropped, encoding="utf-8")
        _, errors = generate(mods, source)
        check(any("exports the field 'hero.level', which the registry no longer has: keep it as an old name" in error
                  for error in errors), f"a field renamed without an old name is refused ({errors})")
        source.write_text(dropped + '\n[table.field_aliases]\nlevel = "rank"\n', encoding="utf-8")
        _, errors = generate(mods, source)
        check(not errors, f"a field renamed with its old name passes ({errors})")
        # A raised major version allows it without the old name.
        source.write_text(dropped, encoding="utf-8")
        (mods / "test-base" / "manifest.toml").write_text('version = "2.0.0"\n', encoding="utf-8")
        _, errors = generate(mods, source)
        check(not errors, f"a field dropped with a raised major version passes ({errors})")


# A scope (data patches spec §6.2): the table foe, keyed by stage as enemy, ids 1 to 99.
SCOPED = HEROES + entry("0x300", "foe_units", "int[3]") + entry("0x310", "foe_routines", "int[3][2]") + '''
[[table]]
name = "foe"
records = 3
built_by = "stage"

[table.fields]
unit = "foe_units"
routines = "foe_routines"

[[scope]]
name = "stage"
first = 1
last = 99

[scope.tables]
enemy = "foe"
'''


def test_scopes():
    with tempfile.TemporaryDirectory() as directory:
        mods, source = workspace(Path(directory), SCOPED)
        files, errors = generate(mods, source)
        check(not errors, f"a registry with a scope generates ({errors})")
        lines = record_map(files).splitlines()
        at = lines.index("    scopes = {")
        check(lines[at - 1] == "    }," and lines[at + 1:at + 3]
              == ['        stage = { first = 1, last = 99, tables = { enemy = "foe" } },', "    },"],
              f"the record map gives each scope its ids and its tables by short name, after the tables ({lines[at:]})")
        check('                routines = { global = "foe_routines", kind = "integer", width = 2 },' in lines,
              "a scoped table's fields are mapped as any table's")
        exports = exports_of(files)
        check(exports[-2:] == ["scope stage", "scope stage.enemy"],
              f"the scope and its short names close exports.txt ({exports[-3:]})")
        with tempfile.TemporaryDirectory() as other:
            plain_mods, plain_source = workspace(Path(other), HEROES)
            plain, _ = generate(plain_mods, plain_source)
            check("scopes =" not in record_map(plain), "a map without a scope has no scopes list")
        write(files)
        # The short name renamed in the same major version is refused, and so is the scope; a raised one allows both.
        source.write_text(SCOPED.replace("enemy = \"foe\"", "foe = \"foe\""), encoding="utf-8")
        _, errors = generate(mods, source)
        check(len(errors) == 1 and "exports the short name 'stage.enemy', which the registry no longer has" in errors[0]
              and "raise the base mod's major version" in errors[0], f"a short name that leaves is refused ({errors})")
        source.write_text(SCOPED.replace('name = "stage"', 'name = "level"'), encoding="utf-8")
        _, errors = generate(mods, source)
        check(len(errors) == 1 and "exports the scope 'stage'" in errors[0],
              f"a scope that leaves is refused, and its error stands for its short names ({errors})")
        (mods / "test-base" / "manifest.toml").write_text('version = "2.0.0"\n', encoding="utf-8")
        _, errors = generate(mods, source)
        check(not errors, f"a raised major version lets a scope go ({errors})")
        # The registry's scope errors block the build.
        source.write_text(SCOPED.replace('enemy = "foe"', 'enemy = "ghost"'), encoding="utf-8")
        _, errors = generate(mods, source)
        check(any("scope 'stage': enemy names 'ghost', which is no table of the registry" in error for error in errors),
              f"a scope's lint error blocks the build ({errors})")


def shape_of(files: dict) -> tuple[str, dict]:
    text = next(text for path, text in files.items() if path.as_posix().endswith("shapes/tables.schema.json"))
    return text, json.loads(text)


# The shape (data patches plan §6.2): a scope, a table's old name and a scoped table's, a field's old name and a state
# field's, beside HEROES' float, string, state and named row and the scoped table's unnamed row.
SHAPED = SCOPED.replace('name = "hero"\nrecords = 4\nbuilt_by = "boot"\n',
                        'name = "hero"\nrecords = 4\nbuilt_by = "boot"\naliases = ["heroes"]\n').replace(
    'name = "foe"\nrecords = 3\nbuilt_by = "stage"\n', 'name = "foe"\nrecords = 3\nbuilt_by = "stage"\naliases = ["foes"]\n'
).replace('end = "hero_levels"\n', 'end = "hero_levels"\n\n[table.field_aliases]\nlvl = "level"\nseen = "seen_flag"\n')


def test_table_shape():
    with tempfile.TemporaryDirectory() as directory:
        mods, source = workspace(Path(directory), SHAPED)
        files, errors = generate(mods, source)
        check(not errors, f"a registry with a scope and old names generates a shape ({errors})")
        text, shape = shape_of(files)
        check(list(shape) == ["$schema", "description", "type", "properties", "additionalProperties", "definitions"]
              and shape["$schema"] == "http://json-schema.org/draft-07/schema#"
              and shape["description"].startswith("DO NOT EDIT -- generated from vm-map/globals-test.toml and ")
              and "base_mod_build.py --build" in shape["description"] and shape["additionalProperties"] is False,
              "the shape says it is generated, and from what, and the top takes no other key")
        top = shape["properties"]
        check(list(top) == ["foe", "foes", "hero", "heroes", "stage"], f"the top names each table and scope ({list(top)})")
        check(top["hero"] == {"$ref": "#/definitions/records.hero"}
              and top["heroes"] == {"description": "heroes: an old name of the table hero; write hero",
                                    "$ref": "#/definitions/records.hero"},
              "a table's old name has its current name's records")
        reached = "foe is reached through its scope: write stage.<id>.enemy"
        check(top["foe"] == {"description": reached, "type": "null"} and top["foes"] == top["foe"],
              "a scoped table's own name, and its old name, are not offered at the top, and say how to reach it")
        definitions = shape["definitions"]
        check(list(definitions) == sorted(definitions) == ["record.foe", "record.hero", "records.foe", "records.hero",
                                                            "scope.stage"], f"the definitions ({list(definitions)})")
        check(definitions["records.hero"] == {"description": "hero: records by id, 0 to 3", "type": "object",
                                              "x-ome-decimal-keys": {"minimum": 0, "maximum": 3},
                                              "additionalProperties": {"$ref": "#/definitions/record.hero"}},
              "a table's records are keyed by decimal id, from 0 to records - 1")
        record = definitions["record.hero"]
        check(record["additionalProperties"] is False and list(record["properties"]) == sorted(record["properties"])
              == ["end", "level", "lvl", "name", "seen", "seen_flag", "speed", "stats"],
              f"a record names its fields and old names, sorted, and takes no other ({list(record['properties'])})")
        fields = record["properties"]
        check(fields["level"] == {"description": "hero.level: hero_levels, a whole number", "type": "integer",
                                  "minimum": -2147483648, "maximum": 4294967295},
              "an integer field takes the applier's range and names its global")
        check(fields["speed"]["type"] == "number" and fields["name"]["type"] == "string"
              and "code page (932)" in fields["name"]["description"], "a float field is a number, a string field text")
        state = "hero.seen_flag is saved state: a load brings the saved value back, so a file does not set it"
        check(fields["seen_flag"] == {"description": state, "type": "null"}
              and fields["seen"]["description"] == state.replace("seen_flag", "seen") and fields["seen"]["type"] == "null",
              "a state field, and its old name, are not offered, and say why")
        check({key: value for key, value in fields["lvl"].items() if key != "description"}
              == {key: value for key, value in fields["level"].items() if key != "description"}
              and fields["lvl"]["description"] == "hero.lvl: an old name of hero.level; write level",
              "a field's old name has its current name's schema")
        stats = fields["stats"]
        check(stats["additionalProperties"] is False and list(stats["properties"]) == ["0", "1", "2", "end", "hp"]
              and stats["properties"]["0"] == {"description": "column 0 is named: write hp", "type": "null"}
              and stats["properties"]["2"] == {"description": "column 2 is named: write end", "type": "null"}
              and stats["properties"]["1"]["type"] == "integer" and stats["properties"]["end"]["type"] == "integer",
              f"a named row offers its names and its unnamed columns' numbers, not its named ones ({stats})")
        routines = definitions["record.foe"]["properties"]["routines"]
        check(routines["x-ome-decimal-keys"] == {"minimum": 0, "maximum": 1}
              and routines["additionalProperties"]["type"] == "integer" and "properties" not in routines,
              "a row without names is keyed by its column numbers")
        scope = definitions["scope.stage"]
        check(scope["x-ome-decimal-keys"] == {"minimum": 1, "maximum": 99}
              and scope["additionalProperties"]["properties"] == {"enemy": {"$ref": "#/definitions/records.foe"}}
              and scope["additionalProperties"]["additionalProperties"] is False and top["stage"]
              == {"$ref": "#/definitions/scope.stage"}, "a scope is keyed by its ids and names its tables by short name")
        check(all(list(schema)[0] == "description" for schema in definitions.values())
              and text.endswith("}\n") and "\r" not in text, "each definition's description comes first; LF, one newline")
        again, _ = generate(mods, source)
        check(shape_of(again)[0] == text, "the shape is the same each time it is generated")
    with tempfile.TemporaryDirectory() as directory:
        mods, source = workspace(Path(directory), profile={})
        files, _ = generate(mods, source)
        check(shape_of(files)[1]["properties"] == {} and shape_of(files)[1]["definitions"] == {},
              "a registry without tables has a shape that takes no key")


def main():
    test_shapes()
    test_layout()
    test_shape_errors()
    test_rename_rule()
    test_record_map()
    test_record_map_without_tables()
    test_record_map_errors()
    test_alias_lint()
    test_old_names()
    test_old_names_lint()
    test_old_names_rename_rule()
    test_scopes()
    test_table_shape()
    print("FAILURES:", len(FAILS))
    return 1 if FAILS else 0


if __name__ == "__main__":
    sys.exit(main())
