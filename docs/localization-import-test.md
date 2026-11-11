# Localization import test

Results of the import test in the localization design (`superpowers/specs/2026-11-10-localization-design.md`
§18.1): converting an install whose scripts were translated in place into a text catalog, and checking what the
catalog carries. The design stays in the spec; this page holds the test's results and the importer's rules, so the
spec can be revised against them. Gate 10 (half 1) ran on 2026-11-10 for milestone 0. Gate 11 (half 2) belongs to
milestone 6 and has not run.

**Status: milestone 0's numbers are waiting for review** (spec §19: "Its numbers are reviewed before milestone 1
starts, and §6 is revised if they call for it"). The section "For the review" lists what the catalog does not carry.

The translated text belongs to its translators. The catalog and the report are local test data under
`build/games/kamidori/import-en/` (gitignored); never commit or ship them. This page quotes only short fragments.

## How it runs

```
dotnet run --project engine/Ome.Cli -- omp text import-scripts --profile kamidori ^
    --from ../Kamidori_Translated --overlay-root patch --allow-bmp-as-agf ^
    --output build/games/kamidori/import-en --verify
```

- Base: the clean install `Kamidori/` (engine 4.33G). Translated: `Kamidori_Translated/` (4.33F) with its `patch/`
  overlay. Both mount the same archives.
- The command, its options and its output are in `tools-reference.md` (`omp text import-scripts`). A run takes
  about 5 s. The full report is `import-report.json` beside the catalog: counts, plus up to 200 samples per list.
- Code: `engine/Ome.Mods/Localization/Import/`, with the segmenter in `engine/Ome.Engine/Localization/`. Tests:
  `TextImportTests`, `TextCatalogFileTests`, `LocalizationTextModelTests` (synthetic scripts).

## Gate 10 results (2026-11-10)

Each row is a report line from spec §18.1's table.

| Report line | Result | What it says |
|---|---|---|
| Scripts that do not align once text instructions are set aside | 0. 302 scripts are identical; 430 changed and all 430 align; 0 jump targets disagree | Alignment is mechanical, as spec §2 found |
| Added `show-text` + `end-text-line` pairs | 18,960 + 18,960, all inside message spans (0 orphans, 0 ambiguous boundaries) | Every hand-made line break joins back into its message (spec §6.2) |
| Messages | 45,968: 45,655 literal, 313 with a variable (all 313 are only a variable). 44,352 translated, 1,616 untranslated (909 of these are the damaged notes below) | The segmenter's message definition holds on real data |
| Messages that could not be joined | 0. 5 the translators emptied, 0 damaged, 0 with an added variable | §6.2 needs no change |
| Scope that serves each message | global 43,806; script 448; site 133; untranslated 1,268. Entries: 47,321 global, 437 script, 133 site, 0 patterns | Decision 2 holds. Site scope is needed for 0.3% of messages (finding 1) |
| `--verify` | 0 mismatches over 45,655 messages, 86 drawn literals and 306 patterns | Messages and drawn strings reproduce exactly |
| Changed literals outside messages | `set-string` 10,665, `draw-string` 15, `concat` 42; also 265 `coroutine-label-yield` and 26 `comment` operands, which nothing draws | Table and UI strings mostly convert as global entries; 695 do not (finding 2) |
| Literals that feed compares or the value switch | 3, all `""` written as `" "`, the translated install's empty-string damage (finding 4) | As the spec expected, they need no entry |
| Emptied furigana | 2,960 of 3,072 runs emptied; 104 keep a reading | Dropping ruby from the presented text matches what the translators did. Spec §2's 2,595 counted only the 120 same-shape scripts |
| Literals that feed `concat` | 42, all in `SHOWCC` and `SC5510`, such as `最大指揮力＋` and `』を覚えた！` | Candidates for patterns (§6.3). Patterns get no real test from this translation: its 313 variable messages are variables only |
| Changed files that are neither scripts nor images | 4: `AGE.EXE`, `AGERC.DLL` and `SYS4RK.BIN` (the 4.33F engine), and a stray `SO009 (older one).AGF` | Nothing the design lacks a home for |
| Replaced images | 232 | The list for `assets/<language>/` (§12). Spec §2's 243 counted image files: 4 names appear twice, 6 are identical to the official loose updates, 1 is the stray file |

