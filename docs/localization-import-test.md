# Localization import test

Results of the import test in the localization design (`superpowers/specs/2026-11-10-localization-design.md`
§18.1): converting an install whose scripts were translated in place into a text catalog, and checking what the
catalog carries. The design stays in the spec; this page holds the test's results and the importer's rules, so the
spec can be revised against them. Gate 10 (half 1) ran on 2026-11-10 for milestone 0. Gate 11 (half 2) ran on
2026-11-11 for milestone 6.

**Status: reviewed, and the review's changes are in (2026-11-10).** Milestone 0's numbers were reviewed before
milestone 1, as spec §19 asks. The review found one loss (line structure), and the user accepted three design
decisions. The importer now carries out every importer change the review asked for (see "Review answers", then
"Implemented"), and the spec has the design changes. The results below are from the importer after the review. The
section "For the review" keeps the findings the review answered, each with what now carries it.

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

Each row is a report line from spec §18.1's table. The numbers are from the importer after the review; where the
first run differed, the row says why.

| Report line | Result | What it says |
|---|---|---|
| Scripts that do not align once text instructions are set aside | 0. 302 scripts are identical; 430 changed and all 430 align; 0 jump targets disagree | Alignment is mechanical, as spec §2 found |
| Added `show-text` + `end-text-line` pairs | 18,960 + 18,960, all inside message spans (0 orphans, 0 ambiguous boundaries) | Every hand-made line break joins back into its message (spec §6.2) |
| Messages | 45,968: 45,655 literal, 313 with a variable (all 313 are only a variable). 44,336 translated, 1,632 untranslated, the damaged messages below among them. The first run had 16 more translated: a message is now checked for damage as a whole, and 20 `$3$ITMES` messages lost a character in every run | The segmenter's message definition holds on real data |
| Messages that could not be joined | 0. 5 the translators emptied, 0 damaged, 0 with an added variable | §6.2 needs no change |
| Line breaks in translated messages | 14,767 wraps and 6,239 deliberate breaks (in 5,892 messages). 998 wraps are uncertain, and 428 breaks stay deliberate by the added-break count. 5,748 entries hold `\n` | Line structure is kept (R1). The first run joined every break |
| Scope that serves each message | global 43,765; script 470; site 137; untranslated 1,283 | Decision 2 holds. Site scope is needed for 0.3% of messages (finding 1) |
| Entries | 47,302 global, 569 script, 690 site, 27 patterns. Stored literals take 126 of the script entries and 553 of the site entries. 89 catalog files carry a fingerprint | Stored literals are scoped like messages (R2, finding 2) |
| `--verify` | 0 mismatches over 45,655 messages, 86 drawn literals, 306 patterns, 34 composed strings and 12,171 stored literals. The comparison is exact. 635 stored literals that the install left untranslated are translated (finding 3) | Every message, literal and pattern reproduces exactly |
| Changed literals outside messages | `set-string` 10,665, `draw-string` 15, `concat` 44; also 265 `coroutine-label-yield` and 26 `comment` operands, which nothing draws | All are carried (finding 2). `concat` had 42: two literals that only lost words to another run now count as translations (R5) |
| Literals that feed compares or the value switch | 3, all `""` written as `" "`, the translated install's empty-string damage (see "The translated install's own damage") | As the spec expected, they need no entry |
| Emptied furigana | 2,960 of 3,072 runs emptied; 104 keep a reading | Dropping ruby from the presented text matches what the translators did. Spec §2's 2,595 counted only the 120 same-shape scripts |
| Composed strings | 30 stores build 34 templates. 3 are untranslated; the other 31, in 11 scripts, become 27 pattern entries with the composing script's scope. 0 walks gave up. Of the first run's "Literals that feed `concat`" row, the claim "all in `SHOWCC` and `SC5510`" was wrong (R4) | Patterns get a real test (R4). The 313 variable messages are only variables, so they test nothing |
| Changed files that are neither scripts nor images | 4: `AGE.EXE`, `AGERC.DLL` and `SYS4RK.BIN` (the 4.33F engine), and a stray `SO009 (older one).AGF` | Nothing the design lacks a home for |
| Replaced images | 232 | The list for `assets/<language>/` (§12). Spec §2's 243 counted image files: 4 names appear twice, 6 are identical to the official loose updates, 1 is the stray file |

## For the review: what the catalog does not carry

The first run's findings, as the review read them. The numbers in each are from the first run. The line after each
finding says what carries it now.

### 1. Site scope (`at`), spec §20's open question

**Now:** `at` ships in version 1 (R3; spec §20). 137 messages take a site entry.


133 messages, in 64 scripts, need a site entry: one script translates the same source two ways. There are 93
distinct sources. Without `at`, each of these shows another translation its script uses. Most are short replies
(`「……」`, `「はい」`), where the other translation would read fine. Others carry content the Japanese line does
not have. `『新しくクエストが登録されました』` ("a new quest was registered") is 18 of the 114 cases of a source translated
two ways in one script. The translators wrote the quest's name into each one, so without `at` a script announces
the wrong quest.

