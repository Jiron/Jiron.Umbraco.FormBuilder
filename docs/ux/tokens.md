# Design tokens

Jiron Form Builder has one visual system (ROADMAP §4.13): every colour, corner, shade, space and type size in a
style is one of the tokens below, and a hand-picked value is a review finding. This file is the single source of the
values; `DESIGN.md` says how they are meant to be used and points here.

There are two surfaces with two token sets:

- **The website** (the forms visitors fill in): Jiron Form Builder's own `--jfb-*` CSS custom properties. The default
  theme in `docs/ux/website/assets/theme.css` (ROADMAP 2.3) declares them, and step 6.3 ships them as they are.
- **The backoffice** (the Forms section): Umbraco's own UUI tokens, by their UUI names, so the screens feel native;
  plus a few of our own where UUI has none.

## The website

### How the tokens work

- A site sets any public token on the form (`.jfb-form`) or on any ancestor, for example
  `body { --jfb-color-accent: #8a1c3c; }`. Every token has a default, so a site sets only what it changes.
- The theme reads each public token through a private shorthand (`--_ink: var(--jfb-color-text, #1d2433)`). The
  `--_*` names are not public surface and may change; only `--jfb-*` is the contract (ADR-0015).
- **The site's type wins.** `--jfb-font-family` and `--jfb-font-size` have no default: unset, the form inherits the
  page's font and size, so it reads as part of the site.
- **Dark scheme** values apply with the class `jfb-scheme-dark`, or `jfb-scheme-auto` when the visitor's system asks
  for dark (ADR-0015). A site that sets a colour token sets it for both schemes, or sets it again inside its own dark
  rule.
- The checker `node scripts/website-mocks.mjs --check` renders every mock and refuses any colour, corner, font size or
  shade that is not a token, and any padding or gap off the 4 px grid.

### Colour

Ratios are WCAG contrast ratios against the form's surface and against the mock host site's ground (`#f6f7f9`
light, `#14181f` dark). Text needs 4.5:1, control boundaries, focus rings and icons 3:1 (WCAG 2.2, 1.4.3 and
1.4.11); the checker measures every element in place as well.

| Token | Light | Dark | Used for | Contrast, light / dark |
| --- | --- | --- | --- | --- |
| `--jfb-color-text` | `#1d2433` | `#e8eaee` | text, typed answers, icons on plain buttons | 15.52 / 13.10 on surface |
| `--jfb-color-text-muted` | `#5b6372` | `#aab2bf` | help, marks, counters, places left, the progress text | 6.04 / 7.38 on surface; 5.49 / 6.48 on wash |
| `--jfb-color-surface` | `#ffffff` | `#1d232d` | controls, choice cards, files, card groups, notices | |
| `--jfb-color-border` | `#767c88` | `#8b95a5` | the boundary of every control | 4.19 / 5.21 on surface; 3.91 / 5.88 on the site |
| `--jfb-color-rule` | `#d9dde3` | `#343c49` | hairlines between groups and rows, steps not reached, the progress track | decoration only |
| `--jfb-color-accent` | `#0f6e5c` | `#5fd0b8` | the primary button, chosen choices, links, done steps, the outcome mark | 6.16 / 8.41 on surface |
| `--jfb-color-on-accent` | `#ffffff` | `#0b1a17` | text and ticks on the accent | 6.16 / 9.53 on the accent |
| `--jfb-color-focus` | `#1a5fd1` | `#8ab4f8` | the focus ring, never anything else | 5.83 / 7.48 on surface |
| `--jfb-color-error` | `#b3261e` | `#f2a59e` | error text, invalid borders, the error summary | 6.53 / 8.00 on surface; 5.83 / 7.00 on error wash |

**Derived colours** follow from the tokens above (`color-mix()` in sRGB), so a site that changes the accent or the
ink gets matching hover and fill colours without setting them:

| Name in the theme | Mix | Used for |
| --- | --- | --- |
| accent strong | 80 % accent, 20 % text | the primary button on hover |
| border strong | 50 % border, 50 % text | a control's boundary on hover |
| tint | 8 % accent on surface | a chosen card, the outcome mark's disc, a file being dragged in |
| wash | 5 % text on surface | disabled and read-only fills, prefix and suffix boxes, hover on plain buttons |
| error wash | 7 % error on surface | the error summary's fill |

