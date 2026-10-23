---
name: Jiron Form Builder
description: Forms for Umbraco sites that read like a well-kept ledger set into the host page.
---

# Design System: Jiron Form Builder

This file says how the design is meant to look and behave. It holds no values. Every colour, corner, shade, space,
type size, size and duration lives in [`docs/ux/tokens.md`](docs/ux/tokens.md), named by its CSS custom property, and
this file names the same properties. Where the two seem to disagree, `docs/ux/tokens.md` wins.

The project has two UI surfaces:

- **The website form**, the default theme that visitors fill in on a customer's site
  (`docs/ux/website/assets/theme.css` and `base.css`, shipped in ROADMAP step 6.3). Most of this file describes it.
- **The backoffice**, the Forms section in Umbraco. See "The backoffice" at the end of the Overview.

## Overview

**Creative North Star: "The Question Ledger"**

The form reads like a well-kept ledger set into the host page. When the form is wide, each titled group owns a quiet
heading column on the left, with its fields in the two thirds beside it. On a phone, or in any narrow column, the
whole form becomes one calm column. Hairlines separate the groups. Nothing is boxed unless an editor asks for a card.

The form brings no font and no ground of its own. It inherits the site's typeface, size and background, so it looks
at home on a site it has never seen. What it adds is a cool ink for text, one evergreen accent for acting and
choosing, a separate blue ring for the keyboard's place, a red that only ever means "fix this", and controls that are
tall, plain and clearly bounded. Density is medium, motion is small and quick, and every state is said in words as
well as colour.

The theme refuses the boxed card inside a card, floating labels and placeholder text used as a label. It carries no
illustration, no decorative gradients and no decorative icons.

**Key Characteristics:**

- The ledger: heading column beside fields from a medium form width, one column below it.
- The site's own font, size and ground; three type steps around the site's size.
- One evergreen accent, one blue focus ring, one error red, each with one meaning.
- Hairline rules between groups, rows and answers instead of boxes.
- Tall controls with corners derived from one radius.
- One ink-tinted shade, used only by card-style groups.
- Drawn checkboxes, radios and switches on native inputs, so keyboards and screen readers work as ever.
- A light scheme by default and an opt-in dark scheme with the same token names.

### The backoffice

