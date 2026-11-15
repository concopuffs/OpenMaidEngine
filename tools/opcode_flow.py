#!/usr/bin/env python3
"""Derive every implemented opcode's control facts from the port's handlers (Lua scripting spec §7.1).

The two facts are whether an instruction waits and how it leaves:

- ``waits``: ``never``, ``may`` or ``always``. An instruction waits when its handler can park the engine thread in the
  wait native (``InputAndWaits.Blocking.cs``, the one place the engine blocks) before it returns: a text or input
  wait, a sleep, a transition, a modal dialog or movie. A present and the front end's modal hold block and are not
  waits (spec §10): the members ``ModCallWaits.cs`` lists under ``NotWaits``, the list a script call refuses by, do not
  count. ``always`` is only given to the handlers listed in ``ALWAYS_WAITS``, each read and found to block on every
  path; the derivation proves ``may``.
- ``leaves``: what the handler can do with control besides blocking, any of
  ``falls`` (it returns to the next instruction), ``jumps`` (it continues at another instruction of its frame),
  ``calls`` (it runs another frame or a callback before it returns: a script, a wait's callback, a menu's script),
  ``ends`` (it ends its frame, the scene or the run, itself or because a frame it ran did),
  ``saves`` (it writes a numbered save of the game state), ``loads`` (it replaces the game state from a save) and
  ``restores`` (it continues a load's restore of the saved frames, op 0xae).

The derivation reads the handler table (``VirtualMachine.Handlers.cs``: each opcode's canonical name to its ``Op``
method), every method of the interpreter's partial classes, and the wait native's methods. It follows a handler
through the interpreter's own methods (a call to a method of the same name; overloads are one), and stops at
``RunFrame`` and ``RunInstructions``, which run other code (``calls``). Step results count in methods that return a
``StepResult``. A call through a field to another native is not followed, except ``_waits.X``, which waits when the
wait native's ``X`` reaches its blocking helpers. It over-approximates: a fact a handler can reach on some path is
given, so ``never`` and a missing ``leaves`` entry are proven absent as far as the source shows. The engine checks the
facts against what each instruction does in the tests' runs (``OpcodeFlowCheck``).

Usage:
  py -3.11 -X utf8 tools/opcode_flow.py            # print the derived facts and what differs from the registry
  py -3.11 -X utf8 tools/opcode_flow.py --apply    # write them into vm-map/opcodes.toml
  py -3.11 -X utf8 tools/opcode_flow.py --check    # exit non-zero when the registry differs from the derivation

Output: build/opcode-flow.json. See docs/tools-reference.md ("Opcode flow facts").
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

import opcodes_model as M
import paths

ENGINE = paths.REPO / "engine"
VM_DIR = ENGINE / "Ome.Engine.Vm"
HANDLERS = VM_DIR / "VirtualMachine.Handlers.cs"
WAITS_DIR = ENGINE / "Ome.Engine" / "Natives"
WAITS_GLOB = "InputAndWaits*.cs"
OUTPUT = paths.SHARED_BUILD / "opcode-flow.json"

WAITS = M.WAITS
LEAVES = M.LEAVES

# The wait native's helpers that block the engine thread (InputAndWaits.Blocking.cs).
BLOCKING_HELPERS = {"WaitAndTick", "WaitAnyAndTick", "WaitModal", "Block"}
# The guard a script call runs the waits through: its NotWaits list names the members that are not waits.
MOD_CALL_WAITS = VM_DIR / "ModCallWaits.cs"
NOT_WAITS_LIST = re.compile(r"\bNotWaits\s*=\s*\[(?P<body>[^\]]*)\]")
NAMEOF = re.compile(r"nameof\(\s*(?:\w+\.)?(?P<name>\w+)\s*\)")
# Interpreter methods that run other code: a frame, or a wait's callback inside the waiting frame.
RUNS_CODE = {"RunFrame", "RunInstructions"}
# Tokens in a body, and the fact each gives.
STEP_TOKENS = {
    "StepResult.Next": "falls",
    "StepResult.Jump(": "jumps",
    "StepResult.FrameReturn": "ends",
    "StepResult.RootReload": "ends",
    "StepResult.HotspotReturn": "ends",
}
THROW_TOKENS = {
    "new RootReloadRequestedException(": "ends",
    "new ProcessExitRequestedException(": "ends",
}
# The save native's entry points (VirtualMachine.Persistence.cs), each read: the numbered save's capture, a load's
# state, and the restore of the saved frames that op 0xae continues.
PERSISTENCE_TOKENS = {
    "_saves.SaveNumbered(": "saves",
    "TryLoadNumberedState(": "loads",
    "_saves.RestoringFrames": "restores",
}
# Handlers read and found to block on every path; the derivation alone proves only `may`. Each opens a modal box
# and waits until it closes (InputAndWaits.Modal.cs), unless the run is stopping. A text or input wait, a sleep, a
# transition and a modal movie are `may`: Skip, Auto and fast-forward pass them without blocking.
ALWAYS_WAITS = {"show-and-clear-diagnostic", "edit-fullwidth-string-dialog"}

DECLARATION = re.compile(
    r"^[ \t]*(?:\[[^\]]*\][ \t]*)*"
    r"(?:(?:public|private|internal|protected|static|override|virtual|sealed|async|unsafe|new|extern|readonly)[ \t]+)*"
    r"(?P<type>[\w.<>\[\],?() ]+?)[ \t]+(?:(?P<iface>I\w+)\.)?(?P<name>[A-Z]\w*)[ \t]*(?:<[^>()]*>)?[ \t]*\(",
    re.M)
BARE_CALL = re.compile(r"(?<![\w.])(?P<name>[A-Z]\w*)\s*(?:<[^>()]*>)?\s*\(")
METHOD_GROUP = re.compile(r"(?<=[(,])\s*(?P<name>[A-Z]\w*)\s*(?=[,)])")
WAITS_CALL = re.compile(r"\b_waits\.(?P<name>\w+)\s*\(")
TABLE_ENTRY = re.compile(r'\["(?P<label>[^"]+)"\]\s*=\s*static\s*\(vm,\s*ins,\s*pc\)\s*=>\s*vm\.(?P<method>\w+)\(')
NOT_METHODS = {"if", "for", "foreach", "while", "switch", "using", "lock", "catch", "return", "nameof", "typeof",
               "sizeof", "default", "new", "throw", "when", "await"}


def strip_code(text: str) -> str:
    """The source with comments removed and every string and character literal emptied (its quotes kept), so braces
    and names inside them do not count. Interpolation holes go with their string."""
    out: list[str] = []
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        if text.startswith("//", i):
            j = text.find("\n", i)
            i = n if j < 0 else j
            continue
        if text.startswith("/*", i):
            j = text.find("*/", i + 2)
            i = n if j < 0 else j + 2
            continue
        if c == "'":
            j = i + 1
            while j < n and text[j] != "'":
                j += 2 if text[j] == "\\" else 1
            out.append("''")
            i = j + 1
            continue
        if c in "$@\"":
            k = i
            verbatim = interpolated = False
            while k < n and text[k] in "$@":
                verbatim |= text[k] == "@"
                interpolated |= text[k] == "$"
                k += 1
            if k >= n or text[k] != '"':
                out.append(c)
                i += 1
                continue
            if text.startswith('"""', k):
                end = text.find('"""', k + 3)
                out.append('""')
                i = n if end < 0 else end + 3
                continue
            j = k + 1
            depth = 0
            while j < n:
                d = text[j]
                if interpolated and d == "{":
                    if text.startswith("{{", j):
                        j += 2
                        continue
                    depth += 1
                elif interpolated and d == "}" and depth > 0:
                    depth -= 1
                elif depth == 0 and d == '"':
                    if verbatim and text.startswith('""', j):
                        j += 2
                        continue
                    break
                elif d == "\\" and not verbatim and depth == 0:
                    j += 1
                j += 1
            out.append('""')
            i = j + 1
            continue
        out.append(c)
        i += 1
    return "".join(out)