Recommendation: keep `at` in version 1. Any site entry needs the offset table of spec §6.1; 64 catalog files carry
a fingerprint.

### 2. Stored text that the translation laid out again (728 strings)

**Now:** all 728 are carried (R2). A stored literal is scoped by the instruction that stores it, so the 695
conflicts become 126 script entries and 553 site entries. Each of the 33 filled empty lines folds, after `\n`, into
the entry of the line before it. In the engine, the record of each store (spec §15) applies these entries where the
string is drawn; without that record, only a global entry could apply to table text.

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

**Now:** unchanged, as R2 settled. An untranslated copy gets no entry, so it takes its scope's translation.

Where a stored source is translated at some sites and left in Japanese at others, the global entry takes the
translation (rule 2 below), so the catalog presents more than the install did. 540 of these are the scene
scripts' own copies of the recollection menu labels: `シーン回想終了` and `キャンセル`, 270 each, at the same
place in every SC script. They are untranslated, and damaged too (finding 4), while the SP scripts translate them.
The rest are labels such as `魔獣／四足合成種` in EBINIT. No change to the design follows; gate 11 will show these
pages differing (see "Gate 11" below).

## The translated install's own damage

The translated scripts carry two systematic changes that are not translations. The importer keeps the source for
both and reports them on their own, in `damage` in the report.

- **Strings missing characters: 6,146.** Strings the translators left in Japanese or ASCII come back shorter:
  `キャンセル` reads `ャンル`, `savemesskip advend` reads `avemesskip advend`, and `週送り。` reads three different
  ways at three sites. The raw script bytes hold the short text (`SC0000@0x22024` reads `ABEL` where the base has
  `LABEL`), so this is in the files, not in the decoder. 6,101 lost one character. 45 lost more and still hold a
  letter; 20 of these are `$3$ITMES` messages in which every run lost a character. The first run's split by
  operand (2,837 distinct sources): `comment` 3,590, `set-string` 1,366, messages 909 (52 developer notes such as
  `施設訪問。` in code no player reaches), `coroutine-label-yield` 265, `concat` 2. The rule is rule 5 below.
- **Shortened translations: 6.** These lost more than one character and keep only punctuation, such as
  `SHOWCC@0x15`'s `』の称号を獲得` → `』`, where the English moved the words into another run. They count as
  translations. The report lists all 51 strings that lost more than one character, each with its class.
- **Empty strings written as one space: 2,735,** all `set-string`, plus the 3 compare literals. They draw the same
  as empty, so they need no entry.

One translated string also holds a control character. `SC2310@0x1232`'s `show-text` begins with a tab, which
`Sys4StringCodec` rejects. The importer reads it leniently, with the tab as a space; the port draws nothing there
(`backlog.md`, "Strings with control characters draw nothing").

## Rules the importer applies

These are the rules as of the review's changes. The code is `ScriptTranslationImporter.cs` (rules 1 to 6 and 8)
and `ComposedStrings.cs` (rule 7).

1. **Alignment.** Greedy, instruction by instruction. An added `show-text` or `end-text-line` is an insertion;
   anything else that does not match fails the script. Matching ignores string operands, code-target operands and
   op `0x64`'s data offset. A post-check confirms every code target lands on the matched instruction.
2. **Choosing entries.** Each source's global entry is its most common translation. When the source is also
   stored, the global entry is its most common *translation*, so untranslated copies cannot outvote it. A script
   whose majority differs gets a script entry. Each message, drawn literal or stored literal that differs from its
   scope's choice gets a site entry, whose `at` is the instruction that draws or stores it; its file carries the
   base script's fingerprint. A stored copy the install left untranslated gets no entry and takes its scope's
   choice. A literal stored into a frame local string gets only the global entry, since nothing records that
   store; this translation has none that conflict.
3. **Message spans.** A base message's translated text is everything from its first run's match up to the next
   base instruction after it that is neither a trailing `end-text-line` nor a marker. The runs are read as lines,
   and each break between two lines is a wrap or deliberate:
   - Widths are in half cells: the CP932 byte count. Each script and layout has a line edge, the width that 99%
     of its translated lines fit. A script and layout with fewer than 20 lines takes the edge of that layout over
     the whole install.
   - The break after line A is a wrap when A without trailing spaces, one space (none between two wide
     characters) and the first word of the next line would cross the edge. A break before a blank line, or before
     a line that starts with a space, a tab or U+3000, is deliberate.
   - A message has at most as many wraps as the breaks the translation added: its kept breaks less the base's
     authored breaks. Breaks past that count stay deliberate, the ones that cross the edge least first.
   - A wrap joins with one space, or nothing between two wide characters. A deliberate break becomes `\n`.
     Spaces inside a line stay, and leading and trailing blank lines are dropped.
   - The report counts wraps, deliberate breaks and the messages that hold one; wraps no wider than the script's
     widest line (uncertain); breaks kept deliberate by the count; each edge, with the lines wider than it; and
     the two spot-check lists.