## For the review: what the catalog does not carry

### 1. Site scope (`at`), spec §20's open question

133 messages, in 64 scripts, need a site entry: one script translates the same source two ways. There are 93
distinct sources. Without `at`, each of these shows another translation its script uses. Most are short replies
(`「……」`, `「はい」`), where the other translation would read fine. Others carry content the Japanese line does
not have. `『新しくクエストが登録されました』` ("a new quest was registered") is 18 of the 114 cases of a source translated
two ways in one script. The translators wrote the quest's name into each one, so without `at` a script announces
the wrong quest.

Recommendation: keep `at` in version 1. Any site entry needs the offset table of spec §6.1; 64 catalog files carry
a fingerprint.

### 2. Stored text that the translation laid out again (728 strings)

Spec §5.2 resolves a stored string where it is drawn, by its text. Every table row that repeats a Japanese
string therefore gets one translation. The translators often wrote one per row, and the global entry can carry
only one of them.

| Table | Strings | What the Japanese stores, and what the translation did |
|---|---|---|
| QUINIT (quests) | 255 | Each quest's description is three line cells built from parts: the client (`漁師からの依頼`), the place (`霊悔の森「汚濁河川の上流地」`) and the action (`の調査`). Cells `0x6ba5`–`0x6ba6` repeat the place and action as the objective. The English reflows a sentence across the same cells, so a part such as `の調査` holds a different fragment in each quest |
| STINIT (stage descriptions) | 31 + 32 empty | Each stage description is four line cells, `0x24ab`–`0x24ae`. The English reflows across the lines, and in 32 stages it needed a fourth line the Japanese left empty (`STINIT@0x12b`). An empty string is no key, so that line is lost. One more empty stored line was filled in `SC5560` |
| EBINIT (units) | 231 | Generic description lines shared by many units (`闇の力により本来の力を超えた能力を持つ`), which the translators made unit-specific |
| CDINIT2 (cards) | 69 | Generic card-type labels (`素材のカード`) replaced by each card's own name |
| other tables and system scripts | 85 | Labels shared across rows, translated per row: ITINIT 20, CIINIT 16, CNINIT 13, OBINIT 12, SKINIT 6, ALINIT 5, CVINIT 4, INIT2 3, and one each in CCINIT, AFINIT, FAINIT, TITLE, STAGEROUND and HOSOKU |
| SC and SP scripts | 24 | Stored labels a scene reuses with another meaning, in 15 scripts |

These come to 695 conflicts with the global choice (388 distinct sources), plus the 33 filled empty lines (spec
§9.3: an empty source is not a valid key). The rows where the global translation is wrong show the wrong fragment
or label. In QUINIT and STINIT the lines before a lost line end mid-sentence.

Two shapes, which may need different answers:

- **Multi-line text built from cells** (QUINIT, STINIT). The Japanese layout is a set of fixed fragments; the
  English is one paragraph per row, wrapped by hand into the cells.
- **Shared labels made row-specific** (EBINIT, CDINIT2 and the rest).

Options the review could weigh. These are the importer author's suggestions, not decisions:

- **Accept both as limits of version 1.** A translator can then patch the table script with an OMS fragment, since
  the table rows are code.
- **Remember which literal wrote each string cell.** `set-string` and copies would carry a site tag beside the
  value, and a draw would resolve the cell's text with that site, so a site entry could apply to a stored string.
  The value itself stays source, which keeps §5.1's identity rule. A site entry could then also key an empty
  source. The tag would need defined rules for copies, `concat` and saves. These tables are rewritten by their
  scripts at boot (QUINIT, EBINIT) or on demand (STINIT), so their tags would not depend on a save.
