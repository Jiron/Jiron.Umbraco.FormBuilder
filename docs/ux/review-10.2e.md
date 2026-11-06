# Step 10.2e: the content and Set state editors

ROADMAP 10.2e builds the content action's editors in an action's sidebar, the Set state action's, and both types'
dry runs in the backoffice's words ([ADR-0192](../decisions/0192-the-content-and-set-state-editors.md), building on
ADR-0162, ADR-0173, ADR-0188, ADR-0190 and ADR-0191). Per ADR-0019 and ADR-0020 the three design skills shaped it before
it was built and ran again on the result; the design comes from Phase 2 ([information-architecture.md](information-architecture.md),
section 5.3, and [copy.md](copy.md)). The owner answers no questions during the run (CLAUDE.md rule 8), so every choice
the skills would have asked about was decided here or in the ADR.

## What was looked at

Before building, a look of the scratch kind in Edge, run from a throwaway clone of the commit before the step, shot the
two sidebars as 10.2a had drawn them: the document type's select saying "One the site no longer has", as the action
types served no choices; where it goes and the item picked apart, the item labelled "Parent, or the item it must stay
under"; no table for the properties at all; and Set state's three verbs under "State". ADR-0192 answers what that look
found.

After building, the same look ran from the working tree, light and dark, at 1440 and 375 px: the actions list, both
sidebars a screen at a time with their dry runs, the odd states (a field that is gone, a property the type lacks, a
value it can't hold, no type yet) and the sidebar of a designer without Manage actions. The choices, the table, the
problems under their boxes and the toasts read as the reviews meant. It found one thing they had not: the help this step
gave Publish it and Update an item of the same name was never drawn, as the inspector's switch drew no help at all, and
the consent field's switch for a captcha had lost its help the same way. A switch now says its help under it and is
described by it (`setting-editor.ts`), as the sidebar's own Stop the rest does, with a test; the look shot it again.

## design-taste-frontend

Read as: Operate-mode product UI inside Umbraco's own design system, for an editor or site builder who turns each entry
into a content item, or moves entries between states under a rule; calm, native, every choice saying what it means.
The skill's landing-page rules don't apply (ADR-0019); dials 2 / 1 / 5, as for 10.1a to 10.2d. What it still asks held:

- **One look:** the browser's own radios as the trigger's and the webhook's choices draw them, UUI's selects, the
  inspector's text boxes, Umbraco's document and media pickers, the email editor's remove buttons and Add links,
  Umbraco's Insert merge tag. No colour, motion or icon of the package's own.
- **Every state composed:** no document type yet, a type being read, one the site no longer has, properties that could
  not be read (with Try again), a type without a property a form can fill, a property nothing on the form fills, a
  field that is gone or one the property can't hold, a value it can't hold, a row not finished, a dry run that would
  leave something out (a warning that stays), and who may only read.
- **Pre-flight:** no emoji, no gradient, no spinner of its own; every string in copy.md's voice.

## impeccable

Operate mode, a refinement inside the incumbent world, shaped before it was built (`shape`, its questions decided in
ADR-0192) and critiqued after. The critique ran as two isolated assessments (method: dual-agent): a design review that
read the code, IA 5.3, copy.md, section 4.13, the ADR and every shot, and the bundled detector (`impeccable detect`, the
bundled engine; no hooks, live server or browser injection, which the owner rules out). The detector found nothing in
`content-editors.ts`, `content-model.ts`, `action-editors.ts`, `action.modal.element.ts` and `setting-editor.ts`, also
with `--no-config`.

The design review scored the first build 24 of 40:

| Heuristic | Score | What it found |
| --- | --- | --- |
| Visibility of status | 3 | loading, a type gone and row problems at their place; a dry run that would leave things out toasted green; Insert merge tag could go nowhere |
| Match with the real world | 2 | "Under the item picked" before anything was picked; "Saves a Event"; "State" over verbs |
| User control | 3 | each type keeps its table; a failed read said to close the sidebar, losing what was typed |
| Consistency | 2 | Where it goes a select beside radios elsewhere; a gone field unlike the email picker's; row problems in server English |
| Error prevention | 2 | Update an item of the same name without help, though an answer can choose the item; Add property offered properties nothing fills |
| Recognition over recall | 2 | on a phone a row read "Heading / Heading"; the item's name had no Insert merge tag |
| Flexibility | 3 | rows from fields of the same alias, values in the property's own form, groups named |
| Minimalist design | 3 | Set state's help repeated the Rule line; an empty media picker for forms without uploads |
| Error recovery | 2 | a gone field a blank select, its key in read-only; no Try again |
| Help | 2 | each place's help on the picker, not its choice; Publish it and the media folder without help |

What was done:

| Finding | What was done |
| --- | --- |
| A row's two selects said "Heading / Heading" on a phone | each field is "The answer to Headline", with no option group, so a closed select says what it holds at any width |
| Update an item of the same name and Publish it without help | help for both, the first saying a visitor's answer can choose the item overwritten |
| The item's name without Insert merge tag | every action's text that takes tags has it, the webhook's within its own editors |
| The table's Insert merge tag went nowhere until a value had the focus | it goes into the last row's text value until another has had the focus |
| A gone field a blank select, a field it can't hold unmarked, a key in read-only | "A field that is gone", "The answer to Email address (Heading can't hold it)", and "Title: a field that is gone" |
| Row problems in server English, with aliases | each problem has a code (`ActionCheckCodes`) the backoffice says without restating the row |
| A dry run that leaves things out toasted green and went | it is a warning that stays until it is closed; "It would set …" after "A run would save …" |
| "Saves a Event" | "Saves an item of type Event", as German says it already |
| Where it goes a select, its help on the picker, the item's label repeating the choice | three radios, each with its line, the picker set in under the chosen one, its name for who hears it |
| Set state's help repeated the Rule line, "State" over verbs | "Move the entry to" Approved, Rejected or Spam, its help saying what approving or rejecting starts |
| Set state's test only repeated the choice | it says how many actions the move starts |
| Add property offered properties nothing on the form fills | it offers those the form can fill; an existing row of another says so in place of its choice |
| A failed read said to close the sidebar | Try again, which reads the type again in place |
| An empty media picker for forms without uploads | Media folder for files shows where a row takes a file upload's answer, or where it holds a folder, with its help |
| The look's list shots caught the Content dashboard; no odd states shot | the look opens the actions list, and shoots the odd rows, no type yet and read-only |

