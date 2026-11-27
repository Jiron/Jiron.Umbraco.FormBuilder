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
- **The site's type wins.** `--jfb-font-family` and `--jfb-font-size` default to `inherit`: unset, the form inherits
  the page's font and size, so it reads as part of the site.
- **Dark scheme** values apply with the class `jfb-scheme-dark`, or `jfb-scheme-auto` when the visitor's system asks
  for dark (ADR-0015), on the form or on any ancestor, such as `<html class="jfb-scheme-auto">` (ADR-0106). A site
  that sets a colour token sets it for both schemes, or sets it again inside its own dark rule.
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
| `--jfb-color-button` | the accent | the accent | the primary button's fill | 6.16 / 8.41 on surface |
| `--jfb-color-on-button` | the text on the accent | the text on the accent | the primary button's text and its busy ring | 6.16 / 9.53 on the button |
| `--jfb-color-button-hover` | accent strong (`#125f54`) | accent strong (`#7ad5c3`) | the primary button on hover | 7.52 / 10.33 with the text on it |
| `--jfb-color-focus` | `#1a5fd1` | `#8ab4f8` | the focus ring, never anything else | 5.83 / 7.48 on surface |
| `--jfb-color-error` | `#b3261e` | `#f2a59e` | error text, invalid borders, the error summary | 6.53 / 8.00 on surface; 5.83 / 7.00 on error wash |

**Derived colours** follow from the tokens above (`color-mix()` in sRGB), so a site that changes the accent or the
ink gets matching hover and fill colours without setting them:

| Name in the theme | Mix | Used for |
| --- | --- | --- |
| accent strong | 80 % accent, 20 % text | the primary button on hover, unless `--jfb-color-button-hover` is set |
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
| `--jfb-font-family` | `inherit`: the site's font | everything |
| `--jfb-font-size` | `inherit`: the site's size | the body step, `1em` |
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
| `--jfb-scroll-margin` | `6rem` | the room above a row added, the group a row left, an option's new place, a field, label, legend or summary that a link or the focus brings into view; a control keeps 4rem more for its label and a line of help, and a picture's choice room for its picture too, as a link without JavaScript lands on the control; a site with a taller sticky header sets more. The base rules read it, so every theme has it, `unstyled` included (ADR-0105) |

Targets are at least 44 by 44 px (§4.13): every button, choice label and standalone link. This is a floor, not a
token. With `prefers-reduced-motion: reduce`, nothing travels: the theme's private distances (`--_rise` 4 px,
`--_slide` 8 px) become 0 and so does the duration of movement (`--_move`), so an error or a new page only fades in,
and a pressed button or a switch's knob jumps to its place instead of gliding there. Fades and colour changes keep
their durations, since they help without moving anything. The busy button's turning ring becomes a slow pulse, and
the skeleton stops pulsing.

### Layout widths

These are container widths of the form itself (`@container jfb-form`), not tokens, so a form in a narrow column
behaves like a form on a phone:

| Form width | What changes |
| --- | --- |
| under 480 px | one column of fields, name and address parts included; navigation buttons take the full width; step progress shows "Step 2 of 3" beside short bars |
| 480 px and more | step progress shows each step's name for up to four steps, and the current step's name stands in for the page title |
| under 540 px | a scale of 8 points or more wraps into even rows, its two end words in one row below |
| under 600 px | a grid (matrix) stacks: each statement above its own choices |
| 720 px and more | the ledger: a titled group's heading and help in the first third, its fields in the other two; a card group's fields start on the same line; step names show for up to six steps |
| any width | seven steps or more show "Step 2 of 7", and so does a form where a word of a step name is wider than its step (the runtime sets `data-jfb-compact`) |

### Icons

Lucide icons (ISC licence), stroke 2, drawn as CSS masks so they take the text colour and survive forced colours:
check, alert, back, up, down, add, remove, upload, file, print, star, mail (an outcome that waits for the visitor to
confirm their address), and calendar and clock for the date and time pickers (which replace the browser's own icons
where the browser allows it). Icons only ever sit next to words or
carry a text alternative; no icon is decoration.

