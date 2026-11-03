# Procedural Interchanges — Agent Guidelines

These guidelines apply specifically to this repository and build on top of the global Cities: Skylines II modding rules.

---

## 1. Development & Quality Protocols
- **4-Stage Development Lifecycle**: When handling multi-step tasks or end-to-end debugging, defer to the [pipeline skill](file:///.agents/skills/pipeline/SKILL.md) (`Triage` -> `Planning` -> `Execution` -> `QA`).
- **Incident & Bug Tracking**:
  - Raw evidence and observable incident facts are logged in [`docs/BUGS.md`](file:///docs/BUGS.md).
  - Triage is strictly an objective incident recorder: record verifiable logs and symptoms without speculative root-cause hypotheses to prevent premature bias during architecture analysis.
- **Handoff & Planning Artifacts**:
  - Handoffs from triage to architecture analysis are written to [`docs/HANDOFF.md`](file:///docs/HANDOFF.md).
  - High-level architectural blueprints and deterministic execution plans are written to [`docs/PLAN.md`](file:///docs/PLAN.md).

---

## 2. Engineering Invariants & Roles
- **Principal Architect Role**: Owns 100% of mathematical formulation (clothoid/Bézier splines, elevation profiles), system contracts, and ECS lifecycle design from first principles. Blueprints must be granular, deterministic, and unambiguous.
- **Execution Role**: Strictly implements the blueprint with a minimal deviation policy (fixing minor typos/syntax allowed; no ad-hoc architectural redesigns). Always report completed items, fixes, and any blocked items.
- **Verification & QA**: All geometry, spline, and archetype changes must verify against the unit test suite (`dotnet test Tests/ProceduralInterchanges.Tests/ProceduralInterchanges.Tests.csproj`) with 0 regressions.
