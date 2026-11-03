# Step 9.4b: the option editor

ROADMAP 9.4b gives the inspector a choice field's options: "Options from" (this field, an option list, a standard list,
the country list or a JSON address, each kind configured through its served schema), "Depends on" for a field whose
options follow another's answer, the field's own options as rows that move by drag and by keyboard, take pasted lines
and open their details, and a grid's rows and columns
([ADR-0172](../decisions/0172-the-option-editor-and-the-option-sources.md)). Per ADR-0019 and ADR-0020 the three design
skills ran on the result; the design comes from Phase 2 ([information-architecture.md](information-architecture.md),
section 5.1, and [copy.md](copy.md)). The owner answers no questions during the run (CLAUDE.md rule 8), so every choice
was decided here or in the ADR.

## What was looked at

The e2e host (the demo on `127.0.0.1:5520`, ADR-0031) in Edge through Playwright, as the designer, with a form of two
dropdowns, a radio field and a grid made through the API, in English and German, and the demo's "Library branches".

| Shot | What it shows |
| --- | --- |
| [no options yet](review-9.4b/radio-empty-1440.png) | Essentials of a radio field: "Options from" on This field, "No options yet. Add them one by one, or paste a list.", Add option and Paste options…, Depends on |
| [pasting](review-9.4b/radio-paste-1440.png) | "One option a line" with four lines, one a repeat, and Add these options |
| [pasted](review-9.4b/radio-pasted-1440.png) | three rows, each a handle, its label, its details and Remove; the canvas's three answers; "Added 3 options. 1 line was an option already." said |
| [an option's details](review-9.4b/radio-details-1440.png) | Stored as with its help, and Places with 12 |
| [a value it can't keep](review-9.4b/radio-value-problem-1440.png) | "Use 1 to 200 characters, without spaces at either end." under an empty Stored as, the box marked |
| [moved by keyboard](review-9.4b/radio-moved-1440.png) | Evening moved up with Alt+Up, its handle keeping the focus; "Moved. Evening: position 2 of 3." said |
| [a drag](review-9.4b/radio-dragging-1440.png) | Morning taken by its handle, dimmed, and the line where it would go |
| [a library list](review-9.4b/dropdown-list-1440.png) | "An option list" and "Library branches · 3 options"; the canvas's dropdown with the branches |
| [a standard list, depending on another field](review-9.4b/dropdown-code-parent-1440.png) | "A standard list" on Months, Depends on Branch with its help |
| [a JSON address](review-9.4b/dropdown-url-1440.png) | the address, where the list is, what is stored and what is shown, each with its help and its default |
| [a grid](review-9.4b/grid-rows-1440.png) | its rows with "Needs an answer" said once over their boxes, then its columns |
| [cards](review-9.4b/cards-image-1440.png) | a radio field on the form's cards: an option's description and the image chosen in Umbraco's media picker, on the canvas's card |
| [German](review-9.4b/german-radio-1440.png) | the option rows in German, each box empty with the English label as its hint |
| [at 920 px](review-9.4b/overlay-920.png) | the inspector over the canvas with the rows at full width |
| [dark](review-9.4b/radio-dark-1440.png) | Umbraco's dark theme, an option's details open |

**Checks that ran on the last look:**

- **Axe** (WCAG 2.2 A and AA) on the inspector with a library list, with a standard list and its parent, with a
  radio field's details open after a refused value, with a card's description and image, and with a grid's rows and
  columns, in `designer-options.spec.ts`: none.
- **The bundled detector** (`impeccable detect --json`) on the option editor and the inspector's styles: `[]` for each.
- **The journeys** of `designer-options.spec.ts`, below, with the whole backoffice project.

## design-taste-frontend

Read as: Operate-mode product UI inside Umbraco's own design system, for editors writing a field's choices, calm and
native. The skill's landing-page rules don't apply (ADR-0019); dials 2 / 2 / 5. What it still asks held: one accent
(Umbraco's selection colour for the drag's line and a ticked box), one radius scale (UUI's), every state composed (no
options, one, many, a value it can't keep, a list that holds its options or resolves them, a source the site lacks, a
parent's answer it no longer offers, another language), and no spinner where nothing waits.

## impeccable

Operate mode, a refinement inside the incumbent world. Critique against IA 5.1 and copy.md:

| Finding | What was done |
| --- | --- |
| A label set as a paragraph ("Options", "Rows") kept the browser's margins: a gap under it twice the others | the inspector's labels have no margin of their own |
| A box with a problem was described but not marked: the rule that marks it was less specific than the boxes' own, so 9.4a's boxes weren't marked either | the marking rule is as specific as the boxes', and the journey checks the border |
| A grid's rows said "Needs an answer" on every row and squeezed each statement into a third of the column | it is said once, over the rows' boxes, and each box is named for its row ("Needs an answer: The talks") |
| Umbraco's media picker came up empty for the designer persona, whose group had no media start node | the persona's group may pick from the whole library, as a site's form designers would; the picker is Umbraco's own, so its rules stay Umbraco's |
| "No German text yet" didn't count a field's options or a grid's rows, which the editor writes in each language | it counts their labels and descriptions too |
| A card's chosen image, and an image field's, never showed on the canvas for a designer: the canvas read media URLs from an endpoint that needs Umbraco's Media section | the canvas asks Umbraco's imaging for them, as the media picker's thumbnails do, which a user of the Content section may; at most 1,200 px wide |

## emil-design-eng

| Before | After | Why |
| --- | --- | --- |
| — | a row moves at once, by keyboard or on a drop | moving options is repeated work, often from the keyboard; the line and the dimmed row show where it goes while dragging |
| — | an option's details open and close at once | they open many times while writing a field |
| — | the drag's line and the dimmed row are the canvas's own (ADR-0016) | one drag across the designer, so the hand learns it once |
| — | UUI's buttons keep their own press feedback | the backoffice's buttons answer as Umbraco's do |

## A reader's review

A reviewer read the whole change with fresh eyes, the server's checks beside the client's. The server held up (the
endpoints' permissions, the lists' paging and counts, the seeder, the draft's strict reading); twelve findings on the
client, each now fixed with a test:

| Finding | What was done |
| --- | --- |
| Each key typed in a JSON address changed the options, and the canvas asked the server for each, which fetched each half-typed address | the canvas asks for a field's first options at once and for a change once the changes pause for 400 ms |
| A new parent, or none, kept the old parent's answers on every option, which the editor no longer showed and publishing refused | a new parent, or none, clears them, and Undo brings them back; an answer the parent no longer offers stays ticked under its value, so it can be unticked |
| An option's description and image showed only when the field's own layout was cards, while the canvas also follows the form's | the inspector reads the layout as the canvas does: the field's own, or the form's where it inherits |
| In another language, a box held the default's words as its own | each box holds the language's own words, empty with the default's as its hint |
| A paste could reach 10,000 options, past the server's default limit of 1,000, and a grid's rows had none | a paste stops at 1,000 options and 300 rows, the server's default limits |
| Changing an option's value drew its row anew and dropped the focus | the box keeps the focus once the changed field is back |
| A kind's settings showed empty boxes where the server uses defaults | they show their defaults ("$", "$.value", "$.label") |
| A source that was unavailable, or a read that failed, was never asked again | only answers with options are kept |
| A field depending on one now after it lost "Depends on" on the screen | "Depends on" names its parent wherever it is |
| After pasting with an option's details open, the focus went to a button inside them | the focus goes back to the button that opened the panel |
| A value cut at 200 units could split a character, and lines broken by the separators the server reads (NEL, LS, PS) were pasted as one | cuts never fall inside a character, labels are cut at 500 as the server's one-line texts are, and every line break the server reads splits the paste |
| Two options storing the same value, which only a draft written elsewhere holds, shared a row's key, and removing one removed both | each row is drawn by a key of its own, and Remove takes the one pressed |

## What the journeys found

`designer-options.spec.ts` gives a dropdown the demo's list and another the months depending on it, with the canvas
showing what they resolve in English and German; pastes a radio field's options, moves them by drag and with Alt+Up,
refuses a value and a place limit before keeping them; gives an option on the form's cards a description and an image
from the media library; and adds, pastes, marks, moves and removes a grid's rows and columns, each saved and read back.

| Failure | What was done |
| --- | --- |
| After Alt+Up, Add option or Remove, the focus was set before the changed field came back through the Design view's history, and the row drawn again took it away | the focus waits for the changed field, then goes to the moved row's handle, the new option's label or the row now in the removed one's place |
| "Move Evening" also named "Remove Evening" | the journey asks for the exact name |
| A pasted option stores its label as visitors' answers store it, letter case kept | the journey expects "Evening", and "evening" refused as a repeat of it |

## The tamper round

The round ran in a throwaway clone of the last work commit (ADR-0117): 59 changes to the option model, the editor, the
inspector's model, the canvas's reads and asks, and the three endpoints, each run against the tests that should notice
it, the server's against `OptionSourceApiTests`. Five went unnoticed, and one server change did not compile, so it was
rewritten and then caught:

| Unnoticed | The test now |
| --- | --- |
| a value with a space at an end, and one of 201 characters, taken | the value rule is held at both edges directly, not only through a label cleaned first |
| an explicit "items" source written back into a field's options | a field's own options leave it out, as a draft writes it |
| Depends on drawn for a grid's columns | it is drawn only for types whose options may follow another field's answer |
| the same options asked again after a pause | selecting another field asks nothing, after a pause too |
