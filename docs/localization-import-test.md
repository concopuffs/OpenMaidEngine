# Localization import test

Results of the import test in the localization design (`superpowers/specs/2026-11-10-localization-design.md`
§18.1): converting an install whose scripts were translated in place into a text catalog, and checking what the
catalog carries. The design stays in the spec; this page holds the test's results and the importer's rules, so the
spec can be revised against them. Gate 10 (half 1) ran on 2026-11-10 for milestone 0. Gate 11 (half 2) ran on
2026-11-11 for milestone 6.

**Status: reviewed, and the review's changes are in (2026-11-10).** Milestone 0's numbers were reviewed before
milestone 1, as spec §19 asks. The review found one loss (line structure), and the user accepted three design
decisions. The importer carries out every change the review asked for, and the spec has the design changes. The
results below are from the importer after the review; "What the first run did not carry" keeps the findings the
review answered, and "The review" keeps what each of its changes found.

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
  `TextImportTests`, `TextCatalogFileTests`, `LocalizationTextModelTests` (synthetic scripts), `ImportPackageTests`.
- **`--package` (2026-11-11)** makes the output one package that loads as it is.
  - It writes the 232 replaced images to `assets/en/` as PNG: 207 from `patch/` and the 25 `$n$` append images in
    the translated root, all BMPs under AGF names.
  - It writes a manifest whose `glyph_delay_scale` comes out at 0.48. The hand-made package of gate 11 used 0.5.
  - `omp check` links it: 48,588 entries from 217 files, no errors.
  - The port runs the clean install with this one package and `--language en`. The opening credits, the title, the
    new-game screen and SC0000's first page are in English, and six of its images (SO001, SO004–6, SO023,
    EV999YB) were served by then.
  - The 25 append images first failed the link: the base-asset check read only `SYS4INI.BIN`'s records. It now
    takes the install's append catalogs too, by their `$n$` names, as AGE and the port's store open them.

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
| Replaced images | 232 | The list for `assets/<language>/` (§12), which `--package` writes (below). Spec §2's 243 counted image files: 4 names appear twice, 6 are identical to the official loose updates, 1 is the stray file |

## What the first run did not carry

The first run's three findings, with the first run's numbers. The line after each says what carries it now.

### 1. Site scope (`at`), spec §20's open question

**Now:** `at` ships in version 1 (R3; spec §20). 137 messages take a site entry.

133 messages, in 64 scripts, needed a site entry: one script translates the same source two ways. There are 93
distinct sources. Without `at`, each of these shows another translation its script uses. Most are short replies
(`「……」`, `「はい」`), where the other translation would read fine. Others carry content the Japanese line does
not have. `『新しくクエストが登録されました』` ("a new quest was registered") is 18 of the 114 cases of a source translated
two ways in one script. The translators wrote the quest's name into each one, so without `at` a script announces
the wrong quest.

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
or label. In QUINIT and STINIT the lines before a lost line end mid-sentence. The tables show two shapes:
multi-line text built from cells (QUINIT, STINIT), where the English is one paragraph per row wrapped by hand into
the cells, and shared labels made row-specific (EBINIT, CDINIT2 and the rest). The record of each store (R2)
carries both.

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

One translated string also holds a control character: `SC2310@0x1232`'s `show-text` begins with a tab. Native reads
every byte before the NUL and lays the tab out as a single-byte glyph. Since 2026-11-11 `Sys4StringCodec` decodes such
a string too (`engine-re.md`, "Strings with control characters"). The catalog keeps the tab, after the line break
before it (`\n\t ―The cliff…`), and the report's `undecodable` count is 0. Presented text draws no glyph for a control
character, so run B leaves out the small glyph that run A draws for the tab.

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
- `SC2310@0x1232`'s leading tab, which run A draws as a small glyph and run B leaves out. The text is the same
  once whitespace is collapsed.

Pages that draw them fail the required check and are reviewed against this list; the opening draws none. Table rows
are not among them, since the engine records each store (R2).

## The review (2026-11-10)

A review read the first run's page, the importer and the generated catalog. It found one loss the first run did
not report (R1), answered findings 1 and 2 (R2, R3), and asked for two smaller importer changes (R4, R5). The
review, the importer author's responses and the forms they settled are in the history (`2482813`); this section
keeps what came of them. The user accepted three design decisions, now in the spec:

1. A site entry also applies to a stored literal, through a record of the instruction that wrote each global
   string cell (spec §5.2, §15).
2. `\n` in the presented text of a `draw-string` starts a new row (spec §7.1, with `row_gap` in §9.3).
3. `at` ships in version 1 (spec §20).

The rules section above states each importer rule in full.

### R1. Line structure

