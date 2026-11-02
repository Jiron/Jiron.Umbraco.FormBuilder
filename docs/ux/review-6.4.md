# Step 6.4: the design skills' review of the runtime

ROADMAP 6.4 enhances the website's forms: errors on the page, sending, the notices, a redirect, a form that changed meanwhile,
uploads, the signature, rows and moves. Per ADR-0019 and ADR-0020 the three design skills were loaded before these were built, and
ran on the result here. The owner answers no questions during the run (CLAUDE.md rule 8), so every choice a skill would ask about
was decided here.

## What was looked at

`tests/e2e/specs/website/runtime-states.spec.ts` walks every state on the demo, in Edge through Playwright, at 375 and 1440 px, in
`default` light, dark and right to left, `bootstrap5` light and dark, and `unstyled`. Axe runs on each state, nothing may run past
the page, and with `JFB_REVIEW_SHOTS` set each state is also a picture. A selection is kept here:

| State | Shot |
| --- | --- |
| The page's errors, dark, 375 px | [shot](review-6.4/default-dark-375-errors.png) |
| A send on its way, 375 px | [shot](review-6.4/default-light-375-busy.png) |
| A send that failed, 375 px | [shot](review-6.4/default-light-375-notice-failed.png) |
| Too many sends, dark, 1440 px | [shot](review-6.4/default-dark-1440-notice-wait.png) |
| The form changed meanwhile, 375 px | [shot](review-6.4/default-light-375-form-changed.png) |
| A redirect's countdown, 375 px | [shot](review-6.4/default-light-375-redirect.png) |
| The same, `bootstrap5`, 1440 px | [shot](review-6.4/bootstrap5-light-1440-redirect.png) |
| Files up, stopped and going, 375 px | [shot](review-6.4/default-light-375-uploads.png) |
| The same, right to left | [shot](review-6.4/default-rtl-375-uploads.png) |
| The same, `bootstrap5` dark | [shot](review-6.4/bootstrap5-dark-375-uploads.png) |
| The same, `unstyled` | [shot](review-6.4/unstyled-light-375-uploads.png) |
| A signature drawn, 1440 px | [shot](review-6.4/default-light-1440-signature-drawn.png) |
| A signature typed, `bootstrap5`, 375 px | [shot](review-6.4/bootstrap5-light-375-signature-typed.png) |
| A ranking moved, "Other", a cascade and a counter past its limit | [shot](review-6.4/default-light-375-moves-and-followers.png) |
| The same, right to left | [shot](review-6.4/default-rtl-375-moves-and-followers.png) |
| The same, `bootstrap5` | [shot](review-6.4/bootstrap5-light-375-moves-and-followers.png) |
| Rows at their most | [shot](review-6.4/default-light-1440-rows-at-their-most.png) |

The right-to-left pictures force the direction on English words, so a sentence's full stop or question mark lands on the wrong side
(".you", "?How"). That is how the browser reads English in that direction, and not something a right-to-left language shows.

`tests/e2e/specs/website/kitchen-sink-by-keyboard.spec.ts` answers and sends the specimen, a field of every built-in type on two
pages, from the top of the page to the stored entry, with Tab, Space, Enter, the arrows and typing alone.

## What changed

| What | Before | After | Found by |
| --- | --- | --- | --- |
| A row added in place | its required fields read "(optional)": a row yet to come had no state of the rules | it requires what the definition requires, and the rules decide the rest (ADR-0154) | looking at the rows |
| A cascading radio in `bootstrap5` | the first radio stood apart from its label: the template behind the legend broke the site's `legend + *` | the template stands behind the list (ADR-0154) | looking at the cascade |
| "Stay here" on `bootstrap5`'s success message | the link colour on the alert, below 4.5:1 | the alert's own link colour, as its links have | axe on the redirect |
| Radio buttons, boxes, text boxes, lists and the pad in `bootstrap5` | Bootstrap's border, about 1.3:1, below WCAG 1.4.11 | `--bs-tertiary-color`, 3:1 and more in both colour modes (ADR-0108) | looking at `bootstrap5`, now tested |
| A counter's words | "Up to 2000 characters", then "1,964 characters left" | "Up to 2,000 characters": the page's numbers from the first word on | looking at the counter |

The review's own spec had one fault worth keeping in mind: waiting for every animation to end waited on the busy button's spinner,
which runs for as long as the send, so the "busy" picture and its axe run showed the send's timeout instead. It now waits only for
what ends.

## design-taste-frontend

The rules that apply to a form on someone else's site: every state of the full cycle, a button's contrast, one accent, one shape.

- **Full cycle.** Loading is the spinner in the pressed button, which keeps its size. Empty is the server's own words, in a counter
  or a list with nothing to offer yet. Errors stand at their fields and in the summary. Nothing here needs a skeleton: the form is
  on the page before the logic comes.
- **Contrast.** The check found "Stay here" on `bootstrap5` and the controls' borders in that theme, both changed above. The
  `default` theme's buttons hold in both schemes.
- **Accent and shape.** A notice, a failed row and an error use the one error colour. A failed row holds the same radius as the
  rows beside it, and the themes' own radii stay.

Kept as it is: the `form_changed` notice is neutral and has no icon. Nothing went wrong, the answers are kept, and a warning colour
would say otherwise.

## impeccable

The bundled engine's `detect --json` on `base.css`, `default.css` and `bootstrap5.css` finds nothing (`[]`). The critique's reading,
in Operate mode, as the visitor finishes a task:

- **Say it once.** A counter past its limit says "3 characters too many", and the error below it "Enter at most 2,000 characters".
  These are two facts, how far off and what is allowed, and the first changes with each key while the second stands until it is
  fixed. Both stay.
- **Nothing squashed on a phone.** Files, a failed row with its "Try again", a stacked grid and the ranking's arrows all hold at
  375 px; the spec fails on anything running past the page.
- **The drop hint.** "Drag a file here" stays where a mouse is, and goes on a touch screen (6.4m).
- **The word for a wait.** "This form was sent too often. Wait 90 seconds, then send it again. Your answers are kept." names the
  cause, the time and what became of the visitor's work.

## emil-design-eng

| Before | After | Why |
| --- | --- | --- |
| Waiting for all animations before a state's check | waiting only for those that end | An endless spinner is not a state changing; the test was measuring the send's timeout |
| A row or a move that might animate into place | nothing moves: a row appears, an option changes place, instantly | Add, Remove and the arrows are pressed many times in a row, often by keyboard, where motion only delays the next press |
| A busy button that might swap its words and change its width | the spinner takes the words' place in the same box | The button stays under the finger, and a second press lands on the same spot |

Kept as it is: the summary's and the notice's fade come from the theme's transition from `@starting-style`, stop under reduced motion
(`keyboard-pass.spec.ts`), and take the focus at once, so nothing waits on them. A failed row changes colour without a shake: the
message and the focus carry it, and a repeated failure should not get louder.
