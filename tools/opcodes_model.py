#!/usr/bin/env python3
"""In-memory model + loader + linter for vm-map/opcodes.toml (the canonical opcode reference).
Read-only: uses stdlib tomllib. See docs/superpowers/specs/2026-07-06-opcode-reference-design.md."""
from __future__ import annotations
import tomllib
from dataclasses import dataclass, field
from pathlib import Path

CATEGORIES = {"marker", "structural", "control", "adv", "draw", "audio", "input", "compute", "unknown"}
SOURCES = {"kelebek", "harness", "investigation", "frida", "unicorn", "inference"}
CONFIDENCE = {"low": 1, "med": 2, "high": 3}

@dataclass
class Semantics:
    name: str
    category: str = "unknown"
    summary: str = ""
    noop_headless: bool = False
    source: str = "kelebek"
    confidence: str = "low"
    depends_on: list[int] = field(default_factory=list)
    evidence: str = ""
    details: str = ""
    confirm_by: str = ""
    args: list[dict] = field(default_factory=list)

@dataclass
class Opcode:
    op: int
    label: str
    argc: int
    code_target_args: list[int] = field(default_factory=list)
    abi_source: str = "kelebek+decode-validated"
    abi_note: str = ""
    semantics: Semantics | None = None

@dataclass
class Model:
    meta: dict
    opcodes: dict[int, Opcode]

def load(path) -> Model:
    data = tomllib.loads(Path(path).read_text(encoding="utf-8"))
    ops: dict[int, Opcode] = {}
    for e in data.get("opcode", []):
        sem = None
        s = e.get("semantics")
        if s is not None:
            sem = Semantics(
                name=s.get("name", e.get("label", "")),
                category=s.get("category", "unknown"),
                summary=s.get("summary", ""),
                noop_headless=bool(s.get("noop_headless", False)),
                source=s.get("source", "kelebek"),
                confidence=s.get("confidence", "low"),
                depends_on=[int(x) for x in s.get("depends_on", [])],
                evidence=s.get("evidence", ""),
                details=s.get("details", ""),
                confirm_by=s.get("confirm_by", ""),
                args=list(s.get("args", [])),
            )
        ops[int(e["op"])] = Opcode(
            op=int(e["op"]), label=e.get("label", ""), argc=int(e["argc"]),
            code_target_args=[int(x) for x in e.get("code_target_args", [])],
            abi_source=e.get("abi_source", "kelebek+decode-validated"),
            abi_note=e.get("abi_note", ""), semantics=sem,
        )
    return Model(meta=data.get("meta", {}), opcodes=ops)

def lint(model: Model) -> tuple[list[str], list[str]]:
    """Return (errors, warnings). Errors: bad vocabulary, dangling depends_on.
    Warnings: confidence exceeds the minimum confidence among its dependencies."""
    errors: list[str] = []
    warnings: list[str] = []
    ops = model.opcodes
    for op, oc in sorted(ops.items()):
        s = oc.semantics
        if not s:
            continue
        tag = f"0x{op:x}"
        if s.category not in CATEGORIES:
            errors.append(f"{tag}: bad category {s.category!r}")
        if s.source not in SOURCES:
            errors.append(f"{tag}: bad source {s.source!r}")
        if s.confidence not in CONFIDENCE:
            errors.append(f"{tag}: bad confidence {s.confidence!r}")
        for dep in s.depends_on:
            if dep not in ops:
                errors.append(f"{tag}: depends_on missing opcode 0x{dep:x}")
        if s.confidence in CONFIDENCE:
            dep_confs = [CONFIDENCE[ops[d].semantics.confidence]
                         for d in s.depends_on
                         if d in ops and ops[d].semantics
                         and ops[d].semantics.confidence in CONFIDENCE]
            if dep_confs and CONFIDENCE[s.confidence] > min(dep_confs):
                warnings.append(f"{tag}: confidence {s.confidence!r} exceeds dependency ceiling")
    return errors, warnings

def dependents(model: Model) -> dict[int, list[int]]:
    """Reverse of depends_on: op -> [ops whose semantics depend on it]."""
    rev: dict[int, list[int]] = {op: [] for op in model.opcodes}
    for op, oc in model.opcodes.items():
        if oc.semantics:
            for dep in oc.semantics.depends_on:
                rev.setdefault(dep, []).append(op)
    for k in rev:
        rev[k].sort()
    return rev