def body_end(code: str, start: int) -> int | None:
    """The end of the body that follows the parameter list opening at ``start``: a block's closing brace, or an
    expression body's semicolon. None for a declaration with no body."""
    depth, i, n = 0, start, len(code)
    while i < n:                          # the parameter list
        if code[i] == "(":
            depth += 1
        elif code[i] == ")":
            depth -= 1
            if depth == 0:
                break
        i += 1
    i += 1
    while i < n and code[i] not in "{;=":
        i += 1
    if i >= n or code[i] == ";":
        return None
    if code[i] == "=":
        if not code.startswith("=>", i):
            return None
        depth = 0
        while i < n:
            if code[i] in "({[":
                depth += 1
            elif code[i] in ")}]":
                depth -= 1
            elif code[i] == ";" and depth == 0:
                return i + 1
            i += 1
        return None
    depth = 0
    while i < n:
        if code[i] == "{":
            depth += 1
        elif code[i] == "}":
            depth -= 1
            if depth == 0:
                return i + 1
        i += 1
    return None


class Method:
    def __init__(self, name: str, returns: str, body: str, where: str):
        self.name, self.returns, self.body, self.where = name, returns, body, where


def methods_of(files: list[Path]) -> dict[str, list[Method]]:
    """Each method of the files by name (overloads in one list), with its body stripped of comments and literals."""
    found: dict[str, list[Method]] = {}
    for path in files:
        code = strip_code(path.read_text(encoding="utf-8-sig"))
        for match in DECLARATION.finditer(code):
            name = match.group("name")
            returns = match.group("type").split()[-1] if match.group("type").split() else ""
            if name in NOT_METHODS or returns in NOT_METHODS or returns in {"class", "struct", "record", "interface",
                                                                             "enum", "delegate", "=", "return"}:
                continue
            end = body_end(code, match.end() - 1)
            if end is None:
                continue
            line = code.count("\n", 0, match.start()) + 1
            found.setdefault(name, []).append(Method(name, returns, code[match.end() - 1:end], f"{path.name}:{line}"))
    return found