### The package's own page

The page that confirms an email address (`GET verify?t=`, 8.5) is served by the package outside the site's
templates (`docs/ux/website/assets/standalone.css` in the mocks). It is not a theme: the form part inside it uses the
default theme with `jfb-scheme-auto`, and the page around it needs only a font, a text colour and a ground:

| What | Light | Dark (`prefers-color-scheme: dark`) |
| --- | --- | --- |
| Font | `system-ui` and the platform fonts after it, 17 px, line height 1.5 | the same |
| Text | `#1d2433`, the theme's ink: 14.5:1 on the ground | `#e8eaee`: 14.8:1 on the ground |
| Ground | `#f6f7f9` | `#14181f` |

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

**At a box's edge** (17.0.1). Three rules in the section's tokens, which every element takes, so no screen states them
itself:

- **A dropdown's arrow** is drawn by the section: two short strokes in the text's colour, 12 px from the box's end, with
  the text ending 34 px before it. The browser's own arrow sits against the edge wherever the padding ends. A list box
  has no arrow, and a visitor's form shown in the section keeps its theme's own. The section uses the browser's
  `<select>` in its screens; Umbraco's own select, whose arrow cannot be moved, only among Umbraco's controls (the form
  picker's theme).
- **A search box's icon** stands 9 px in from the box's edge.
- **What a sidebar's or dialog's footer holds at its start** (Paste questions…, Delete rule, Test) starts 24 px in, under
  the content's start; a second one follows 6 px after the first.

### Type

| Token | Value | Used for |
| --- | --- | --- |
| `--uui-font-family` | Lato, then system fonts | everything |
| `--uui-type-small-size` | 12 px | help under settings, meta lines, column heads |
| `--uui-type-default-size` | 14 px | body text, controls, tables |
| `--jfb-bo-type-title` | 16 px | box and panel headings |
| `--jfb-bo-type-display` | 20 px | workspace and dialog titles |

UUI names no 16 or 20 px type size, so the backoffice package declares these two on its own elements.
Merge tags, aliases and configuration keys use a monospace stack (`Consolas, "Cascadia Mono", Menlo, monospace`),
`--jfb-bo-font-mono`. The package declares its own tokens once, in `packages/backoffice/src/styles/tokens.ts`, and each
element adds them to its styles.

### Colour

UUI's semantic colours (`--uui-color-text`, `-surface`, `-background`, `-border`, `-interactive`, `-selected`,
`-positive`, `-danger`, `-warning`, `-focus`) carry every meaning, and UUI's components keep their own look. The
colours below are ours, for what UUI does not cover at AA. Each is mixed from UUI's own colours, so they follow
Umbraco's light, dark and high-contrast themes without knowing which one is on (the theme leaves no mark on the page
that we could read):

