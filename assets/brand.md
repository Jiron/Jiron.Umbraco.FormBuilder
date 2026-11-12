# Jiron Form Builder's brand

Jiron Form Builder is a form builder for Umbraco: editors build forms, visitors send them, and the site's team works
with what arrives. The mark says that in two moves, and nothing more.

## The mark

A form card with two fields, and in the place of its submit button a check mark:

- **The card,** evergreen, is the form. Its corners are round, as a card on a page is.
- **The two fields** are cut out of the card, not painted white, so the mark sits on any background, light or dark.
- **The check mark,** marigold, stands where the form's button would be, at the start of the last row, as the default
  theme puts the primary button. It is the form sent, and the entry safely kept.

The shapes are simple on purpose: one rounded square, two rounded bars and one stroke. They are drawn on a 64 unit
grid with the bars on whole pixels at 32 px, where the fields are still two lines and the check still a check. There
is no Umbraco logo and no "U" in it: Jiron Form Builder is an Umbraco package, not Umbraco's.

## Colours

Two brand colours and one neutral. The evergreen is the default theme's accent, the colour a visitor's form gives its
button and its chosen answers, so the mark and the forms it makes share it. The ink is the one the owner's other
Jiron package uses beside its mark, so the family reads as one.

| Name | Hex | Use |
| --- | --- | --- |
| **Evergreen** | `#0F6E5C` | The card; the default theme's accent (`--jfb-color-accent`) |
| **Marigold** | `#F5BF4F` | The check mark, and only the check mark |
| **Ink** | `#16181D` | The one-colour mark, and the name next to the mark |

On white, evergreen has a contrast of 6.2:1, enough for text (WCAG AA), and the ink 17.8:1. The marigold check sits on
the evergreen card at 3.7:1, enough for graphics (WCAG 1.4.11 asks 3:1), and never on the page itself, where it would
have 1.7:1. On a near-black background (`#15171C`) the card keeps 2.9:1; where the mark must stand out on a dark
ground, use the one-colour mark in white.

## Files

| File | What it is |
| --- | --- |
| `logo.svg` | The mark in colour; transparent background |
| `logo-mono.svg` | The mark in one colour: `currentColor`, the ink when nothing sets a colour, with the fields and the check cut out. Use it for print, stamps and anywhere one colour is all there is |
| `icon-section.svg` | The backoffice icon: the mark as a 24 px line icon in Umbraco's style (a 1.75 stroke in `currentColor`). The backoffice shows it as `icon-jfb`, beside the form picker in Umbraco's list of property editors |
| `icon-{512,256,128,64}.png` | The colour mark rendered, for the NuGet package (`icon-128.png`), the Marketplace and documents |

The PNGs are rendered from `logo.svg` with Playwright, which the browser tests already use, so the repository needs
no image library:

```bash
node scripts/icons.mjs
```

It also draws a preview sheet in `.local/icons/preview.png`: the mark at 128 and 32 px on light and dark and beside the
name, the one-colour mark in ink and in white, and the backoffice icon at 48 and 24 px. Look at it after every change
to the artwork. `scripts/tests/icons.test.mjs` checks every PNG's size and transparency, that the mark uses only the
colours above, and that no Umbraco logo or name is in the artwork.

## Using the mark

- **Clear space:** keep a margin of at least a quarter of the mark's width around it.
- **Smallest size:** 32 px on screen, 10 mm in print. Below that, use the name alone, or the backoffice icon at 24 px.
- **Next to the name:** "Jiron" or "Jiron Form Builder" in the ink, or white on dark, set in the UI font, bold, to the
  right of the mark, its height about half the mark's.
- **Do not:**
  - recolour the card or the check, or swap them;
  - put the marigold check on its own, outside the card;
  - add an outline or a shadow;
  - put the mark on a busy photo;
  - rotate or stretch it;
  - combine it with Umbraco's logo.
