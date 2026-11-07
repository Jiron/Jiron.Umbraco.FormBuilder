# Phase 10 review: the UX walk

Step 10.4 walks every screen Phase 10 added beside those of Phase 9: the form's Settings view with After sending and
an outcome's sidebar, the Actions view with an action's sidebar for each kind, Test and a run's Details, History with a
version's comparison, two compared and an earlier version chosen, the publish dialog's changes, a save someone else
made first with Review both versions, the preview with its test entries and sample answers, Settings › General, and
the option editor's new place in the list. It reads the result against ROADMAP §4.13 with the three design skills of
ADR-0020, takes up the option editor's move without dragging that 10.1b left to it (ADR-0185), and runs the phase's
wiring sweep. What changed is decided in [ADR-0205](../decisions/0205-moving-an-option-without-dragging.md) and
[ADR-0206](../decisions/0206-the-phase-10-ux-walk.md).

## How the walk works

`tests/e2e/specs/ux/walk.spec.ts` (`JFB_E2E_UX=1`, project `ux`, then `ux-sheets`) now visits 74 screens in twelve
combinations, 375 × 812, 768 × 1024 and 1440 × 900, light and dark, English and German, 888 shots a run. It finds its
way by tags, `data-mark` and icons, never by words. Its Phase 10 forms come from the API: one published twice with
draft saves between and a change since, one whose draft the walk's own second sign-in saves while the walk edits it,
and the demo's contact form with its actions and a webhook the test stub answers 503, so Run actions leaves a delivered
mail and a failed run with Details. Each shot keeps axe's WCAG 2.2 A and AA findings inside the section's own
elements, the page's sideways overflow, boxes that scroll inside themselves and words cut by an ellipsis.

## What was looked at

The last walk ran after the fixes (`.local/ux/10.4-2`, not committed). These shots stand for it; "before" is the first
walk (`.local/ux/10.4-1`).

