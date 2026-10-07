#!/usr/bin/env python3
"""Census of the original AGE compiler's output: the structural facts a faithful compiler must reproduce.

Read-only over the selected profile's script corpus (`paths.scripts()`). Every number quoted in
docs/sys4-format-notes.md "Compiler-emitted structure and layout regularities" comes from this report.

Sections:
  tables     T1/T2/T3 equal the 0x71/0x03/0x8f instructions in code order
  data       string sharing, duplicated text, data order versus first reference, unreferenced data
  control    jcc lowering shapes (if, if-else, for, while, conditional goto, ...)
  blocks     statement blocks (0x1f4/0x1f5): balance, nesting, branch edges crossing a boundary
  code_refs  how call / callback / coroutine / switch targets terminate
  locals     header local-bank counts versus the highest index used; temporary-pool position
  constants  negative immediates and the `sub tmp 0 n` idiom
  helpers    local subroutine bodies shared verbatim across scripts (statically linked library)

Usage:
  py -3.11 -X utf8 tools/compiler_census.py [--profile ID] [--output PATH]
Output: build/games/<profile>/compiler-census.json (+ a printed summary)
"""
from __future__ import annotations

import argparse
import collections
import hashlib
import json
from pathlib import Path

import paths
import sys4load
from age_opcodes import CODE_TARGET_ARGS

NONE = 0xFFFFFFFF
OP_EXIT, OP_RET, OP_CALL_SCRIPT, OP_JMP, OP_CALL = 0x2, 0x5, 0x3, 0x8C, 0x8F
OP_JCC, OP_SWITCH_CASE, OP_SWITCH_JUMP, OP_RESUME = 0xA0, 0xA2, 0xA3, 0x7C
OP_T1, OP_BLOCK_BEGIN, OP_BLOCK_END, OP_SUB, OP_ARRAY = 0x71, 0x1F4, 0x1F5, 0x51, 0x64
LOCAL_BANK_TYPES = {0: 0x9, 1: 0xA, 2: 0xB, 3: 0xC, 4: 0xD, 5: 0xE}   # header field -> operand type
CALLBACK_SITES = {0x90: (5, 6, 7), 0xCC: (2,), 0xFB: (2,), 0xD4: (3, 4)}
TRANSFER_OPS = {OP_JMP, OP_JCC, OP_SWITCH_CASE, OP_SWITCH_JUMP}     # control transfers
REFERENCE_OPS = {0x7B, 0x90, 0xCC, 0xD4, 0xD5, 0xFB}                 # code references (registrations)


def code_targets(ins) -> list[int]:
    """Code offsets referenced by one instruction (sentinel -1 excluded)."""
    return [v for a, (t, v) in enumerate(ins.args)
            if a + 1 in CODE_TARGET_ARGS.get(ins.opcode, ()) and v != NONE]


# --------------------------------------------------------------------------- control-flow shapes
def jcc_shape(instructions: list, k: int, index: dict[int, int]) -> str:
    """Classify the jcc at position k by the source construct its layout implies."""
    i = instructions[k]
    on_true, on_false = i.args[1][1], i.args[2][1]
    if on_true == NONE and on_false != NONE:
        if on_false <= i.offset:
            return "branch-false-backward"
        end = index.get(on_false)
        if end is None:
            return "invalid-target"
        last = instructions[end - 1]
        if last.opcode == OP_JMP:
            back = last.args[0][1]
            if back > on_false:
                return "if-else"
            if back <= i.offset:
                head = index.get(back)
                if head is not None and head > 0 and instructions[head - 1].opcode == OP_JMP \
                        and instructions[head - 1].args[0][1] > back:
                    return "for"
                return "while"
            return "if"      # then-block ends with a nested loop's back edge
        return "if"
    if on_true != NONE and on_false == NONE:
        return "conditional-goto" if on_true > i.offset else "conditional-goto-backward"
    if on_true != NONE and on_false != NONE:
        return "conditional-goto-else"
    return "no-target"


