# Website form design (ROADMAP 2.3)

The default theme of the forms visitors fill in on a customer's site, drawn as mock HTML and CSS with no build step.
The stylesheets in `assets/` are the theme itself: step 6.3 ships `theme.css` as the `default` theme and `base.css`
as the functional base rules (ADR-0015, ADR-0022). The tokens are listed in [`../tokens.md`](../tokens.md), and
[`DESIGN.md`](../../../DESIGN.md) says how the theme is meant to look and move.

## The pages

Two forms on a stand-in host site (a public library, `assets/site.css`), whose font and page the form inherits on
purpose: the theme brings no font of its own.

| Page | What it shows | States |
| --- | --- | --- |
| `book-details.html` | page 1 of 3 of a booking: step progress, the ledger, "(optional)" marks, yes/no buttons, a field a rule shows, a dropdown | `default`, `errors` (the error summary and inline errors), `focus` (the keyboard on a field), `answered` (every answer given, "Yes" chosen, the card number shown) |
| `book-session.html` | page 2: choice cards with places left (one full), a row of radios, a card-style group with a repeater row, a text area with its counter | `default` |
| `book-check.html` | page 3: the answers to check, each with "Change", then the consent | `default`, `errors` |
| `book-sent.html` | the outcome: the editor's message, the reference on its own line, then the copy of the answers with "Print" | `default` |
| `book-closed.html` | a notice in place of the form | `default` (closed), `opens` (not open yet) |
| `apply-room.html` | page 3 of 4 of a room hire form: bar progress, the upload with three files (done, uploading, refused), the signature | `default` (drawn), `typed` (the name typed instead) |
| `fields.html` | the specimen: every field type and state in one form, then a form that marks required questions with an asterisk | `default`, plus forced colours |

`assets/placeholder.svg` stands in for the photos an editor would add to picture choices and image fields.

## Rendering and checking

```bash
node scripts/website-mocks.mjs --check
```

This renders every state of every page with Playwright in Edge at 375 px (as a touch phone) and 1440 px, on the
host site's light and dark design, into `png/` (`page[--state]--width[--dark].png`), plus the forced-colours shots
(`--forced`). With `--check` it also measures each shot: WCAG 2.2 AA contrast for every text and control, a focus
ring on everything focusable, 44 px targets, token-only colours, corners, sizes and shades, the 4 px grid, no
dashes, no overflow, axe-core, and once per run the stylesheets (scoped to `.jfb-form`, logical properties only,
within 10 KB gzip). A filter renders one page: `node scripts/website-mocks.mjs --check book-check`.

`assets/mock.js` only sets up the pictures (`?state=`, `?scheme=dark`, `?dir=rtl`): showing a state's elements,
filling answers (`data-mock-fill`, and `data-mock-answer` for the `answered` state), showing what a rule would show
(`data-mock-reveal`), marking errors as the server would, and drawing a sample signature. The runtime of step 6.4 does
the real work.

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
  `jfb-matrix-head` for a grid; `jfb-ranking` for a ranking with move buttons.
- **Other parts:** `jfb-affix` (prefix and suffix); `jfb-select` around a single dropdown for the theme's chevron;
  `jfb-range` (a slider and its `output`); `jfb-output` (a calculated value); `jfb-parts`, `jfb-part` and
  `jfb-part-wide` (name and address); `jfb-repeater` and `jfb-row`; `jfb-signature`, `jfb-signature-pad` and
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
  so the page slides in 8 px from that side (mirrored right to left, none with reduced motion).