def blocking_waits(methods: dict[str, list[Method]]) -> set[str]:
    """The wait native's methods that reach its blocking helpers."""
    calls = {name: {call.group("name") for m in overloads for call in BARE_CALL.finditer(m.body)} & methods.keys()
             for name, overloads in methods.items()}
    blocking: set[str] = set(BLOCKING_HELPERS)
    changed = True
    while changed:
        changed = False
        for name, callees in calls.items():
            if name not in blocking and callees & blocking:
                blocking.add(name)
                changed = True
    return blocking


def not_waits() -> set[str]:
    """The wait members that block and are not waits: ModCallWaits.cs's NotWaits list (spec §10)."""
    found = NOT_WAITS_LIST.search(MOD_CALL_WAITS.read_text(encoding="utf-8"))
    names = {m.group("name") for m in NAMEOF.finditer(found.group("body"))} if found else set()
    if not names:
        raise SystemExit(f"no NotWaits list found in {MOD_CALL_WAITS}")
    return names


def handler_table() -> dict[str, str]:
    """Each implemented opcode's canonical name to its handler method."""
    table = {m.group("label"): m.group("method") for m in TABLE_ENTRY.finditer(HANDLERS.read_text(encoding="utf-8"))}
    if not table:
        raise SystemExit(f"no handler entries found in {HANDLERS}")
    return table


def derive() -> tuple[dict[str, dict], dict[str, list[Method]]]:
    """Each implemented opcode's canonical name to its facts and the methods that gave each."""
    vm = methods_of(sorted(VM_DIR.glob("VirtualMachine*.cs")))
    waits_native = methods_of(sorted(WAITS_DIR.glob(WAITS_GLOB)))
    blocking = blocking_waits(waits_native) - not_waits()
    results: dict[str, dict] = {}
    for label, method in sorted(handler_table().items()):
        if method not in vm:
            raise SystemExit(f"{label}: handler {method} not found in {VM_DIR}")
        facts: set[str] = set()
        why: dict[str, str] = {}
        waits = False
        seen: set[str] = set()
        pending = [(method, True)]       # (name, its step results are the handler's)
        while pending:
            name, steps = pending.pop()
            key = f"{name}/{steps}"
            if key in seen:
                continue
            seen.add(key)
            for m in vm.get(name, []):
                def give(fact: str, token: str) -> None:
                    facts.add(fact)
                    why.setdefault(fact, f"{m.name} ({m.where}): {token}")
                if steps and m.returns == "StepResult":
                    for token, fact in STEP_TOKENS.items():
                        if token in m.body:
                            give(fact, token)
                for token, fact in {**THROW_TOKENS, **PERSISTENCE_TOKENS}.items():
                    if token in m.body:
                        give(fact, token)
                for call in WAITS_CALL.finditer(m.body):
                    if call.group("name") in blocking and not waits:
                        waits = True
                        why["waits"] = f"{m.name} ({m.where}): _waits.{call.group('name')}"
                for call in BARE_CALL.finditer(m.body):
                    callee = call.group("name")
                    if callee in RUNS_CODE:
                        give("calls", callee)
                    elif callee in vm and callee != m.name:
                        # A callee's step results are the handler's only when it returns one and this method does.
                        pending.append((callee, steps and m.returns == "StepResult"))
                # A method passed as an argument (a wait's callback, _waits.WaitForInput(ServiceHotspotCallback, …))
                # runs inside the call it is passed to; its step results are not the handler's.
                for group in METHOD_GROUP.finditer(m.body):
                    callee = group.group("name")
                    if callee in RUNS_CODE:
                        give("calls", callee)
                    elif callee in vm and callee != m.name:
                        pending.append((callee, False))
        results[label] = {
            "method": method,
            "waits": "always" if label in ALWAYS_WAITS else "may" if waits else "never",
            "leaves": [fact for fact in LEAVES if fact in facts],
            "why": why,
        }
    return results, vm