4. **Variable runs** become `{v1}`, `{v2}`… patterns with script scope.
5. **Damage.** A translated string that is its source with characters missing is damage, and keeps the source,
   when one character is missing or a letter or digit is left. One that lost more and keeps only punctuation is a
   translation. A message is checked as the concatenation of its runs. An empty source written as whitespace needs
   no entry.
6. **`--verify`** compares exactly: both sides hold the importer's reading of the line breaks. A pattern or
   composed string is also checked by sample: its source, filled with sample values and presented by the engine's
   own `TextPresenter` (spec §6.4's order), must present as its translation filled the same way. A stored literal
   is checked at its storing site. One the install left untranslated is listed apart as "translated anyway", and
   a literal stored into a frame local is checked against the global entry and listed apart as a conflict.
7. **Composed strings.** Start at each instruction that stores a string variable into another (`set-string`),
   draws it (`draw-string`) or shows it (`show-text`), where some `concat` writes that variable. Walk back along
   each path inside the function to the write that starts its value; a call on the path is stepped over. The
   writes on a path give the template: literals as text, a `toString` result or an integer operand as
   `{vN:num}`, a string operand as `{vN}`, named in the order the base's template shows them. The aligned
   instructions give the translated template; a damaged translated literal falls back to the base's. A walk gives
   up after 8 paths or 2,048 steps, at a loop or at the function's entry, and the store is reported as skipped.
   Each template is a pattern entry with the composing script's scope. A template of literals only is an exact
   entry.
8. **Filled empty lines.** The text the translation wrote into an empty stored literal goes, after `\n`, into the
   entry of the line stored to the cell before it (index − 1), which the game draws one row higher. That line is
   the nearest earlier store into that cell in the same block, or the line it was folded into. With no such store
   the text is reported as not carried.

## Gate 11 results (2026-11-11)

**How it ran.** The port drove both installs over the same steps, `tools/drive/scenarios/kamidori-opening-pages.steps`
with `rand-seed 13`: the title, a new game, then a page click every 2.5 s through SC0000's opening until the first
stage's field (FIELD). The steps wait on scripts, never on offsets, so they run unchanged on the translated scripts.

- Run A: `Kamidori_Translated/` with `--overlay-root patch --allow-bmp-as-agf`, no catalog.
- Run B: `Kamidori/` with this catalog as a local package (`build/games/kamidori/import-en-mod`, local only) and
  `--language en`.

Each run wrote its page map with `--page-map`, and `tools/page_map_compare.py A.jsonl B.jsonl` compared them (usage
in `tools-reference.md`, "Runtime page locator"). Each run took about 6.5 minutes and reached the field after 151
clicks.

| Check | Result |
|---|---|
| Pages | 143 in each run, in the same order. The wait offsets differ, as expected |
| Same text on every page (required) | **143 of 143.** Run A's text with its wraps joined equals run B's presented text once whitespace is collapsed |
| Run B left in Japanese | none: every page presents English, speaker names included |
| Line breaks (reviewed) | run A has 64 wraps and 93 deliberate breaks by the box's real right edge, most of the deliberate ones between a name plate and its line. 12 pages break where the catalog has a wrap: see below |
| Wrapping (reviewed) | 1 page of run B ran past its layout's bottom: page 2 (`SC0000@0xd5a`), the narration. The translated install lays out every half-width character on an 11-pixel cell, and the translators broke this sentence into two lines of 61 characters. The port draws presented text at its glyphs' own widths (spec §7.4), and in メイリオ bold at 22 pixels the joined 123 characters wrap into three rows of a window that holds two (710 pixels each). Since the language's `overflow = "shrink"` (the default; `docs/modding/languages.md`), the port draws it at 96 % in two rows; `--text-log` lists it (`SC0000@0xd55`, `width_percent` 96). Rerun on 2026-11-11: 143 pages, same text on every page, none cut |
| Damaged strings, untranslated stored strings, `SC2310@0x1232` | not reached by the opening |

**The real edge does not find the translators' wraps.** The spec expected the real right edge to classify run A's
breaks without an estimate. On these pages it is the wrong model: the translators kept every line to 64 half cells,
and the box takes at least 67. On the 12 pages, the line, a space and the next word come to 65 to 67 half cells, so
the word would have fitted and the page map calls the break deliberate. Every one of them breaks a sentence in the
middle. The importer's edge, the width 99% of a script's lines fit, calls them wraps, and the catalog joins them,
which is right. So the page map's wrap class is a lower bound for an install wrapped by hand, and gate 11 needs only
the comparison that ignores line breaks.

**Overflow across the catalog (an estimate, not a run).** Measured with メイリオ bold's own advances at 22 pixels
against the 11-pixel half cells, and assuming every entry sits in a two-row window 710 pixels wide, 848 of the
19,846 entries that take two rows on half cells take three or more at the glyphs' widths (4 %). Narrowing fits each
of them back into two rows at 88 % or more, 97 % at the median; the run above narrowed page 2 a little less than
this model predicts. This is why `overflow = "shrink"` is the default.

**Saves** were not compared again: gate 2 already holds for run B's catalog (milestone 3).

### Expected differences

Run A (the translated install) and run B (the clean install with this catalog) are expected to differ on
pages that draw:

- strings damaged in the translated install, which run B shows in full Japanese;
- the 635 stored strings run B translates and run A does not;
- `SC2310@0x1232`, unless the decoder gap is fixed first.

Pages that draw them fail the required check and are reviewed against this list; the opening draws none. The review
answered finding 2, so table rows are no longer among them, provided the engine records each store (R2).

## Review answers (2026-11-10)

A review of the first run's page, the importer and the generated catalog. The review found one loss the first run
did not report (R1), answered findings 1 and 2 (R2, R3), and asked for two smaller importer changes (R4, R5). The
design changes have moved to the spec, and "Implemented" at the end of this section says what the importer does
and what it found. The plan was to cut this section down to the results at this point. That waits until the review
text is committed, so the discussion stays in the history.

**Decided by the user on 2026-11-10: yes to all three.** The importer's author answers each item under
"Responses from the importer's author" at the end of this section: it agrees with all three decisions and with R1
to R5, and proposes changes to R2, R4 and R5. Those go back to the user before the spec is revised.

1. A site entry also applies to a stored literal, through a record of which literal wrote each string cell (R2).
2. `\n` in the presented text of a `draw-string` starts a new row (R2, the filled empty lines).
3. `at` ships in version 1, which closes spec §20's open question (R3).

**For the importer:** R1, R4, R5 and R2's importer half. None of them waits on anything.

### R1. The catalog loses line structure, and `--verify` cannot see it

None of the 47,891 entries in `text/en/*.toml` holds a line break. Rule 3 joins every break with a space and
collapses runs of spaces, and rule 6 compares with whitespace collapsed, so `--verify` reports 0 mismatches. Spec
§18.1 asked for that comparison. It assumed every break in a translation is a hand-made wrap, and that is wrong
for the breaks the base scripts have themselves.

- **The base's own breaks are structure.** 2,252 base messages hold an authored `end-text-line`
  (`messages.with_authored_breaks`). The base disassembly has 2,609 `show-text`, `end-text-line`, `show-text`
  triples in 93 scripts: ITMES 987, VIMES 412, SKMES 375, `$3$ITMES` 279, FAMES 161, CIMES 68. These are item,
  vocabulary, skill, objective and profile texts.
- **Example.** `SKMES@0x14` is a header line (`【移動：飛行】…CP03`) and a description line. The translated install
  shows two lines: a description, then ` [ Movement Skill ]` padded with 17 spaces to `CP: 3`. The catalog entry is
  one run of text with single spaces, so the two lines run together and the column is gone.
- **Alignment cannot tell the breaks apart.** It is greedy and pairs runs within a message arbitrarily
  (`ScriptAlignment.TryAlign`), so "matched" and "inserted" do not say which `end-text-line` the base authored.

Asked of the importer, replacing the join in rule 3:

1. Keep each translated line as written. Do not collapse spaces inside a line.
2. Classify each break by content. Measure in half cells (a wide character counts 2). Let `W` be the widest
   translated line of that script on that layout. The break after line A is a **wrap** when A (without trailing
   spaces), one space and the first word of the next line together exceed `W`: the word did not fit. Otherwise it
   is **deliberate**. A next line that is blank, or starts with a space or U+3000, is always deliberate.
3. A wrap joins with one space. A deliberate break becomes `\n` in the entry (spec §7.1 already forces a break
   there).
4. Report the counts: wraps, deliberate breaks, messages with a deliberate break. List two groups for a spot
   check: messages whose base has an authored break and whose translation has no deliberate one, and messages
   whose translation has a deliberate break where the base has none.
5. Rule 6 compares with wraps joined and deliberate breaks kept.

`W` is an estimate, and one overlong unwrapped line inflates it. Check the result on ITMES, SKMES, FAMES and one
dialogue script before trusting the counts. A rough pass over the translated scripts found deliberate breaks in
983 ITMES, 375 SKMES and 155 VIMES blocks. It could not separate the boxes in dialogue scripts, so there is no
count for dialogue yet.

For the spec: §18.1's "ignoring line breaks and runs of spaces" becomes "ignoring wraps", for `--verify` and for
gate 11. Gate 11 can classify run A's breaks with the layout's real right bound, which the port knows at run
time. §6.2's key is unchanged: it still joins runs with nothing between them.

The space padding assumes a fixed-pitch font. It stays in the entry as written; whether presented text needs a
way to push a field to the right edge is a later question, not part of this request.

### R2. Finding 2: let a site entry apply to a stored literal

Recommended: the second option listed under finding 2, "remember which literal wrote each string cell", in a
form that needs no rules for copies, `concat` or saves.

**Why it is cheap.** The engine already knows the cell when it draws table text:

| Fact | Evidence |
|---|---|
| Table text is drawn through a pointer into a named string table | INFO2@`0x1171`: `lookup-array-2d` into `quest_description_lines`, then `draw-string` with the pointer |
| That is the usual shape of a variable draw | Himegari: 17,361 of 17,401 variable `draw-string` sites. Kamidori: 121 of 152, and all 313 variable `show-text` sites |
| The VM resolves such an operand to its cell | `BaseAddr` (`VirtualMachine.cs`), as `TryResolveSharedProfileCell` already does |
| A cell address alone is not enough | 9,398 of the 9,429 global cells that literals write have one writing site. 31 have several, and the stage loader (STINIT and its three append layers) writes 3,553 literals into them |

**The mechanism.**

- **Record.** When `set-string` (or `mov`) stores a literal operand into a global string cell, the VM records the
  storing instruction and the text for that cell (`VirtualMachine.Values.cs`, the `mov`/`set-string` case). The
  destination may be a direct cell or a pointer; both resolve to the cell.
- **Use.** At the three presentation sites and the three length opcodes, an operand that resolves to a global
  string cell (operand types 5, 8 and 14) takes the cell's record as its origin, but only if the cell still holds
  the recorded text. Any other write that changes the text therefore cancels the record without a hook of its
  own.
- **Lookup order.** Site entry at the origin instruction; site entry at the drawing instruction; script entry
  for the origin's script; script entry for the drawing script; global. For a literal operand the origin and the
  drawing instruction are the same, so nothing changes for messages.
- **Catalog syntax is unchanged.** `script` and `at` name the instruction that holds the literal, whether it
  draws it or stores it. `text/en/QUINIT.toml` then translates the strings QUINIT stores.
- **Off means off.** Records are kept only while a language is active, and a root reload clears them.

**What it carries, and its limits.**

- Of this page's 200 sampled conflicts (695 in all), 177 write a cell of their own, 16 store through a pointer
  and 7 write a shared cell. A site entry on the storing instruction carries all three kinds.
- **Loads.** Records are not saved. Boot tables (QUINIT, EBINIT and the others INIT2 runs) are written before any
  load, and a load writes the same text back, so their records stay valid. A cell filled on demand, such as the
  stage loader's, has no record after a load until its loader runs again; until then it is matched by text, as
  today. When the stage loader runs again after a load was not checked.
- **Not covered:** strings held in frame locals, and strings built with `concat` (R4 covers those).

**Importer half.** A stored literal gets entries as a message does: the global entry by majority, a script entry
where a script's majority differs, and a site entry (`at` = the storing instruction) for the rest. An untranslated
copy still gets no entry. `--verify` then checks a stored literal with its storing site.

**The 33 filled empty lines** cannot be carried by any per-cell entry. `SELSTAGE@0x1c4a` compares each stage
description line with `""` and skips the draw when the source is empty, and compares see the source (spec §5.1).
Recommended: fold each one into the entry of the line before it.

- **Importer.** A translated text for an empty stored literal is appended, after `\n`, to the entry of the
  nearest preceding non-empty literal stored to the cell just below it in the same block. If there is none, it
  stays on the "not carried" list.
- **Engine (decision 2, accepted).** `\n` in the presented text of a `draw-string` starts a new row at the same x, lower by
  the font height plus a gap. SELSTAGE steps 18 pixels for a 16-pixel font, and INFO2's quest lines step 21, so the
  gap needs a default (2 pixels fits SELSTAGE) and an entry attribute to override it.

### R3. Finding 1: keep `at`

Of the 114 sources translated two ways in one script, 102 differ in wording, 11 only in punctuation or case, and
one is an untranslated copy. The wording differences are real context: `「分かりました」` is "Understood." at one
site and "Yes, sir." at another. Site scope is a normal need of a translation, not an artifact of this one. R2
uses the same `at` and the same offset table (spec §6.1).

### R4. Composed strings are neither carried nor tested

The 42 changed `concat` literals build gameplay notices: a title gained, a skill learned, a weapon level, an item
found. They produce no entries, and `--verify` does not check them.

- **Correction to the table above.** They are not "all in SHOWCC and SC5510". By script: FIELD 14, SHOWCC 6,
  ALCHEMY 6, ADDSTOCK 3, SC5510 2, ADDEXP 2, CHECKIMPROVE 2, FELLOW 2, SALE 2, INFO2 2, ENHANCE 1.
- **Asked of the importer.** The chains are straight-line code (`SHOWCC@0x0`–`0x15`: a name, `が『`, a title,
  `』の称号を獲得`). Within a block with no branch target inside, follow the `concat`, `toString` and `set-string`
  writes to one destination and build a template: literal pieces as text, every other operand as a placeholder,
  a `toString` result as `{name:num}`. The aligned chain in the translated script gives the translated template
  with the same placeholders. Write the pair as a pattern entry (spec §6.3), with script scope where the pattern
  has no literal text at its start or end. `--verify` fills both templates with the same sample values and
  compares.
- **Why now.** This page notes that patterns get no real test from this translation. These chains are that
  test, and the English reorders the parts, which is what patterns are for.
- **Digits.** `toString` writes ASCII digits. A number operand read as a string comes out full width
  (`VirtualMachine.ReadStr`). Both occur in these chains.

### R5. The damage is real; the rule is slightly too wide

- **Confirmed in the files.** `patch/SC0000.bin` at dword `0x22024` holds `ABEL` and four NULs where the base
  holds `LABEL`. The strings beside it are each short by their first character too.
- **It is almost always one character.** 198 of the report's 200 samples lost exactly one. The position moves:
  three copies of `週送り。` in SC0010, 253 dwords apart, lost their third, second and first character in turn.
  That points at the translators' tool, not at edits.
- **One sample is a translation.** `SHOWCC@0x15` changes `』の称号を獲得` to `』` because the English moved the
  words in front of the title. Rule 5 files it as damage and keeps the source, which would spoil R4's pattern.
- **Asked of the importer.** Damage is a translated string that is its source with exactly one character
  missing. A string that lost more goes on its own list for review and is kept as a translation.

### Also learned

- Himegari's name editor writes cell `0x279`, and 1,666 `show-text` sites draw that cell. This answers the spec's
  "not verified" item about what the editor names.

### Not checked by the review

- The importer and its tests were not run. The review read the code, the report and the catalog.
- The conflict split in R2 comes from the report's 200-sample lists, not from all 695.
- There is no firm count of deliberate breaks in dialogue (R1).
- What caused the translated install's missing characters.

### Order of work

1. R1: break classification and kept spaces. Re-run, and expect entries with `\n`.
2. R5: the one-character damage rule.
3. R4: pattern entries from `concat` chains, with `--verify`.
4. R2's importer half: site entries for stored literals, and the folded empty lines. The conflict list should
   shrink to stored strings with no storing literal.
5. Update the results table and findings 1 to 3 on this page, then revise the spec: §5.2 and §7 (`\n` in
   `draw-string`), §6.1 and §6.4 (origin and lookup order), §15 (the record), §18.1 (the comparison), §20 (`at`).

### Responses from the importer's author (2026-11-10)

The importer's author agrees with R1 to R5 and with the three decisions. It proposes changes to R2 (record every
store, and a wording fix for the folded lines), R4 (scope and joined paths) and R5 (the damage rule), and an addition
to R1. The numbers below come from the clean install's scripts (its loose updates over the archives) and from one
importer run with every report list complete, not cut to 200 samples.