def if_goto_bodies(instructions: list, index: dict[int, int]) -> int:
    """`if (c) { goto L }` spelled as a false-skip around one jmp (the alternative lowering)."""
    count = 0
    for k, i in enumerate(instructions):
        if i.opcode == OP_JCC and i.args[1][1] == NONE and i.args[2][1] != NONE:
            end = index.get(i.args[2][1])
            if end == k + 2 and instructions[k + 1].opcode == OP_JMP:
                count += 1
    return count


# --------------------------------------------------------------------------- statement blocks
def block_structure(instructions: list) -> dict:
    """Balance, nesting and boundary-crossing branch edges of 0x1f4/0x1f5 blocks."""
    stack: list[int] = []
    owner: dict[int, tuple] = {}
    next_id, max_depth, unbalanced = 0, 0, 0
    for i in instructions:
        if i.opcode == OP_BLOCK_BEGIN:
            owner[i.offset] = tuple(stack)
            next_id += 1
            stack.append(next_id)
            max_depth = max(max_depth, len(stack))
        elif i.opcode == OP_BLOCK_END:
            owner[i.offset] = tuple(stack)
            if stack:
                stack.pop()
            else:
                unbalanced += 1
        else:
            owner[i.offset] = tuple(stack)
    edges = crossing = references = crossing_references = 0
    for i in instructions:
        if i.opcode not in TRANSFER_OPS and i.opcode not in REFERENCE_OPS:
            continue
        for target in code_targets(i):
            if target not in owner:
                continue
            crosses = owner[target] != owner[i.offset]
            if i.opcode in TRANSFER_OPS:
                edges += 1
                crossing += crosses
            else:
                references += 1
                crossing_references += crosses
    return {"blocks": next_id, "max_depth": max_depth, "unbalanced": unbalanced + len(stack),
            "transfer_edges": edges, "crossing_transfer_edges": crossing,
            "reference_edges": references, "crossing_reference_edges": crossing_references}


# --------------------------------------------------------------------------- code-reference endings
def terminators(instructions: list, index: dict[int, int], start: int) -> frozenset[str]:
    """Terminal opcodes reachable from start without leaving through ret/exit/resume."""
    names = {OP_RET: "ret", OP_EXIT: "exit", OP_RESUME: "coroutine-resume", 0x1: "throw-exit"}
    seen, work, found = set(), [start], set()
    while work and len(seen) < 50000:
        off = work.pop()
        if off in seen or off not in index:
            continue
        seen.add(off)
        i = instructions[index[off]]
        if i.opcode in names:
            found.add(names[i.opcode])
            continue
        after = off + 1 + 2 * len(i.args)
        if i.opcode == OP_JMP:
            work.append(i.args[0][1])
        elif i.opcode == OP_JCC:
            work.extend(after if v == NONE else v for v in (i.args[1][1], i.args[2][1]))
        elif i.opcode == OP_SWITCH_JUMP:
            work.append(i.args[1][1])
        else:
            work.append(after)
    return frozenset(found)


# --------------------------------------------------------------------------- helpers (library)
def subroutine_key(script, instructions: list, index: dict[int, int], entry: int) -> tuple[str, int]:
    """Position-independent fingerprint of the body reachable from a call target."""
    body = []
    seen, work = set(), [entry]
    while work:
        off = work.pop()
        if off in seen or off not in index:
            continue
        seen.add(off)
        i = instructions[index[off]]
        after = off + 1 + 2 * len(i.args)
        if i.opcode in (OP_RET, OP_EXIT, OP_RESUME):
            continue
        if i.opcode == OP_JMP:
            work.append(i.args[0][1]); continue
        if i.opcode == OP_JCC:
            work.extend(after if v == NONE else v for v in (i.args[1][1], i.args[2][1]))
            continue
        work.append(after)
    for off in sorted(seen):
        i = instructions[index[off]]
        norm = [i.opcode]
        for a, (t, v) in enumerate(i.args):
            if a + 1 in CODE_TARGET_ARGS.get(i.opcode, ()) and v != NONE:
                norm.append(("rel", v - entry))
            elif t == 2:
                norm.append(("s", script.strings.get(v, ("?",))[0]))
            else:
                norm.append((t, v))
        body.append((off - entry, tuple(norm)))
    return hashlib.sha1(repr(body).encode("utf-8")).hexdigest()[:16], len(body)


