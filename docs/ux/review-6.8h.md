# Step 6.8h: the second reading's fixes to the themes and the pass

ROADMAP 6.8h fixes what a second reading of 6.8d and 6.8e found: where a post and a link land, a refused rating that
stays in the error's colour without JavaScript, the focus of `bootstrap5`'s own controls, and a pass that claimed more
than it checked. Per ADR-0019 and ADR-0020 the three design skills' rules applied to the themes' changes. The owner
answers no questions during the run (CLAUDE.md rule 8), so every choice a skill would ask about was decided here.

## What changed

| What | Before | After |
| --- | --- | --- |
| A picture choice's scroll margin | up to a card as wide as the form, which on a wide form or a short window pushed the control below the window | never more than the window less the control's room (`100svh - 4rem`) |
| The form itself | no scroll margin, so a post without JavaScript put its summary, title or outcome under a sticky header | the same margin as what is inside it |
| A summary's link with JavaScript | scrolled to the label, which left the control below a picture taller than the window (found by the new test, in `unstyled`) | still scrolls to the label, whatever stands between them, and a plain focus brings a control still out of view in (6.8i: scrolling to the control alone put a long legend or a grid's under a sticky header) |
| A refused rating without JavaScript | the stars after the chosen one stayed in the error's colour until the next post | the error's colour only until a star is chosen |
| `bootstrap5`'s own controls | Bootstrap's focus shadow, about 1.4:1, below WCAG 1.4.11 | the theme's 2 px outline (ADR-0108) |

## What the pass now holds

| It claimed | What it did | What it does now |
| --- | --- | --- |
| A control shows focus | one changed pixel passed | a ring's worth of pixels, while two shots without the focus match |
| Each refused control is invalid | read a tree that reports five of them anyway | reads the markup's mark beside the tree, and checks the refused name too |
| Never the honeypot | the walk stopped before reaching it | it goes one Tab past the main button, and holds `tabindex="-1"` |
| Every control | not Back, a row's Remove or a summary's link | those too |
| Reduced motion stops every slide and rise | added no row and toggled no switch | does both, and records across the page load |
| Enter runs no move on a form with a ranking | with JavaScript only | without it too |
| Every theme and scheme | Tab, the keyboard, reduced motion and right to left in light only | in both |

## What was looked at

The demo's specimen on the e2e host, in Edge through Playwright. The specs measure the default, `bootstrap5` and
`unstyled` themes at 1920 by 768, 1280 by 500 and 667 by 375 for the landings, and at 1280 by 900 for the focus; these
shots are the ones looked at.

| Shot | What it shows |
| --- | --- |
| [a text box's focus, `bootstrap5`](review-6.8h/bootstrap5-light-text.png) | the outline 2 px outside the box, in the emphasis colour |
| [the same, dark](review-6.8h/bootstrap5-dark-text.png) | the dark mode's lighter emphasis colour |
| [a card at 1280 by 500](review-6.8h/default-short-window-card.png) | the legend and the card's radio button both in the window |
| [a picture in `unstyled` at 1280 by 500](review-6.8h/unstyled-short-window-picture.png) | a picture's full height, the focused radio button still in the window |
| [a post's landing without JavaScript](review-6.8h/default-post-landing.png) | the form's top well below where a 5rem header ends |
| [a refused rating, one chosen, without JavaScript](review-6.8h/default-refused-rating-chosen.png) | the unchosen stars no longer red, its error still below |

## design-taste-frontend

**The design read:** form controls inside a host site's page, for visitors filling in a form, accessibility first.
**Dials:** variance 3, motion 2, density 5, as for 6.8d. Of its pre-flight the form contrast check and the colour lock
apply: `bootstrap5` gains no colour of its own, as the outline uses the emphasis colour 6.8d chose.

## impeccable

**Mode:** Operate, a refinement of the incumbent themes. `detect` on `base.css`, `default.css` and `bootstrap5.css`: 0
findings. **Drift classified:** all local defects, none a change of direction. **Decided:** the margin's cap uses
`svh`, so it holds with a phone's browser bar showing; `box-shadow: none` on Bootstrap's own controls under focus, so
one state has one mark, and a site's Bootstrap keeps every other colour.

## emil-design-eng

| Before | After | Why |
| --- | --- | --- |
| A shadow and, in forced colours, an outline on Bootstrap's controls | one outline, whose colour forced colours replace | one state, one property, nothing to keep in step |
| A margin that assumes the widest card | a margin capped by the window | a focused control off the screen is worse than a legend off it |
