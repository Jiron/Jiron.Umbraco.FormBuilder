# Step 6.8e: the keyboard and screen-reader-name pass

ROADMAP 6.8e walks every kind of control on the demo's specimen with the keyboard alone and reads what a screen reader
gets, in the default, `unstyled` and `bootstrap5` themes, light and dark, in forced colours where it applies, with
JavaScript and without. The pass found nothing wrong in the themes' stylesheets, so this step changes no rule of them;
the three design skills stayed loaded from 6.8d, whose review holds their sections.

## What the pass checks

| What | How it is held |
| --- | --- |
| A visible focus on 32 kinds of control, a row's Remove, a summary's link and Back, also in forced colours | a ring's worth of pixels of a control's field changes when it takes the focus from the keyboard, while two shots without the focus are alike (`keyboard-pass.spec.ts`, stronger since 6.8h) |
| Tab through the form | each field reached in the page's order, every stop on the page, past the main button never the honeypot, and Shift+Tab goes back, in both schemes |
| The keyboard alone | Space and an arrow answer radio buttons, cards, yes or no, a rating and a scale; Space a box and the switch; an arrow the slider; Enter a ranking's move, a row's Add and Remove, a summary's link |
| Enter in a text box | sends a form, and on a form with a ranking asks for the next page and runs no move, both with JavaScript and without |
| Role, name, description | read from Edge's own accessibility tree for every kind of control and group (`names-pass.spec.ts`) |
| Required and invalid | exactly the required controls carry `required`, nothing is marked invalid on a first render, and after a refused send each refused control carries the mark, is invalid in the tree and is described by its error, with no group invalid itself. The kinds that are optional on the specimen (an address's parts, "Other", a signature's typed name) are held by `FormRenderingChoicesTests` and `FormRenderingInputsTests` |
| `autocomplete` | present on the personal fields: the name and its parts, email and its repeat, phone, web address, the address's parts, country, a signature's typed name |
| A summary's link | lands with the label clear of a sticky header in every theme, with JavaScript and without (`focus-and-landing.spec.ts`) |
| Reduced motion | a recorder of every running animation, across a new row, the switch, a refused send, a press and a page, finds no slide or rise, and without reduced motion it finds the switch's and a press's |
| Right to left | the stars stand and fill from the right, the switch's knob goes left, the Back arrow turns |

## What was looked at

| Shot | What it shows |
| --- | --- |
| [an email's repeat](review-6.8e/default-repeat.png) | the repeat directly below the address, its own label, the focus ring outside it |
| [a summary's link](review-6.8e/default-summary-link.png) | the link's ring, with the summary's links in the page's order |
| [a grid's cell, `bootstrap5`](review-6.8e/bootstrap5-grid.png) | Bootstrap's own radio focus, unchanged |
| [a file, `unstyled`](review-6.8e/unstyled-file.png) | the browser's own focus ring, with no theme |
| [a rating, right to left](review-6.8e/default-rtl-rating.png) | three stars chosen, filled from the right |
| [the switch, right to left](review-6.8e/default-rtl-switch.png) | on, its knob on the left |

## What the pass settled

- **Edge reports a required radio button, box or select that is empty as invalid from the first render.** This is its
  native constraint validation, not the markup, which writes no `aria-invalid` until a refusal; it does not do so for an
  empty required text box. Writing `aria-invalid="false"` on those controls would hide it, at the price of a mark on
  every required choice in the views and the element, and a runtime that restores it instead of clearing it, past what
  its budget has left. It is left to the browser, as on any page with a native `required`, and the spec holds the
  markup (ADR-0113).
- **`bootstrap5`'s own controls no longer rest on Bootstrap's focus shadow.** This pass kept it as visible; the second
  reading held it to 1.4.11, as about 1.4:1 does not pass, and 6.8h gives them the theme's outline (ADR-0108).
- **The specimen's email asks for its repeat,** so the pass and every theme's axe runs cover that control as well.
