# Procedural Interchanges — Agent Guidelines

These guidelines apply specifically to this repository and build on top of the global Cities: Skylines II modding rules:

---

## 1. "Triage" Protocol (Debugging & Incident Analysis)
Whenever the user says **"triage"** (or triggers triage on a bug, glitch, or crash):
1. **Automated Evidence Collection**:
   - Immediately inspect the latest entries in:
     - `%LOCALAPPDATA%Low\Colossal Order\Cities Skylines II\Logs\ProceduralInterchanges.log` (Our dedicated mod log!)
     - `%LOCALAPPDATA%Low\Colossal Order\Cities Skylines II\Logs\Modding.log` (Engine loader & mod errors)
     - `%LOCALAPPDATA%Low\Colossal Order\Cities Skylines II\Player.log` (Unity runtime, Burst crashes, and native engine stack traces)
     - `%LOCALAPPDATA%Low\Colossal Order\Cities Skylines II\Logs\UI.log` (Coherent Gameface binding and frontend errors)
     - `%LOCALAPPDATA%Low\Colossal Order\Cities Skylines II\Logs\SceneFlow.log` (Game load sequence and phase transitions)
     - `%LOCALAPPDATA%Low\Colossal Order\Cities Skylines II\Crashes\` (Unity crash dumps / error reports if CTD occurred)
   - Check the active build/install timestamp in `%LOCALAPPDATA%Low\Colossal Order\Cities Skylines II\Mods\ProceduralInterchanges`.
2. **Interactive Information Gathering**:
   - Prompt the user for key context if not already provided:
     - Exact symptom (e.g. crash to desktop, placement rejected, pillar glitch, lane weaving, UI freeze).
     - Archetype attempted (Diamond, Trumpet, Parclo, etc.) and terrain state (flat, slope, water).
     - Tool mode / step (hover preview, left-click commit, cancellation).
3. **In-Game Debugging Requests**:
   - Guide the user with specific developer actions:
     - Press <kbd>Tab</kbd> to inspect the native dev panel / Net debug overlays.
     - Check UI console at `http://localhost:9222` for Gameface / React errors.
     - Reproduce with or without Anarchy to isolate geometric validation errors vs engine exceptions.
4. **Action Item Logging (Raw Facts & Evidence Only — No Speculative Root Causes)**:
   - Triage must act purely as an objective incident recorder to prevent premature bias or anchoring when Argon/the Principal Architect analyzes the issue.
   - Record **strictly observable facts** in `docs/BUGS.md`:
     - Raw Symptom (what was seen/heard)
     - Exact Reproduction Steps & User Context (archetype, terrain, tool action)
     - Raw Evidence (exact stack traces, log lines, error codes, and timestamps)
     - *Explicitly omit speculative root-cause hypotheses* — leave root-cause analysis and solution design entirely to the Principal Architect during implementation planning.

5. **Handoff Document Generation (docs/HANDOFF.md)**:
   - After completing triage, generate a structured docs/HANDOFF.md addressed to the Argon Implementor / Principal Architect.
   - The handoff document must synthesize all triage findings, reference attached screenshots, enumerate the triaged items from docs/BUGS.md, and explicitly delineate technical code implementation tasks from separate testing suite requirements.
---

## 2. "Create Implementation Plan" Protocol (Principal Architect Role)
Whenever the user asks to **"create an implementation plan"** (or design/plan a feature):
- Act as the **Principal Architect**: own 100% of the high-level thinking, mathematical formulation, technical problem-solving, and system design from first principles.
- The plan is written for **Gemini 3.8 Flash execution bots**, who will write the actual code based strictly on your blueprint.
- Because execution bots focus purely on implementation rather than architecture, plans must be **completely deterministic, granular, and unambiguous**:
  - Exact data structures, fields, and types.
  - Complete mathematical formulations (e.g. Bézier control point derivations, clothoid spiral equations, clearance envelopes).
  - Exact method signatures, arguments, and return types.
  - Specific Unity ECS queries, component tags, and system update phase registrations (imperative `UpdateSystem`).
  - Strict step-by-step sequencing so execution bots never have to make architectural choices, guess edge-case handling, or invent interfaces.

---

## 3. "Execute Implementation Plan" Protocol (Flash Execution Bot Role)
When tasked with executing an implementation plan:
- **Strict Adherence to Blueprint**: Execution bots must focus purely on writing the exact code defined in the plan without overthinking or second-guessing the architecture.
- **Minimal Deviation Policy**:
  - Do **NOT** invent new architectures, alter method signatures, swap data structures, or add unapproved systems.
  - Minor fixes (fixing compile typos, resolving unambiguous syntax errors, or obvious one-line null-checks) are permitted.
  - **Refrain from big architectural fixes**: If an unplanned design flaw, engine collision, or API incompatibility arises, do **NOT** improvise an ad-hoc architectural redesign.
- **End-of-Turn Handoff Report**:
  - At the conclusion of execution, explicitly report:
    1. What was successfully implemented and verified (compilation, file outputs).
    2. Any minor compile/syntax fixes that were necessary.
    3. **Crucially: anything that could NOT be implemented** strictly following the implementation plan, along with the exact blocking reason, so the Principal Architect can review and revise the blueprint.
