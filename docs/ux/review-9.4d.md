# Step 9.4d: changing a field's type, and every type

ROADMAP 9.4d lets an editor change a field's type, from the inspector and from the element's "...", with a preview of
what goes before anything changes, and has every built-in type configured through its inspector
([ADR-0174](../decisions/0174-changing-a-fields-type-in-the-designer.md), building on ADR-0059 and ADR-0060). Per
ADR-0019 and ADR-0020 the three design skills ran on the result; the design comes from Phase 2
([information-architecture.md](information-architecture.md), section 5.1, and [copy.md](copy.md)). The owner answers no
questions during the run (CLAUDE.md rule 8), so every choice was decided here or in the ADR.

## What was looked at

The e2e host (the demo on `127.0.0.1:5520`, ADR-0031) in Edge through Playwright, as the designer, with a form of a
short text with a help text, a placeholder, a maximum length, a counter, a pattern and its own message, a nickname
shown when the name contains an "a", and checkboxes with "Other", made through the API.

| Shot | What it shows |
| --- | --- |
| [the inspector](review-9.4d/inspector-1440.png) | "Short text field" with Change type beside it, before the fold and Close |
| [the element's "..."](review-9.4d/menu-1440.png) | Change type... first, then Start a new page after this and Copy alias |
| [the dialog](review-9.4d/dialog-empty-1440.png) | "Change the type of Your name", New type with "Choose a type", "Now: Short text", the hint, Change type off |
| [to a dropdown](review-9.4d/dialog-dropdown-1440.png) | What goes: the maximum length, the counter, the placeholder with its text, the pattern, the own message; Needed before it publishes: at least one option and the nickname's rule using "contains" |
| [to a long text](review-9.4d/dialog-textarea-1440.png) | "Everything the field has stays, and it needs nothing more." |
| [to a calculation](review-9.4d/dialog-calculation-1440.png) | the formula among what it needs |
| [changed](review-9.4d/changed-1440.png) | "Dropdown field", its options asked for, the canvas's dropdown with nothing to choose yet, the toast with Undo |
| [checkboxes to a text](review-9.4d/dialog-checkboxes-text-1440.png) | "Other", the fewest ticked and the options among what goes |
| [at 920 px](review-9.4d/inspector-920.png) | the inspector over the canvas, Change type by the type's name, the element's bar without a second one |
| [the dialog at 920 px](review-9.4d/dialog-920.png) | the same width as at 1440 px |
| [at 375 px](review-9.4d/dialog-375.png) | the dialog within the window |
| [dark](review-9.4d/dialog-dark-1440.png) | Umbraco's dark theme |

**Checks that ran on the last look:**

- **Axe** (WCAG 2.2 A and AA) on the inspector of every built-in type with every section open, in
  `designer-types.spec.ts`; on the Change type dialog with nothing chosen, with a type chosen, and in Umbraco's dark
  theme, in the journeys and the look: none.
- **The bundled detector** (`impeccable detect --json`) on the dialog, its words, the change module, the inspector, the
  canvas element and the Design view: `[]` for each.
- **The journeys** of `designer-types.spec.ts` and `designer-locks.spec.ts`, below, with the whole backoffice project.

## design-taste-frontend

Read as: Operate-mode product UI inside Umbraco's own design system, for editors reshaping a form they have built,
calm and native. The skill's landing-page rules don't apply (ADR-0019); dials 2 / 2 / 4. What it still asks held: one
accent (Umbraco's positive colour for the one button that changes something), one radius scale (UUI's), the browser's
own select for 25 types rather than a wall of cards, every state composed (nothing chosen yet, a change that takes
nothing, a type the site or the form refuses, a read that fails), and no spinner: a selected field's changes are read
ahead, so the dialog opens at once.

## impeccable

Operate mode, a refinement inside the incumbent world. Critique against IA 5.1 and copy.md, and what the look in Edge
found:

| Finding | What was done |
| --- | --- |
| The dialog took the width of its preview, so it jumped as types were chosen: 468 px with nothing chosen, 340 px with a dropdown's preview | one width for every type: 520 px, or the window less its margins |
| The locks journey's host could not seed the demo: a lock of a dropdown's most options alone makes every dropdown of one choice unpublishable, as that setting fits only several | the host makes every dropdown take several options, three at most; a host's locks are its own to keep together, and publishing names the setting (ADR-0060) |
| In Umbraco's dark theme the confirm button's label is 4.38:1 on its green, under AA's 4.5:1; the theme's blue primary is 4.34:1, so every primary button of the backoffice shares it | carried into 9.6, to be decided once for every dialog and footer button of the section and held with axe in the dark theme |
| A selected field's changes were read only when Change type was asked for, so the dialog waited on the network | they are read as the field is selected, once per selection |

**Noticed:** an unlabelled field is "Untitled field" on the canvas but named by its type in a dialog's headline (Move
to... and Change type... alike, since 9.3c); 9.6's review of every screen looks at the designer's names together.