def registry_plan(model: M.Model, derived: dict[str, dict]) -> dict[int, tuple[str, list[str]]]:
    """Each implemented opcode's facts by opcode. Every implemented name is one opcode with a semantics block."""
    names: dict[str, list[int]] = {}
    for op, oc in model.opcodes.items():
        names.setdefault(oc.semantics.name if oc.semantics and oc.semantics.name else oc.label, []).append(op)
    plan: dict[int, tuple[str, list[str]]] = {}
    for name, entry in derived.items():
        ops = names.get(name, [])
        if len(ops) != 1 or model.opcodes[ops[0]].semantics is None:
            raise SystemExit(f"{name}: the handler table names it, and the registry has "
                             + (f"{len(ops)} opcodes of that name" if len(ops) != 1 else "no semantics block for it"))
        plan[ops[0]] = (entry["waits"], entry["leaves"])
    return plan


def differences(model: M.Model, plan: dict[int, tuple[str, list[str]]]) -> list[str]:
    """What the registry records against the derivation: changed facts, and facts on an opcode the port lacks."""
    found = []
    for op, oc in sorted(model.opcodes.items()):
        s = oc.semantics
        recorded = (s.waits, s.leaves) if s and s.waits else None
        due = plan.get(op)
        if recorded != (None if due is None else (due[0], list(due[1]))):
            name = s.name if s else oc.label
            found.append(f"0x{op:x} {name}: registry {recorded or 'none'}, handlers {due or 'not implemented'}")
    return found


def apply_to_registry(text: str, plan: dict[int, tuple[str, list[str]]]) -> str:
    """The registry text with each opcode's `waits` and `leaves` lines after its `noop_headless` line, and none on an
    opcode the plan lacks. Every other line is kept byte for byte."""
    out: list[str] = []
    op: int | None = None
    in_semantics = False
    for line in text.split("\n"):
        stripped = line.strip()
        if stripped.startswith("["):
            in_semantics = stripped == "[opcode.semantics]"
            if stripped == "[[opcode]]":
                op = None
        elif op is None and stripped.startswith("op = "):
            op = int(stripped.split("=", 1)[1].split("#")[0].strip(), 0)
        if in_semantics and (stripped.startswith("waits = ") or stripped.startswith("leaves = ")):
            continue
        out.append(line)
        if in_semantics and stripped.startswith("noop_headless = ") and op in plan:
            waits, leaves = plan[op]
            out.append(f'waits = "{waits}"')
            out.append("leaves = [" + ", ".join(f'"{kind}"' for kind in leaves) + "]")
    return "\n".join(out)


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--apply", action="store_true", help="write the derived facts into vm-map/opcodes.toml")
    parser.add_argument("--check", action="store_true", help="exit non-zero when the registry differs")
    parser.add_argument("--why", metavar="NAME", help="print the methods that gave one opcode's facts")
    args = parser.parse_args(argv)

    derived, _ = derive()
    if args.why:
        entry = derived.get(args.why)
        if entry is None:
            print(f"{args.why}: no such implemented opcode", file=sys.stderr)
            return 2
        print(json.dumps(entry, indent=2))
        return 0
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(json.dumps({"generated_by": "tools/opcode_flow.py", "opcodes": derived}, indent=2,
                                 ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    counts: dict[str, int] = {}
    for entry in derived.values():
        counts[entry["waits"]] = counts.get(entry["waits"], 0) + 1
        for fact in entry["leaves"]:
            counts[fact] = counts.get(fact, 0) + 1
    print(f"opcode_flow: {len(derived)} implemented opcodes; "
          + ", ".join(f"{key} {counts.get(key, 0)}" for key in (*WAITS, *LEAVES)) + f" -> {OUTPUT}")

    toml_path = paths.VM_MAP / "opcodes.toml"
    model = M.load(toml_path)
    plan = registry_plan(model, derived)
    if args.apply:
        text = toml_path.read_text(encoding="utf-8")
        toml_path.write_bytes(apply_to_registry(text, plan).encode("utf-8"))   # keep the registry LF on Windows
        errors, _ = M.lint(M.load(toml_path))
        print(f"opcode_flow: wrote the facts of {len(plan)} opcodes -> {toml_path}; lint errors={len(errors)}")
        for error in errors:
            print(f"  {error}")
        return 1 if errors else 0
    found = differences(model, plan)
    for line in found[:40]:
        print(f"  {line}")
    if len(found) > 40:
        print(f"  ... and {len(found) - 40} more")
    if found:
        print(f"opcode_flow: the registry differs from the handlers for {len(found)} opcodes; run "
              "py -3.11 -X utf8 tools/opcode_flow.py --apply, then tools/opcodes_build.py --build")
    else:
        print("opcode_flow: the registry's facts match the handlers")
    return 1 if found and args.check else 0


if __name__ == "__main__":
    raise SystemExit(main())
