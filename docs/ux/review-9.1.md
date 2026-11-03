# Step 9.1: the design skills' review

ROADMAP 9.1 ships the Forms section's shell: the sidebar, the Overview with its setup checklist and health line, and the first forms
tree ([ADR-0165](../decisions/0165-the-section-shell.md)). Per ADR-0019 and ADR-0020 the three design skills ran on the result; the design itself comes from Phase 2
([overview.html](wireframes/overview.html), [information-architecture.md](information-architecture.md), sections 1 and 2). The owner answers no questions during the
run (CLAUDE.md rule 8), so every choice was decided here or in the ADR.

## What was looked at

The e2e host (the demo on `127.0.0.1:5520`, ADR-0031) in Edge through Playwright, as the administrator, the persona without grants and the privacy officer. The states
a shared host can't hold on its own (a fresh install, warnings, a failure) had the three reads answered by hand, with the design's own sample texts.

| Shot | What it shows |
| --- | --- |
| [first run, light, 1440 px](review-9.1/first-run-light-1440.png) | the checklist with the action on the next step, the health line, and no entries yet |
| [first run, 920 px](review-9.1/first-run-light-920.png) | the narrowest backoffice: the texts keep their width, and the button stays beside them |
| [the form block's dialog](review-9.1/block-dialog-light-1440.png) | what it creates, and that it changes nothing that exists |
| [complete, light, 1440 px](review-9.1/default-light-1440.png) | setup complete: the health line with its "…", and the newest entries |
| [complete, 920 px](review-9.1/default-light-920.png) | the same at the narrowest |
| [warnings](review-9.1/warnings-light-1440.png) | one line per warning, the link to the screen that fixes it |
| [no access](review-9.1/no-access-light-1440.png) | a user without any permission: the sentence in the tree's place, and who can do each step |
| [a block fails](review-9.1/error-light-1440.png) | Recent entries failed, the rest stands |
| [loading](review-9.1/loading-light-1440.png) | a block on its way, in its own shape |
| [dark](review-9.1/dark-1440.png) · [high contrast](review-9.1/high-contrast-1440.png) | Umbraco's two other themes, everything together |
| [a screen still to come](review-9.1/coming-light-1440.png) | the privacy officer's sidebar, with Retention open and its one sentence |

**Checks that ran on the last look:**

- **Axe** (WCAG 2.2 A and AA, the whole page, what lies inside the Overview counted) in the light, dark and high-contrast themes: none.
- **Contrast**, measured in each theme by `section.spec.ts`: `--jfb-bo-color-text-muted` at 4.5:1 or more and `--jfb-bo-color-control-border` at 3:1 or more, on UUI's surface and background.
- **The bundled detector** (`impeccable detect --json`) on the four elements: `[]`.
- **Each role's menus,** read from the page: exactly the table of the design, with the privacy officer's People and Retention, and no Data collection.

## design-taste-frontend

Read as: Operate-mode product UI inside Umbraco's own design system, for editors and site owners, calm and native, so the skill's landing-page rules (variance, motion, display type) don't apply, and
UUI's components and tokens are the system (the roadmap wins, per ADR-0019). What it still asks held: one accent, the one radius scale, no pure-black shadow, every state composed, no spinners but skeletons in the block's shape.

## impeccable

Operate mode, a refinement of the incumbent world. Critique against the wireframes:

| Finding | What was done |
| --- | --- |
| Three of the five names read differently: the form block's button took its weight from the reset of the link style, so "Put the form on a page" looked lighter than "Choose who sees entries" | the weight is set after the reset, on both |
| The box heading came out in regular weight at body size, flatter than the steps inside it | a span in UUI's heading slot in the token for box headings (`--jfb-bo-type-title`, 16 px, 700); `headline-variant="h2"` keeps the outline, which axe had caught as `heading-order` |
| Slotting an `h2` nested it in UUI's own heading element | the box draws the heading, the slot only fills it |
| "Show the setup checklist" stood as a permanent button | the design's "…" with the one item, named "More actions for the overview" |
| A sentence in the tree's place sat off the sidebar's grid, in a smaller type | the heading and text take the padding and size of Umbraco's sidebar headings |
| Recent entries' rows all underlined, a wall of links | the row reads as text until hover or focus; the table goes edge to edge in the box, as the wireframe |

**"Said twice?"** per screen (§4.13):

- **Overview:** the section is named by the top bar and the sidebar header, the page adds no title. "1 of 5 done" is in the box's header only. "Done" is said once, to a screen reader. While the checklist's email step is open, Health leaves out what it says. A form's name is in Recent entries once per row, and the state of an entry is not repeated.
- **Sidebar:** a heading stands only above items the user sees, and no item repeats its heading.
- **Warnings:** the sentence says what to do, and the link says where; no chip restates either.
- **A screen still to come:** the highlighted item names it, and the box adds only the sentence.

**At most three secondary actions, one primary:** the Overview has no primary. The next step's button is its one action, the "…" holds one item. **Nothing squashed:** the step's text has a basis of 280 px, so a button that doesn't fit takes a row of its own; at 920 px the workspace has 620 px, and the texts keep over half.

## emil-design-eng

Nothing on this page animates, and rightly: it is read many times a day, and Umbraco's own components bring what motion they have. Details reviewed:

| Before | After | Why |
| --- | --- | --- |
| `background: var(--uui-color-current)` on the unseen dot | `var(--uui-color-interactive)` | the accent was a pale pink on white, and a state's mark must be seen; it now follows each theme at 3:1 or more |
| The done mark in the same weight as the open ring beside it | a tick in the positive colour, the ring in the control border's token | the eye finds what is left without reading |
| A skeleton of one size for any block | two bars, 62% and 41%, inside the block's own heading | loading keeps the page's shape, so nothing jumps when the rows come |
| The dialog's text swapping for the result in a new dialog | the same dialog says what is left, with Open document types as its primary | one place, and the next step in the eye's way |

## What stays open, and where

- **Forms at a glance** comes with 11.5, and the Inbox's badge with 11.3 (their data doesn't exist yet).
- **The tree's marks, its "+" and "…", the Recycle Bin** come with 9.2, as does Choose a template… opening the gallery.
- **The links of the checklist** lead to the stand-in until each screen's step; the full green run is 12.6's.
