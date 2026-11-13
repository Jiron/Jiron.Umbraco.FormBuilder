# 0251. The runtime's budget grows to 100 KB

- Status: accepted; the owner's decision on section 4.12, asked in `docs/OWNER-TODO.md`; ends the wait of step 8.6b
  ([ADR-0163](0163-save-and-resume.md)) and changes the number in
  [ADR-0146](0146-the-runtimes-budget-grows-to-60-kb.md), whose way of counting stands
- Date: 2026-11-14
- Roadmap step: at 19.1, the Phase 19 review (the owner wrote during the run, so the message beats the plan)

## Context

ADR-0146 gave the website runtime 60 KB gzip with every feature and left anything past it to the owner. The runtime
with every feature it has stands at 59.52 KB on `main` (the gate on 2a6c09c2). Step 8.6b's control, "Save and continue
later" with the restoring of a saved form's answers, is built on `wip/8.6b-resume-control` and comes to about 2 KB as a
chunk that only a form offering it loads, about 61.7 KB in all. It has waited since 2026-11-02 and was retried at every
phase's review since; `docs/OWNER-TODO.md` asked for a number and recommended 64 KB.

## Decision

- **The owner raised the budget to 100 KB gzip with everything,** on 2026-11-14: more than 60 KB is fine, up to 100 KB
  at most for now. `scripts/size-check.mjs` holds 100 KB, and the gate still fails past it. Everything else in section
  4.12 stands.
- **The core stays at 16 KB:** it is what every page with a form loads before its visitor touches it, 2.53 KB today.
  Features still load on demand, so the 100 KB is the most any one form can ask for, not what a page loads. ADR-0116's
  count stands too: every feature and the largest language's words. A page with a form still scores in Lighthouse as the
  page without it (16.2).
- **The room is a ceiling, not a target.** A runtime feature still keeps its code clear and loads only where a form uses
  it, and the size check prints the runtime's size at every build, so growth stays in sight.
- **Step 8.6b resumes** from `wip/8.6b-resume-control`, with every check it had, once 19.1 is ticked (its own checks have
  passed) and before 20.1's clean-clone verification, so the verified build holds it.

## Consequences

- No step waits on the runtime's budget any more: 8.6b is open again, and the question in `docs/OWNER-TODO.md` is marked
  decided.
- The owner set the number for now: past 100 KB is theirs to decide again, and a step that would cross it goes to
  `docs/OWNER-TODO.md` as before.
