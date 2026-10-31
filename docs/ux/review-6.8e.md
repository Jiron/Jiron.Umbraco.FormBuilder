# Step 6.8e: the keyboard and screen-reader-name pass

ROADMAP 6.8e walks every kind of control on the demo's specimen with the keyboard alone and reads what a screen reader
gets, in the default, `unstyled` and `bootstrap5` themes, light and dark, in forced colours where it applies, with
JavaScript and without. The pass found nothing wrong in the themes' stylesheets, so this step changes no rule of them;
the three design skills stayed loaded from 6.8d, whose review holds their sections.

## What the pass checks

| What | How it is held |
| --- | --- |
| A visible focus on 32 kinds of control, also in forced colours | a control's field changes its pixels when the control takes the focus from the keyboard (`keyboard-pass.spec.ts`) |
| Tab through the form | each field reached in the page's order, every stop on the page, never the honeypot, and Shift+Tab goes back |
| The keyboard alone | Space and an arrow answer radio buttons, cards, yes or no, a rating and a scale; Space a box and the switch; an arrow the slider; Enter a ranking's move, a row's Add and Remove, a summary's link |
| Enter in a text box | sends a form, with JavaScript and without; on a form with a ranking it asks for the next page and runs no move |
| Role, name, description | read from Edge's own accessibility tree for every kind of control and group (`names-pass.spec.ts`) |
| Required and invalid | exactly the required controls carry `required`, nothing is marked invalid on a first render, and after a refused send each refused control is invalid and described by its error, with no group invalid itself |
| `autocomplete` | present on the personal fields: the name and its parts, email and its repeat, phone, web address, the address's parts, country, a signature's typed name |
| A summary's link | lands with the label clear of a sticky header in every theme, with JavaScript and without (`focus-and-landing.spec.ts`) |
| Reduced motion | a recorder of every running animation finds no slide or rise, and without reduced motion it does find one |
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
- **`bootstrap5`'s own radio buttons, boxes and text boxes keep Bootstrap's focus shadow.** It is visible, and it is
  the site's Bootstrap; forced colours replace it with an outline (6.8d, ADR-0108).
- **The specimen's email asks for its repeat,** so the pass and every theme's axe runs cover that control as well.
