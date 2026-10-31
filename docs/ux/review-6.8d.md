# Step 6.8d: the design skills' review

ROADMAP 6.8d fixes what the phase review found in the website themes: the focus of what `bootstrap5` draws, where an
error summary's link lands under a sticky header, the error look of a refused scale and rating, and two members of the
theme interface nothing reads ([ADR-0108](../decisions/0108-the-bootstrap5-theme.md),
[ADR-0105](../decisions/0105-the-theme-registry-and-the-base-rules.md),
[ADR-0106](../decisions/0106-the-default-theme-ships.md)). Per ADR-0019 and ADR-0020 the three design skills were loaded
before the themes changed and ran on the result. The owner answers no questions during the run (CLAUDE.md rule 8), so
every choice a skill would ask about was decided here.

## What was looked at

The demo's specimen on the e2e host, in Edge through Playwright, with its scale and rating now required so that its
errors page shows both, in the default and `bootstrap5` themes, light, dark and in forced colours, at 1100 and 375 px.

| Shot | What it shows |
| --- | --- |
| [a scale's focus, `bootstrap5`](review-6.8d/bootstrap5-light-focus-scale.png) | the outline in Bootstrap's emphasis colour, 2 px outside the point |
| [a rating's focus, `bootstrap5` dark](review-6.8d/bootstrap5-dark-focus-rating.png) | the same outline in the dark mode's lighter emphasis colour |
| [a ranking's move, `bootstrap5`](review-6.8d/bootstrap5-light-focus-move.png) | the move's outline, within the row, clear of its neighbour |
| [yes or no, forced colours](review-6.8d/bootstrap5-forced-focus-yesno.png) | the outline kept where the old shadow went |
| [a refused rating, default](review-6.8d/default-light-refused-rating.png) | the stars in the error's colour, its words below |
| [a refused scale, default](review-6.8d/default-light-refused-recommend.png) | a scale with words at its ends, each point with the error's border |
| [a refused scale, `bootstrap5` dark](review-6.8d/bootstrap5-dark-refused-recommend.png) | Bootstrap's invalid colour for its dark mode |
| [a link's landing on a phone](review-6.8d/default-phone-landed.png) | the scale's label below where a 5rem header ends, the point focused |

## design-taste-frontend

**The design read:** form controls inside a host site's page, for visitors filling in a form, in a calm and
accessibility-first language, inside the default theme and Bootstrap 5.3's own variables. **Dials:** variance 3, motion
2, density 5. The skill's §13 puts forms outside its landing-page rules, so of its pre-flight only the form contrast
check, the error states and the shape and colour locks apply: the focus outline holds 3:1 and more in every scheme, the
errors stay inline in the one error colour, and `bootstrap5` gains no colour of its own. No em dashes were added.

## impeccable

**Mode:** Operate, `polish`, a refinement of the incumbent themes. The engine asks for a question to the user once;
CLAUDE.md rule 8 overrides it, so this file records the decisions. `detect` on `base.css`, `default.css` and
`bootstrap5.css`: 0 findings. **Drift classified:** the focus shadow a local defect, since the theme drew it with a value
that was never meant to stand alone; the scale's error rule a local defect, as it assumed a level the markup does not
always have; the rating's missing rule a local defect in `default`, which `bootstrap5` already had. **Found by the
inspection, not the review:** without JavaScript a link lands on a picture card's radio button, below its picture, so
the legend ended up above the window. Those radio buttons now keep room for the picture (`67cqi`, 3:2 of a card as wide
as the form), an overestimate on a wide form that only leaves more above the control.

**Decided:** the outline's colour is `--bs-primary-text-emphasis`, not `--bs-primary` (4.5:1 and 3.4:1 with Bootstrap's
defaults, and any light primary a site picks would fall below 3:1) or `--bs-emphasis-color` (black or white, which holds
but drops the site's hue). Bootstrap derives it from the site's primary for each colour mode: 13:1 and 6:1 by default.

## emil-design-eng

| Before | After | Why |
| --- | --- | --- |
| `box-shadow: 0 0 0 .25rem` at 25% for focus on what the theme draws | `outline: 2px solid` the emphasis colour, 2 px outside | about 1.4:1 before, and forced colours drop a shadow |
| `transition: box-shadow .15s` on a drawn button's label | no transition on the focus | a keyboard action never animates; the outline is there at once |
| A checked card's focus repeating its inset shadow beside the ring | the card's shadow left alone, the outline on top | one state, one property, nothing to keep in step |
| `[aria-invalid] > .jfb-choices-scale` | `.jfb-choices-scale > .jfb-choice > [aria-invalid] + label` | the rule follows the mark, whatever wraps the choices |
| A refused rating's stars in the idle border colour | the stars in the error's colour until one is chosen | the error shows where the answer is missing, with no motion added |

## Tokens and the dark scheme

`IFormBuilderTheme.Tokens` and `DarkScheme` are read only by the registry's tests. Their reader is step 12.2's theme
editor, which lists a base theme's tokens with their defaults and offers dark colours only where the theme has a dark
scheme; ADR-0105 names it, and the step now says so.
