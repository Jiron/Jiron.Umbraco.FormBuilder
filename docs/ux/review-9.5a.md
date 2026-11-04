# Step 9.5a: the rules builder

ROADMAP 9.5a gives the designer its rules builder: a sidebar opened from the inspector's Rules section, whose first line
names what the rule does and to what, with plain-language conditions, condition groups nested ten levels deep, the
switch Use this rule, Delete rule with Undo, and a live preview, Try it, that runs on `@jfb/logic`
([ADR-0175](../decisions/0175-the-rules-builder.md), building on ADR-0002, ADR-0005, ADR-0061 and ADR-0170). Per
ADR-0019 and ADR-0020 the three design skills ran on the result; the design comes from Phase 2
([information-architecture.md](information-architecture.md), section 5.1, the wireframe `wireframes/rules-builder.html`
and [copy.md](copy.md)). The owner answers no questions during the run (CLAUDE.md rule 8), so every choice was decided
here or in the ADR.

## What was looked at

The e2e host (the demo on `127.0.0.1:5520`, ADR-0031) in Edge through Playwright, as the designer, with a form of a
country dropdown with "Other", topics as checkboxes, an arrival and a departure date and a number of guests, a "Which
country?" field shown when the country is Other, and the query value `ref` allowed, made through the API.

| Shot | What it shows |
| --- | --- |
| [Rules closed](review-9.5a/rules-closed-1440.png) | the inspector's Rules line: the first rule's sentence, Add a rule beside the header |
| [Rules open](review-9.5a/rules-open-1440.png) | "Shown when Country is Other" as a button, then Add a rule and the section's one sentence of help |
| [a rule](review-9.5a/builder-rule-1440.png) | "Show this field: Which country? when", one condition: Country, is, Other; Use this rule on; Delete rule, Cancel, Update |
| [condition groups](review-9.5a/builder-groups-1440.png) | the rule's conditions as its first condition group and a second one, "all of these are true" above them and "and" between |
| [Try it](review-9.5a/builder-try-1440.png) | a box for Country, and "Which country? stays hidden." said under it |
| [a new rule](review-9.5a/builder-new-1440.png) | Add a rule: the first effect the element can still take, one empty condition, Add |
| [refused](review-9.5a/builder-refused-1440.png) | Add with the row empty: "Choose a question" under it, the focus on the question |
| [two fields compared](review-9.5a/builder-compare-1440.png) | Departure is after the answer to Arrival, from a select of where the value comes from, on a line of its own |
| [at 920 px](review-9.5a/builder-920.png) | the same sidebar over the canvas |
| [at 375 px](review-9.5a/builder-375.png) | the sidebar the whole width, each row's controls wrapped, nothing cut |
| [dark](review-9.5a/builder-dark-1440.png) | Umbraco's dark theme |

**Checks that ran on the last look:**

- **Axe** (WCAG 2.2 A and AA) on the inspector's Rules section closed and open, and on the builder with a rule, with
  condition groups, with Try it open, refused at a row and comparing two fields: none. In Umbraco's dark theme the one
  finding is the primary button's contrast, below.
- **The bundled detector** (`impeccable detect --json`) on the builder, its words, its draft model, the inspector and
  its styles, and the Design view: `[]` for each.
- **Widths:** at 1440, 920 and 375 px no control is wider than its row and the sidebar has no sideways scroll.
- **The journeys** of `designer-rules.spec.ts`, below, with the whole backoffice project.

## design-taste-frontend