The backoffice (the Forms section in Umbraco) uses Umbraco's own UUI components and tokens, by their UUI names, so
the screens feel native to Umbraco and follow the editor's light, dark or high-contrast theme. The few tokens of our
own (`--jfb-bo-*`) fill gaps UUI does not cover. Both sets are listed in the backoffice section of
[`docs/ux/tokens.md`](docs/ux/tokens.md#the-backoffice). The rest of this file is about the website form only.

## Colors

A restrained palette: cool ink on the site's own ground, with one evergreen accent and three colours that each carry
exactly one meaning.

### Primary

- **Evergreen** (`--jfb-color-accent`): the primary button, chosen radios, checkboxes, choice cards and yes/no
  buttons, links, done and current steps, the filled stars of a rating, the progress bar and the outcome mark. Text
  and ticks on it use `--jfb-color-on-accent`. Its hover state (accent strong) and its quiet fill (tint) are mixed
  from it, so a site that changes the accent gets matching states for free.

### Secondary

- **Signal Blue** (`--jfb-color-focus`): the focus ring and nothing else, drawn by `--jfb-focus-ring` just outside
  the element. It is kept apart from the accent so the keyboard's place is never confused with what is chosen.

### Tertiary

- **Correction Red** (`--jfb-color-error`): error text, invalid borders and the error summary. Its pale fill (error
  wash) is mixed from it. It is never decoration.

### Neutral

- **Cool Ink** (`--jfb-color-text`): text, typed answers and the icons on plain buttons.
- **Slate Note** (`--jfb-color-text-muted`): help, "(optional)" marks, counters, places left, the progress text and
  captions.
- **Plain Surface** (`--jfb-color-surface`): the fill of controls, choice cards, files, card groups and notices. The
  page around the form stays the site's own ground.
- **Boundary Grey** (`--jfb-color-border`): the edge of every control, measured to stay visible against both the
  surface and the site.
- **Hairline** (`--jfb-color-rule`): rules between groups, rows and summary answers, steps not yet reached and the
  progress track. Decoration only, never a control's edge.
- **Wash** (mixed from ink and surface): disabled and read-only fills, prefix and suffix boxes, hover on plain
  buttons.

### Named Rules

**The One Meaning Rule.** The accent means act, chosen or done. The blue means focus. The red means fix this. The
muted ink means secondary. None of them is ever borrowed for decoration.

**The Never Colour Alone Rule.** A chosen state also fills or ticks. An error also carries the alert icon and words.
Green, blue and red are never the only signal.

**The Mixed, Not Picked Rule.** Hover, tint and wash colours are mixed from the tokens with `color-mix()`. A
hand-picked colour in a style is a review finding.

## Typography

**Display Font:** none. The form inherits the site's font (`--jfb-font-family` is unset by default).
**Body Font:** the site's font, at the site's size (`--jfb-font-size` is unset by default).

**Character:** the form speaks in the host's voice. Weight and three size steps, not a typeface, make the hierarchy.

### Hierarchy

- **Title** (`--jfb-font-size-title`, `--jfb-font-weight-strong`, tight heading line height): the page title and the
  outcome heading. Under step progress on a wide form, the current step's name stands in for it (see Components).
- **Headline** (`--jfb-font-size-large`, strong weight): group headings, the summary's section headings, the error
  summary heading, notice headings and calculated values.
- **Body** (the site's size, `--jfb-line-height`): questions' answers, text blocks and choice labels. Running text
  keeps a reading measure of about 65 characters per line. What a visitor types is never smaller than the size at
  which phones stop zooming in.
- **Label** (body size, `--jfb-font-weight-strong`): field labels, legends, buttons, inline errors and summary terms.
- **Small** (`--jfb-font-size-small`, regular weight, muted ink): help, counters, places left, the progress text,
  captions and a grid's column heads.

### Named Rules

**The Site's Type Wins Rule.** The theme never ships a font. A site that wants another face sets
`--jfb-font-family`; otherwise the form reads as part of the page.

**The Help Before the Input Rule.** A field reads label, help, control, error, in that order. Help never hides inside
the control as placeholder text.

## Layout

The form is one grid, capped at `--jfb-size-form`, and every layout change is a container query on the form's own
width (`@container jfb-form`), not the viewport. A form in a narrow sidebar behaves like a form on a phone.

- **Wide (the ledger):** when the form is wide, a page with titled groups splits each group into a heading column
  (one third: heading and one line of help) and a field column (two thirds), with `--jfb-space-7` between them. The
  summary page's sections use the same split. The navigation buttons start on the field column's line, so the
  primary button lines up with the inputs.
- **Medium:** from a small form width, step progress names every step, and the current step's name is the visible
  page title.
- **Narrow:** one column of fields; group headings sit above their fields; navigation buttons take the full width;
  step progress becomes "Step 2 of 3" beside short bars; long scales wrap into even rows; a grid of statements stacks
  each statement above its choices; summary answers drop under their question.
- **Field columns:** a group can lay its fields in two to four columns, and a field can span several. All collapse to
  one column on a narrow form.
- **Rhythm:** every padding and gap is a step of the spacing grid (`--jfb-space-1` to `--jfb-space-7`). Small steps
  join a label to its help and control; the middle steps space fields and pad cards; `--jfb-space-6` separates groups
  and the form's parts (progress, page, navigation); `--jfb-space-7` is reserved for the ledger's gutter.
- **Direction:** every rule uses logical properties, so right-to-left pages mirror without extra CSS.

The exact container widths are listed under "Layout widths" in `docs/ux/tokens.md`.

## Elevation & Depth

The form is flat. Depth comes from hairlines and from the site's ground showing around it, not from shadows. There
is exactly one shade, `--jfb-shade`, tinted with the ink, and only a card-style group (`jfb-group-card`) wears it.
That card has no visible border: the shade alone lifts it, and its transparent border becomes the edge in forced
colours, where shades are dropped. `--jfb-shade: none` flattens every card.

Inset rings are not elevation: an invalid control and a chosen choice card double their border with an inset ring in
the error or accent colour, which stays flat.

### Named Rules

**The One Shade Rule.** One shade, for card groups only. Notices, the error summary, choice cards, files and the
upload zone are drawn with borders or fills, never lifted.

**The No Box in a Box Rule.** A repeater row inside a card is separated by a hairline, never by another box.

## Shapes

One radius, `--jfb-radius`, and four steps derived from it, so setting the one radius to zero squares the whole form:

- **Panel** (`--jfb-radius-panel`, the full radius): card groups, the error summary and notices.
- **Card** (`--jfb-radius-card`): choice cards, the upload zone, file rows, the signature pad, the ranking list and
  images. A picture inside a card takes the card's corner less its border.
- **Control** (`--jfb-radius-control`, half the radius): inputs, dropdowns, buttons, and prefix and suffix boxes.
- **Tag** (`--jfb-radius-tag`): checkboxes and file thumbnails.

Round shapes are not steps: radios and the outcome mark are circles; the switch and the progress bar are pills.

Borders are a single hairline on controls. A drop zone and anything disabled or full use a dashed edge, which reads
as "not available" without hiding the text. The error summary uses a heavier error border.

## Components

The feel throughout is plain and sure: tall, clearly bounded controls that change colour quickly and move very
little.

### Buttons

- **Shape:** control corners (`--jfb-radius-control`), at least `--jfb-size-control` high, strong weight.
- **Primary:** one per page, filled with the accent, text in `--jfb-color-on-accent`. On hover it deepens to accent
  strong.
- **Secondary:** surface fill, boundary-grey edge, ink text. On hover it takes the wash and a stronger edge.
- **Link button:** accent text, underlined, for small actions such as "Change" and "Remove". Its underline thickens on hover.
- **Icon button:** a transparent square target for move up, move down and remove, always with a text alternative.
- **Pressed:** every button sinks by one small step (`--jfb-motion-fast`, `--jfb-motion-ease`).
- **Disabled:** muted text, no fill, dashed edge.
- **Focus:** the focus ring, outside the button.

### Choices

- **Checkbox and radio:** drawn on the native input. Unchecked: surface fill and a heavier boundary-grey edge. A
  chosen checkbox fills with the accent and shows a tick; a chosen radio shows a thick accent ring. The whole label is
  the target, and the box sits on the label's first line.
- **Choice cards:** a responsive grid of cards with card corners. Chosen: tint fill, accent edge doubled by an inset
  accent ring. Full or disabled: dashed edge, no fill, picture in greyscale, text still readable.
- **Yes/no buttons and scales:** buttons with the input kept in place but unseen; chosen fills with the accent.
- **Rating:** stars in the boundary grey that fill with the accent up to the chosen one.
- **Switch:** a pill with a knob that slides to the end and turns `--jfb-color-on-accent` on the accent.

### Cards / Containers

- **Card group:** panel corners, surface fill, the one shade, a transparent border, padding of `--jfb-space-5`
  (`--jfb-space-4` on a narrow form). On a wide form it keeps its frame inside the form and aligns only its field
  column with every other group's (ADR-0022).
- **Notice:** shown in place of a closed form or one not yet open. Panel corners, surface fill, hairline border, no
  shade.
- **File row:** surface fill, hairline border, card corners, a thumbnail or file icon, then the name, its state and
  a remove button. A refused file takes the error edge.

### Inputs / Fields

- **Style:** surface fill, boundary-grey hairline, control corners, at least `--jfb-size-control` high. The caret is
  in the accent. Placeholders are muted and only ever show an example, never the label.
- **Dropdown:** the browser arrow is replaced by the theme's chevron in ink.
- **Date and time:** the pickers wear the theme's calendar and clock icons where the browser allows it.
- **Prefix and suffix:** wash-filled boxes joined to the control, sharing its edge and corners.
- **Focus:** the focus ring, just outside the control. The edge itself does not change.
- **Error:** the edge turns error red and doubles with an inset ring; the message below is bold, red and led by the
  alert icon. It comes in with a short fade and drop (`--jfb-motion-base`).
- **Disabled and read-only:** muted text, wash fill, dashed edge.

### Navigation

- **Step progress:** a row of equal bars across the form, each with its step name in the small size. Steps not
  reached use the hairline colour and muted text; the current and done steps take the accent bar and ink text; the
  current step is bold and done steps carry a tick. On a narrow form the names hide and "Step 2 of 3" leads the bars.
- **Bar progress:** "Step 3 of 4" beside a pill bar on the hairline track, filled with the accent.
- **Form navigation:** the primary button first, then Back as a secondary button with a back chevron. On a narrow form
  every button takes the full width.
- **Pages moving:** the new page fades in from a few pixels on the side the visitor moved towards.

### Error Summary

The system's signature moment. A panel at the top of the page, with the error wash fill, a heavier error border and
panel corners. Its heading carries the alert icon. Each problem is a bold red link that names the field and the fix
and takes the visitor to it. The same words repeat under the field. It takes focus when it appears and fades in.

### Summary and Outcome

- **Summary:** each section follows the ledger; each answer is a row under a hairline with the question in bold, the
  answer, and a "Change" link. An empty answer is muted.
- **Outcome:** a check mark on a tint disc, the title-size heading, the editor's message, and the reference on its own
  line in tabular figures. The answers follow below it, once.

### Upload and Signature

- **Upload:** a dashed drop zone with an upload icon around the native file input. While a file is dragged over it,
  it takes the tint and an accent edge. On touch screens the frame and drop text go away and only "Choose files" and
  "Take a photo" remain.
- **Signature pad:** a bounded pad with card corners and one baseline hairline to sign on, plus a typed-name
  alternative for keyboards and screen readers.

## Do's and Don'ts

### Do:

- **Do** take every value from a `--jfb-*` token listed in `docs/ux/tokens.md`, and mix hover and fill colours from
  them.
- **Do** let a titled group sit in the ledger on a wide form: heading and help in the first third, fields in the
  other two, the primary button on the field line.
- **Do** separate groups, repeater rows and summary answers with hairlines (`--jfb-color-rule`).
- **Do** keep the order label, help, control, error, and mark optional questions with "(optional)" by default.
- **Do** keep every target at least the touch-target floor, and every control at least `--jfb-size-control` high.
- **Do** say each fact once: under step progress on a wide form, the current step's name is the visible page title
  and the page heading stays for screen readers only.
- **Do** draw icons as masks in the text colour, next to words or with a text alternative, so they survive forced
  colours.
- **Do** give every state a non-colour signal: a fill, a tick, a dashed edge, an icon or words.

### Don't:

- **Don't** put a box inside a box: no card inside a card, no boxed repeater row inside a card group.
- **Don't** use floating labels or placeholder text as a label.
- **Don't** ship a font, a background for the page, or anything loaded from another host.
- **Don't** use the accent for focus, or the focus blue for anything but focus.
- **Don't** add a second shade, or put the shade on anything but a card-style group.
- **Don't** add illustration, decorative gradients or decorative icons.
- **Don't** pick a colour, corner, size or space by hand; a value off the tokens or off the spacing grid is a finding.
- **Don't** animate with reduced motion on: both durations drop to zero.
