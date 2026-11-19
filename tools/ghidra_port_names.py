#!/usr/bin/env python3
"""Carry function names from one engine build's Ghidra image to another's.

A new game's engine dump imports into Ghidra with no names. This tool pairs the functions of a named build with
those of the new one and writes a name plan that ``tools/ghidra/ApplyFunctionNames.java`` applies:

  1. In Ghidra, on the named image, run ``tools/ghidra/ExportFunctionNames.java`` with an output path.
  2. ``ghidra_port_names.py REFERENCE OTHER --names EXPORT.json --program /folder/image --output PLAN.json``
  3. In Ghidra, on the new image, run ``tools/ghidra/ApplyFunctionNames.java`` with the plan's path, then save.

REFERENCE and OTHER are ``tools/operand_access.py`` ``ENGINES`` ids, as for ``tools/revision_diff.py``, whose
function correspondence this tool uses: same-opcode handlers pair exactly, and aligning their instruction streams,
then their callees', votes on which functions correspond. Here the walk goes on to the leaves (``--depth``), and
it starts from a second set of pairs too: two functions that are each the only one in their build to name some
string literal. That reaches code no opcode does, such as start-up and the settings loaders.

A pair is used when its votes are clear (the best candidate has at least twice the runner-up's), no other function
claims the same partner with more votes, and the two functions' instruction shapes are alike
(``--min-similarity``); a handler pair needs no vote. A ported plate comment ends with a note that names its
source and says its addresses and context offsets are the reference build's. Every opcode handler the reference
gives no name gets one from the opcode registry, ``op_0xNN_<name>``.

A function that neither an opcode nor a string pair reaches keeps Ghidra's name. Needs ``capstone``. Usage: docs/tools-reference.md, "Native engine RE (Ghidra)".
"""
from __future__ import annotations

import argparse
import collections
import difflib
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import paths  # noqa: E402

DEFAULT_DEPTH = 64
DEFAULT_MIN_SIMILARITY = 0.6
CLEAR_MARGIN = 2            # the best candidate's votes are at least this many times the runner-up's
STRING_VOTES = 3            # weight of one string literal that a single function of each build names
SHARED_HANDLER_OPCODES = 4  # a handler bound to this many opcodes or more is the table's default, and gets no name
_REVISION_SUFFIX = re.compile(r"_sys4\d{3}", re.IGNORECASE)


def ported_name(name: str) -> str:
    """A reference name as the other image takes it: a script-revision suffix (``_sys4433``) says which build the
    function was read in, which the other image's function is not."""
    return _REVISION_SUFFIX.sub("", name)


def handler_name(opcode: int, semantic_name: str | None) -> str:
    """``op_0xNN_<name>`` from the registry's semantic name, or ``op_0xNN_handler`` without one. An upstream
    placeholder label is not a name: ``u0043AA20`` is another build's address, and ``32a`` the opcode again."""
    words = re.sub(r"[^0-9a-z]+", "_", (semantic_name or "").lower()).strip("_")
    if re.fullmatch(r"u[0-9a-f]{8}", words) or words == f"{opcode:x}":
        words = ""
    return f"op_0x{opcode:x}_{words or 'handler'}"


def clear_pairs(votes: dict[int, collections.Counter]) -> tuple[dict[int, tuple[int, int, int]], dict[int, str]]:
    """Reference function -> (partner, votes, runner-up votes) for every clear, one-to-one pair, and the reason
    each other voted function was left out."""
    candidates: dict[int, tuple[int, int, int]] = {}
    rejected: dict[int, str] = {}
    for function, counter in votes.items():
        ranked = counter.most_common(2) + [(None, 0)]
        (partner, top), (_, runner) = ranked[0], ranked[1]
        if top < CLEAR_MARGIN * runner or top == runner:
            rejected[function] = f"votes are not clear ({top}:{runner})"
            continue
        candidates[function] = (partner, top, runner)
    by_partner: dict[int, list[int]] = collections.defaultdict(list)
    for function, (partner, _, _) in candidates.items():
        by_partner[partner].append(function)
    pairs = {}
    for partner, claimants in by_partner.items():
        claimants.sort(key=lambda function: (-candidates[function][1], function))
        best = claimants[0]
        if len(claimants) > 1 and candidates[claimants[1]][1] == candidates[best][1]:
            for function in claimants:
                rejected[function] = f"shares partner 0x{partner:x} with equal votes"
            continue
        pairs[best] = candidates[best]
        for function in claimants[1:]:
            rejected[function] = f"partner 0x{partner:x} belongs to 0x{best:x}"
    return pairs, rejected