Read as: Operate-mode product UI inside Umbraco's own design system, for editors saying when part of a form applies,
calm and native. The skill's landing-page rules don't apply (ADR-0019); dials 2 / 2 / 4. What it still asks held: one
accent (Umbraco's positive colour for the button that writes the rule), one radius scale (UUI's), the browser's own
selects and date boxes rather than custom pickers, every state composed (a new rule, an incomplete row, a refused one,
a broken rule opened, a rule that is off, a site whose member groups the user can't read), and no spinner: the member
groups are read as the builder opens, and their select fills in when they arrive.

## impeccable

Operate mode, a refinement inside the incumbent world. Critique against IA 5.1, the wireframe and copy.md, and what the
look in Edge found:

| Finding | What was done |
| --- | --- |
| A nested condition group showed its "all of these are true" twice, once in its own header and once above its items | the match is said once, above the items, where the joiners hang from it |
| A nested condition group of one condition kept an empty header band | the group has no header; its remove is the last of its own actions, after Add condition and Add condition group |
| The select of where a value comes from was cut to "the answer t…" beside the value | it takes a line of its own, the value under it |
| "Between"'s two boxes wrapped one under the other at 1440 px | the two share the row and shrink together |
| A user without the Members section saw two refused requests in the browser's console as the builder opened | the member groups are asked for only by a user whose sections include Members; anyone else types a group's name |
| Try it's boxes stretched to the tallest one's height | each box keeps its own height |
| In Umbraco's dark theme the primary button's label is under AA's 4.5:1, as every primary button of the backoffice is | already carried into 9.6 (9.4d), to be decided once for every dialog and footer button of the section |

**Noticed:** a field's rules can only be reached from its inspector until 9.5c adds the chips and the overview; 9.5c's
look covers finding a rule from the canvas.

## emil-design-eng

| Before | After | Why |
| --- | --- | --- |
| — | the sidebar is Umbraco's own, with its own slide | it opens from a line in the inspector, as Umbraco's other sidebars do; an editor knows it |
| — | a condition or condition group appears at once, the focus on its question | building a rule is a sequence of choices; motion between them would only slow the next one |
| — | Try it's sentence changes as the boxes change, said by a status region | it is read while trying answers; it must keep up with the typing |
| the focus lost after a condition group was removed | the focus on the condition before it, or on the first one left | a removed control must not drop the keyboard user to the page's start |
| the focus lost when the sidebar closed | the focus on the rule's line in the inspector, set once the sidebar is gone | the dialog gives the focus back to its opener as it closes; setting it earlier was undone |
| — | Delete rule acts at once and offers Undo in a toast | a confirmation before a delete that can be undone only slows the editor |

## A reader's review

A reviewer read the whole change with fresh eyes, the server's publish checks and the decision records beside the
client. Each finding was taken up; each fix has a test:

| Finding | What was done |
| --- | --- |
| A condition could look at a hidden field, or at a field a member's value stands in for, which publishing refuses in a page's, group's or field's rule | neither is offered; Phase 10's actions, whose rules run on the server, will offer them (ADR-0175) |
| A rule broken already (by a type change, or through the API) opened looking fine and was refused only at Update | it opens with each row saying what it lacks; Cancel leaves it as it was |
| The first line's refusal was said but not tied to its controls | the effect and the element are described by it |
| Another answer since deleted was said at the value, not at where the value comes from | it is said at the select of where the value comes from |
| A value written where the operator takes none could not be taken away | it is said, with a button that takes it away |
| A list's answers that aren't options were dropped on Update; a count or pattern was carried into a value of another shape | the list keeps them ("Also kept:" under it); a count or pattern starts empty in another shape |
| Removing a condition group dropped the focus | the focus goes to the condition before it, or to the first one left |
| A rule on the language was not readable on a site of one language | the language stays among the questions for a rule that has it |
| "Group" for a condition group, where the designer's groups are a form's group of fields; "Aus" beside the line, outside the button; German operator words with an article | "condition group" throughout, in English and German; Off inside the rule's button; the words fixed |
| The vectors ran only through the adapter, not through what the builder writes and its boxes | every case a draft can hold also runs through the draft written and read back and through the boxes |
| A rule opened and saved unchanged could differ only by key order and become a step of undo | rules are compared after normalising, and Update of an unchanged rule is no step |

## What the journeys found

`designer-rules.spec.ts` builds a rule of condition groups three levels deep with a condition of every operator on
"Which country?", axe clean, and reads it back from the saved draft; and builds a "required when" rule on the guests
comparing the departure with the arrival's answer and the query value `ref`, refused at its empty row first, tried in
Try it (required, not required, and off), read back, and deleted from its line with Undo in the toast.

| Failure | What was done |
| --- | --- |
| The focus, set on the rule's line as the sidebar answered, was moved back to the opener by the dialog as it closed | the Design view sets it once the builder is gone (ADR-0175) |
| A require rule on Departure could not compare Departure: the element itself is left out | the journey requires the guests |
| "Add condition" also found "Add condition group" | the buttons are found by their exact names |
| A sentence read with a leading space failed an anchored pattern | the journey reads the trimmed text |
| A value's label also named the remove button beside it | the value is found as a text box by its exact name |
| Try it's boxes held their labels inside, so a box's name took in its help | each box has a label of its own for it |

## The tamper round

The round ran in a throwaway clone of the work (ADR-0117), paused while the gate of 9.4d measured its
budget and resumed beside the gate's integration tests: 112 changes to the draft model, the words, Try it's adapter,
the landing, the names, the member groups, the sidebar, the inspector, the Design view and the engine's operators
table, each run against the tests that should notice it. 95 were caught at once. Of the 17 that went unnoticed, two
showed code that did nothing and is gone (the builder's operators filtered by the engine's table at every call, now a
test that holds the table to the engine's; the selection put back after a landing, which the editor model keeps), and
two change nothing a test could see: the engine's own check refuses the same rows (another answer since deleted, a
range with one end). The thirteen others each got a test, and all were caught on 8e75ad2:

| Unnoticed | The test now |
| --- | --- |
| a choice offered "contains" | every operator offered, for every kind and part of the visit, is one the engine takes |
| a checkbox's stray value kept from "is ticked" to "is not ticked" | the value goes; an option's "ticked" keeps its option |
| a grid row named without its grid | the names, a row by its grid and itself, a page and a group without a title by their number |
| the Design view never told the builder is shown, or gone | the builder says both, once each |
| an effect the new element can't take kept when the rule moves | a page takes Show, and the switch's help says the page |
| "all of these are true" shown for one condition | none for one |
| a condition group offered inside the tenth level | the tenth level offers Add condition alone |
| Try it of a rule a row says is incomplete, where the engine would run it | Try it says to complete it |
| a require rule's switch help said as a show rule's | "The field is then never required." |
| no Rules section for a type that can only be required | Rules is there, for "Required when" |
| a second ask opening a second builder | asked twice at once, the builder opens once |
| a new rule starting on an effect the element has | it starts on the first effect the element has no rule for |