| Shot | What it shows |
| --- | --- |
| [The header at 768 px, German](review-10/header-pending-768-de.png) ([before](review-10/header-pending-768-de-before.png)) | the name's box beside "Veröffentlicht (Änderungen bereit)" on two lines; before, a sliver of a name |
| [The preview on a phone](review-10/preview-phone.png) ([before](review-10/preview-phone-before.png)) | the headline whole on two lines, the note after sending with Run actions… |
| [Publish, dark](review-10/publish-changes-dark.png) ([before](review-10/publish-changes-dark-before.png)) | "Since the published version": a label changed in two languages named once |
| [An option's place](review-10/option-place-1440.png) | a press on Market Street's handle: "Place in the list", 1 of 2, under its row |
| [After sending](review-10/settings-after-1440.png) | the outcomes with their rules in words, Add outcome |
| [An email action, dark, German](review-10/action-sidebar-dark-de.png) | the settings beside the mail as the team gets it, Place in the list |
| [A failed run's Details on a phone, dark](review-10/action-run-phone-dark.png) | the server's answer, the tries and what can be done, Test |
| [History with its draft saves](review-10/history-drafts-1440.png) | the current draft first, the live version's chip once |
| [A version beside the draft on a phone, German, dark](review-10/history-version-phone-dark-de.png) | each changed property with both values, Restore as a new draft |
| [Review both versions on a phone, German](review-10/conflict-review-phone-de.png) | each difference under the two versions, the side that made it chosen |
| [The preview's "…", German](review-10/preview-menu-de.png) | the scheme, the language and Run actions for test entries in one menu |
| [Run actions…, dark](review-10/preview-run-actions-768-dark.png) | each action with whom it reaches, the failing webhook among them |
| [Settings › General](review-10/general-settings-1440.png) | how long draft saves stay, each with its unit and help |

## What the walk measured

- **The name and the longest state.** A form live with changes waiting carries "Published (pending changes)",
  "Veröffentlicht (Änderungen bereit)": beside it the name's box was a few pixels wide at 375 and 768 px, where
  Umbraco's workspace is 620 px and its header leaves the name and the chip about 245 px. axe found the name and
  Umbraco's back arrow too small to hit on the six screens of that form, in every combination at those widths.
- **The preview's headline** was cut at 375 px in all four looks.
- **A name that skips its words:** axe's label in name on the note's Run actions… in German, at 768 and 1440 px.
- **The rest** was what Phase 9 left as it is: Umbraco's 920 px page (545 and 152 px sideways at 375 and 768 px), its
  tree's open row in the dark theme and its rows' names, and rule chips cut where an element is narrow.

## design-taste-frontend

Reading this as: an operator's tool inside Umbraco's backoffice, for the designers and editors who set a form up,
publish it and try it, in Umbraco's own language (UUI, its tokens and sidebars), leaning toward restraint: variance 3,
motion 2, density 5.

| Finding | What was done |
| --- | --- |
| The longest state chip took the header's room, and the name's box all but vanished | the name keeps 6rem in a box of its own, and the chip's words take a second line inside it rather than being cut (ADR-0206, decision 1) |
| Four sizes of sidebar in one phase: small for a run's Details, medium for an action and the comparisons, large for an email and an outcome with their previews, full screen for the preview | kept: each is the size its content asks for, as Umbraco sizes its own (a picker small, an editor medium, a preview beside settings large) |
| The sample entry answers a text with its own label in the email preview ("Your name: Your name") | kept (ADR-0187): the label shows where the answer goes, and a made-up sentence would read as a visitor's |
| One look across the new screens | held: UUI's controls and the section's tokens, Umbraco's two radii, the same muted line under each heading, skeleton lines while reading, a sentence and a next step in every empty and failed state |

## impeccable

The bundled detector (`impeccable detect --json`, no downloads, hooks or live mode) on the nine elements and styles
10.4 changed, the header, the preview, the option editor and its styles, the press, the action list, the email
preview, History and the action's sidebar: `[]`. It cannot see a layout's widths, so the walk's measures and its shots
carried the audit.

| Finding | What was done |
| --- | --- |
| The form's name and Umbraco's back arrow too small to hit beside the longest state (axe, target size), the name squeezed to a sliver | the name's own box and a chip that wraps; `narrowest.spec.ts` opens such a form in German at 375 and 768 px and finds the name's input 96 px at least, nothing cut and no target too small |
| The preview's headline cut on a phone | a heading of its own in the sidebar's header, which grows to a second line (ADR-0206, decision 2) |
| "Aktionen für Testeintrag #1 ausführen…" as the name of a button that shows "Aktionen ausführen…" (WCAG 2.5.3) | "Aktionen ausführen für Testeintrag #1…"; the dictionaries' test holds each button named more fully than it shows to a name that starts with its words |
| The publish dialog's few words named a text once per language: "Email address: Label, Label (German), Required" | once; History's comparison still says each language |
| The option editor's rows moved only by dragging or Alt and the arrows (WCAG 2.5.7, carried from 10.1b) | a press on a row's handle opens its "Place in the list" (ADR-0205) |
| A refused preview ticket left the frame saying only that it could not send (the wiring sweep's finding) | the note says the preview can send no more, as an alert, with Load again (ADR-0203, amendment) |

## emil-design-eng

| Before | After | Why |
| --- | --- | --- |
| the preview's widths, the email preview's Light and Dark, the "…" of a card and of a version's row, a chip's remove button: no press | `pressOf`: 120 ms on the strong ease-out to `scale(0.97)`, nothing with reduced motion | every button answers a press the same way, as the designer's do since 9.6a |
| a long state cut with an ellipsis and its title | the chip's words on two lines | a state cut in half says nothing, and a title is out of reach on touch |
| the option's place opening with no motion | kept so | it opens from a press or a key many times a day; at once is fastest |
| the preview's "…" menu | kept: a 120 ms fade and a growth from its corner as it opens, none as it closes | it says where the menu came from; closing at once keeps it quick |
| Run actions… spinning while its dialog asked (10.3f) | kept as fixed there: busy only once the run goes out | a spinner means work under way, not a question open |

## The words

Read in both languages against copy.md: the German name of the note's button, the publish dialog's line of a text
changed in two languages, and the preview's expired note ("This preview can't send any more: its access has run out.",
"Diese Vorschau kann nichts mehr senden: Ihr Zugang ist abgelaufen.") with Load again. Every new string has its row in
copy.md, and the dictionaries' tests hold both languages to the same keys.

## Kept as they are

- Umbraco's 920 px page, its tree's open row in the dark theme and its rows' names (ADR-0182, OWNER-TODO).
- Rule chips cut where an element is narrow (ADR-0178), at 768 and 1440 px on the walk's forms.
- The radio half under the action sidebar's sticky footer at 375 px in English (10.2c's review): axe measures the
  sidebar as it was scrolled, and the radio is whole once scrolled to.
- The outcome sidebar's Cancel at 768 px in the dark theme (10.1c's review): a sheet clipped under it that the
  browser's own hit test does not find.
- A language named with its region in Umbraco's words in the comparison, "Label (Deutsch (Deutschland))".
- Save draft's failed mark in the footer while the conflict dialog asks: Umbraco's own for a save its action refused.

## The §4.13 checklist, screen by screen

One primary action where Umbraco puts it, at most three secondary actions in sight, each fact said once, nothing
squashed at the 620 px workspace, and the screen's states.

| Screen | Primary | Secondary in sight | Said twice? | At 620 px | States |
| --- | --- | --- | --- | --- | --- |
| The form's header | (the footer's) | the "…" | no: the state once, as its chip | the name keeps 6rem, the chip wraps | — |
| Settings view | Publish… (footer) | Preview, Save draft | no: a closed group says what is set in one line | one column | each problem under its setting, counted on its closed group |
| More options | — | its toggle | no | one column | — |
| After sending | — | Add outcome | no: an outcome's rule once, in its row | rows wrap beside their handle | Never used in the warning text, the list's limit |
| An outcome's sidebar | Update | Cancel, Delete | no | the preview after the settings below 960 px | the preview's wait, failure with Try again |
| Actions view | Publish… (footer) | Add action; a card's Test and "…" | no: a trigger's heading once, its cards under it | Test in the "…" on a narrow card | no actions yet per trigger, read-only for who may not change actions |
| An action's sidebar (each kind) | Add / Update | Cancel, Test, Delete action | no | one column, the email's preview after its settings | each problem under its box |
| Test's toast | — | — | no: the action named once, leading | Umbraco's corner | sent, dry run, refused with what to do |
| A run's Details | Close | Test | no: the line's time not repeated in Tries | one column | — |
| History | Compare (two ticked) | Show draft saves, a row's "…" | no: the live version's chip once | its columns fit the workspace, nothing cut | skeleton, none yet, failed with Try again |
| A version's comparison | Restore as a new draft | Restore and publish…, Close | no: when and who once, under the headline | the footer's buttons stack | nothing changed |
| Two compared | Close | — | no | one column | — |
| An earlier version, from the "…" | — (a version's name opens its comparison, which restores) | Show draft saves, Close | no: when and who once a row | one column | Show more for older versions |
| Publish, with what changed | Publish | Cancel | no: a text changed in two languages named once | the dialog's width | checking, errors, warnings, first version, nothing changed |
| A save someone else made first | Review both versions | Cancel | no: who and when once | Umbraco's dialog | — |
| Review both versions | Save | Cancel | no: each difference once, under the two versions | one column | changed in both waits until chosen |
| The preview | (the frame's own Send) | the widths, Fill with sample answers, the "…" | no: the note says what sending does, once | Narrowed to fit, the headline whole | loading, failed with Try again, rules with errors, expired with Load again |
| Its "…" | — | Light or dark, Language, Run actions for test entries | no | its menu | — |
| Sent | Run actions… | Start again | no | the note wraps | running, waiting for its confirmation, refused |
| Run actions…' confirmation | Run 3 actions | Cancel | no: each action once, with whom it reaches | Umbraco's dialog | — |
| Settings › General | Save | — | no | one column | locked by the host, out of range |
| An option's place | — | its select | no | under its row | — |

## What the journeys found

- **The narrowest journey** (`narrowest.spec.ts`, German, 375 and 768 px) now opens a form live with a change waiting:
  the chip reads "Veröffentlicht (Änderungen bereit)" on two lines, Umbraco's name input is 96 px wide at least,
  nothing of the section is cut, and axe finds no target too small to hit; it passed again on its final strings.
- **The copy-actions screens,** which fell past the forms tree's first page in the second walk, in all twelve looks
  once the walk pressed Load more: 12 of 12.
- **The option journey** (`designer-options.spec.ts`) moves a radio field's option with single presses on its handle
  and its place in the list, no drag.
- **The preview, dark theme and form check journeys** (`preview.spec.ts`, `dark-theme.spec.ts`,
  `designer-form-check.spec.ts`) pass with the header's and the headline's change: 9 of 9.

## What the last walk found

The walk ran twice, before the fixes (`10.4-1`) and after them (`10.4-2`), 888 shots each, and the copy-actions
screens a third time in all twelve looks once the walk pressed the tree's Load more (12 of 12). Inside the section's
own elements:

| Measure | First | Last | What remains, and whose |
| --- | --- | --- | --- |
| axe target size | 52 shots | 7 | the radio half under the action sidebar's sticky footer at 375 px in English, whole once scrolled to (review-10.2c) |
| axe label in name | 8 | 0 | — |
| axe colour contrast | 2 | 2 | the outcome sidebar's Cancel at 768 px dark, a clipped sheet the browser's hit test does not find (review-10.1c) |
| words cut by an ellipsis | 36 | 16 | rule chips where an element is narrow (ADR-0178); the preview's headline is whole |
| boxes that scroll inside the section | 0 | 0 | — |
| the page's sideways scroll beyond Umbraco's | 0 | 0 | Umbraco's own 545 and 152 px at 375 and 768 px (ADR-0182) |
| shots that could not be taken | 38 | 2, then 0 | the first walk's Details had no failed run and its late save no sign-in; the second's copy-actions fell past the forms tree's first page, which the walk now loads further |

Umbraco's tree rows' names (850 and 886 shots) and its open row in the dark theme (22 and 24) stay Umbraco's
(ADR-0182, OWNER-TODO).

## The tamper round

Each tamper took back one rule of what 10.4 changed, with the file restored after it, and counts as caught only when a
named test failed.

- **In the client, 22.** The option's place (nine), the German name, the publish dialog's words, the preview's
  headline, the small buttons' press (four) and the refused ticket's note (six, before its commit). Twenty were caught
  at once. The two missed, the handle's focus after choosing the row's own place and Escape, found the place's test
  checking neither; it checks both now, and both were caught on a second run.
- **In the browser, two,** each building the client and running the narrowest journey in Edge after an untampered run
  passed. The chip cut with an ellipsis again was caught ("texts cut short inside the section"). The name without its
  own box was not: with the chip wrapping, the name keeps more than 6rem at 375 and 768 px with the box or without it,
  so the box only guards a header narrower than any window gives today. It stays, as it says the rule where a longer
  state would need it, and the journey measures Umbraco's name input itself, which is what squeezed.
