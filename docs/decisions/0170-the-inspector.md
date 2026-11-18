# 0170. The inspector: plain boxes, a section's one line, typing as one step, and languages from the canvas

- Status: accepted; the palette's fold on selection is superseded by [ADR-0182](0182-the-phase-9-ux-walk.md), whose
  inspector column stays beside the canvas, and so is the closed section's one line, whose summary now wraps. "More
  options" inside a section is superseded by [ADR-0258](0258-a-forms-views-pick-first.md): Messages and Privacy are
  sections of their own, and no section folds inside. The inspector while nothing is selected (the open page) is
  [ADR-0256](0256-the-designer-in-three-columns.md)'s, and a closed section's height and the closed Rules section's
  "Add a rule" are [ADR-0259](0259-the-other-screens-and-the-walks-count.md)'s
- Date: 2026-11-03
- Roadmap step: 9.4a (decided and built); builds on ADR-0060 (designer locks), ADR-0168 (the canvas) and ADR-0169 (the
  moves); 9.4b to 9.4d add the option editor, the richer editors and the change of type to the same frame

## Context

Step 9.4a gives the designer its third column (IA 5.1): what the selected field, group or page is, then the sections
Essentials, Validation, Rules, Appearance and Advanced, with a generic settings renderer over the schema the field
types endpoint serves (ADR-0055, ADR-0056), the designer locks of ADR-0060 shown, alias editing with the rule of section
4.3.1, texts in every language of the site, the palette's fold beside it (IA 5.1, ADR-0168), and the element's "…". The
step had to settle:

- **What a setting's box is drawn with:** UUI's elements or the browser's own.
- **When a value is checked,** and what a box holds while it can't be kept.
- **How typing meets Undo:** every key a change of the form, as on the canvas, would make Ctrl+Z take back one letter.
- **What a closed section says,** and how much an open one shows.
- **Where the site's languages come from,** and where a text in another language than the default goes.
- **The alias rule,** "editable until the first published version has entries; after that, changing it asks for
  confirmation and keeps the old alias", with a designer who may not view entries.
- **When the palette folds:** IA 5.1 names a window "between 620 and 1024 px"; the view only knows its own width.
- **Keys inside the inspector:** the Design view's shortcuts act on the selection, and Delete pressed on a list box in the
  inspector would delete the field.

## Options

For the boxes:

1. **UUI's elements** (`uui-input`, `uui-toggle`, `uui-select`): Umbraco's look for free, but each box sits in a shadow
   root of its own, so a help text or a problem outside it can't be its `aria-describedby`, and the label of a
   `uui-toggle` is its own.
2. **The browser's own boxes in UUI's tokens**: a `label` for each, the help and the problem tied to the box by
   `aria-describedby`, `aria-invalid` where it is wrong, and a checkbox drawn as a switch with `role="switch"`. Chosen.

For checking:

1. **On every key**, saying why at once: a decimal box says "Enter a number" between `1` and `1.5`.
2. **On every key, quietly; said once the box is left** (`change`): a value the form can keep goes in as it is typed, one
   it can't stays in the box, and leaving the box says why under it until the box holds one it can keep. Chosen. The
   checks are the server's rules as the schema serves them: whole numbers, numbers in the wire form (ADR-0005), times,
   real dates, lengths, counts, allowed values and the limits with their exclusivity.

For typing and Undo:

1. **A change per key,** as now: Ctrl+Z after typing "Your full name" takes back one letter.
2. **A step per box visit:** a change says which box it came from and which visit of the focus to the inspector it is,
   and the history joins a change to the step before it while both say the same; any other change, a selection, an undo
   or a redo in between ends that. Chosen. A click (a switch, a list box) is a step of its own, and the focus stays
   where it is when Ctrl+Z and Ctrl+Y are pressed in the inspector.

For a section:

1. **Every item open**, as the canvas's earlier wireframes drew it: the Validation of a number field alone is twelve
   boxes.
2. **IA 5.1 as written:** Essentials open and the others as this browser left them last; a closed section says in one
   line what is set ("Maximum length: 120 · Show a character counter"), or "Nothing changed", and Rules "Always shown" or
   how many rules; an open one shows seven items at most, then "More options (5)". Chosen. A section with nothing in it
   for the type is left out, and Rules shows where the type takes rules; its builder comes with 9.5a, so in 9.4a it says
   how many rules the element has and that a rule sits on the element it shows, hides or requires.

For languages:

1. **Umbraco's language repository** in the backoffice: its endpoints are for users of the Settings section, and a
   designer may have only Forms.