**R1. Agreed.** This loss is real and the results above missed it: rule 3 joins every break. `SKMES@0x14` reads as
the review describes. One addition: check `W` from both sides instead of trusting the widest line alone.

- 43,716 base messages have no authored break. Assume that their translated breaks are all wraps. Each such break
  then bounds `W` from above: `W` < A + 1 + the next line's first word. Each line bounds it from below: `W` ≥ its
  width.
- Classify with the widest line, as asked, and report every script and layout whose bounds conflict, with the breaks
  that cause the conflict. A conflict means an overlong line, or a deliberate break in a message the base did not
  break. Either way, the widest line cannot be trusted there.

**R2. Agreed, with one change to the record.**

- **(a) Record every store into a global string cell, not only a literal store.** The origin is the storing
  instruction, and the origin's script also scopes patterns. R4's notices need this: they are composed in one
  script and drawn by another.
  - String array `0x2340`, with its count in `G[0x137130]`, is the list of lines that MES presents: notices, and
    choices such as MENU's `はい` and `いいえ`. 17 scripts append to it, with 83 stores in all. `SHOWCC@0x23` stores
    the local string that `0x7`–`0x15` built (`が『`, a title, `』の称号を獲得`).
  - Only MES, SBUNKI and SBUNKIMOVE read the list. All three measure its lines with `halve-strlen` (`MES@0x71`,
    `SBUNKI@0xd2`, `SBUNKIMOVE@0x71`), and `MES@0x40f` and `SBUNKI@0x86c` draw them. `MES@0x641` and `SBUNKI@0xabe`
    empty the list by resetting the count. No script copies a line from one cell to another, so a record made at
    the store is still valid at the measurement and at the draw.
  - A pattern that starts with a placeholder needs script scope, and the drawing script is `MES` (or `SBUNKI`) for
    every line in the list. With (a), the pattern is scoped to `SHOWCC`. Without (a), the importer would have to
    follow the list to find `MES`, and `MES`'s scope would then hold the patterns of 17 scripts, where they could
    match each other's lines.
  - Cost: the same record, written on every store instead of only on literal stores. The rule that the cell must
    still hold the recorded text stays as it is. A copy from one global cell to another takes the copying
    instruction as its origin. Nothing in the conflict lists needs more than that.