# --------------------------------------------------------------------------- per-script census
def census(scripts: dict[str, object]) -> dict:
    out = {
        "scripts": 0,
        "tables": collections.Counter(),
        "data": collections.Counter(),
        "control": collections.Counter(),
        "if_goto_bodies": 0,
        "blocks": collections.Counter(),
        "code_refs": collections.defaultdict(collections.Counter),
        "locals_slack": {f"F{k}": collections.Counter() for k in LOCAL_BANK_TYPES},
        "unused_top_local_int": collections.Counter(),
        "condition_temp_depth": collections.Counter(),
        "constants": collections.Counter(),
        "negative_constant_magnitudes": collections.Counter(),
    }
    helper_scripts: dict[str, set] = collections.defaultdict(set)
    helper_size: dict[str, int] = {}
    helper_sample: dict[str, tuple] = {}
    for name, script in sorted(scripts.items()):
        out["scripts"] += 1
        ins = script.instructions
        index = {i.offset: k for k, i in enumerate(ins)}
        # tables
        for table, op in (("t1", OP_T1), ("t2", OP_CALL_SCRIPT), ("t3", OP_CALL)):
            derived = [i.offset for i in ins if i.opcode == op]
            out["tables"][f"{table}_derivable"] += list(script.table_entries(table)) == derived
        # data layout
        blobs, first_refs = {}, []
        for i in ins:
            for a, (t, v) in enumerate(i.args):
                if t == 2 and v in script.strings:
                    blobs[v] = script.strings[v][1]
                elif i.opcode == OP_ARRAY and a == 1:
                    blobs[v] = script.dwords[v] + 1 if v < len(script.dwords) else 1
                else:
                    continue
                if v not in first_refs:
                    first_refs.append(v)
        refs = collections.Counter(script.string_refs.values())
        texts = collections.Counter(text for text, _ in script.strings.values())
        out["data"]["shared_string_scripts"] += any(c > 1 for c in refs.values())
        out["data"]["duplicate_text_scripts"] += any(c > 1 for c in texts.values())
        out["data"]["data_not_in_first_reference_order"] += first_refs != sorted(first_refs)
        cursor, gap = script.code_end, 0
        for off in sorted(blobs):
            gap += max(0, off - cursor)
            cursor = max(cursor, off + blobs[off])
        gap += max(0, script.code_len - cursor)
        out["data"]["unreferenced_data_scripts"] += gap > 0
        # control flow
        for k, i in enumerate(ins):
            if i.opcode == OP_JCC:
                out["control"][jcc_shape(ins, k, index)] += 1
        out["if_goto_bodies"] += if_goto_bodies(ins, index)
        # statement blocks
        b = block_structure(ins)
        out["blocks"]["scripts_with_blocks"] += b["blocks"] > 0
        out["blocks"]["blocks"] += b["blocks"]
        out["blocks"]["unbalanced"] += b["unbalanced"]
        for key in ("transfer_edges", "crossing_transfer_edges", "reference_edges", "crossing_reference_edges"):
            out["blocks"][key] += b[key]
        out["blocks"][f"max_depth_{b['max_depth']}"] += 1
        # code-reference endings
        for i in ins:
            kinds = []
            if i.opcode == OP_CALL:
                kinds.append(("call", i.args[0]))
            elif i.opcode == 0x7B:
                kinds += [("coroutine-handler", i.args[0]), ("coroutine-handler", i.args[1])]
            elif i.opcode == OP_SWITCH_CASE:
                kinds.append(("switch-case", i.args[1]))
            for a in CALLBACK_SITES.get(i.opcode, ()):
                kinds.append(("callback", i.args[a - 1]))
            for kind, (t, v) in kinds:
                if t == 0 and v != NONE:
                    ends = terminators(ins, index, v)
                    out["code_refs"][kind]["+".join(sorted(ends)) or "none"] += 1
        # locals
        for field, ty in LOCAL_BANK_TYPES.items():
            used = [v for i in ins for t, v in i.args if t == ty]
            out["locals_slack"][f"F{field}"][script.fields[field] - (max(used) + 1 if used else 0)] += 1
        ints = {v for i in ins for t, v in i.args if t == 0x9}
        if ints:
            out["unused_top_local_int"][(script.fields[0] - 1) not in ints] += 1
            conds = [i.args[0][1] for i in ins if i.opcode == OP_JCC and i.args[0][0] == 0x9]
            if conds:
                out["condition_temp_depth"][script.fields[0] - min(conds)] += 1
        # constants
        for i in ins:
            for a, (t, v) in enumerate(i.args):
                if t == 0 and v >= 0x80000000 and a + 1 not in CODE_TARGET_ARGS.get(i.opcode, ()):
                    out["constants"]["negative_immediates"] += 1
            if i.opcode == OP_SUB and i.args[1] == (0, 0) and i.args[2][0] == 0:
                out["constants"]["sub_zero_idiom"] += 1
                out["negative_constant_magnitudes"][i.args[2][1]] += 1
        # helpers
        for target in sorted({i.args[0][1] for i in ins if i.opcode == OP_CALL and i.args[0][0] == 0}):
            if target in index:
                key, size = subroutine_key(script, ins, index, target)
                helper_scripts[key].add(name)
                helper_size[key] = size
                helper_sample.setdefault(key, (name, target))
    shared = {k: v for k, v in helper_scripts.items() if len(v) > 1}
    out["helpers"] = {
        "distinct_bodies": len(helper_scripts),
        "shared_bodies": len(shared),
        "shared_in_50_or_more_scripts": sum(len(v) >= 50 for v in shared.values()),
        "largest_shared_instructions": max((helper_size[k] for k in shared), default=0),
        "scripts_with_shared_helper": len(set().union(*shared.values())) if shared else 0,
        "top": [
            {"key": k, "scripts": len(v), "instructions": helper_size[k],
             "sample": f"{helper_sample[k][0]}@0x{helper_sample[k][1]:x}"}
            for k, v in sorted(shared.items(), key=lambda kv: (-len(kv[1]), -helper_size[kv[0]]))[:25]
        ],
    }
    return out