Questions skipped: the owner answers no questions during the run (CLAUDE.md rule 8); each was decided here.

## emil-design-eng

| Before | After | Why |
| --- | --- | --- |
| where it goes as a select, the picker under it with its own label and help | three radios, each with its line, the picker set in under the chosen one | a choice says what it means where it is chosen; the label the picker had said the choice twice |
| a closed Takes select showing "Heading" | "The answer to Heading" | a closed select says what it holds without being opened |
| a value box as close to the next row as to its own | rows further apart than a row's own boxes | closeness is what ties "Stories" to "A value you write" |
| an empty 180 × 160 media picker in More options | only where a row takes a file upload | an empty control that big outweighs the settings around it |
| a dry run's toast going after a few seconds | a warning that stays where it has lines to act on | four to six lines can't be read and acted on in that time |
| — | no transition when a row or a choice comes and goes | set up a few times; the focus move says where you are |

## The words

English and German for every string: the settings' labels and help in `lang/field-types.*.ts`, which the server's
schema names; the editors' words and the test's in `lang/actions.*.ts`; the check's in `lang/check.*.ts`. copy.md has
where it goes, what a property takes, a property nothing fills, the help of Publish it, Update an item of the same name
and the media folder, Set state's choice, the card, the dry run's lines and the check's words for a row and a loop.

## Kept as they are

- **Insert merge tag's size** under the table: the picker is Umbraco's own, as under every other text of the sidebar.
- **The loop refused without manual approval too:** a move to Rejected starts the Rejected actions whatever the form's
  approval, so the pair loops there as well.
- **The document type's endpoint for any user of the section:** the action types are, and a designer needs the
  properties without Umbraco's Settings section; it answers names, groups and what each takes, no content.
- **The journey types the item's name before it picks the place:** Umbraco's picker gives the focus back to its button
  once its panel has gone, which takes keys typed before then.

## A reader's review

A reader who had not seen the work read it against the step, ADR-0192, IA 5.3, copy.md and the review of 10.2d. They
found nothing a visitor or an editor writes drawn as markup, no answer of a real entry in a test's lines, the row paths
of the check and the sidebar's ids in step, and every test code worded in both languages. These findings were fixed,
each with a test:

1. **A sensitive repeatable group's answer could reach content** from a version published before the step: the run's
   check of the group's sensitivity had gone with the rule that refuses such a mapping at publishing. It is back, and a
   run of such a version keeps the group's field out unless the action names the group.
2. **A row's problems stayed hidden until Add or Update** once the action had had its first check. A row's box shows
   the check's problems once the table is written.
3. **Fix it of a row landed on the action's name:** the row's boxes are drawn once the document type is read, which
   takes a request. The focus goes there when it is read, unless it moved on meanwhile.
4. **1,5 for a decimal was stored as 15:** the server read the comma as a thousands separator. It reads a point only,
   and the value box writes a decimal comma as a point.
5. **The item's name had no Insert merge tag**, though IA 5.3 says it does. It has.
6. **Who may only read saw "Document type: 1 chosen"**; they see the type's name.
7. **Another property kept a value written for the one before** where the new one stores another form (Yes over
   "Hello"). The value stays where it fits; otherwise its box starts again, a yes-or-no one at Yes, as it shows.
8. **A table emptied by hand came back with its starter rows** after another type and back. It stays empty.
9. **A row not finished survived another document type**, naming a property the new one lacks. It goes with its type.
10. **The new check codes had no backoffice words,** and the loop's sentence was English only. Each has a code
    (`ActionCheckCodes`) and its words, the loop's naming the other action.
11. **A test of the visitor's page or site root without an item to stay within** said the item picked was gone. It
    says to pick the item.
12. **ADR-0192 said a site's own type that gave sentences compiles as before;** one that built them as a list of
    strings converts each with `ActionTestLine.FromString`. The ADR says so.
13. **A field that is gone was a blank select** and a key in read-only. Both say "A field that is gone".
14. **"Saves a Article":** the card and the lines say "an item of type Article".
15. **Properties came out of Umbraco's order** across tabs and compositions. They come as Umbraco shows them, tested
    with two tabs, a group in each and a composition's group of the same alias.
16. **Who may only read saw the item picked without what it is for.** It is under "Only within this item" or "Under this
    item".
17. **The document type and the state never said they were invalid** to who hears them. Both selects carry
    `aria-invalid` with the check's problem.

One was decided against: **refusing the loop only with manual approval** (18). A move to Rejected starts the Rejected
actions whatever the form's approval, so two Set state actions loop without it too; the refusal stays.

## The tamper round

77 tampers ran in a throwaway clone of the commit before the step with this step's files in it, each taking back one
rule and counted as caught only where a named test failed: 27 on the server (the check's codes, the dry run's lines,
the site's document types and their order, Set state's pair and count) and 50 in the backoffice (the model, the
editors, the sidebar, the test's words, the card and the check's words). 63 were caught at once. The 14 missed each
got a test, and all were caught on the rerun:

- a blank document type given the code of one the site lacks;
- a dry run that sets one property without its line;
- a file upload's answer said to fit any property;
- a composition's tab sorted before the type's own coming after them;
- the media folder shown without a file upload, or hidden while it holds a folder;
- a row not finished kept where the next type's table is the same;
- a decimal comma written as typed;
- who may only read seeing no media folder, or Stop the rest always off;
- a warning that stays toasted as one that goes;
- the card naming the document type by its alias;
- the loop naming a Set state action that is off.

The switch's help, which the look found, has its own tamper, caught too.

## What the journeys found

`content-editors.spec.ts` runs in Edge against the demo site: a content action configured with the demo's page type,
its properties from the table, the item's name with a merge tag and the home page picked with Umbraco's picker, then its
dry run and the draft as the server keeps it; Change the entry's state configured and tested; and the form check's Fix
it of a property the type lacks landing on its row. Beside them ran the actions view's other journeys (the list, Test,
the email and the webhook editors). Three failed at first, each on words this step's reviews had changed: a row's
choice now reads "The answer to Headline", the check says a property the type lacks in the backoffice's words, and Set
state's choice is "Move the entry to". The journeys say the new words; all fifteen pass.

## The checks

Since the owner's message of 2026-11-06 (ADR-0194) the full gate runs at the phase review, Phase 10's here, and a step
is finished on its focused checks: the formatters, the build, `dotnet format`, the unit tests, the integration tests of
what it touches, the backoffice's lint, typecheck and tests, the journeys' typecheck and the journeys above. WORKLOG
has their results.
