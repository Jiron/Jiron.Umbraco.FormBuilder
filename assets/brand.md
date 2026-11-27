# Jiron Form Builder's brand

Jiron Form Builder is a form builder for Umbraco: editors build forms, visitors send them, and the site's team works
with what arrives. The mark is the name's first letter, made of the thing a form is made of: a strip.

## The mark

A strip of ribbon folded into a J (the owner's pick, 2026-11-27, ADR-0267):

- **The bar,** deep blue, is the strip's far side. Its free end is cut to a swallowtail, as a ribbon's is.
- **The fold** turns the strip at the bar's end: there it shows its near side, blue, and comes down as the letter's
  stem and round its hook.
- **The end,** marigold, is the hook's last stretch, dipped in colour: the place a strip is filled in.

"Jirón" is Spanish for a strip of cloth, and a form's field is a strip too. The mark stands free, with no tile behind
it, so it sits on any background. It is drawn on a 64 unit grid with every corner on an even unit, so each edge falls
on a whole pixel at 32 px, where the bar, the stem and the end are still three parts. There is no Umbraco logo and no
"U" in it: Jiron Form Builder is an Umbraco package, not Umbraco's.

## Colours

Three brand colours and one neutral. The blue is Ligata's, the colour of the owner's company site (its `--accent`), so
the mark reads as part of that family; the owner chose it on 2026-11-13 (ADR-0245). The deep blue is the same hue in
shade, for the side of the ribbon turned away. The ink is the one the owner's other Jiron package uses beside its mark.
The forms themselves keep their own colours: the default theme's accent stays evergreen (`--jfb-color-accent`,
`#0F6E5C`), as a visitor's form belongs to the site that shows it, not to us.

| Name | Hex | Use |
| --- | --- | --- |
| **Blue** | `#2457F5` | The stem and the hook: Ligata's blue |
| **Deep blue** | `#1637A8` | The bar, and only the bar |
| **Marigold** | `#F5BF4F` | The hook's end, and only the end |
| **Ink** | `#16181D` | The one-colour mark, and the name next to the mark |

On white, the blue has a contrast of 5.6:1 and the deep blue 9.8:1, the ink 17.8:1. The marigold end has 1.7:1 on
white: it is an accent that lies against the blue, and the letter reads without it. On a near-black background
(`#15171C`) the blue keeps 3.2:1, enough for graphics (WCAG 1.4.11 asks 3:1), and the marigold 10.6:1; the deep bar
falls back to 1.8:1 there, so the stem and the hook carry the letter. Where the whole mark must stand out on a dark
ground, use the one-colour mark in white.

## Files

| File | What it is |
| --- | --- |
| `logo.svg` | The mark in colour; transparent background |
| `logo-mono.svg` | The mark in one colour: `currentColor`, the ink when nothing sets a colour, with a thin line cut out at the fold and at the end. Use it for print, stamps and anywhere one colour is all there is |
| `icon-section.svg` | The backoffice icon: the mark as a 24 px line icon in Umbraco's style (a 1.75 stroke in `currentColor`), the ribbon in outline, its end solid. The backoffice shows it as `icon-jfb`, beside the form picker in Umbraco's list of property editors |
| `icon-{512,256,128,64}.png` | The colour mark rendered, for the NuGet package (`icon-128.png`), the Marketplace and documents |

The PNGs are rendered from `logo.svg` with Playwright, which the browser tests already use, so the repository needs
no image library:

```bash
node scripts/icons.mjs
```

It also draws a preview sheet in `.local/icons/preview.png`: the mark at 128 and 32 px on light and dark and beside the
name, the one-colour mark in ink and in white, and the backoffice icon at 48 and 24 px. Look at it after every change
to the artwork. `scripts/tests/icons.test.mjs` checks every PNG's size and transparency, that the mark uses only the
colours above, that it is drawn on even units, and that no Umbraco logo or name is in the artwork.

## Using the mark

- **Clear space:** keep a margin of at least a quarter of the mark's width around it.
- **Smallest size:** 32 px on screen, 10 mm in print. Below that, use the name alone, or the backoffice icon at 24 px.
- **Next to the name:** "Jiron" or "Jiron Form Builder" in the ink, or white on dark, set in the UI font, bold, to the
  right of the mark, its height about half the mark's.
- **Do not:**
  - recolour the bar, the stem or the end, or swap them;
  - put the mark on a tile, or add an outline or a shadow;
  - mirror it, or cut the swallowtail or the end away;
  - put the mark on a busy photo;
  - rotate or stretch it;
  - combine it with Umbraco's logo.
