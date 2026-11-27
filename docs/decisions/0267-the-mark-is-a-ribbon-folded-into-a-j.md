# 0267. The mark is a ribbon folded into a J

- Status: accepted (the owner, 2026-11-27); replaces the concept of roadmap step 17.1's mark and amends
  [ADR-0245](0245-the-mark-takes-ligatas-blue.md) (which part is blue)
- Date: 2026-11-27
- Roadmap step: 17.1 (the logo and icon), after the release of 17.0.0

## Context

Step 17.1 drew the mark as a rounded card with two fields cut out and a check mark in the submit button's place, and
[ADR-0245](0245-the-mark-takes-ligatas-blue.md) gave the card Ligata's blue. 17.0.0 went to NuGet and the Marketplace
with it. Before the next release the owner wrote that the mark was "a tiiiny bit too simplistic", the kind of tile any
generated logo has, and asked for more ideas. Six sketches away from the tile went to the owner
(`.local/icons/ideas-2.png`): a ribbon J, two pages, bricks, drag and drop, the inbox, a merge tag. The owner chose the
first: "Icon the ribbon please". The owner's message beats the plan (CLAUDE.md rule 8), and the mark is the owner's to
choose.

## Decision

1. **The mark is a strip of ribbon folded into a J.** "Jirón" is Spanish for a strip of cloth, and a form's field is a
   strip too, so the letter is made of the product's own material. It stands free: no tile, no card, no check mark.
   - **The bar** is the strip's far side, in a deep blue (`#1637A8`, Ligata's blue in shade), its free end cut to a
     swallowtail.
   - **The fold** turns the strip at the bar's other end: from there it shows its near side in Ligata's blue
     (`#2457F5`, as ADR-0245 chose) and comes down as the stem and round the hook.
   - **The end** of the hook is dipped in marigold (`#F5BF4F`).
2. **Drawn from the sketch, not copied from it.** Against the sketch the bar starts where the hook does, the
   swallowtail and the fold are both cut at 45°, and every corner sits on an even unit of the 64 unit grid (the sketch
   had its stem on half pixels at 32 px). A fold as paper makes it, with the outer corner cut off, was drawn and looked
   at beside the sketch's; it read as a page's turned corner, and the sketch's square corner kept the letter's outline
   whole, so the sketch's stays. A lighter bar that keeps 3:1 on a dark ground was looked at too; it reads as a second
   colour, not as the strip's other side.
3. **The one-colour mark** is the same outline in `currentColor`, with a thin line cut out where the colour mark
   changes colour: at the fold and at the end.
4. **The backoffice icon** (`icon-jfb`, `assets/icon-section.svg`) is the ribbon in outline with a line at the fold and
   the end solid, in Umbraco's line style. Six drawings of it were looked at at 24, 20 and 16 px; without the solid end
   or without the swallowtail it lost what makes it this mark.
5. **Contrast, said plainly** (`assets/brand.md`): the blue holds 5.6:1 on white and 3.2:1 on near-black; the deep bar
   9.8:1 on white and 1.8:1 on near-black, where the stem and the hook carry the letter; the marigold end is an accent
   at 1.7:1 on white. A logo is outside WCAG's contrast criteria; where the whole mark must stand out on a dark ground,
   the one-colour mark in white is the one to use.

## The checks, which replace 17.1's and are stronger

Step 17.1 asked that the PNGs exist, that the 128 px icon is looked at, and that the brand document is committed; its
test checked sizes, a transparent corner, the colours of the SVG and that the one-colour mark cut out the same fields.
`scripts/tests/icons.test.mjs` now also decodes every PNG and reads its pixels: the bar, the fold, the stem, the hook
and the end have their colour at every size, the swallowtail and the four corners are transparent, the SVG's parts are
painted in the order bar, stem, end, every coordinate is an even unit, and the one-colour mark is that outline with its
two cuts. The backoffice's own test still holds `icon-jfb` equal to `assets/icon-section.svg`. The preview sheet was
looked at (`.local/icons/preview.png`), and the icon in the running backoffice.

## Consequences

- `assets/brand.md` describes the ribbon J and names four colours: Blue, Deep blue, Marigold and Ink.
- The packages from 17.0.1 on carry the new icon; 17.0.0 keeps the old one on nuget.org, as a published version cannot
  change.
- No visitor-facing colour changes: the default theme's accent stays evergreen, as ADR-0245 decided.
- The texts of roadmap step 17.1 stay as the record of what was built then, with a line that points here.
