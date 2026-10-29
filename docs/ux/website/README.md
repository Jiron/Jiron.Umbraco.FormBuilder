# Website form design (ROADMAP 2.3, revised in 2.4)

The default theme of the forms visitors fill in on a customer's site, drawn as mock HTML and CSS with no build step.
The stylesheets in `assets/` are the theme itself: step 6.3 ships `theme.css` as the `default` theme and `base.css`
as the functional base rules (ADR-0015, ADR-0022). The tokens are listed in [`../tokens.md`](../tokens.md), and
[`DESIGN.md`](../../../DESIGN.md) says how the theme is meant to look and move.

## The pages

Two forms on a stand-in host site (a public library, `assets/site.css`), whose font and page the form inherits on
purpose: the theme brings no font of its own.

| Page | What it shows | States |
| --- | --- | --- |
| `book-details.html` | page 1 of 3 of a booking: step progress, the ledger, "(optional)" marks, yes/no buttons, a field a rule shows | `default`, `errors` (the error summary and inline errors), `focus` (the keyboard on a field), `answered` (every answer given, "Yes" chosen, the card number shown), `loading` (the headless element's skeleton) |
| `book-session.html` | page 2: choice cards with places left (one full), a row of radios, a card-style group with a repeater row, a text area with its counter, a dropdown | `default`, `errors` (no workshop chosen), `guests` (the repeater at its limit) |
| `book-rtl.html` | page 2 on an Arabic site, right to left: what the theme mirrors | `default` |
| `book-check.html` | page 3: the answers to check, each with "Change", then the consent | `default`, `errors`, `sending` (Send busy), `failed` and `limited` (not sent: a notice above the buttons), `changed` (the form was published again meanwhile), `full` (the chosen workshop filled up) |
| `book-sent.html` | the outcome: the editor's message, the reference on its own line, then the copy of the answers with "Print" | `default`, `verify` (confirm your address first), `redirect` (a countdown with "Go now" and "Stay here") |
| `book-verify.html` | the page the verification link opens, served by the package on its own (`assets/standalone.css`) | `default` (one button confirms), `confirmed`, `expired` (send a new link) |
| `book-closed.html` | a notice in place of the form | `default` (closed), `opens` (not open yet), `full`, `members` (sign in first), `sent-before` (one entry per member) |
| `apply-room.html` | page 3 of 4 of a room hire form: bar progress, the upload with four files (done, uploading, refused, stopped), the signature, "Save and continue later" | `default` (drawn), `typed` (the name typed instead), `errors` (too many files, no signature), `save` (where to send the link), `saved`, `resumed` (back through the link), `expired` (the link is too old) |
| `fields.html` | the specimen: every field type and state in one form, then a German six-page form that marks required questions with an asterisk | `default`, `alternate` (the upload without JavaScript, a file dragged over, a list with no options, a failed captcha check), plus forced colours |

`assets/placeholder.svg` stands in for the photos an editor would add to picture choices and image fields.

## Rendering and checking

```bash
node scripts/website-mocks.mjs --check
```

This renders every state of every page with Playwright in Edge at 375 px (as a touch phone), 768 px and 1440 px, on
the host site's light and dark design, into `png/` (`page[--state]--width[--dark].png`), plus the forced-colours
shots (`--forced`). `--widths=480,600,720,1024` checks every state at other widths without writing pictures. With `--check` it also measures each shot: WCAG 2.2 AA contrast for every text and control, a focus
ring on everything focusable, 44 px targets, token-only colours, corners, sizes and shades, the 4 px grid, no
dashes, no overflow, axe-core, and once per run the stylesheets (scoped to `.jfb-form`, logical properties only,
within 10 KB gzip). A filter renders one page: `node scripts/website-mocks.mjs --check book-check`. The shots are
taken with reduced motion, and the checker waits for the fades that start on load to end.

`assets/mock.js` only sets up the pictures (`?state=`, `?scheme=dark`, `?dir=rtl`): showing a state's elements,
filling answers (`data-mock-fill`, and `data-mock-answer` for the `answered` state), showing what a rule would show
(`data-mock-reveal`), marking errors as the server would, emptying or busying controls per state
(`data-mock-clear`, `data-mock-busy`, `data-mock-drop`), drawing a sample signature, and folding step names that do
not fit (as the runtime does). The runtime of step 6.4 does the real work.

## What step 6 takes from here

The markup follows the contract of ADR-0015. The mocks add these classes and attributes, which steps 6.2 and 6.3
should adopt:

- **Structure:** `jfb-title` and `jfb-intro` on the page title and intro; `jfb-fields` around a group's fields;
  `jfb-group-card` for the card style; `jfb-mark` for "(optional)" or the asterisk; `jfb-counter` (with
  `data-jfb-over` past the limit); `jfb-places` for places left.
- **Progress:** `jfb-progress-steps` (an `ol` with `aria-current="step"` and `data-jfb-done`) or `jfb-progress-bar`
  (a `progress` element), each with `jfb-progress-text`.
- **Choices:** `jfb-choices-horizontal`, `-cards`, `-buttons` (yes/no), `-scale`, `-rating`; `jfb-scale` and
  `jfb-ends` around a scale; `jfb-switch` (with `role="switch"`) on a yes/no checkbox; `jfb-matrix` with
  `jfb-matrix-head` for a grid; `jfb-ranking` for a ranking with move buttons; an option's description as a
  `jfb-help` after its label in the `jfb-choice`, which its input names in `aria-describedby` (added in 6.2d).
- **Other parts:** `jfb-affix` (prefix and suffix); `jfb-select` around a single dropdown for the theme's chevron;
  `jfb-range` (a slider and its `output`); `jfb-output` (a calculated value); `jfb-parts`, `jfb-part` and
  `jfb-part-wide` (name and address); `jfb-repeater` (the rows and Add, with the field's id) and `jfb-row` (a
  fieldset per row with `data-jfb-row`, ADR-0098); `jfb-signature`, `jfb-signature-pad` and
  `jfb-signature-tools`; `jfb-heading`, `jfb-text`, `jfb-divider` and `jfb-image` for content.
- **Files:** `jfb-upload` (`data-jfb-dropzone`; `data-jfb-drop` while a file is dragged over it), and
  `jfb-files` / `jfb-file` with `data-jfb-busy` while uploading and `data-jfb-error` when refused. Without
  JavaScript the native file input shows. The runtime adds `data-jfb-enhanced` and the two label buttons "Choose
  files" and "Take a photo"; the camera button and its input (`jfb-upload-camera`, `capture`) show only on touch
  screens, where "Drag files here" is hidden.
- **The default button:** a page with other submit buttons before Next or Send (Remove, Change) starts with a
  visually hidden copy of its Next or Send button (`tabindex="-1"`, `aria-hidden="true"`), so Enter in a field
  means Next or Send.
- **Single choices with help:** a single checkbox or switch puts its help after the choice, aligned with the label.
- **The typed signature:** the field's legend is the only visible label; the name input's own label is visually
  hidden, and the help says that typing counts as signing.
- **The outcome:** `jfb-outcome` takes the focus. The entry's reference is rendered by the view on its own line
  (`jfb-reference`, the value in `strong`), so an editor's message need not repeat it; the copy of the answers reuses
  `jfb-summary` without Change.
- **Page changes:** the runtime sets `data-jfb-direction` (`next` or `back`) on the form while a new page comes in,
  so the page slides in 8 px from that side (mirrored right to left; with reduced motion it only fades).
- **Step names that do not fit:** after layout and on every resize, the runtime checks whether a word of a step name
  is wider than its step (long compounds where the browser cannot hyphenate) and then sets `data-jfb-compact` on
  `.jfb-progress-steps`, which shows "Step 2 of 6" with short bars and the page title. Without JavaScript the theme
  folds them by count and width alone: names show from 480 px for up to four steps, from 720 px for up to six.
- **Sending:** while the answers are on their way the runtime sets `data-jfb-busy` and `aria-disabled="true"` on the
  primary button (it stays focusable and keeps its size; a second press does nothing) and the live region says
  "Sending your answers". A refused or failed send puts a `jfb-notice jfb-notice-error` just above the buttons, with
  `role="alert"`, and moves the focus to it; the answers stay. `form_changed` puts a `jfb-notice` where the error
  summary goes and moves the focus there (ADR-0004). A choice that filled up meanwhile is an ordinary error: on the
  summary page the error summary links to that answer's Change button, and the row shows the error.
- **Notices:** `jfb-notice` stands in place of the form (closed, not yet open, full, members only, sent before,
  expired link), above it (the form has changed, welcome back) or just above the buttons (not sent, save for later,
  saved). A notice just above the buttons lines up with them. `jfb-notice-error` is the red variant for a send that
  did not go through.
- **Save and continue later** (8.6): a link button after Back. It opens a panel in place of the buttons: the email
  address, what is not kept (files, the signature, consents), and Send the link. Coming back through the link shows
  a "Welcome back" notice; an expired link shows a notice in place of the form, with Start again.
- **The outcome:** while the visitor must still confirm their address, the outcome carries `data-jfb-pending`, which
  shows a letter instead of the tick. A redirect after a delay shows where it goes and when, with "Go now" and "Stay
  here" (WCAG 2.2.1); the countdown is not announced each second, only once.
- **The verification page** (`GET verify?t=`, 8.5) is served by the package outside the site's templates: a plain page
  in the system font with the site's name and the form's theme (`jfb-scheme-auto`), one button that posts the
  confirmation, and on an expired link one button that sends a new one.
- **Uploads:** a file whose upload stopped shows "Try again" in its row (ADR-0006); a file being uploaded shows its share
  in words beside the bar. Without JavaScript the drop frame is solid and the drop text is hidden, since nothing can
  be dropped on it.
- **The signature:** "Clear" turns into "Undo clear" until the next stroke, so a slip of the finger loses nothing.
- **Other parts:** `jfb-other` holds the text an "Other" choice asks for, lined up with the choice labels;
  `jfb-captcha` holds the notice and "Load the check" button of a third-party captcha in the `explicit` consent
  mode (ROADMAP 4.10), and its error after `captcha_failed`; the server renders the place empty and the theme hides
  it until something fills it (added in 6.2e); `jfb-skeleton` is the headless element's placeholder
  while the definition loads (`aria-busy` on the form and a `role="status"` text), pulsing unless motion is reduced.
  A move button at the end of a ranking is `aria-disabled`, so it stays in the tab order.
- **Colour scheme:** a form with no `jfb-scheme-*` class takes the light tokens. When the ground behind such a form is
  dark (the site is dark and did not opt in), the runtime adds `jfb-scheme-dark`, so labels never sit dark on dark
  (ADR-0022). Without JavaScript the class must be set; the theme editor and the docs say so.
