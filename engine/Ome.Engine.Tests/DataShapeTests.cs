using Ome.Mods.Data;

/// <summary>
/// The shape check of docs/superpowers/specs/2026-11-17-data-patches-design.md §7 (milestone 3, plan §6.2): a kind's
/// shape in a subset of JSON Schema, read with each keyword outside the subset refused, and a document checked against
/// it with the path, the line and the reason of each problem: the nearest names for a key an object refuses, the range
/// for a key that is a whole number in decimal (x-ome-decimal-keys), and the shape's own reason for a field it marks as
/// not offered. Through the link and on the shipped base mods: DataLinkTests, DataShapeDriftTests and gate 12's tests.
/// </summary>
public sealed class DataShapeTests
{
    private const string File = "data/a.toml";

    /// <summary>A shape like the generated ones, small: records by id, a record with a value, a row with a named and an
    /// unnamed column and a named column's number not offered, a state field, and a scoped table's name.</summary>
    private const string Units = """
        {
          "$schema": "http://json-schema.org/draft-07/schema#",
          "description": "test.tables",
          "type": "object",
          "properties": {
            "unit": { "$ref": "#/definitions/records.unit", "description": "unit: records by id, 0 to 999" },
            "units": { "$ref": "#/definitions/records.unit", "description": "an old name of unit; write unit" },
            "foe": { "type": "null", "description": "foe is reached through its scope: write stage.<id>.foe" }
          },
          "additionalProperties": false,
          "definitions": {
            "records.unit": {
              "type": "object",
              "x-ome-decimal-keys": { "minimum": 0, "maximum": 999 },
              "additionalProperties": { "$ref": "#/definitions/record.unit" }
            },
            "record.unit": {
              "type": "object",
              "properties": {
                "experience_reward": { "type": "integer", "minimum": -2147483648, "maximum": 4294967295 },
                "name": { "type": "string" },
                "speed": { "type": "number" },
                "growth_exp": { "type": "null", "description": "unit.growth_exp is saved state: a load brings the saved value back, so a file does not set it" },
                "stats": {
                  "type": "object",
                  "properties": {
                    "max_hp": { "type": "integer" },
                    "0": { "type": "integer" },
                    "1": { "type": "null", "description": "column 1 is named: write max_hp" }
                  },
                  "additionalProperties": false
                },
                "flags": {
                  "type": "object",
                  "x-ome-decimal-keys": { "minimum": 0, "maximum": 4 },
                  "additionalProperties": { "type": "integer" }
                }
              },
              "additionalProperties": false
            }
          }
        }
        """;

    private static DataShape Shape(string json)
    {
        DataShapeRead read = DataShape.Read(json);
        Assert.True(read.Shape != null, string.Join("\n", read.Problems));
        return read.Shape!;
    }

    private static string[] Broken(string json)
    {
        DataShapeRead read = DataShape.Read(json);
        Assert.Null(read.Shape);
        Assert.NotEmpty(read.Problems);
        return read.Problems.ToArray();
    }

    private static DataShapeResult Check(string shape, string toml)
    {
        DataReadResult read = DataDocumentReader.Read(toml, File);
        Assert.True(read.Ok, string.Join("\n", read.Problems.Select(problem => problem.Describe(File))));
        return DataShapeCheck.Check(Shape(shape), read);
    }

    private static string[] Problems(string shape, string toml)
    {
        DataShapeResult result = Check(shape, toml);
        Assert.False(result.Ok);
        Assert.Null(result.StepLimit);
        return result.Problems.Select(problem => problem.Describe(File)).ToArray();
    }

    // ------------------------------------------------------------------------------------------------- a document