Four colours carry meaning and are never borrowed for decoration: the accent (act, chosen, done), the focus colour
(where the keyboard is), the error colour (what to fix) and the muted text (secondary). Green, blue and red are
never the only signal: chosen states also fill or tick, errors also carry the alert icon and words.

### Corners

One radius, four steps derived from it. Setting `--jfb-radius: 0` squares the whole form; each step can also be set
on its own.

| Token | Default | Used for |
| --- | --- | --- |
| `--jfb-radius` | `12px` | the base the steps derive from |
| `--jfb-radius-panel` | the radius (12 px) | card groups, the error summary, notices |
| `--jfb-radius-card` | the radius × 0.75 (9 px) | choice cards, the upload zone, files, the signature pad, the ranking list, images |
| `--jfb-radius-control` | the radius ÷ 2 (6 px) | inputs, dropdowns, buttons, prefix and suffix boxes |
| `--jfb-radius-tag` | the radius ÷ 4 (3 px) | checkboxes, file thumbnails |

Round shapes are not steps: radios and the outcome mark are circles (50 %); the switch and progress bars are pills
(999 px). A picture inside a card takes the card's radius less its 1 px border.

### Shade

| Token | Light | Dark |
| --- | --- | --- |
| `--jfb-shade` | `0 1px 2px rgb(29 36 51 / .08), 0 4px 16px -2px rgb(29 36 51 / .12)` | `0 1px 2px rgb(5 7 10 / .4), 0 8px 24px -4px rgb(5 7 10 / .5)` |

One shade, tinted with the text colour, used only by card-style groups (`jfb-group-card`). The card has no visible
border: the shade alone lifts it, and its transparent border becomes the edge in forced colours, where shades are
dropped. `--jfb-shade: none` flattens every card.

### Spacing: the 4 px grid

| Token | Default | Typical use |
| --- | --- | --- |
| `--jfb-space-1` | `4px` | label to help, help lines, a scale's buttons |
| `--jfb-space-2` | `8px` | label or help to the control, between buttons of a choice row, padding in a file row |
| `--jfb-space-3` | `12px` | box to label in a choice, button gaps, a control's inline padding |
| `--jfb-space-4` | `16px` | between field columns, a group heading to its fields, card padding on phones |
| `--jfb-space-5` | `24px` | between fields, card and summary padding |
| `--jfb-space-6` | `32px` | between groups and between the form's parts (progress, page, navigation) |
| `--jfb-space-7` | `48px` | the gap between the ledger's heading column and its fields |

Every padding and gap is a multiple of 4 px; the checker measures them. Fixed sizes such as the 24 px checkbox or
the 44 px icon button are sizes, not spacing.

### Type

| Token | Default | Used for |
| --- | --- | --- |
| `--jfb-font-family` | none: the site's font | everything |
| `--jfb-font-size` | none: the site's size | the body step, `1em` |
| `--jfb-line-height` | `1.5` | everything except headings (1.25) |
| `--jfb-font-size-small` | `.875em` | help, counters, places left, the progress text, captions, the grid's column heads |
| `--jfb-font-size-large` | `1.25em` | group headings, the summary's section headings, the error summary heading, notices, calculated values |
| `--jfb-font-size-title` | `1.5em` | the page title and the outcome heading |
| `--jfb-font-weight-strong` | `600` | labels, legends, headings, buttons, errors |

Three steps around the site's own size, so the scale follows the site. What the visitor types is at least 16 px
(`max(1em, 16px)`), so phones never zoom into a field.

### Sizes, focus and motion

| Token | Default | Used for |
| --- | --- | --- |
| `--jfb-size-control` | `48px` | the height of inputs, dropdowns and buttons |
| `--jfb-size-form` | `960px` | the form's widest width |
| `--jfb-focus-ring` | `2px solid` the focus colour | every focus ring, 2 px outside the element |
| `--jfb-motion-fast` | `120ms` | a button sinking when pressed, colour changes on hover and choice |
| `--jfb-motion-base` | `200ms` | an error, a new repeater row or a new page coming in |
| `--jfb-motion-ease` | `cubic-bezier(.23, 1, .32, 1)` | every movement (colour changes use `ease`) |