- **Off means off: agreed.** It is sound because spec §10 guarantees a full language switch only after a return to
  the title. A return to the title re-runs INIT2: op `0x9` restarts SYSTEM4, which runs LOADCONFIG/INITCONFIG, INIT2
  and INIT (`engine-re.md`). The boot tables therefore get their records back at the title. A switch away from `ja`
  during play leaves boot-table rows on their global and script entries until then. §15 should say so.
- **The complete conflict split** (the review used 200 samples). Of the 695:
  - 636 store a literal into a cell that has one literal writer;
  - 37 store into a cell with several writers: 31 in the stage loader (STINIT and its layers), 1 in CCINIT and 5
    in scene scripts;
  - 22 store through a local string pointer, in scene scripts, STAGEROUND, TITLE and HOSOKU.

  A site entry on the storing instruction carries all 695.
- **Folding the empty lines: agreed, with one wording fix.** "The cell just below it" should read "the cell before
  it (index − 1), drawn one row higher".
  - SELSTAGE draws cells `0x24ab` + i, for i < 4, at y = 15 + 18·i (`SELSTAGE@0x1c58`–`0x1c6d`). It skips a cell
    that holds `""` (`0x1c4a`).
  - `STINIT@0x12b` stores the empty fourth line in `0x24ae`. Its English therefore goes, after `\n`, into the entry
    for `STINIT@0x126`'s literal (cell `0x24ad`).
  - With a 16-pixel font and a 2-pixel gap, that second row lands at y = 69, where the fourth line would have been
    drawn.
  - The row-step attribute is a new entry key, so step 5 of "Order of work" also needs spec §9.3.