    [Fact]
    public void ADocumentTheShapeTakesPassesWithItsOldNamesDecimalKeysAndAWholeFloat()
    {
        DataShapeResult result = Check(Units, """
            [unit.761]
            experience_reward = 4294967295
            name = "Lily"
            speed = 2
            stats = { max_hp = 5.0, 0 = 1 }
            flags.4 = 1

            [units.0]
            experience_reward = -2147483648
            """);
        Assert.True(result.Ok);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void EachProblemGivesItsLineItsPathAndItsReason()
    {
        string[] problems = Problems(Units, """
            [unit.761]
            experiance_reward = 999
            growth_exp = 3
            stats.1 = 4
            stats.max_hp = "5"
            name = 7
            speed = true

            [unit.12]
            experience_reward = 4294967296
            stats = { maxhp = 5 }
            flags = { 5 = 1, x = 2 }

            [foe.3]
            unit_id = 1
            """);
        Assert.Equal([
            "data/a.toml:2: unit.761.experiance_reward: no such key; the nearest name here is experience_reward",
            "data/a.toml:3: unit.761.growth_exp: unit.growth_exp is saved state: a load brings the saved value back, so a file does not set it",
            "data/a.toml:4: unit.761.stats.1: column 1 is named: write max_hp",
            "data/a.toml:5: unit.761.stats.max_hp: takes a whole number, not a string",
            "data/a.toml:6: unit.761.name: takes a string, not 7",
            "data/a.toml:7: unit.761.speed: takes a number, not a boolean",
            "data/a.toml:10: unit.12.experience_reward: takes a whole number from -2147483648 to 4294967295, not 4294967296",
            "data/a.toml:11: unit.12.stats.maxhp: no such key; the nearest name here is max_hp",
            "data/a.toml:12: unit.12.flags.5: a key here is a whole number from 0 to 4, not 5",
            "data/a.toml:12: unit.12.flags.x: a key here is a whole number in decimal from 0 to 4, not x",
            "data/a.toml:14: foe: foe is reached through its scope: write stage.<id>.foe",
        ], problems);
    }

    [Theory]
    [InlineData("[unit.0x2f9]", "unit.0x2f9: a key here is written in decimal: 761")]
    [InlineData("[unit.0761]", "unit.0761: a key here is written without a leading zero: 761")]
    [InlineData("[unit.-1]", "unit.-1: a key here has no sign")]
    [InlineData("[unit.1000]", "unit.1000: a key here is a whole number from 0 to 999, not 1000")]
    [InlineData("[unit.99999999999999999999]", "unit.99999999999999999999: a key here is a whole number from 0 to 999, not 99999999999999999999")]
    [InlineData("[unit.lily]", "unit.lily: a key here is a whole number in decimal from 0 to 999, not lily")]
    [InlineData("[unit.\"7 7\"]", "unit.\"7 7\": a key here is a whole number in decimal from 0 to 999, not \"7 7\"")]
    public void AKeyThatShouldBeAWholeNumberInDecimalSaysHow(string header, string problem)
    {
        Assert.Equal([$"data/a.toml:1: {problem}"], Problems(Units, header + "\nexperience_reward = 1\n"));
    }

    [Fact]
    public void TheNearestNamesAreTheOfferedOnesAndWithNoneNearTheNamesThereAreListed()
    {
        const string many = """
            { "type": "object", "properties": {
                "accuracy": {}, "agility": {}, "evasion": {}, "luck": {}, "max_hp": {}, "max_sp": {}, "max_fs": {},
                "movement": {}, "physical_attack": {}, "physical_defense": {}, "hidden": { "type": "null" } },
              "additionalProperties": false }
            """;
        Assert.Equal(["data/a.toml:1: max_xp: no such key; the nearest names here are max_hp, max_sp and max_fs"],
                     Problems(many, "max_xp = 1\n"));
        Assert.Equal(["data/a.toml:1: hiddn: no such key; the names here are accuracy, agility, evasion, luck, max_fs, "
                      + "max_hp, max_sp, movement and 2 more"], Problems(many, "hiddn = 1\n"));
        Assert.Equal(["data/a.toml:1: Luck: no such key; the nearest name here is luck"], Problems(many, "Luck = 1\n"));
        Assert.Equal(["data/a.toml:1: x: no key is taken here"],
                     Problems("""{ "type": "object", "additionalProperties": false }""", "x = 1\n"));
    }

    [Fact]
    public void ANameCutShortOrWithAWordTooManyHasItsNearestNames()
    {
        // Plan §12.5: one of the two starts the other, the shorter having three characters at least, and the near names
        // rank by distance.
        const string shape = """
            { "type": "object", "properties": {
                "name_id": {}, "min_count": {}, "max_count": {}, "spawn_x": {}, "spawn_y": {}, "spawn_slot": {},
                "colour": {}, "first_only": {}, "weight": {} },
              "additionalProperties": false }
            """;
        Assert.Equal(["data/a.toml:1: name: no such key; the nearest name here is name_id"], Problems(shape, "name = 1\n"));
        Assert.Equal(["data/a.toml:1: min: no such key; the nearest name here is min_count"], Problems(shape, "min = 1\n"));
        Assert.Equal(["data/a.toml:1: spawn: no such key; the nearest names here are spawn_x, spawn_y and spawn_slot"],
                     Problems(shape, "spawn = 1\n"));
        Assert.Equal(["data/a.toml:1: weight_percent: no such key; the nearest name here is weight"],
                     Problems(shape, "weight_percent = 1\n"));
        // Two characters are too few to start a name.
        Assert.Equal(["data/a.toml:1: na: no such key; the names here are colour, first_only, max_count, min_count, "
                      + "name_id, spawn_slot, spawn_x, spawn_y and 1 more"], Problems(shape, "na = 1\n"));
    }

    [Fact]
    public void AValueOfTheWrongKindWhereATableOrAnArrayGoesSaysWhatGoesThereInTheShapesWords()
    {
        const string shape = """
            { "type": "object", "properties": {
                "by_id": { "description": "by_id: names by id; write by_id.<id> = ...", "type": "object",
                           "x-ome-decimal-keys": { "minimum": 0, "maximum": 99 }, "additionalProperties": { "$ref": "#/definitions/name" } },
                "names": { "description": "The names, in order.", "type": "array", "items": { "type": "string" } },
                "plain": { "type": "object" },
                "level": { "description": "level: a whole number", "type": "integer" },
                "label": { "$ref": "#/definitions/name" } },
              "definitions": { "name": { "description": "a name", "type": "string" } } }
            """;
        // Only where a table or an array goes: a scalar's description adds nothing to "takes a string".
        Assert.Equal([
            "data/a.toml:1: by_id: takes a table, not an array (by_id: names by id; write by_id.<id> = ...)",
            "data/a.toml:2: names: takes an array, not a string (The names, in order.)",
            "data/a.toml:3: plain: takes a table, not 5",
            "data/a.toml:4: level: takes a whole number, not a table",
            "data/a.toml:5: label: takes a string, not 3",
        ], Problems(shape, "by_id = [1]\nnames = \"x\"\nplain = 5\nlevel = { a = 1 }\nlabel = 3\n"));
    }

    [Fact]
    public void PastTheFirstTwentyAProblemIsCountedAndOneOfsFormsCountNone()
    {
        // Thirty keys the record does not take and one value of the wrong kind: each is counted, and the first twenty
        // are kept with their reasons (a reason is built only for a problem that is kept).
        string many = "[unit.761]\n" + string.Concat(Enumerable.Range(0, 30).Select(k => $"experience_reward{k:D2} = 1\n"))
                      + "name = 7\n";
        DataShapeResult result = Check(Units, many);
        Assert.Equal(31, result.ProblemCount);
        Assert.Equal(20, result.Problems.Count);
        Assert.Equal("data/a.toml:21: unit.761.experience_reward19: no such key; the nearest name here is experience_reward",
                     result.Problems[^1].Describe(File));
        Assert.All(result.Problems, problem => Assert.EndsWith("the nearest name here is experience_reward", problem.Text));

        // A form of oneOf that fails on each of its keys counts nothing; the value that fits no form is one problem.
        const string forms = """
            { "type": "object", "properties": {
                "pick": { "oneOf": [ { "type": "object", "additionalProperties": false }, { "type": "string" } ] } } }
            """;
        DataShapeResult picked = Check(forms, "pick = { a = 1, b = 2, c = 3 }\n");
        Assert.Equal(1, picked.ProblemCount);
        Assert.Equal(["data/a.toml:1: pick: matches none of the 2 forms its shape gives here"],
                     picked.Problems.Select(problem => problem.Describe(File)));
    }

    [Fact]
    public void EnumOneOfItemsAndTheRangeAreChecked()
    {
        const string shape = """
            { "type": "object", "properties": {
                "mode": { "enum": ["easy", "hard", 3, true] },
                "level": { "type": "integer", "minimum": 1, "maximum": 99 },
                "ratio": { "type": "number", "minimum": 0 },
                "pick": { "oneOf": [ { "type": "string" }, { "type": "integer", "maximum": 9 }, { "type": "number", "minimum": 5 } ] },
                "pick2": { "oneOf": [ { "type": "integer" }, { "minimum": 5 } ] },
                "list": { "type": "array", "items": { "type": "string" } } } }
            """;
        Assert.True(Check(shape, "mode = 3.0\nlevel = 99\nratio = 0.5\npick = 2\nlist = [\"a\", \"b\"]\n").Ok);
        Assert.Equal([
            "data/a.toml:1: mode: takes one of \"easy\", \"hard\", 3, true, not a string",
            "data/a.toml:2: level: takes a whole number from 1 to 99, not 0",
            "data/a.toml:3: ratio: takes a number of at least 0, not -0.5",
            "data/a.toml:4: pick: matches none of the 3 forms its shape gives here",
            "data/a.toml:5: pick2: matches 2 of the 2 forms its shape gives here, and oneOf takes one",
            "data/a.toml:6: list[2]: takes a string, not 7",
        ], Problems(shape, "mode = \"normal\"\nlevel = 0\nratio = -0.5\npick = true\npick2 = 7\nlist = [\"a\", 7]\n"));
    }

    [Fact]
    public void TheLineOfAnArrayOfTablesAnInlineTableAndAnInlineArraysElement()
    {
        const string shape = """
            { "type": "object", "properties": {
                "a": { "type": "object", "properties": { "b": { "type": "integer" } }, "additionalProperties": false },
                "rows": { "type": "array", "items": { "type": "object", "properties": { "n": { "type": "integer" },
                          "inner": { "type": "array", "items": { "type": "object", "additionalProperties": false } } } } },
                "inline": { "type": "array", "items": { "type": "integer" } } } }
            """;
        string[] problems = Problems(shape, """
            inline = [
              1,
              "two",
            ]
            a = { b = 1, c = 2 }

            [[rows]]
            n = 1

            [[rows]]
            n = "two"

            [[rows.inner]]
            x = 1

            [[rows.inner]]
            y = 1
            """);
        Assert.Equal([
            "data/a.toml:3: inline[2]: takes a whole number, not a string",
            "data/a.toml:5: a.c: no such key; the nearest name here is b",
            "data/a.toml:11: rows[2].n: takes a whole number, not a string",
            "data/a.toml:14: rows[2].inner[1].x: no key is taken here",
            "data/a.toml:17: rows[2].inner[2].y: no key is taken here",
        ], problems);
    }

    [Fact]
    public void APathsLineIsThatOfTheFirstKeyOrHeaderThatWritesIt()
    {
        // A dotted key writes its tables on its own line; a header writes its table; a key writes its value.
        Assert.Equal(["data/a.toml:3: unit.761.stats.1: column 1 is named: write max_hp"],
                     Problems(Units, "[unit.761]\nname = \"x\"\nstats.1 = 4\n"));
        Assert.Equal(["data/a.toml:2: unit.761.stats: takes a table, not 150"], Problems(Units, "[unit.761]\nstats = 150\n"));
        Assert.Equal(["data/a.toml:1: unit.761: takes a table, not 5"], Problems(Units, "unit.761 = 5\n"));
        Assert.Equal(["data/a.toml:3: unit.lily: a key here is a whole number in decimal from 0 to 999, not lily"],
                     Problems(Units, "[unit.761]\nname = \"x\"\n[unit.lily.stats]\nmax_hp = 1\n"));
    }

    [Fact]
    public void ADocumentPastTheFirstTwentyProblemsIsCountedAndAShapeBuiltToCostTimeStopsAtItsStepBound()
    {
        string many = string.Concat(Enumerable.Range(0, 25).Select(k => $"[unit.{1000 + k}]\nname = \"x\"\n"));
        DataShapeResult result = Check(Units, many);
        Assert.Equal(25, result.ProblemCount);
        Assert.Equal(20, result.Problems.Count);

        // oneOf over oneOf, sixteen deep through one definition: each level checks both forms of the next.
        const string costly = """
            { "$ref": "#/definitions/deep",
              "definitions": { "deep": { "oneOf": [ { "$ref": "#/definitions/deep2" }, { "$ref": "#/definitions/deep2" } ] },
                               "deep2": { "type": "object", "additionalProperties": { "$ref": "#/definitions/deep" } } } }
            """;
        string nested = "a.b.c.d.e.f.g.h.i.j.k.l.m.n.o = 1\n";
        DataShapeResult costlyResult = Check(costly, nested);
        Assert.Equal(DataShapeCheck.StepsBase + DataShapeCheck.StepsPerValue, costlyResult.StepLimit);
        Assert.False(costlyResult.Ok);
    }

    // ------------------------------------------------------------------------------------------------- a broken shape

    [Theory]
    [InlineData("""{ "type": "object", "required": ["a"] }""", "#/required: the keyword required is outside the shape subset")]
    [InlineData("""{ "properties": { "a": { "pattern": "x" } } }""", "#/properties/a/pattern: the keyword pattern is outside the shape subset")]
    [InlineData("""{ "x-taplo": { "hidden": true } }""", "#/x-taplo: the keyword x-taplo is outside the shape subset")]
    [InlineData("""{ "$ref": "other.json#/definitions/a" }""", "#/$ref: names a definition of this file (#/definitions/NAME)")]
    [InlineData("""{ "$ref": "#/definitions/missing" }""", "#/$ref: names no definition (#/definitions/missing)")]
    [InlineData("""{ "$ref": "#/definitions/a", "type": "object", "definitions": { "a": {} } }""", "#: a schema with $ref takes only a description beside it, not type")]
    [InlineData("""{ "$ref": "#/definitions/a", "definitions": { "a": { "$ref": "#/definitions/b" }, "b": { "$ref": "#/definitions/a" } } }""", "#/definitions/a/$ref: the chain of $ref comes back to itself")]
    [InlineData("""{ "x-ome-decimal-keys": { "minimum": 5, "maximum": 1 } }""", "#/x-ome-decimal-keys: minimum 5 is above maximum 1")]
    [InlineData("""{ "x-ome-decimal-keys": { "minimum": -1, "maximum": 1 } }""", "#/x-ome-decimal-keys/minimum: is a whole number from 0 to 2^53")]
    [InlineData("""{ "x-ome-decimal-keys": { "minimum": 0 } }""", "#/x-ome-decimal-keys: gives minimum and maximum")]
    [InlineData("""{ "x-ome-decimal-keys": { "minimum": 0, "maximum": 1 }, "additionalProperties": false }""", "#: x-ome-decimal-keys takes the keys additionalProperties checks")]
    [InlineData("""{ "properties": { "a": { "$schema": "x" } } }""", "#/properties/a/$schema: $schema is only at the top of a shape")]
    [InlineData("""{ "type": "text" }""", "#/type: text is not a type")]
    [InlineData("""{ "type": ["string", "string"] }""", "#/type: names string twice")]
    [InlineData("""{ "items": [ {}, {} ] }""", "#/items: is one schema here, for every element")]
    [InlineData("""{ "enum": [ {} ] }""", "#/enum: holds strings, numbers and booleans")]
    [InlineData("""{ "oneOf": [] }""", "#/oneOf: is a list of one schema at least")]
    [InlineData("""{ "minimum": 3, "maximum": 1 }""", "#: minimum 3 is above maximum 1")]
    [InlineData("""{ "description": 5 }""", "#/description: is a string")]
    [InlineData("""[ 1 ]""", "#: a shape is a JSON object")]
    [InlineData("""{ "type": "object", }""", "line 1: not JSON")]
    public void AShapeOutsideTheSubsetOrBrokenIsRefusedWithWhereAndWhy(string json, string problem)
    {
        Assert.Contains(Broken(json), line => line.StartsWith(problem, StringComparison.Ordinal));
    }

    [Fact]
    public void AShapePastItsSizeIsRefused()
    {
        string big = "{ \"description\": \"" + new string('x', (int)DataShape.MaxBytes) + "\" }";
        Assert.Equal(["#: a shape is at most 4 MiB"], Broken(big));
    }

    [Fact]
    public void TheDistanceCountsASwapOfNeighboursAsOne()
    {
        Assert.Equal(1, DataShapeCheck.Distance("experiance", "experience"));
        Assert.Equal(1, DataShapeCheck.Distance("maxhp", "max_hp"));
        Assert.Equal(1, DataShapeCheck.Distance("stast", "stats"));
        Assert.Equal(1, DataShapeCheck.Distance("Luck", "luck"));
    }
}