The first run joined every line break with a space, so no entry held `\n`, and `--verify`, which compared with
whitespace collapsed, could not see the loss. 2,252 base messages hold an authored break: item, vocabulary, skill,
objective and profile texts (ITMES, VIMES, SKMES, `$3$ITMES`, FAMES, CIMES). In `SKMES@0x14` a header line and a
description ran together, and the column the translators padded with spaces was gone.

The review settled on the widest translated line of a script and layout as the edge, checked from both sides. That
failed on the data in two ways:

- **Overlong lines set the widest line.** Scene scripts hold lines of 71 and 103 half cells in a box 64 wide; the
  engine wraps them at the box's edge.
- **The upper bound assumed that a message the base does not break holds only wraps.** Translators add
  deliberate breaks in dialogue: 3,692 messages gain one. Wherever they did, the two bounds conflicted, and the
  conflicts said nothing about the edge.

The edge is now the width that 99% of a script's lines fit, with the install's edge where a script has fewer than
20 lines, and the added-break count caps the wraps (rule 3). Box widths differ by script, and the per-script edge
follows them: 64 half cells in scene scripts, 42 to 44 in ITMES and SKMES, 51 in VIMES, FAMES and CIMES. The
uncertain count is 998 wraps no wider than their script's widest line, which a widest-line rule would call
deliberate.

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

### R2. Stored text

- **The 695 conflicts.** 636 store a literal into a cell that has one literal writer; 37 store into a cell with
  several writers (31 in the stage loader, STINIT and its layers, 1 in CCINIT and 5 in scene scripts); 22 store
  through a local string pointer, in scene scripts, STAGEROUND, TITLE and HOSOKU. A site entry on the storing
  instruction carries all of them: they become 126 script entries and 553 site entries. No literal stored into a
  frame local conflicts.
- **The 33 filled empty lines** fold into the entry of the line stored to the cell before (index − 1), drawn one
  row higher: 32 stage description lines in STINIT and its layer, and one objective in SC5560. None is left
  uncarried. SELSTAGE draws cells `0x24ab` + i at y = 15 + 18·i and skips a cell that holds `""` (`0x1c4a`), so
  `STINIT@0x12b`'s English goes into the entry of `STINIT@0x126`'s literal, and with a 16-pixel font and the
  default 2-pixel gap its second row lands at y = 69, where the fourth line would have been drawn.
- 89 catalog files now carry a fingerprint (64 before). `--verify` checks each stored literal at its storing site:
  0 mismatches.
- The engine records every write of a global string cell, not only literal stores: R4's notices are composed in one
  script (`SHOWCC@0x23` stores into MES's line list) and drawn by another, so a pattern takes the composing
  script's scope. The engine half came in milestone 2.

### R3. Site scope

Of the 114 sources translated two ways in one script, 102 differ in wording, 11 only in punctuation or case, and
one is an untranslated copy. The wording differences are real context: `「分かりました」` is "Understood." at one
site and "Yes, sir." at another. Site scope is a normal need of a translation, not an artifact of this one.

### R4. Composed strings

The first run carried none of the 42 changed `concat` literals, which build gameplay notices: a title gained, a
skill learned, a weapon level, an item found. By script: FIELD 14, SHOWCC 6, ALCHEMY 6, ADDSTOCK 3, SC5510 2,
ADDEXP 2, CHECKIMPROVE 2, FELLOW 2, SALE 2, INFO2 2, ENHANCE 1. The walk of rule 7 now carries them: 30 stores
build 34 templates, giving 27 pattern entries, and 0 walks gave up.

- `SHOWCC@0x93` stores either of two paths, which join at a branch target before the store:
  `基本出撃コスト－{v1:num}` → `Command Cost -{v1}`, and the same with `＋`.
- FIELD's notices `{v1}を占拠した！` and `{v1}を占拠された！` (captured, and was captured) both read `{v1} captured!`
  in the translation. That is the translation's choice, carried as it is.
- `--verify` checks each composed string by sample: 0 mismatches.
- The first version of the walk stopped at every call. It took the script's T2 and T3 tables for function entries,
  but they list call sites. The walk now steps over calls, so a callee that writes the variable is not seen; that
  costs at most a pattern that never matches.

### R5. Damage

Damage is a translated string that is its source with characters missing, where one character is missing or a
letter is left. 6,101 strings lost one character and 45 lost more but still hold a letter; all of these keep the
source. The 6 that keep only punctuation are translations: three whose words the English moved into another run
(`SHOWCC@0x15`'s `』の称号を獲得` → `』`, `ENHANCE@0x1093`, `SC6350@0x3e7f`), and three developer notes such as
`施設訪問。` cut down to `。`. A message is checked as a whole, which finds 20 damaged `$3$ITMES` messages whose runs
had each passed one at a time.
