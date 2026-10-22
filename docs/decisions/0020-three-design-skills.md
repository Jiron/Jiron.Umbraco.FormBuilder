# 0020. Design work uses three skills

- Status: accepted
- Date: 2026-10-22
- Roadmap step: 1.2b (recorded here); extends ADR-0019 and applies to the same UI steps and reviews

## Context

The owner wrote during the run, on 2026-10-22: use three skills for design passes, `emil-design-eng`,
`design-taste-frontend` and `impeccable`, and ask the owner no questions even where a skill wants the user to
choose a design; decide instead. ADR-0019 already made `design-taste-frontend` part of every UI step and review.
CLAUDE.md rule 8 says an owner message beats the plan: record it, change the roadmap without weakening any
step, and continue.

The three skills look at different things:

- `design-taste-frontend` infers the brief, guards against default-looking output and ends with a pre-flight
  check. It is written for landing pages and portfolios, so its stack defaults (a React framework, a utility
  CSS framework, a motion library, icon libraries, hosted fonts) do not fit Jiron Form Builder.
- `impeccable` works in modes (Operate for task screens, Read for documentation) and offers commands such as
  `shape`, `critique`, `audit`, `polish`, `harden`, `clarify`, `adapt`, `layout` and `typeset`, with a quality
  floor it reads before any UI edit. It keeps its project context in `PRODUCT.md` and `DESIGN.md`, and its
  launcher runs an engine binary that ships inside the installed skill (`bin/windows-x64`) and otherwise
  downloads one. Some of its flows ask the user to pick a direction or a variant, and it can install editor
  hooks.
- `emil-design-eng` covers motion and interaction feel: whether something should animate at all (actions
  repeated many times a day should not), easing, durations under 300 ms, animating only transform and
  opacity, reduced motion, press feedback, and a Before/After/Why table for its reviews.

The constraints of ADR-0019 still bind: the backoffice is native Umbraco (UUI and Umbraco's patterns, D12),
calm screens (§4.13, D18), WCAG 2.2 AA, the §4.12 budgets, nothing loaded from third-party hosts (D13 and
CLAUDE.md rule 4), and designs of our own (CLAUDE.md rule 2). CLAUDE.md rule 5 limits where files are written,
and rule 8 says the owner answers no questions during the run.

## Options

1. **Keep ADR-0019 alone.** Ignores two thirds of the owner's instruction.
2. **Use all three skills, each for what it does best, inside the binding constraints, and take every choice a
   skill would ask about ourselves.** Three independent views on every screen; nothing waits for the owner.
3. **Let each skill run its full default workflow.** Its questions would stall the run (rule 8), its stack
   defaults would add dependencies rule 4 does not allow, and its engine download, hooks and caches would
   write outside this repository (rule 5).

## Decision

Option 2.

- **When:** the same steps as ADR-0019. Every step that designs or builds UI loads all three skills before it
  starts; every full design pass and UX review runs all three again.
- **What each one does:**
  - `design-taste-frontend`: the one-line design read, the anti-default discipline and the pre-flight check,
    as in ADR-0019;
  - `impeccable`: Operate mode for the backoffice and the website forms, Read mode for the documentation and
    the READMEs; `shape` before a new screen is designed, `critique` and `audit` in every review, `polish`
    before a UI step is called done, and `harden`, `clarify`, `adapt`, `layout` or `typeset` where a review
    finds that kind of gap;
  - `emil-design-eng`: every motion and feedback decision, and a Before/After/Why table in every review. In the
    backoffice the default is no added animation, because editors repeat its actions all day and UUI already
    gives the native feedback.
- **No questions:** where a skill asks the user to choose (a direction, a variant, a clarifying question), we
  choose, using the roadmap's constraints, and the review document records the choice and the options that
  were weighed. A choice that shapes more than one screen becomes an ADR.
- **Context files:** `PRODUCT.md` (written in 2.2) and `DESIGN.md` (written in 2.3) are tracked, in our own
  words, and drawn from the roadmap and the token file. The token file stays the source of the tokens;
  `DESIGN.md` points to it rather than repeating values that could drift.
- **The engine:** `impeccable` runs only with the engine bundled in the installed skill. It never downloads an
  engine and nothing is written to a user-level cache; if the bundled engine is missing or fails, the skill's
  own fallback applies (read `PRODUCT.md` and `DESIGN.md` directly) and the review says so. Its editor hooks,
  pins and live browser mode stay off, because they change configuration outside a step's work or wait for a
  person; the reviews run its checks explicitly instead.
- **Precedence:** as in ADR-0019. Where a skill and the roadmap disagree, the roadmap's binding constraints
  win, and the review says so. The skills' stack defaults are not adopted; any library a review suggests goes
  through CLAUDE.md rule 4 like every other dependency. Products and brands a skill names are vocabulary only.
- **Recording:** every review document has one section per skill: the design read and pre-flight result, the
  `impeccable` mode, commands and findings, and the `emil-design-eng` table. A UI step's WORKLOG line names
  the skills that ran.

## Consequences

- Each UI step and review takes longer and writes more down. The run has no deadline, and three views catch
  more than one.
- The owner can overrule any recorded choice later, because each one names its alternatives.
- ROADMAP "How to use", §4.13 and the UI steps named in ADR-0019 now name all three skills, and 2.2 and 2.3
  write the `impeccable` context files. No check was removed: the critique steps that allowed `impeccable` or
  a subagent now require all three skills.
- Revisit when the owner changes the instruction, or when a skill changes its scope or is no longer installed;
  then record that in WORKLOG and continue with the remaining skills and the §4.13 checklist.