def load_corpus() -> dict[str, object]:
    scripts = {}
    for name, path in sorted(paths.scripts().items()):
        try:
            scripts[name] = sys4load.load(path)
        except sys4load.Sys4Error:
            continue
    return scripts


def jsonable(value):
    if isinstance(value, dict):
        return {str(k): jsonable(v) for k, v in value.items()}
    if isinstance(value, (list, tuple)):
        return [jsonable(v) for v in value]
    return value


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--output", type=Path, default=None)
    args = parser.parse_args(argv)
    report = census(load_corpus())
    report["profile_id"] = paths.CONTEXT.profile_id
    report["generated_by"] = "tools/compiler_census.py"
    report["negative_constant_magnitudes"] = dict(report["negative_constant_magnitudes"].most_common(10))
    output = args.output or paths.CONTEXT.game_build / "compiler-census.json"
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(jsonable(report), indent=1, ensure_ascii=False) + "\n",
                      encoding="utf-8", newline="\n")
    c = report["control"]
    total = sum(c.values())
    canonical = sum(c[k] for k in ("if", "if-else", "for", "while"))
    gotos = sum(c[k] for k in ("conditional-goto", "conditional-goto-else", "conditional-goto-backward"))
    print(f"compiler-census: profile={report['profile_id']} scripts={report['scripts']} -> {output}")
    print(f"  jcc={total}: structured {canonical} ({100 * canonical / max(total, 1):.1f}%), "
          f"conditional goto {gotos}, other {total - canonical - gotos}; if{{goto}} bodies {report['if_goto_bodies']}")
    print(f"  blocks: {dict(report['blocks'])}")
    print(f"  helpers: {({k: v for k, v in report['helpers'].items() if k != 'top'})}")
    print(f"  locals: unused top int slot {dict(report['unused_top_local_int'])}; "
          f"F0 slack {dict(report['locals_slack']['F0'].most_common(3))}")
    print(f"  constants: {dict(report['constants'])}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
