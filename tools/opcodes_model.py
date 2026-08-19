#!/usr/bin/env python3
"""In-memory model + loader + linter for vm-map/opcodes.toml (the canonical opcode reference).
Read-only: uses stdlib tomllib. See docs/superpowers/specs/2026-07-06-opcode-reference-design.md."""
from __future__ import annotations
import tomllib
import collections
from dataclasses import dataclass, field
from pathlib import Path

CATEGORIES = {"marker", "structural", "control", "adv", "draw", "audio", "input", "compute", "unknown"}
SOURCES = {"kelebek", "harness", "investigation", "frida", "unicorn", "inference"}
CONFIDENCE = {"low": 1, "med": 2, "high": 3}
EVIDENCE_METHODS = {
    "catalog-corpus", "upstream-catalog", "native-re", "frida", "harness",
    "inference", "unicorn",
}


@dataclass(frozen=True)
class Evidence:
    method: str
    artifact: str
    site: str
    scope: str
    confidence: str
    text: str
    profile_ids: tuple[str, ...] = ()
    engine_revisions: tuple[str, ...] = ()


@dataclass(frozen=True)
class Observation:
    profile_id: str
    catalog_revision: str
    script_revision: str
    engine_abi_id: str
    method: str
    artifact: str
    script_count: int
    instruction_count: int
    opcodes: frozenset[int]

@dataclass
class Semantics:
    name: str
    category: str = "unknown"
    summary: str = ""
    noop_headless: bool = False
    source: str = "kelebek"
    confidence: str = "low"
    depends_on: list[int] = field(default_factory=list)
    evidence: list[Evidence] = field(default_factory=list)
    details: str = ""
    confirm_by: str = ""
    args: list[dict] = field(default_factory=list)

@dataclass
class Opcode:
    op: int
    label: str
    argc: int
    observed_by: frozenset[str] = frozenset()
    observed_revisions: frozenset[str] = frozenset()
    legacy_observation_field: bool = False
    code_target_args: list[int] = field(default_factory=list)
    abi_source: str = "kelebek+decode-validated"
    abi_note: str = ""
    semantics: Semantics | None = None

@dataclass
class Model:
    meta: dict
    opcodes: dict[int, Opcode]
    observations: dict[str, Observation]
    observation_profile_ids: tuple[str, ...]


def _evidence_record(
    raw: dict,
    defaults: dict,
    confidence: str,
    text: str,
    op: int,
) -> Evidence:
    return Evidence(
        method=raw.get("method", defaults.get("method", "")),
        artifact=raw.get("artifact", defaults.get("artifact", "")),
        site=raw.get("site", f"vm-map/opcodes.toml opcode 0x{op:x}"),
        scope=raw.get("scope", defaults.get("scope", "")),
        confidence=raw.get("confidence", confidence),
        text=raw.get("text", text),
        profile_ids=tuple(raw.get("profile_ids", defaults.get("profile_ids", []))),
        engine_revisions=tuple(
            raw.get("engine_revisions", defaults.get("engine_revisions", []))
        ),
    )

def load(path) -> Model:
    data = tomllib.loads(Path(path).read_text(encoding="utf-8"))
    observations: dict[str, Observation] = {}
    for raw in data.get("observation", []):
        profile_id = raw.get("profile_id", "")
        observations[profile_id] = Observation(
            profile_id=profile_id,
            catalog_revision=raw.get("catalog_revision", ""),
            script_revision=raw.get("script_revision", ""),
            engine_abi_id=raw.get("engine_abi_id", ""),
            method=raw.get("method", ""),
            artifact=raw.get("artifact", ""),
            script_count=int(raw.get("script_count", 0)),
            instruction_count=int(raw.get("instruction_count", 0)),
            opcodes=frozenset(int(op) for op in raw.get("opcodes", [])),
        )
    observed_by: dict[int, set[str]] = {}
    observed_revisions: dict[int, set[str]] = {}
    for observation in observations.values():
        for op in observation.opcodes:
            observed_by.setdefault(op, set()).add(observation.profile_id)
            observed_revisions.setdefault(op, set()).add(observation.script_revision)

    evidence_defaults = data.get("evidence_defaults", {})
    ops: dict[int, Opcode] = {}
    for e in data.get("opcode", []):
        op = int(e["op"])
        sem = None
        s = e.get("semantics")
        if s is not None:
            evidence: list[Evidence] = []
            raw_evidence = s.get("evidence", "")
            defaults = evidence_defaults.get(s.get("source", "kelebek"), {})
            if isinstance(raw_evidence, str):
                if raw_evidence:
                    evidence.append(_evidence_record(
                        {}, defaults, s.get("confidence", "low"), raw_evidence, op
                    ))
            elif isinstance(raw_evidence, list):
                evidence.extend(
                    _evidence_record(
                        raw,
                        defaults,
                        s.get("confidence", "low"),
                        raw.get("text", ""),
                        op,
                    )
                    for raw in raw_evidence
                )
            sem = Semantics(
                name=s.get("name", e.get("label", "")),
                category=s.get("category", "unknown"),
                summary=s.get("summary", ""),
                noop_headless=bool(s.get("noop_headless", False)),
                source=s.get("source", "kelebek"),
                confidence=s.get("confidence", "low"),
                depends_on=[int(x) for x in s.get("depends_on", [])],
                evidence=evidence,
                details=s.get("details", ""),
                confirm_by=s.get("confirm_by", ""),
                args=list(s.get("args", [])),
            )
        ops[op] = Opcode(
            op=op, label=e.get("label", ""), argc=int(e["argc"]),
            observed_by=frozenset(observed_by.get(op, ())),
            observed_revisions=frozenset(observed_revisions.get(op, ())),
            legacy_observation_field="observed_in_himegari" in e,
            code_target_args=[int(x) for x in e.get("code_target_args", [])],
            abi_source=e.get("abi_source", "kelebek+decode-validated"),
            abi_note=e.get("abi_note", ""), semantics=sem,
        )
    return Model(
        meta=data.get("meta", {}),
        opcodes=ops,
        observations=observations,
        observation_profile_ids=tuple(
            raw.get("profile_id", "") for raw in data.get("observation", [])
        ),
    )

