# 0206. What the Phase 10 UX walk changed: the name beside the longest state, a headline kept whole, and names that start with the words shown

- Status: accepted; supersedes the sentence of [ADR-0167](0167-the-form-workspace-and-the-editor-model.md) on the
  header's chip ("On a narrow window the chip shortens before the name does, with the whole state in its title"), and
  builds on [ADR-0182](0182-the-phase-9-ux-walk.md) (the walk and the phone) and
  [ADR-0020](0020-three-design-skills.md) (the design reviews)
- Date: 2026-11-07
- Roadmap step: 10.4 (part of 10.3, ADR-0197)

## Context

Step 10.4 walks the screens Phase 10 added (the form's Settings and Actions views with their sidebars, History, the
publish dialog's changes, a save conflict, the preview with its test entries, Settings › General and the option
editor's place in the list) with those of Phase 9, at 375, 768 and 1440 px, light and dark, in English and German
(`tests/e2e/specs/ux/walk.spec.ts`), and has the three design reviews of ADR-0020 read the result against ROADMAP
§4.13. What the first walk found:

1. **The name crushed by the longest state.** Umbraco's workspace is 620 px wide on a phone and a tablet (ADR-0182),
   and its header leaves the form's name and chip about 245 px beside the views. A form live with changes waiting
   carries the longest state, "Published (pending changes)", "Veröffentlicht (Änderungen bereit)" in German, which
   kept its whole width, so the name's box shrank to a few pixels: the header's rule gave the name 6rem at least, but
   Umbraco's name element lays out as its input alone (`display: contents`), so that rule never applied. axe found the
   name's box and Umbraco's back arrow too small to hit on every screen of such a form.
2. **The preview's headline cut.** Umbraco's sidebar headline is one line, cut with an ellipsis: "Preview of" and a
   form's name did not fit a phone's sidebar.
3. **A name that skips the words shown.** In German the note's "Aktionen ausführen…" was named "Aktionen für
   Testeintrag #1 ausführen…" for a screen reader; a voice user saying what the button shows names nothing (WCAG 2.5.3,
   axe's label in name).
4. **A text named once per language** in the publish dialog's few words: "Email address: Label, Label (German),
   Required".
5. **Small buttons that do not answer a press.** Phase 9 gave the designer's own small buttons a press of 120 ms
   (ADR-0182); Phase 10's own, the preview's widths, the email preview's light and dark, the "..." of an action's card
   and of a version's row, and the remove button of a chip in an action's sidebar, had none.
6. **The walk itself:** a run's Details show only for a run that failed, and every action of the walk's form
   delivered, so the walk found none to open; and its own save meanwhile ran ten minutes after it signed in, when that
   sign-in no longer held.

The owner answers no questions during the run (CLAUDE.md rule 8); each choice below is this record's.

## Decisions

1. **The name keeps its room, and the state is never cut.** A box of the section's own holds the name (or, for who may
   not design the form, its heading): it takes what the chip leaves and never less than 6rem. The chip then wraps its
   words to a second line inside itself rather than cut them, which Umbraco's 70 px header has the height for; its
   title, which repeated its words for the cut case, goes. `narrowest.spec.ts` now opens a form live with a change
   waiting, in German at 375 and 768 px: the name's box is 96 px at least, nothing of the section is cut, and axe
   finds no target of the section too small to hit.
2. **The preview's headline is whole.** It is a heading of the sidebar's own in the header's slot, in Umbraco's type,
   and the header grows to a second line where the name needs it (Umbraco's `header-fit-height`); at one line the
   header keeps Umbraco's 70 px.
3. **A button's name starts with its words.** Where a button is named more fully for a screen reader than it shows,
   its name starts with what it shows: "Aktionen ausführen für Testeintrag #1…". The dictionaries' test holds the
   preview's Run actions… and the last run's Details to it in both languages.
4. **The publish dialog names a text once,** however many of its languages changed; History's comparison and Review
   both versions still say each language.
5. **Every small button answers a press the same way.** `pressOf` (`styles/press.ts`) gives a selector's buttons
   the designer's press, shrinking to 0.97 on the motion tokens and moving nothing with reduced motion, and the Phase 10
   screens give it to their small buttons; a test holds the rule and each screen's use of it.
6. **The walk** gives its form with actions a webhook that the test stub answers 503, so Run actions leaves a failed
   run with Details, and signs in again for the save it makes meanwhile.

## Consequences

- Positive: a phone's header says the form's name and its whole state on every form; the preview names the form
  whole; a voice user can say what the note's button shows in both languages.
- Negative: a header with the longest state on a narrow window has a chip of two lines.
- Kept from earlier reviews, as they stand: Umbraco's 920 px page, its tree's open row in the dark theme and its tree
  rows' names (ADR-0182); rule chips cut where an element is narrow (ADR-0178); the radio half under the action
  sidebar's sticky footer at 375 px (review-10.2c); the outcome sidebar's Cancel at 768 px dark, a clipped sheet the
  browser's own hit test does not find under it (review-10.1c).