2. **The canvas endpoint** (ADR-0168) serves the site's languages beside its words, the default first: any user of the
   Forms section reads them. Chosen. The toolbar's language switch shows on a site with more than one language, the
   canvas and the inspector show that language's texts, and the inspector says "Editing the German texts. Where one is
   empty, visitors see the English text." A box in another language than the default shows that language's own text,
   with the default's as its placeholder, and writes to the language's own key (or the neutral one it read); in the
   default language a text goes back to the key it was read from, `''` when it has none of its own. A field with a
   written text (label, help, placeholder, a message or a text setting, nested ones too) that has no words in the
   language shown carries the chip "No German text yet", which is its select button's description too, and not while
   it is selected, where the inspector says it.

For the alias:

1. **Ask whenever a published version exists:** a form published once and never used would ask for nothing.
2. **The draft endpoint says whether the form has entries** (`hasEntries`, in any state), a yes or no, so a designer who
   may not view entries learns no count. Chosen. The inspector checks an alias as the server does (the pattern, the
   reserved names, another field's alias or former alias) when the box is left; a free one goes to the Design view,
   which asks first ("Change the alias?") when the form has entries and the field is saved with the alias it has. A
   field added since the draft was read changes freely: no entry knows it. Confirmed, the field keeps the saved alias in
   its former aliases, which merge tags outside the form still resolve; changed back, it gives that up. Cancelled, the
   box shows the alias again.

For the palette's fold:

1. **The window's width** (`matchMedia('(max-width: 1024px)')`): Umbraco's sidebar can be resized, so a 1024 px window
   leaves the view anything from about 700 to 780 px.
2. **The view's own width, as the narrow mode already is** (ADR-0168): from 701 to 1024 px, while an element is selected
   and the inspector is open beside the canvas, the palette folds to its rail, whose buttons open it over the canvas;
   clearing the selection, or folding the inspector to its rail, gives the palette back as the user left it. Chosen. At
   a 1024 px window the view is 724 px wide, in the range; at 1440 px it is wider and nothing folds. Up to 700 px the
   inspector opens over the canvas with the element's bar (Move to…, Duplicate, Delete, "…") in its header, and the
   focus goes to its heading.

For the keys:

1. **The Design view's keys everywhere:** Delete on a list box deletes the field, Alt+Up moves it from a section's button.
2. **From inside the inspector only Escape, Undo, Redo and `?`:** Escape on a box does nothing (a list box takes it), on
   anything else it lets the element go and gives the focus back to it on the canvas; Ctrl+Z and Ctrl+Y leave the focus
   where it is. Chosen.

## Decision

The inspector is `jfb-designer-inspector`, the Design view's third column: 320 px beside the canvas, a 56 px rail when
folded, over the canvas up to 700 px. It locates the selection in the definition itself and sends every change as a
request (`jfb-inspector-request`): the element's key, a pure update, what Undo calls it ("Changed Maximum length") and,
for typing, the box and visit that make it one step. The Design view makes it an edit through `change()` of the editor
model, so a change the form refuses (a taken alias, a cycle) is said like any other. `inspector-model.ts` decides which
items a section holds from the type's features and settings (pure, tested for every built-in type), `setting-values.ts`
reads, checks and writes a value as the server does, and `setting-editor.ts` draws the plain editors: text, whole and
decimal numbers, a switch, a choice, lists of texts, dates or choices, nested settings, times, date bounds (none, a
date, or a number of days, weeks, months or years from today; a chain of steps is kept and named), and a formula as
text. Rich text, merge tags, pickers, the pattern check and options are left out of the sections until 9.4b and 9.4c
draw them. A locked setting shows its value and the configuration key behind it, read-only; a hidden one is not drawn,
nested ones included (ADR-0060). The element's "…" on the canvas, and in the inspector's bar over a narrow canvas, has
Start a new page after this (for fields and groups, with `splitPage`) and Copy alias (for a field that has one); the
bar's buttons show their icons alone where a sheet is 430 px wide or less, and in a group with columns. With the
inspector beside it the canvas is narrower, so the bar shares the gap above the selected element with the insert point
there: that insert point shows its "+" alone at its start, beside the handle, as each in a group with columns does, and
a group's handle, while it shows, lies above the insert point below the field before it. The draft endpoint answers
`hasEntries`, and the canvas endpoint the site's languages; both are in the reviewed API snapshots.

## Consequences

- A setting a later step draws needs only its editor in `setting-editor.ts` and its name in `PLAIN_EDITORS`; the
  sections and the summaries follow.
- Typing makes one step per visit to a box, so the history stays short and Ctrl+Z means what a person meant.
- The Rules section counts rules until 9.5a's builder fills it; "Add a rule" comes with it. (9.5a, ADR-0175: the
  section lists an element's rules in words, each opening the builder, and holds Add a rule.)
- A designer who may not view entries learns whether a form has any, through the alias rule; the count stays theirs who
  may view them.
- Seven items before "More options" hides a field's CSS class behind "More options (1)" in Advanced; IA 5.1 says seven,
  and the class is the setting changed least.