## emil-design-eng

| Before | After | Why |
| --- | --- | --- |
| — | the dialog is Umbraco's own, with its own short fade | it opens rarely, from a menu or a button; Umbraco's dialogs are what an editor knows |
| the dialog as wide as its preview | one width for every type | a dialog that grows and shrinks as the choice changes moves the very list being used |
| — | the preview changes at once as another type is chosen, without motion | it is read while choosing; a fade between types would only slow the comparison |
| — | the changes are read when a field is selected | a click on Change type that waits on the network feels broken; reading ahead makes it open at once |
| — | the change itself says what it did and offers Undo in a toast, the focus back on the field | the change may take things away; Undo makes trying a type cheap |

## A reader's review

A reviewer read the whole change with fresh eyes, the server's checks and the decision records beside the client.
Each finding was taken up; each fix has a test:

| Finding | What was done |
| --- | --- |
| A refused type was a disabled option, which the arrow keys skip, so a keyboard or screen reader user never heard why | a refused type is listed as unavailable; chosen, the preview says why and Change type stays off |
| A narrowed setting could be carried where the server's settings check would refuse it: a text per culture without its default, a short text against a minimum, a date, time or key in another form, a choice of a setting without choices, a list item twice | the designer checks a value kind by kind as the server's settings check does, and carries none it would refuse |
| Another field's options that follow the field's answer, and another field that must match it, broke at publishing unnamed | both are named under what the field needs, where the new type can't keep them (no options; no rule kind, or a value kept on the server) |
| After a failed read the focus was lost, two quick asks opened two dialogs, and the read ahead asked again at every edit while it failed | the focus goes back to the field, a second ask while the first reads does nothing, and the read ahead runs once per selection |
| Undo after a save of a change could bring back a value from before a lock, which the next save refused, since the stored draft held it under another type | the save takes a value the stored draft holds for the same field key, whatever its type there (ADR-0174, amending ADR-0060's rule); a value the field never held stays refused |
| An action without a name was named by its type's alias; a validation rule without its field read "of  uses" | an action is named by its place ("action 2"), as an outcome is; a rule without its field is "A validation rule" |
| "Options, one at least", "pass them by", a sentence in parentheses after a type's name, "that" in "can't hold that", and "angehakt" where the field types say "angekreuzt" | "At least one option", "skip them", "Long text (unavailable)" with the reason in the preview, "can't hold a field of the type Long text", and "angekreuzt" |
| Enter's test held only the value, not that the dialog closed; an unused helper; the focus on opening untested; `mayChangeType` an optional parameter | the test holds the submit, the helper is gone, the focus is tested, and the parameter is required |

The reviewer also found the lock checks, what a change drops against the server's property checks, the dictionaries
and the endpoint as they should be.

## What the journeys found

`designer-types.spec.ts` adds a field of every built-in type from the palette and configures each in its inspector,
axe clean with every section open, and saves and reads each back; and changes a text field with a placeholder and a
condition using `contains` into a dropdown through the element's "...", after the inspector's Change type and Cancel,
with the preview naming what goes and what it needs. `designer-locks.spec.ts` changes checkboxes into a dropdown on a
host that locks a dropdown's most options.

| Failure | What was done |
| --- | --- |
| The menu item's words came with the whitespace around them | the journey reads the words it holds |
| The Design view's announcement and the dropdown's own status region both have the status role | the journey reads the one that says the change |
| An unlabelled captcha placement is "Untitled field" on the canvas | the journey finds it so |
| "Parts" also found "Required parts" | the groups are found by their exact names |
| The locks host failed to seed (above) | the host makes every dropdown take several options |
| The first gate: 9.4a's keyboard journey to Start a new page after this found the focus on Change type..., now the first item of the element's "..." (the two journeys above had run alone) | the journey tabs from Change type... to it; the whole backoffice project then ran |

## The tamper round

The round ran in a throwaway clone of f4c1694 (ADR-0117): 99 changes to the type change and its words, the dialog, the
Design view, the workspace's read-ahead, the canvas and the inspector, the change map under locks, the endpoint and its
models, and the lock check's value from before a lock, each run against the tests that should notice it, the server's
against `FieldTypeChangesTests`, `FieldTypeModelsTests`, `FieldSettingLocksTests`, `FieldTypeApiTests` and
`FieldSettingLockApiTests`. 93 were caught at once. Six went unnoticed; each got a test, and all six were caught on 652d294:

| Unnoticed | The test now |
| --- | --- |
| a lock of the new type named where the field has no value of that setting | a lock is named only where the field has a value it would carry |
| the places kept on options that stay, where the new type has none | they come off every option that stays |
| "Everything the field has stays" said whatever goes | the dialog says it only when nothing goes, is locked or is needed |
| a group of its own in the list for every type | two types of one palette group share their group |
| a field changed from its "..." while another is selected, the selection left where it was | the change selects the field and gives it the focus |
| the field itself among the fields that name it | a field whose own options name it as their parent is left out |