| Token | Value | Why |
| --- | --- | --- |
| `--jfb-bo-color-text-muted` | `color-mix(in srgb, var(--uui-color-text) 50%, var(--uui-color-border-emphasis))` | secondary text. Light theme `#545454`: 7.6:1 on the surface, 7.1:1 on the background; dark theme `#a8aeb5`: 5.7:1 and 6.8:1; high contrast: black |
| `--jfb-bo-color-control-border` | `color-mix(in srgb, var(--uui-color-text) 20%, var(--uui-color-border-emphasis))` | the boundary of controls we draw ourselves (the designer canvas, the rules builder), for WCAG 1.4.11. Light theme `#828282`: 3.8:1 on the surface, 3.6:1 on the background; dark theme `#7e8892`: 3.5:1 and 4.2:1; high contrast: black. UUI's own inputs keep UUI's border |
| `--jfb-bo-color-danger-text` | `color-mix(in srgb, var(--uui-color-danger) 50%, var(--uui-color-text))` | text and icons that say something failed or is gone (the form picker's "Form not available", 6.7f). UUI's danger is 2.5:1 on the dark theme's surface; the mix: light `#731832` 11.0:1 on the surface, dark `#dc9593` 5.3:1 (4.8:1 on its emphasis surface), high contrast `#660420` 13.1:1 |
| `--jfb-bo-color-positive-text` | `color-mix(in srgb, var(--uui-color-positive) 50%, var(--uui-color-text))` | text that says something holds (the inspector's "Try it" when a pattern takes the answer, 9.4c), beside its words. UUI's positive is 2.5:1 on the dark theme's surface; the mix: light `#09442c` 11.2:1 on the surface, dark `#91b694` 5.7:1 (5.1:1 on its emphasis surface), high contrast `#0a4725` 10.8:1 |
| `--jfb-bo-color-warning-text` | `color-mix(in srgb, var(--uui-color-warning-standalone) 50%, var(--uui-color-text))` | text and icons that warn (the form check's warnings, a rule that conflicts, 9.6a). UUI's warning is 1.3:1 on the high-contrast theme's white; the mix: light `#543f03` 10.1:1 on the surface, dark `#cfb581` 6.4:1 (5.8:1 on its emphasis surface), high contrast `#837225` 4.8:1 |
| `--jfb-bo-color-focus` | `color-mix(in srgb, var(--uui-color-focus) 70%, var(--uui-color-text))` | every focus ring the package draws, and UUI's own buttons, inputs, toggles, checkboxes, selects, menu items and references inside its elements (9.6a). UUI's focus colour is 2.5:1 on the dark theme's surface; the mix: light `#2956b4` 6.8:1 on the surface, dark `#6a94d5` 4.1:1 (3.7:1 on its emphasis surface, 4.9:1 on the background), high contrast `#2956b4` 6.8:1. The designer's sheet is white in every theme, so its own controls take `#2956b4` there |
| `--jfb-bo-color-selected` | `color-mix(in srgb, var(--uui-color-selected) 85%, var(--uui-color-surface))` | the background of a chosen item in lists we draw ourselves (the form picker's sidebar, 6.7f), under `--uui-color-selected-contrast`. UUI's selected colour carries its contrast text at 4.3:1 in the dark theme (3.7:1 for its emphasis); the mix: light `#4858a7` 6.5:1, dark `#3064b5` 5.0:1, high contrast 6.5:1. A chosen item also shows a check mark, so the state never rests on the colour |
| `--jfb-bo-color-current` | `color-mix(in srgb, var(--uui-color-current) 85%, var(--uui-color-surface))` | the background of what is open in lists we draw ourselves (the open entry's row beside its panel, 11.6; the group picked in a form's Settings, 22.3) and of the match Enter picks in the field picker (22.4), under `--uui-color-current-contrast`, as Umbraco's tree marks its open item. UUI's current colour carries its contrast text at 4.3:1 in the dark theme; the mix: light `#f7cac6` 9.9:1, dark `#3064b5` 5.0:1, high contrast 9.9:1 |

The ratios come from the UUI values of Umbraco 17.7.1 (`light.css`, `dark.css`, `high-contrast.css` in
`Umbraco.Cms.StaticAssets`). Step 9.1 checks them again in all three themes with axe, since UUI may change them. The
two of 6.7f were found by axe in Edge on the dark theme, where UUI's own danger and selected colours fall short.

**A filled button's label** (9.6a, ADR-0182). UUI draws a positive, default or danger button's label in
`--uui-color-positive-contrast`, `-default-contrast` and `-danger-contrast`. The dark theme sets them to `#eeeeef`,
4.37:1, 4.34:1 and 4.32:1 on its fills (`#347d39`, `#316dca`, `#ca3b37`), under AA; the light and high-contrast themes
set them to `#fff`. The tokens set all three to `#fff` for the section's elements, Umbraco's footer and dialogs inside
them included: 5.07:1, 5.04:1 and 5.01:1 in the dark theme, and nothing changes in the other two. A selected
item's label (`--uui-color-selected-contrast`) is white for the same reason, and a warning's
(`--uui-color-warning-contrast`) black: the dark theme's `#eeeeef` is 3.2:1 on its warning fill `#af7c12`, black 5.7:1,
as the other two themes have it. `dark-theme.spec.ts` holds it with axe on every kind of filled button the section
shows, and measures the state chip of a form with changes to publish, the focus rings and the form check's icon.

**Status colours.** UUI's `-standalone` colours (danger, warning, positive) fall to 2.5:1 on the dark theme's surface,
and its warning to 1.3:1 on the high-contrast theme's white, so the package never draws text, icons or a control's
border in them: those take the three `-text` mixes above.

**Motion.** `--jfb-bo-motion-fast` (`120ms`) and `--jfb-bo-motion-ease` (`cubic-bezier(0.23, 1, 0.32, 1)`) time every
press, fade and reveal the package draws, the same values as the website's `--jfb-motion-fast` and `--jfb-motion-ease`.
A small button of the package's own, a "…", a remove button or a choice of a few, shrinks to 0.97 as it is pressed
(`pressOf` in `packages/backoffice/src/styles/press.ts`), as the designer's do.
Hover and colour changes use `ease`. With `prefers-reduced-motion: reduce`, nothing moves or scales; fades keep their
time. The canvas's move handle and insert points show and hide at once: every selection, focus and hover reveals one
and hides another, and a fade would paint the whole sheet again for each of its frames, which a form of 300 fields
cannot afford (ADR-0169).

### Waiting

While a box's words are read, it shows still lines in their place (`packages/backoffice/src/ui/skeleton.ts`), in
`--uui-color-surface-alt`, busy (`aria-busy`) and with one sentence for screen readers; they never pulse. A screen
whose name and the user's access are read with its list keeps its skeleton until both have come, so its name, its
words and its buttons arrive together. The designer's canvas shows its sheet with still bones where the title and
fields will be (IA 5.1). A turning loader is only for work started by the user that takes a moment, such as the form
check running again, and it sits where it moves nothing.

### The designer's sheet

The sheet is white in every theme, as visitors see the form, so what the designer draws on it has fixed colours,
named on the canvas (`designer/canvas/canvas.styles.ts`):

| Name | Value | Used for |
| --- | --- | --- |
| `--bo-sheet-ink` | `#1d2433` | the sheet's text, the default theme's ink |
| `--bo-sheet-quiet` | `#4a5263` | the words of an empty page or group and of a note in a field's place, 7.8:1 on white |
| `--bo-sheet-line` | `#8a92a0` | the dashed edge of a place to add a field and of a dragged element's place, 3.1:1 on white |
| `--bo-sheet-focus` | `#2956b4` | the focus ring of the designer's controls on the sheet, 6.8:1 on white |
| `--bo-sheet-slot` | `#eceff3` | a dragged field's or group's place, and the waiting sheet's bones |
| `--bo-chip-missing-*` | line `#d9b44a`, fill `#fff6d9`, ink `#533d00` | the chip of a field without a text in the language shown, 9.6:1 |
| `--bo-chip-rule-*` | line `#b8bfdc`, fill `#eef0f8`, ink `#1f2a5c` | the chip of a rule in use, 12.0:1 |

Everything else the designer draws there (the selected element's frame and bar, the insert points, the handle) takes
Umbraco's selected colour, which keeps 5:1 on white in every theme (8.0:1 light and high contrast, 5.0:1 dark).
Chips use Umbraco's small type (12 px), and the canvas's controls the two radii above.

The wireframes of 2.2 (`docs/ux/wireframes/assets/kit.css`) used these values under provisional `--wf-*` names; the
backoffice code in Phases 9 to 14 uses the names above.