def string_owners(revision) -> dict[str, set[int]]:
    """String literal -> the engine functions whose own body names it."""
    owners: dict[str, set[int]] = collections.defaultdict(set)
    for function in revision.image.entries():
        if not revision.is_engine(function):
            continue
        try:
            features = revision.features(function)
        except Exception:      # a call target the raw scan found that is not a function entry
            continue
        for fact, _ in features:
            if fact[0] == "str":
                owners[fact[1]].add(function)
    return owners


def string_pairs(reference_owners: dict[str, set[int]], other_owners: dict[str, set[int]]) -> collections.Counter:
    """(reference function, other function) -> the string literals only that one function of each build names."""
    pairs: collections.Counter = collections.Counter()
    for text, functions in reference_owners.items():
        others = other_owners.get(text, ())
        if len(functions) == 1 and len(others) == 1:
            pairs[(next(iter(functions)), next(iter(others)))] += 1
    return pairs


def extend_from_strings(correspondence, depth: int) -> int:
    """Add string-literal pairs to the correspondence's votes and align on from them, as its own walk does from
    the handler pairs. Returns the number of string pairs."""
    seeds = string_pairs(string_owners(correspondence.ref), string_owners(correspondence.other))
    queue = collections.deque()
    for (a, b), count in sorted(seeds.items()):
        correspondence.votes["func"][a][b] += STRING_VOTES * count
        queue.append((a, b, 0))
    while queue:
        a, b, level = queue.popleft()
        if (a, b) in correspondence.aligned_pairs:
            continue
        correspondence.aligned_pairs.add((a, b))
        for callee_a, callee_b in correspondence._align(a, b):
            if level < depth and (callee_a, callee_b) not in correspondence.aligned_pairs:
                queue.append((callee_a, callee_b, level + 1))
    return len(seeds)


def port_note(reference_program: str, name: str, address: int, similarity: float, top: int, runner: int,
              reference_version: str) -> str:
    return (f"[Ported from {reference_program} {name}@0x{address:x} by tools/ghidra_port_names.py "
            f"(similarity {similarity:.2f}, votes {top}:{runner}). Not read in this build: addresses and context "
            f"offsets in this text are {reference_version}'s.]")


def semantic_names() -> dict[int, str | None]:
    path = paths.SHARED_BUILD / "opcodes.json"
    data = json.loads(path.read_text(encoding="utf-8"))
    return {int(entry["op"], 16): (entry.get("semantics") or {}).get("name") for entry in data["opcodes"]}