Targets are at least 44 by 44 px (§4.13): every button, choice label and standalone link. This is a floor, not a
token. With `prefers-reduced-motion: reduce`, both durations are 0 s.

### Layout widths

These are container widths of the form itself (`@container jfb-form`), not tokens, so a form in a narrow column
behaves like a form on a phone:

| Form width | What changes |
| --- | --- |
| under 480 px | one column of fields; navigation buttons take the full width; step progress shows "Step 2 of 3" beside short bars |
| 480 px and more | step progress shows each step's name, and the current step's name stands in for the page title |
| under 540 px | a scale of 8 points or more wraps into even rows, its two end words in one row below |
| under 600 px | a grid (matrix) stacks: each statement above its own choices |
| 720 px and more | the ledger: a titled group's heading and help in the first third, its fields in the other two; a card group's fields start on the same line |

### Icons

Lucide icons (ISC licence), stroke 2, drawn as CSS masks so they take the text colour and survive forced colours:
check, alert, back, up, down, add, remove, upload, file, print, star, and calendar and clock for the date and time
pickers (which replace the browser's own icons where the browser allows it). Icons only ever sit next to words or
carry a text alternative; no icon is decoration.

## The backoffice

The backoffice uses UUI's tokens by name; Umbraco 17.7.1's values are given for reference (light theme). Umbraco's
dark and high-contrast themes swap the values, never the names, so a screen built on these follows the editor's
theme by itself.

### Corners: two radii

| Token | Value | Used for |
| --- | --- | --- |
| `--uui-border-radius` | 3 px (`umb-css.css` sets it to `--uui-size-1`) | controls, boxes, cards, sidebars, dialogs |
| round | `999px` (UUI has no token; ours is `--jfb-bo-radius-round`) | badges, state dots, chips, avatars, switches |

`--uui-border-radius-1` (6 px) is not used: two radii, as in Tandem (ROADMAP §4.13).

### Shade

`--uui-shadow-depth-3` (`#0000001a 0px 6px 16px 0, #0000001a 0 0 2px 0`): menus, sidebars and dialogs. No other
depth is used; a box on the page is a border, not a shade.

### Spacing: the 3 px grid

| Token | Value |
| --- | --- |
| `--uui-size-space-1` | 3 px |
| `--uui-size-space-2` | 6 px |
| `--uui-size-space-3` | 9 px |
| `--uui-size-space-4` | 12 px |
| `--uui-size-space-5` | 18 px |
| `--uui-size-space-6` | 24 px |
| `--uui-size-12` | 36 px, page gutters and the gap between a workspace's columns |

### Type

| Token | Value | Used for |
| --- | --- | --- |
| `--uui-font-family` | Lato, then system fonts | everything |
| `--uui-type-small-size` | 12 px | help under settings, meta lines, column heads |
| `--uui-type-default-size` | 14 px | body text, controls, tables |
| `--jfb-bo-type-title` | 16 px | box and panel headings |
| `--jfb-bo-type-display` | 20 px | workspace and dialog titles |

UUI names no 16 or 20 px type size, so the backoffice package declares these two on its own elements.
Merge tags, aliases and configuration keys use a monospace stack (`Consolas, "Cascadia Mono", Menlo, monospace`).

### Colour

UUI's semantic colours (`--uui-color-text`, `-surface`, `-background`, `-border`, `-interactive`, `-selected`,
`-positive`, `-danger`, `-warning`, `-focus`) carry every meaning, and UUI's components keep their own look. Two
colours are ours, for what UUI does not cover at AA:

| Token | Value | Why |
| --- | --- | --- |
| `--jfb-bo-color-text-muted` | `#575757` (UUI palette "mine grey dimmed") | secondary text at 6.9:1 on the background |
| `--jfb-bo-color-control-border` | `#8d8d8d` (UUI palette "dusty grey dark") | the boundary of controls we draw ourselves (the designer canvas, the rules builder), 3.3:1 on white for WCAG 1.4.11; UUI's own inputs keep UUI's border |

The wireframes of 2.2 (`docs/ux/wireframes/assets/kit.css`) used these values under provisional `--wf-*` names; the
backoffice code in Phases 9 to 14 uses the names above.