- **"An untranslated copy still gets no entry"** is read here as: rule 2's preference stays. A stored source's
  global entry is its most common translation, and run B translates the 635 copies that the install left in
  Japanese, 540 of them the SC scripts' recollection labels. Gate 11 then lists them as expected differences, as
  this page already says. If the review meant something else, this reading is wrong and should be corrected.

**R3. Agreed.** This was finding 1's recommendation.

**R4. Agreed, and the correction is right.** The claim "all in SHOWCC and SC5510" came from the sampled list. Two
changes:

- **Scope.** As R2 (a) shows, the pattern of a line in MES's list needs the composing script's scope, so it needs
  the record of non-literal stores. Without that record, such a pattern can only be global, and only when it has
  literal text at both ends.
- **"No branch target inside" is too strict.** `SHOWCC@0x55`–`0x93` builds `基本出撃コスト－{n}` (`0x71`–`0x76`) or
  `基本出撃コスト＋{n}` (`0x80`–`0x85`). The two paths join at `0x8c`, a branch target, before the store at `0x93`.
  Instead, start from each store of a composed string and walk back along each path inside the function to the
  chain's first write. Each path gives one template.

**R5. Agreed in substance. Proposed rule: one character missing, or a letter left.** The complete list confirms
the review's sample: 6,101 of the 6,132 lost exactly one character. Of the 31 that lost more:

- **25 still hold a letter** (kana, kanji, Latin or a digit): 17 comments, 1 coroutine label, 1 message and 6
  stored literals. The stored literals are
  `SC1490`'s `シーン回想終了` → `シン想終了`, `SC2000`'s `ーン回終了` and `ャンル`, `$1$EBINIT`'s `精霊／ヘタレ種` → `精`,
  `$3$EBINIT`'s `一般属／飛竜種` → `一`, and `$3$ITINIT`'s `『庭』配置家具` → `『庭』配`. Under the rule as asked, these
  6 count as translations. Once R2's importer half is in, each would get a site entry that reproduces the damaged
  Japanese.
- **6 hold only punctuation.** Three are translations whose words the English moved into another run:
  `SHOWCC@0x15` (`』の称号を獲得` → `』`), `ENHANCE@0x1093` (`スキル『` → `『`) and `SC6350@0x3e7f` (`パシッ――` →
  `――`). Three are developer notes such as `施設訪問。`, cut down to `。` (`SC1020@0xac7a`, `SC3510@0xa3d`,
  `SC4510@0x1ad2`); either class does for them.

Damage is then a translated string that is its source with characters missing, where either one character is
missing or a letter is left. The review list holds all 31, each with its class. `SHOWCC@0x15` stays a translation,
so R4's pattern is safe.

**Order of work: agreed.** R2's importer half needs no change to the catalog syntax. Additions to step 5: §9.3 (the
row-step attribute), and in §15 the record of every store (R2 (a)) and the switch rule.

### Settled (2026-11-10)

