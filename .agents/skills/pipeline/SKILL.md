---
name: pipeline
description: "Orchestrates the 4-stage human-in-the-loop development pipeline: 1. Triage (Flash 3.8 Low) -> User Gate -> 2. Implementation Planning (Principal Architect - Flash 3.8 High or Argon at user discretion) -> User Gate -> 3. Execution (Flash 3.8 Medium) -> User Gate -> 4. Verification & QA (Flash 3.8 Low). Triggered whenever the user says 'pipeline', 'triage and fix', or asks to run the full dev pipeline."
---

# Procedural Interchanges Development Pipeline

This skill orchestrates a strict, 4-stage development workflow where each stage uses an optimal model tier and requires **explicit user manual sign-off** before progressing to the next stage.

> [!IMPORTANT]
> Every stage of this pipeline must strictly adhere to the global Cities: Skylines II modding rules in ~/.gemini/config/plugins/cs2-modding/rules/AGENTS.md (the core CS2 ground truth hierarchy, imperative `UpdateSystem` ordering, power-of-two intervals, zero-field tags, query hygiene, and reference mod corpus).

---

## Stage 1: Triage (Model: Flash 3.8 Low)

### Objective
Act purely as an objective incident recorder to capture observable facts, logs, and reproduction steps without speculative root-cause hypotheses (avoiding premature bias for the Principal Architect).

### Automated Actions
1. **Automated Evidence Collection**:
   - Inspect the latest entries in:
     - `%LOCALAPPDATA%Low\Colossal Order\Cities Skylines II\Logs\ProceduralInterchanges.log` (Dedicated mod log)
     - `%LOCALAPPDATA%Low\Colossal Order\Cities Skylines II\Logs\Modding.log` (Loader & mod errors)
     - `%LOCALAPPDATA%Low\Colossal Order\Cities Skylines II\Player.log` (Unity runtime, Burst crashes, native stack traces)
     - `%LOCALAPPDATA%Low\Colossal Order\Cities Skylines II\Logs\UI.log` (Gameface / React binding errors)
     - `%LOCALAPPDATA%Low\Colossal Order\Cities Skylines II\Logs\SceneFlow.log` (Phase transitions & scene loading)
     - `%LOCALAPPDATA%Low\Colossal Order\Cities Skylines II\Crashes\` (Unity crash dumps / error reports)
   - Verify the active build timestamp in `%LOCALAPPDATA%Low\Colossal Order\Cities Skylines II\Mods\ProceduralInterchanges`.

2. **Interactive Information Gathering**:
   - Prompt the user for missing context:
     - Exact observed symptom (e.g., CTD, placement rejection, pillar glitch, lane weaving, UI freeze).
     - Scenario context: archetype attempted (Diamond, Trumpet, Parclo, etc.) and terrain state (flat, slope, water).
     - Interaction step: hover preview, left-click commit, or cancellation.

3. **In-Game Debugging Requests**:
   - Guide the user with specific actions if needed:
     - Press <kbd>Tab</kbd> to inspect the native dev panel / Net debug overlays.
     - Check UI console at `http://localhost:9222` for Gameface / React errors.
     - Reproduce with or without Anarchy to isolate geometric validation errors vs engine exceptions.

4. **Action Item Logging (Raw Facts Only)**:
   - Append strictly observable facts to `docs/BUGS.md`:
     - Raw Symptom (what was seen/heard)
     - Exact Reproduction Steps & User Context
     - Raw Evidence (exact stack traces, error codes, log lines, timestamps)
     - *Explicitly omit speculative root-cause hypotheses*.

5. **Handoff Document Generation (`docs/HANDOFF.md`)**:
   - Synthesize all triage findings into `docs/HANDOFF.md` addressed to the Principal Architect.
   - Reference attached screenshots/logs, enumerate triaged items from `docs/BUGS.md`, and delineate technical code implementation tasks from separate testing suite requirements.

🛑 **USER GATE 1**: Present the completed `docs/HANDOFF.md` to the user. Ask the user for manual sign-off and clarify whether they want to use **Gemini 3.8 Flash (High)** or **Argon / Pro** for the Principal Architect stage. **DO NOT proceed to Stage 2 without explicit user confirmation.**

---

## Stage 2: Implementation Planning (Model: Flash 3.8 High or Argon / Pro)