def lint(model: Model) -> tuple[list[str], list[str]]:
    """Return (errors, warnings). Errors: bad vocabulary, dangling depends_on.
    Warnings: confidence exceeds the minimum confidence among its dependencies."""
    errors: list[str] = []
    warnings: list[str] = []
    ops = model.opcodes
    known_profiles = set(model.meta.get("profile_ids", []))
    if "opcodes_used_by_himegari" in model.meta:
        errors.append("meta.opcodes_used_by_himegari is obsolete; use [[observation]]")
    known_revisions = {item.script_revision for item in model.observations.values()}
    duplicate_profiles = [
        profile_id for profile_id, count in
        collections.Counter(model.observation_profile_ids).items()
        if count > 1
    ]
    if duplicate_profiles:
        errors.append(f"duplicate observation profiles: {sorted(duplicate_profiles)}")
    missing_observations = known_profiles - set(model.observations)
    if missing_observations:
        errors.append(f"profiles missing observation records: {sorted(missing_observations)}")
    for profile_id, observation in model.observations.items():
        tag = f"observation[{profile_id or '?'}]"
        if profile_id not in known_profiles:
            errors.append(f"{tag}: unknown profile id")
        if observation.method != "catalog-corpus":
            errors.append(f"{tag}: bad method {observation.method!r}")
        for field_name in ("catalog_revision", "script_revision", "engine_abi_id", "artifact"):
            if not getattr(observation, field_name):
                errors.append(f"{tag}: missing {field_name}")
        if observation.script_count <= 0 or observation.instruction_count <= 0:
            errors.append(f"{tag}: missing positive corpus counts")
        expected_artifact_prefix = f"build/games/{profile_id}/"
        if observation.artifact and not observation.artifact.startswith(expected_artifact_prefix):
            errors.append(
                f"{tag}: artifact must stay in selected profile output "
                f"({expected_artifact_prefix}...)"
            )
        missing = sorted(observation.opcodes - set(ops))
        if missing:
            errors.append(
                f"{tag}: references missing opcodes "
                + ", ".join(f"0x{op:x}" for op in missing)
            )
    for op, oc in sorted(ops.items()):
        if oc.legacy_observation_field:
            errors.append(f"0x{op:x}: observed_in_himegari is obsolete; use [[observation]]")
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
        for index, evidence in enumerate(s.evidence, 1):
            evidence_tag = f"{tag}: evidence {index}"
            if evidence.method not in EVIDENCE_METHODS:
                errors.append(f"{evidence_tag}: bad method {evidence.method!r}")
            if evidence.confidence not in CONFIDENCE:
                errors.append(f"{evidence_tag}: bad confidence {evidence.confidence!r}")
            for field_name in ("artifact", "site", "scope", "text"):
                if not getattr(evidence, field_name):
                    errors.append(f"{evidence_tag}: missing {field_name}")
            unknown_profiles = set(evidence.profile_ids) - known_profiles
            if unknown_profiles:
                errors.append(
                    f"{evidence_tag}: unknown profiles {sorted(unknown_profiles)}"
                )
            unknown_revisions = set(evidence.engine_revisions) - known_revisions
            if unknown_revisions:
                errors.append(
                    f"{evidence_tag}: unknown revisions {sorted(unknown_revisions)}"
                )
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
