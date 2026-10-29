# 0098. The repeater's rows

- Status: accepted; extends [ADR-0015](0015-themes-and-the-markup-contract.md) (a repeatable group's rows, their names
  and ids, and its buttons), [ADR-0093](0093-the-razor-rendering.md) (the post state's rows; the rows' rules) and
  [ADR-0002](0002-form-definition-model-versions-and-public-projection.md) (the `rowLabel` setting)
- Date: 2026-10-29
- Roadmap step: 6.2f (decided and built); 6.2h's surface action fills the post state's rows and 6.2i reads the buttons, 6.4
  adds and removes rows in the browser

## Context

Step 6.2f renders a repeatable group (`repeater`) on a Razor page: rows of child fields, each row named
`<alias>[<row>].<child>` (ADR-0015), and an Add and a Remove button that a page without JavaScript posts (6.2i). The
specimen in `docs/ux/website/book-session.html` shows two rows of guests, each with its legend ("Guest 1") and a
Remove button, and an Add button that is gone once the group has its most rows.

Open were: what a row's legend says; how many rows a page shows; how a row's fields are named, which ids they carry and
which rules, errors and marks they follow; what the buttons post and when they show; and what the post state carries
for 6.2h.

## Options

**A row's legend:**

1. **The package's word with the number** ("Row 1"). Right in every language, but says nothing of what a row is.
2. **The field's label with the number** ("Guests 1"). A label is usually plural, as it names the whole group, so
   the row reads wrongly in most languages.
3. **A setting for one row** (`rowLabel`, "Guest") with the number, and the package's word without it. One more text
   for the editor, translated like the others.

**The rows a first render starts with:**

1. **One row always.** A posted row counts as a row, empty or not, and its fields are checked (ADR-0072), so a
   visitor with nothing to add to a group without a least would first have to remove a row whose required fields ask
   for answers.
2. **Its least** (`minRows`), so a group without one starts with its Add button alone.

## Decision

Option 3 and option 2.

- **A row** is `<fieldset class="jfb-row" id="<instance>-<alias>-<row>" data-jfb-row="<row>">`, rows counted from 0.
  Its legend is the row label and the row's number from 1, separated by a space ("Guest 1"), or the package's "Row 1"
  without a row label. Its fields sit in `jfb-fields jfb-columns-<n>` with the columns of the group the field is in,
  as a repeater's children take their spans from that group (ADR-0034).
- **A row's field** carries the child's key in `data-jfb-field` and its path in `data-jfb-alias`
  (`guests[0].name`); its controls are named by the path (`guests[0].name`, a part after it: `guests[0].contact.first`,
  a consent's wording after its prefix: `jfb-consent-guests[0].terms`) and its id is `<instance>-<alias>-<row>-<child>`.
  Aliases hold only letters and digits, so no row's id meets another field's.
- **The rows** are a post's (at most a group's most possible, each with its child fields' answers only), or none on a
  first render, and never fewer than the group's least: each row added starts as a first render starts, its fields as
  a field outside a group starts (its default, a date's today, a slider's middle). A group a rule hid posts no rows, as
  its controls are disabled, so once the rules show it, it shows its least.
- **The rules** read the rows' answers as the pipeline does: only while their number keeps the group's limits, each
  row's answers read by their types and a grid's rows by row. Each row's fields follow their row's state (hidden,
  required, a calculation's value); a hidden group hides every row's fields, so their controls are disabled and never
  posted. Rows past the group's limits show as posted, so the visitor can remove one, with no row state.
- **The buttons** are submit buttons named `jfb-nav`, as navigation's are: Add (`add:<alias>`) below the rows while the
  group has fewer than its most, and Remove (`remove:<alias>:<row>`) in each row while it has more than its least.
  Remove's visible text is the remove label or the package's "Remove", and its accessible name adds the row's legend
  ("Remove Guest 1"); Add's is the add label or the package's "Add a row". A hidden group's buttons are disabled.
- **Enter** in a field submits the page's next or send button, not the first submit button in the markup: a page with a
  repeater carries the form's default button first, as one with a ranking does (ADR-0096).
- **Marks:** the group's legend says "(optional)" under the optional style when its least is 0, and is never marked
  required: its least rows are there, and their fields say what each needs.
- **Errors** come in the post state: each repeatable group's rows by its key, each row its answers and its errors by
  the child's key (`FormPostRow`), which 6.2h fills from the pipeline's problems at their paths. A row's error shows at
  its field and links from the error summary to its control; the group's own error (`rowCount`) shows below the rows
  and links to the rows' container (`jfb-repeater`), which carries the field's id and takes the focus. The summary
  lists the errors in the page's order, a group's own after its rows'.
- **Texts with merge tags** in a row's field render once for the page, as the merge-tag service renders a visitor text
  for the whole form; `field.<repeater>.<child>` recalls the shown rows' answers as a list (ADR-0014).
- **Words:** "Add a row", "Remove" and "Row {0}", in English and German (14.2 adds the others).

## Consequences

- The designer shows one more text setting for a repeatable group, the row label, beside the Add and Remove labels.
- A page without JavaScript shows a group's least rows and adds or removes one per post (6.2i); the runtime does the
  same in place (6.4), with the same names, ids and legends.
- **Revisit** if editors need a row's legend with the number elsewhere than at the end, or a row's text with merge tags
  that recalls its own row's answers.