### Objective
Act as the **Principal Architect**: own 100% of the high-level thinking, mathematical formulation, technical problem-solving, and system design from first principles.

### Guidelines
1. The plan is written for **Flash execution bots**, who focus purely on implementation rather than architecture.
2. The blueprint in `docs/PLAN.md` must be **completely deterministic, granular, and unambiguous**:
   - Exact data structures, fields, and types.
   - Complete mathematical formulations (e.g. Bézier control point derivations, clothoid spiral equations, clearance envelopes).
   - Exact method signatures, arguments, and return types.
   - Specific Unity ECS queries, component tags, and system update phase registrations (imperative `UpdateSystem`).
   - Strict step-by-step sequencing so execution bots never have to make architectural choices, guess edge-case handling, or invent interfaces.
3. Explicitly delineate core C# mod changes from unit test specifications in `Tests/`.

🛑 **USER GATE 2 (Model Switch Pause)**: 
The plan is written to `docs/PLAN.md`. Stop and give a brief one-line notification to the user:
> *"Plan written to `docs/PLAN.md`. Ready to switch model to **Flash 3.8 Medium** for Stage 3 (Execution)."*
Do not require the user to review the full technical plan unless they ask. Wait for the user to prompt to proceed.

---

## Stage 3: Execution & Testing (Model: Flash 3.8 Medium)

### Objective
Act as the **Flash Execution Bot**: write the exact code and tests defined in the blueprint with minimal deviation.

### Guidelines
1. **Strict Adherence to Blueprint**:
   - Write the exact code defined in `docs/PLAN.md` without overthinking or second-guessing the architecture.
2. **Minimal Deviation Policy**:
   - Do **NOT** invent new architectures, alter method signatures, swap data structures, or add unapproved systems.
   - Minor fixes (fixing compile typos, resolving unambiguous syntax errors, or obvious one-line null-checks) are permitted.
   - **Refrain from big architectural fixes**: If an unplanned design flaw, engine collision, or API incompatibility arises, do **NOT** improvise an ad-hoc architectural redesign.
3. **Automated Verification**:
   - Run `dotnet test Tests/ProceduralInterchanges.Tests/ProceduralInterchanges.Tests.csproj` to verify all unit tests pass.

🛑 **USER GATE 3 (Model Switch Pause)**:
Do **NOT** print an extensive execution report. Simply state that execution and initial tests passed, and give a brief one-line notification:
> *"Execution complete and initial tests pass. Ready to switch model to **Flash 3.8 Low** for Stage 4 (Verification & QA)."*
Wait for the user to prompt to proceed.

---

## Stage 4: Verification & QA (Model: Flash 3.8 Low)

### Objective
Act as the **Quality Assurance & Verification Agent**: objectively verify that the bug is resolved or the feature behaves as expected according to the original triage evidence and test suite.

### Actions
1. **Automated Suite Regression**:
   - Run the complete unit test suite: `dotnet test Tests/ProceduralInterchanges.Tests/ProceduralInterchanges.Tests.csproj`.
   - Verify 0 failures and no regressions across all existing interchange test fixtures.
2. **Lab Gallery Verification** (if geometric/visual changes occurred):
   - Run the Lab suite: `dotnet run --project Tests/ProceduralInterchanges.Lab/ProceduralInterchanges.Lab.csproj`.
   - Confirm SVG plan generation and inspect for kinked tangents or clipping envelopes.
3. **In-Game Playtesting Protocol Guidance**:
   - Provide the user with a tailored, concise checklist for in-game confirmation:
     - Exact archetype and footprint to test in-game.
     - Specific developer overlay checks (<kbd>Tab</kbd> $\rightarrow$ Net Debug).
     - Expected log confirmation in `%LOCALAPPDATA%Low\Colossal Order\Cities Skylines II\Logs\ProceduralInterchanges.log`.
4. **Bug Tracker Resolution**:
   - Update [`docs/BUGS.md`](file:///c:/Users/liand/Documents/antigravity/procedural-interchanges/docs/BUGS.md):
     - Move the issue from *Active Issues* to *Resolved Issues*.
     - Document the resolution summary, verified commit hash, and test results.
   - Archive or remove `docs/HANDOFF.md` once verified.

🏁 **COMPLETION**: Present the final QA verification summary and in-game playtesting steps to the user.