The reviewer accepted every proposal above and added the amendments marked *reviewer*. The user closed the review
there. These are the forms the importer work and the spec revision follow:

- **R1.** Keep each line as written, and classify each break by the widest translated line of its script and layout.
  Check that width from both sides as proposed above, report each script and layout whose bounds conflict, and count
  the breaks whose class depends on the bound used (*reviewer*).
- **R2, engine.** Every instruction that writes a global string cell records itself and the text it wrote
  (*reviewer*: the record is made at the string write, not at `set-string`). A copy from a global string cell whose
  record is still valid inherits that record instead (*reviewer*). Records are kept whenever the mounted stack
  defines a language, not only while one is selected (*reviewer*), so switching language during play is exact for
  boot tables. A root reload clears them, so a reload that mounts the first language during play starts with no
  records until the title. Use, lookup order and catalog syntax are as in R2.
- **R2, importer.** As asked, with the folded lines going into the entry of the cell before (index − 1), and §9.3
  in the spec revision.
- **R4.** Start from each store of a composed string and walk back along each path inside the function. Cap the number
  of paths, stop at a loop, and report the chains this skips (*reviewer*). A pattern takes the composing script's
  scope.
- **R5.** Damage is a translated string that is its source with characters missing, where one character is missing or
  a letter is left. All strings that lost more than one go on the review list with their class.

### Implemented (2026-11-10)

The importer changes, in the order of work, and what each found. The rules section above states each rule in full.

**R1: the settled edge did not survive the data, so the importer uses another.** The widest line, checked from
both sides, failed in two ways:

- **Overlong lines set the widest line.** Scene scripts hold lines of 71 and 103 half cells in a box 64 wide; the
  engine wraps them at the box's edge.
- **The upper bound assumed that a message the base does not break holds only wraps.** Translators add
  deliberate breaks in dialogue: 3,692 messages gain one. Wherever they did, the two bounds conflicted, and the
  conflicts said nothing about the edge.

The edge is now the width that 99% of a script's lines fit, with the install's edge where a script has fewer than
20 lines, and the added-break count caps the wraps (rule 3). Box widths differ by script, and the per-script edge
follows them: 64 half cells in scene scripts, 42 to 44 in ITMES and SKMES, 51 in VIMES, FAMES and CIMES. The
reviewer's count of breaks whose class depends on the bound becomes the uncertain count: 998 wraps no wider than
their script's widest line, which a widest-line rule would call deliberate.

- 14,767 wraps and 6,239 deliberate breaks, in 5,892 messages. 334 script layouts are measured, 46 of them on the
  install's edge, and 50 lines are wider than their edge.
- The count keeps 428 breaks deliberate. An example is an ITMES line that fills the box before a tag line such as
  `[ Accessory ]`, where the base breaks too and the translation added nothing.
- 25 messages lose an authored break, and 3,692 gain a deliberate one; both lists are in the report.
- **Trade-off: reflowed prose.** 281 messages in VIMES, CIMES and FAMES are help prose that the translators
  reflowed through the base's breaks, and the count keeps `\n` at some of their wrap points. They draw as in the
  translated install, but a presenter that wraps again at another width breaks those lines early.
- **Trade-off: overlong lines** stay as written and rely on the engine's wrap at the box's edge. Some translators
  padded with spaces to push a word onto the next row (`at the  bar.`), or ran words together (`tellyou`).

**R5.** As settled. 6,101 strings lost one character, and 45 lost more and still hold a letter; all of these keep
the source. The 6 that keep only punctuation are translations. A message is now checked as a whole, which finds 20
damaged `$3$ITMES` messages whose runs had each passed one at a time.

**R4.** As settled, with the walk of rule 7. 30 stores build 34 templates; 27 pattern entries; 0 walks gave up.

- `SHOWCC@0x93` stores either of two paths: `基本出撃コスト－{v1:num}` → `Command Cost -{v1}`, and the same with `＋`.
- FIELD's notices `{v1}を占拠した！` and `{v1}を占拠された！` (captured, and was captured) both read `{v1} captured!`
  in the translation. That is the translation's choice, carried as it is.
- `--verify` checks each composed string by sample: 0 mismatches.
- The first version of the walk stopped at every call. It took the script's T2 and T3 tables for function entries,
  but they list call sites. Since that was fixed, the walk steps over calls. A callee that writes the variable is
  then not seen; that costs at most a pattern that never matches.

**R2, importer half.** As settled.

- The 695 conflicts become 126 script entries and 553 site entries. No literal stored into a frame local conflicts.
- All 33 filled empty lines fold into the line before: 32 stage description lines in STINIT and its layer, and one
  objective in SC5560. None is left uncarried.
- 89 catalog files now carry a fingerprint (64 before).
- `--verify` checks each stored literal at its storing site: 0 mismatches.

The engine half of R2 (the record of each store, and `\n` as a new row in `draw-string`) is in spec §15 and §7.1;
it belongs to milestone 2.
