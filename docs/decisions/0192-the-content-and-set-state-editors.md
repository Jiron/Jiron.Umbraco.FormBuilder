# 0192. The content and Set state editors

- Status: accepted
- Date: 2026-11-06
- Roadmap step: 10.2e; builds on [ADR-0162](0162-state-changes-verification-and-content.md) (the content action and
  state changes), [ADR-0173](0173-rich-text-merge-tags-pickers-and-patterns-in-the-inspector.md) (the site's items as a
  setting's choices), [ADR-0188](0188-the-actions-view.md), [ADR-0190](0190-test-an-action-as-the-sidebar-holds-it.md)
  and [ADR-0191](0191-the-webhook-slack-and-teams-editors.md) (the box editors and their kept values)

## Context

The content action (8.5) creates or updates a content item from an entry: a document type, a parent (picked, or the
visitor's page or site root within a picked item), a name with merge tags, a mapping of properties to a field's answer
or a value written for it, publish, update by name, a media folder and the sensitive fields it may copy. The Set state
action moves the entry to Approved, Rejected or Spam under its rule. Before this step their sidebars drew what the
inspector knew: the document type's select said the site had none (the action types served no choices), the place
was a choice and a picker apart, and the mapping had no editor. Their tests answered as dry runs with one English
sentence each (ADR-0190). What the survey found:

1. **The document types.** Umbraco lists document types only to users of its Settings section; most form designers
   have none. The option kinds already serve the site's page types as a setting's choices (ADR-0173), but the action
   types endpoint had no way to.
2. **The properties.** The mapping needs each property of the chosen type, what its editor can hold and whether it takes
   a value written for it, by the content action's own rules (`Fits`, `TryConvert`). Umbraco's document type endpoints
   need the Settings section too.
3. **Problems at a row.** The check said every mapping problem at `mapping`, so a Fix it could land only on the table,
   and a sidebar with ten rows could not say which one was wrong.
4. **What a test says.** "An entry would save a Note." tells the editor little, in English only.
5. **A loop.** A move to Approved starts the Approved actions and a move to Rejected the Rejected ones, and Approved and
   Rejected may move to each other. A Set state action on each trigger moving to the other's state would move an
   entry back and forth for ever, every action of both triggers running each time (mail too). Nothing refused it.
6. **A repeatable group's field.** The check took a field in a repeatable group, whose answer has a row each; the run
   took the first row's.

## Options

For the document types and the properties:

1. **Umbraco's own endpoints**, which most designers may not call.
2. **The content action's own**: its document types served as its setting's choices, and an endpoint that answers one
   type's properties with what each takes, by the action's rules.

For a test's lines:

1. **A sentence each,** as now.
2. **A code and its args each**, which the backoffice words, with the sentence for a code it does not know.

For the loop:

1. **A guard at run time**, which would stop the second move after the first.
2. **The form check refuses the pair**, so it never publishes.

## Decision

**Option 2 in each.**

- **An action type may serve the site's items as a setting's choices** (`IFormBuilderActionType.SiteChoicesAsync`, a
  default member that serves none, virtual in `FormBuilderActionTypeBase`), which `GET action-type` applies, as the
  option kinds do. The content action serves its document types an item may have, by name, element types left out.
- **`GET action-type/content/document-type/{alias}`** (any user of the Form Builder section, as the action types)
  answers a document type an item may have with its properties a form can set, its compositions' included, in the
  order Umbraco shows them (the groups outside every tab, then each tab, its own properties before its groups; a
  composition's container of the same alias is the type's own): each with its name, its group, the field types whose answer it can hold, and what a
  value written for it must be (`text`, `wholeNumber`, `decimal`, `date`, `boolean`, or none where only an answer
  fits, as for media). A type the site lacks, or an element type, is not found (`document_type_not_found`).
- **The editors.** Where it goes is one box (`contentParent`): three choices, as When it runs and the webhook's ways,
  each with its line (under an item you pick, or under the visitor's page or site root within one), and the item picked
  set in under the chosen one, named "Under this item" or "Only within this item" to who hears it. Properties is a
  table (`contentMapping`): each row a property of the type and what it takes, "The answer to" a field it can hold
  (one answer each), a field it can't hold or one that is gone said so, or a value in the form it stores (words with
  Insert merge tag, a whole number, a decimal with a point, which a decimal comma becomes, a date, Yes or No). Add
  property offers the properties not set yet that the form can fill, by their groups; a row whose property nothing
  fills says so. A row stays out of the draft until it has both, and the table's rows are kept while it is open, as
  the webhook's are. Another property keeps a value written where it stores it the same way. Another document type
  keeps the table of the one before while the sidebar is open, an emptied one too; one chosen for the first time
  starts with the properties a field of the same alias can fill. A failed read has Try again. Media folder for files
  (`contentMediaFolder`) shows where a row takes a file upload's answer, or where it holds a folder. The item's name
  has Insert merge tag, as every action's text that takes tags, the webhook's within its own editors. Publish it, Update
  an item of the same name (a visitor's answer can choose the item) and the media folder have help; a switch says its
  help under it and is described by it, which the inspector's switches did not before. Umbraco's pickers
  say the check's problems under them, and who may only read sees a picked item as Umbraco's own read-only input names
  it, under what it is for.
- **Problems at their row.** The check says a row's problem at its part: `mapping[2].property`, `mapping[2].field`,
  `mapping[2].value`. `settingAtPath` keeps the row, and the sidebar leads a row's path to that row's box where its
  editor draws rows (`ROW_EDITORS`); a row's ids follow its place among the rows the setting holds, so an unfinished
  row among them changes nothing. A row's box shows the check's problems once the table is written, without waiting
  for Update, and a Fix it of a row waits for the document type to be read, unless the focus has moved on by then. A
  property set twice is a warning at the later row, an element type no document type an item can have, and a
  repeatable group's field refused, since a property holds one answer; a version published before still runs, and a
  sensitive group's field stays out of it unless the action names the group.
- **Codes the backoffice words.** Each problem of the content and Set state checks has a code of its own
  (`ActionCheckCodes`, through `ActionSettingsCheck.Invalid(setting, code, message)`), which the form check and the
  sidebar say in the backoffice's language without restating the row it is under; the English sentence stays for the
  API and a backoffice that does not know the code. A decimal is read with a point only, so 1,5 is refused rather than
  stored as 15.
- **A test's lines** are `ActionTestLine`s: an English sentence, a code and its args. A sentence converts to a line
  without a code, so a site's own type gives sentences in a collection expression as before; a list of strings it
  built takes `ActionTestLine.FromString` for each. The content action's dry run says where a run
  would save or publish the item and its name from the sample entry (`content_under`, `content_root`,
  `content_page`, `content_site`), whether it would update one of that name (`content_update`), the properties it
  sets (`content_sets`) and each it would leave out: a property the type lacks, a field the form lacks, a sensitive
  field not copied, an answer or a value the property cannot hold; a test that would leave something out is a
  warning, which stays until it is closed. It fails as a run would where the document type or the parent is gone, or
  the visitor's page or site root has no item to stay within (`target_missing`, with which), or the name comes out
  empty (`no_name`). Set state says the state and how many actions the move starts (`state_move`). The card and the
  lines name the type as "an item of type Story", which no type's first letter can make wrong.
- **The loop is refused** by Set state's own check: an action on Approved that moves to Rejected while an action on
  Rejected moves back to Approved, both on, refuses each at its state, with or without manual approval, as a move to
  Rejected starts the Rejected actions either way. A move to the state the trigger's entries are in already is a
  warning: it changes nothing. Set state's choice is "Move the entry to" Approved, Rejected or Spam, whose help says
  what approving or rejecting starts.

## Consequences

- `ActionTestResult.Preview` holds lines; the test endpoint answers each line's text, code and args. A site's type
  that built its sentences as a `string[]` or a `List<string>` converts each with `ActionTestLine.FromString`.
- The action types snapshot (`docs/api/action-types.json`) and the OpenAPI snapshot change; the permission matrix has
  the new endpoint.
- A form whose content action mapped a repeatable group's field, or two Set state actions that would loop, no longer
  publishes; the form check says why at the setting.
- The card says what a content action saves or publishes ("Saves an item of type Story"), by the name the site gives
  the type.
- The check codes join the form check's own words (`OWN_WORDS`); the dictionaries' test reads them from
  `ActionCheckCodes`.
- 10.2f's Details offers the same Test, with the same lines.