- **A grouped entry for consecutive line cells,** translating the joined lines as one text and wrapping it into the
  cells at draw time. That needs the presenter to know which cells form a group, which nothing in the scripts
  says.

### 3. Stored strings the install left untranslated (635)

Where a stored source is translated at some sites and left in Japanese at others, the global entry takes the
translation (rule 2 below), so the catalog presents more than the install did. 540 of these are the scene
scripts' own copies of the recollection menu labels: `シーン回想終了` and `キャンセル`, 270 each, at the same
place in every SC script. They are untranslated, and damaged too (finding 4), while the SP scripts translate them.
The rest are labels such as `魔獣／四足合成種` in EBINIT. No change to the design follows; gate 11 will show these
pages differing (see "Gate 11" below).

## The translated install's own damage

The translated scripts carry two systematic changes that are not translations. The importer keeps the source for
both and reports them on their own, in `damage` in the report.

- **Strings missing characters: 6,132** (2,837 distinct sources). Strings the translators left in Japanese or
  ASCII come back shorter: `キャンセル` reads `ャンル`, `savemesskip advend` reads `avemesskip advend`, and
  `週送り。` reads three different ways at three sites. The raw script bytes hold the short text
  (`SC0000@0x22024` reads `ABEL` where the base has `LABEL`), so this is in the files, not in the decoder. By
  operand: `comment` 3,590, `set-string` 1,366, messages 909 (52 developer notes such as `施設訪問。` in code no
  player reaches), `coroutine-label-yield` 265, `concat` 2. The rule: the translated text is non-empty, shorter
  than the source, and a subsequence of it.
- **Empty strings written as one space: 2,735,** all `set-string`, plus the 3 compare literals. They draw the same
  as empty, so they need no entry.

One translated string also holds a control character. `SC2310@0x1232`'s `show-text` begins with a tab, which
`Sys4StringCodec` rejects. The importer reads it leniently, with the tab as a space; the port draws nothing there
(`backlog.md`, "Strings with control characters draw nothing").

## Rules the importer applies

The review may want to change these, so they are stated here. The code is `ScriptTranslationImporter.cs`.

1. **Alignment.** Greedy, instruction by instruction. An added `show-text` or `end-text-line` is an insertion;
   anything else that does not match fails the script. Matching ignores string operands, code-target operands and
   op `0x64`'s data offset. A post-check confirms every code target lands on the matched instruction.
2. **Choosing entries.** Each source's global entry is its most common translation. When the source also occurs as
   a stored literal, the global entry is the most common *translation*, so untranslated copies cannot outvote it:
   a stored literal can only use the global entry. A script whose majority differs gets a script entry. Each
   message or drawn literal that differs from its scope's choice gets a site entry, with the base script's
   fingerprint in its file.
3. **Message spans.** A base message's translated text is everything from its first run's match up to the next
   base instruction after it that is neither a trailing `end-text-line` nor a marker. Runs are joined with a space
   at each line break, then runs of ASCII spaces collapse; U+3000 stays text.
4. **Variable runs** become `{v1}`, `{v2}`… patterns with script scope.
5. **Damage.** A translated string that is a shorter, non-empty subsequence of its source counts as untranslated.
   An empty source written as whitespace needs no entry. An empty source filled with text is reported as not
   carried.
6. **`--verify`** compares with every run of whitespace collapsed, as gate 11 will. A stored literal is checked
   against the global entry only, and any difference is reported apart from mismatches, as a conflict or as
   "translated anyway".

## Gate 11 (milestone 6)

Not run. Run A (the translated install) and run B (the clean install with this catalog) are expected to differ on
pages that draw:

- strings damaged in the translated install, which run B shows in full Japanese;
- the 635 stored strings run B translates and run A does not;
- table rows where finding 2's global choice is not that row's translation;
- `SC2310@0x1232`, unless the decoder gap is fixed first.

The gate will need a rule for these known differences, or the review's answer to finding 2.
