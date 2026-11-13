# 0245. The mark takes Ligata's blue

- Status: accepted (the owner, 2026-11-13)
- Date: 2026-11-13
- Roadmap step: 17.1 (the logo and icon)

## Context

Step 17.1 drew Jiron Form Builder's mark as an evergreen card with two fields cut out and a marigold check, the
evergreen being the default theme's accent (`assets/brand.md`). During the phase review the owner wrote: "for form
builder logo btw take the blue used in ligata.ch". The owner's message beats the plan (CLAUDE.md rule 8).

## Decision

1. **The card is Ligata's blue, `#2457F5`,** the `--accent` of the owner's company site, read from that site's own
   style tokens, where every token file gives the same value. The two fields stay cut out and
   the check stays marigold (`#F5BF4F`): on the blue card it has 3.3:1, enough for graphics (WCAG 1.4.11 asks 3:1). The
   blue has 5.6:1 on white and 3.2:1 on near-black, so the colour mark now holds on a dark ground as well.
2. **Only the mark changes.** The owner named the logo. The default theme's accent stays evergreen (`#0F6E5C`): it is a
   visitor-facing colour with its own contrast audits, and a visitor's form belongs to the site that shows it. The
   one-colour mark (ink, `currentColor`) and the backoffice's line icon (`currentColor`) are unchanged; the PNGs are
   rendered again from `logo.svg`, and the phone screenshot's ground takes a blue tint instead of a green one.
3. **A test pins the choice:** `scripts/tests/icons.test.mjs` fails unless the card is `#2457F5`.

## Consequences

- `assets/brand.md` names the colours Blue, Marigold and Ink, and says why the mark and the default theme differ.
- Should the owner want the forms' default accent to follow, that is a change of the default theme (its tokens, its dark
  scheme and its contrast checks) and of the screenshots, done as its own change.
