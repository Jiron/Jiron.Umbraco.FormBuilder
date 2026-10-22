# 0019. Frontend work uses the design-taste-frontend skill

- Status: accepted, extended by ADR-0020 (three design skills)
- Date: 2026-10-22
- Roadmap step: 0.2 (recorded here); applies to every later step that designs, builds or reviews UI

## Context

The owner wrote during the run, on 2026-10-22: use the `design-taste-frontend` skill for frontend work, and run
it again whenever a full design pass happens. CLAUDE.md rule 8 says an owner message beats the plan: record
it, change the roadmap for it without weakening any step, and continue.

Until then the roadmap named the installed design skills only as an option for critiques (§4.13, "Checked,
not assumed"). The skill describes itself as aimed at landing pages, portfolios and redesigns, while most of
Jiron Form Builder's UI is product UI with binding constraints of its own:

- the backoffice must feel native to Umbraco, built with UUI and Umbraco's patterns, with no foreign design
  system (D12);
- every screen follows the calm-screen rules of §4.13 (one primary action, at most three secondary actions,
  say it once, nothing squashed on phones) (D18);
- the website forms meet WCAG 2.2 AA, stay within the §4.12 budgets, load nothing from third-party hosts and
  use no web fonts from CDNs (CLAUDE.md rule 4, D13);
- designs are Jiron Form Builder's own, and other products' screens are never design references (CLAUDE.md
  rule 2).

## Options

1. **Keep the skill as an optional critique tool.** No change to the plan, but it ignores the owner's
   instruction.
2. **Use the skill for every frontend step and rerun it on every full design pass, inside the roadmap's
   binding constraints.** Follows the owner, adds a second, independent eye to every screen, and keeps the
   product native, calm and accessible.
3. **Let the skill's defaults decide the look.** Its defaults (expressive layouts, motion, brand-led
   typography) would conflict with D12, §4.13, §4.12 and rule 4, and could pull in dependencies the
   licence rules do not allow.

## Decision

Option 2.

- **When it runs:**
  - every step that designs or builds UI loads the skill before it starts: the wireframes (2.2), the website
    form design and tokens (2.3), the themes (6.3), the visible parts of the website runtime (6.4), every
    backoffice screen from 9.1 on, the theme and email-layout editors (12.2), the logo (17.1) and the
    screenshots (17.3);
  - every full design pass or UX review runs it again: 2.4, 9.6, 10.4, 11.6, 12.6 and 16.6.
- **How it is used:** its brief inference (the one-line "design read"), its anti-default discipline and its
  pre-flight check. Its own honesty rule, which says to use a product's official design system when the
  brief names one, means UUI and Umbraco's patterns in the backoffice.
- **Precedence:** where the skill and the roadmap disagree, the roadmap's binding constraints win: D12, D13,
  D18 and §4.13, WCAG 2.2 AA, the §4.12 budgets, the Phase 2 tokens, and CLAUDE.md rules 2 and 4. Any
  product or brand the skill names is vocabulary only, never a model to copy.
- **Recording:** every review document records the skill's design read, its findings, what changed because of
  it, and each conflict with the roadmap together with how it was resolved. A UI step's WORKLOG line says that
  the skill ran.

## Consequences

- Each UI step and each review has one more input to apply and to record, which costs some time; the run has
  no deadline, and quality comes first.
- Conflicts are resolved toward the roadmap and written down, so the owner can overrule a specific one later.
- ROADMAP "How to use" gains a rule for this, §4.13 names the skill, and steps 2.2, 2.3, 2.4, 6.3, 9.6 (which
  the reviews 10.4, 11.6 and 12.6 follow), 12.2, 16.6 and 17.1 name it in their work or their "Done when"
  checks.
- Revisit when the owner changes the instruction, or when the skill changes its scope or is no longer
  installed (then record that in WORKLOG and continue with the §4.13 checklist alone).