def build_plan(reference: str, other: str, exported: dict, program: str, depth: int, min_similarity: float) -> dict:
    import revision_diff as RD   # needs capstone; imported here so the pure helpers above do not

    ref, new = RD.Revision(reference), RD.Revision(other)
    correspondence = RD.Correspondence(ref, new, depth)
    seeded = extend_from_strings(correspondence, depth)
    pairs, rejected = clear_pairs(correspondence.votes["func"])
    handler_pairs = {ref.table[op]: new.table[op] for op in set(ref.table) & set(new.table)}
    version = ref.facts.version or reference

    def similarity(a: int, b: int) -> float:
        return difflib.SequenceMatcher(None, ref.shape(a), new.shape(b), autojunk=False).ratio()

    functions: dict[int, dict] = {}
    skipped = []
    named = [entry for entry in exported["functions"] if entry.get("named") or entry.get("plate")]
    for entry in named:
        address = int(entry["address"], 16)
        if address not in pairs:
            skipped.append({"address": entry["address"], "name": entry["name"],
                            "reason": rejected.get(address, "neither an opcode nor a string pair reaches it")})
            continue
        partner, top, runner = pairs[address]
        if not new.is_engine(partner):
            skipped.append({"address": entry["address"], "name": entry["name"],
                            "reason": f"partner 0x{partner:x} is not engine code"})
            continue
        alike = similarity(address, partner)
        if alike < min_similarity and handler_pairs.get(address) != partner:
            skipped.append({"address": entry["address"], "name": entry["name"],
                            "reason": f"similarity {alike:.2f} with 0x{partner:x} is below {min_similarity}"})
            continue
        note = port_note(exported["program"], entry["name"], address, alike, top, runner, version)
        planned = {"address": f"0x{partner:x}", "from": entry["address"], "similarity": round(alike, 3),
                   "plate": f"{entry['plate']}\n\n{note}" if entry.get("plate") else note}
        if entry.get("named"):
            planned["name"] = ported_name(entry["name"])
        functions[partner] = planned

    # Opcode handlers: a name from the registry where the reference gave none.
    names = semantic_names()
    opcodes_of: dict[int, list[int]] = collections.defaultdict(list)
    for opcode, handler in new.table.items():
        opcodes_of[handler].append(opcode)
    from_registry = shared = 0
    for handler, opcodes in sorted(opcodes_of.items()):
        if len(opcodes) >= SHARED_HANDLER_OPCODES:
            shared += 1
            continue
        if "name" in functions.get(handler, {}):
            continue
        opcode = min(opcodes)
        listed = ", ".join(f"0x{op:x}" for op in sorted(opcodes))
        note = (f"Opcode {listed} handler, from the dispatch table at ctx+0x{new.facts.table_offset:x}. Named from "
                f"the opcode registry by tools/ghidra_port_names.py; not read in this build.")
        planned = functions.setdefault(handler, {"address": f"0x{handler:x}"})
        planned["name"] = handler_name(opcode, names.get(opcode))
        planned["plate"] = f"{planned['plate']}\n\n{note}" if "plate" in planned else note
        from_registry += 1

    # Operand accessors: the pairing leaves them out as engine plumbing, and ENGINES pins each by name.
    reference_accessors = {accessor.name: address for address, accessor in ref.facts.accessors.items()}
    exported_at = {int(entry["address"], 16): entry for entry in exported["functions"]}
    accessors_named = 0
    for address, accessor in sorted(new.facts.accessors.items()):
        if "name" in functions.get(address, {}):
            continue
        source_address = reference_accessors.get(accessor.name)
        source = exported_at.get(source_address) if source_address is not None else None
        note = (f"[Operand accessor ({accessor.access}), pinned by name in tools/operand_access.py ENGINES"
                + (f"; the text above is {exported['program']} {source['name']}@0x{source_address:x}'s, and its "
                   f"addresses and context offsets are {version}'s" if source and source.get("plate") else "") + ".]")
        plate = source["plate"] + "\n\n" + note if source and source.get("plate") else note
        functions[address] = {"address": f"0x{address:x}", "name": accessor.name, "plate": plate}
        accessors_named += 1

    # ApplyFunctionNames needs a name for every entry: a plate-only reference function keeps Ghidra's default.
    entries = [entry for _, entry in sorted(functions.items()) if "name" in entry]
    return {
        "generated_by": "tools/ghidra_port_names.py",
        "program": program,
        "reference_program": exported["program"],
        "reference": reference,
        "other": other,
        "depth": depth,
        "min_similarity": min_similarity,
        "summary": {
            "reference_named_functions": len(named),
            "string_pairs": seeded,
            "paired_functions": len(pairs),
            "ported": sum(1 for entry in entries if "from" in entry),
            "handlers_named_from_registry": from_registry,
            "accessors_named": accessors_named,
            "shared_handlers_left": shared,
            "skipped": len(skipped),
        },
        "functions": entries,
        "skipped": skipped,
    }


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("reference", help="engine id whose image has the names (operand_access.py ENGINES)")
    parser.add_argument("other", help="engine id whose image takes them")
    parser.add_argument("--names", type=Path, required=True, help="ExportFunctionNames.java's JSON of the reference")
    parser.add_argument("--program", required=True, help="the other image's Ghidra path, which the plan is bound to")
    parser.add_argument("--output", type=Path, required=True, help="the plan to write")
    parser.add_argument("--depth", type=int, default=DEFAULT_DEPTH, help="callee levels below each handler")
    parser.add_argument("--min-similarity", type=float, default=DEFAULT_MIN_SIMILARITY,
                        help="least instruction-shape similarity of a ported pair (handler pairs are exempt)")
    args = parser.parse_args(argv)
    exported = json.loads(args.names.read_text(encoding="utf-8"))
    plan = build_plan(args.reference, args.other, exported, args.program, args.depth, args.min_similarity)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(plan, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"ghidra-port-names: {args.reference} -> {args.other}: {plan['summary']} -> {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
