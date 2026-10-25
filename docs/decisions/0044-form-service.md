# 0044. The form service: lifecycle, refusals, notifications and the field catalogue

- Status: accepted; amends [ADR-0002](0002-form-definition-model-versions-and-public-projection.md) (a save that
  changes nothing writes no version); the new-form defaults for forms from a template amended by
  [ADR-0046](0046-copies-templates-and-import.md) in 4.5c; the trash, the permanent delete and the folder service
  added by [ADR-0047](0047-trash-delete-and-folders.md) in 4.5d; split in three parts with one change pipeline by
  [ADR-0053](0053-form-service-in-parts.md) in 4.8d; its field types come from the registry of
  [ADR-0055](0055-field-type-framework.md) since 5.1a
- Date: 2026-10-24
- Roadmap step: 4.5a (decided and built); 4.5b to 4.5d add versions, copies and the trash on the same rules, 4.7
  maps the refusals to the API, 5.1 takes over the storage kinds, 6.1 adds the published-form cache, 8.3 the
  notify-the-creator default, 9.5b the form check, 12.5 the mirror file, 15.1 the public surface

## Context

Step 4.5 puts a service over the form repositories of 4.3a: create, save, publish and unpublish, versions, copies,
the trash, folders, and a notification for every lifecycle event. ADR-0002 fixed the document, its versions and the
public projection, ADR-0035 what publishing refuses, ADR-0036 the units of work and the two exceptions of the
repositories, and ADR-0042 that stored definitions are read with the highest limits. The step was split in four
(WORKLOG); this record decides what the first part needs and what the other three build on:

- **The unit of work.** A publish writes a version, the form's row, the field catalogue and an audit entry together.
  Notifications that say "it happened" must not reach handlers before the commit, or for a change that rolled back.
- **Refusals.** The API (4.7) needs one stable code per refusal: a stale version, a taken alias, a definition that a
  save or a publish refuses (with every problem and its path), a form in the trash, a handler that cancelled.
- **Saves without changes.** ADR-0002 appends a version for every save. A designer that saves twice without a change
  would fill the History with identical versions.
- **Trashed forms and folders.** The repositories let a draft be saved, a form be published and a form be moved into
  a folder while the form or the folder is in the trash.
- **Aliases.** A form alias follows the field alias pattern and is unique among all forms, but an editor creating a
  form types a name, not an alias.
- **The field catalogue.** ADR-0003 gives every published field a row, with a storage kind, before its first value;
  the storage kinds belong to the field type framework of 5.1, which comes later.
- **The new-form defaults** (§4.8) are a consent field and an action that notifies the form's creator. General
  settings are per site, but a new form belongs to no site, and the email action arrives in 8.3.
- **Who reads the published version.** ADR-0002 has a cache refresher tell every server about a publish, but nothing
  caches a form yet.

## Options

**The unit of work:**

1. **Join the caller's unit.** A service call could then be part of a larger change, but its "ed" notifications would
   need Umbraco's scoped notifications, and 4.5d deletes stored files after a commit it would not own.
2. **A unit of its own for every change,** refused inside a caller's unit or an Umbraco scope, as the settings save
   (ADR-0043): the cancelable notification before the unit, the "ed" notification after the commit.

**Refusals:**

1. **A result type per operation.** Every caller unpacks it, and the API maps each type.
2. **Exceptions,** the repositories' two plus a validation exception that lists the problems and a cancellation
   exception that carries the handlers' messages, and **one mapping from an exception to its code**, which the API
   uses and the tests check.

**A save without changes:**

1. **Append a version anyway** (ADR-0002 as written).
2. **Write nothing** when the definition's hash is the draft's: no version, no audit entry, no notification. A stale
   version is still refused first, so "a stale save returns `concurrency_conflict`" holds for every save.

**Trashed forms and live folders:**

1. **Read first, then write.** Two statements, and a trash between them goes unnoticed.
2. **Conditions of the write** (ADR-0036): a save, a publish, a rename and a move require the form outside the trash;
   a create and a move require a folder that exists outside the trash, while a form that stays in its folder needs
   nothing of it. When the write changed nothing, the repository reads to say why. Unpublishing applies in the trash
   too: it only lessens what visitors get, and it lets go of the form's theme, which a trashed form keeps (ADR-0037).

**The new-form defaults:**

1. **Per site,** as the other general fields. A form is created in the library, not on a site, so no site applies.
2. **For every site at once** (`[GlobalOnly]`), with the notify-the-creator default moved to 8.3, the step that builds
   the email action it needs ("settings come with their feature").

**The consent default's wording:**

1. **A wording that ships with Jiron Form Builder.** A consent text is the site's own legal statement; a shipped one
   would be agreed to by visitors without anyone at the site having written it.
2. **The editor's own label and text,** per culture, stored in the setting; the default is off.

## Decision

Option 2 in each case. In detail:

### The service

`IFormBuilderFormService` (`Core.Forms`, internal until 15.1 completes the public surface of §4.9) is a singleton over
the repositories:

- **Every change is a unit of work of its own.** Called inside a caller's unit or an Umbraco scope, it throws
  `InvalidOperationException`. The order is always: the checks that need no write, the cancelable notification, one
  unit with the change and its audit entry, then the "ed" notification.
- **Reads** go through `FormDefinitionSerializer.ReadStored`, so a form stored under a higher limit keeps opening
  (ADR-0042). What the service writes meets the site's limits.
- **Forms are named by key.** The service answers with `FormInfo` (key, alias, name, folder key, concurrency
  version, whether it is published, the public revision, trash, times and users), never with row ids; opening a form
  (`GetDraftAsync`) adds the draft's definition, the draft and published version numbers, whether the draft has
  unpublished changes, and the problems of a stored definition that cannot be read at all.
- **Field types** come from the registered `FieldTypeRegistry` (amended in 5.1a, ADR-0055), so a site's own types
  publish, enter the catalogue and project as the built-in ones do.

**Amended in 4.8d** (ADR-0053): the service keeps the lifecycle and the trash; the versions of ADR-0045 are
`IFormBuilderFormVersionService`'s and the copies, templates, imports and exports of ADR-0046
`IFormBuilderFormExchangeService`'s, on the same rules. Every service of Core takes the steps above through one change
pipeline, `FormBuilderChanges<TService>`.

### Refusals and their codes

`FormBuilderErrorCodes.Of(exception)` (`Core`) is the one mapping:

| Exception | Code |
| --- | --- |
| `FormBuilderConcurrencyException` | `concurrency_conflict` |
| `FormBuilderValidationException` | the first problem's code, such as `limit_exceeded` or `alias_invalid`; every problem keeps its code, path and message |
| `FormBuilderConflictException` | `alias_taken`, `key_taken`, `folder_missing`, `folder_into_itself`, `folder_too_deep`, `folder_not_empty`, `in_use`, `trashed`, `not_trashed`, `landing_changed`, `duplicate_submission`, `challenge_used`, `file_missing`, `field_missing`, `version_missing` |
| `FormBuilderCancelledException` | `cancelled`, with the messages the handlers gave |

A missing form is no refusal: the service returns null or false, and the API answers `404`. A validation problem's
path is a JSON path into the definition (`$.pages[0]…`), or `name` or `alias` for the form's row. `Trashed` is a new
conflict: the form is in the trash, so only a restore, an unpublish or a delete applies.

### Create from blank

- **The name** is required: 1 to 255 characters, not only white space, no control characters. The backoffice sends
  its own "Untitled form".
- **The alias** is the caller's, checked against `^[a-z][a-zA-Z0-9]{0,63}$` and refused with `alias_taken` when another
  form has it. Without one, it comes from the name: letters and digits without their accents, as camelCase words, with
  `form` before a leading digit and `form` for a name with neither; then the lowest free number from 2 when it is
  taken (`contact`, `contact2`), found with one read of the aliases that start with it. A create that loses the alias
  to a writer at the same moment tries the next free one, three times at most.
- **The folder** exists outside the trash, or the form goes to the root.
- **The definition:** a new key, one page with one group, and one outcome: a message without text, which shows the
  built-in thank-you message in the visitor's language. While the consent default is set, the group starts with a
  required consent field (alias `consent`) with the editor's label and text.
- `FormSavingNotification` (cancelable, with `IsNew`) comes first, then one unit writes the form, its version 1 and
  `form.created`, then `FormSavedNotification`.

### Save

- The definition's key is the form's (`invalid_value` at `$.key` otherwise), and the save checks of ADR-0034 run with
  the site's limits, also on a definition built in code. A save above a lowered limit is refused with
  `limit_exceeded`; the stored draft stays as it was.
- A stale concurrency version is refused before the definition is checked. A definition whose hash is the draft's
  writes nothing and returns the form as it is (this amends ADR-0002, "Versions").
- The stored document is the canonical form, with its `DefinitionHash` and `PublicHash` (ADR-0035), and an optional
  comment of up to 500 characters. `FormSaving` → one unit (the draft version, `form.saved`) → `FormSaved`.

### Publish

Publishing takes the stored draft, so what goes live is what the editor saved:

1. The draft is read with `ReadStored`, which upgrades it to the current schema in memory.
2. **The publish checks** run in order, each an `IFormPublishCheck`; the first is the structural validation of
   ADR-0035 with the site's limits (`StructuralPublishCheck`). Every problem of every check refuses the publish at once. The form
   check (9.5b) is the next check; the mirror file (12.5) is a handler of `FormPublishedNotification`.
3. `FormPublishingNotification` (cancelable, with the definition).
4. **One unit:** the published version (its public revision moves only when its `PublicHash` differs from the last
   publish's, ADR-0036), the theme alias, the field catalogue (below) and `form.published`, which records the version,
   the revision and whether it moved.
5. `FormPublishedNotification`, with the version number, the revision and whether it moved.

A publish whose draft is the live version (the same definition, once both are upgraded) writes nothing and returns the
form as it is, as a save without changes does. After an unpublish, publishing the same draft again appends a version
with the revision it had, since visitors get what they got before.

**Unpublished changes** compare the draft with the live version after both are read and upgraded, so a version
stored in an older schema does not count as a change.

### The field catalogue

On every publish, in the publish's unit:

- **Rows:** every field that holds a value, the children of repeatable groups included, in document order; each row
  of a grid, under the grid's alias with the row's label and the grid as its parent; and the grid and the repeatable
  group themselves, with the storage kind `none`, so the columns of their parts have a heading even after a later
  version drops them. The label is the default text, empty without one.
- **Storage kinds** are the ones of §4.3.2 (`text`, `longText`, `number`, `date`, `bool`, `file`, `json`, `none`), a
  trait each field type declares in the registry since 5.1a (ADR-0055); 5.1c maps them to the typed columns of
  ADR-0003. The built-in types store as: text
  for the one-line text and choice types (yes/no and ranking among them), country, time, hidden and consent (a consent
  value stores the wording's hash, ADR-0009); longText for long text; json for name and address, whose values have
  parts; number for number, rating, scale, slider and calculation; date for date and date with time; bool for the
  checkbox; file for files and signatures.
- **New fields** get a row with `firstVersionNumber` set to the publish; a draft-only row (preview entries, ADR-0003)
  gets its first version now. A field that came back gets `lastVersionNumber` cleared.
- **Fields the publish dropped** get `lastVersionNumber`: the number of the publish before, the last one that had
  them. Draft-only rows stay as they are.
- Every other property follows the published definition, so a renamed label or alias shows in the entry columns.

### Unpublish, rename and move

- **Unpublish:** `FormUnpublishing` → one unit (the row, `form.unpublished`) → `FormUnpublished`, also in the trash.
  An unpublished form stays as it is, with no notification.
- **Rename** changes the name and, when given, the alias; `FormRenaming` and `FormRenamed` carry both, before and
  after, since a changed alias breaks the Razor views and tag helpers that name it. **Move** names a folder outside
  the trash or the root; `FormMoving` and `FormMoved` carry both folders. Each is one unit with `form.renamed` or
  `form.moved`, and a change to the place the form already has writes nothing.

### Notifications

Public, in `Core.Forms`, each with the form's key, alias and name and the user (null when Jiron Form Builder acts):

- `FormSavingNotification` (cancelable) and `FormSavedNotification`, for a create and every draft save;
- `FormPublishingNotification` (cancelable) and `FormPublishedNotification`;
- `FormUnpublishingNotification` (cancelable) and `FormUnpublishedNotification`;
- `FormRenamingNotification` (cancelable) and `FormRenamedNotification`;
- `FormMovingNotification` (cancelable) and `FormMovedNotification`.

The cancelable ones derive from Umbraco's `CancelableNotification`: a handler calls `CancelOperation` with a message,
and the service throws `FormBuilderCancelledException` with the messages before it writes anything. The others are
published on the saving server after the commit; a handler that fails is logged, and the change stands. 4.5b to 4.5d
add the notifications of their events.

### The audit log

Entity type `form`, item the form's key: `form.created`, `form.saved`, `form.published`, `form.unpublished`,
`form.renamed` and `form.moved`, each written in the unit of its change (ADR-0041). Details hold version numbers, the
revision, flags, folder keys and the aliases (identifiers), never a name or a text of the definition.

### Settings

`general.newForms` is `[GlobalOnly]`: `{ "consent": { "label": { "": "…" }, "text": { "": "…" } } }`, null by default.
Its validator checks both texts as a definition checks its own (`DefinitionSaveChecks.CheckText`): a default text
that is not only white space, culture names that are ones and appear once, the label on one line of at most 500
characters, the text rich text of at most 50,000. A create checks both again, since a row may be written past the
validator: the label with the definition's save checks, and the text, which those checks do not read in a field's
settings, as the validator does. A default either check refuses leaves the field out of new forms, with a warning in
the log. The roadmap's §4.8 says these defaults are for every site at once.

### Deferred, with the step that does it

- **The published-form cache (6.1):** the form endpoint reads the published version through a cache with a form
  cache refresher, which the service's publish, unpublish, rename, trash, restore and delete then raise. A refresher
  with nothing to refresh would be wiring that nothing uses (§5, the wiring sweep). A trashed form is answered as a
  missing one and takes no submission.
- **The notify-the-creator default (8.3),** with the email action.
- **The storage kinds (5.1),** declared by each type; its registry replaces the registered catalogue.
- **The public surface (15.1):** the service and its records become public, with a snapshot test.

## Consequences

- The API maps refusals with one call, and a test covers the code of every exception and conflict.
- Handlers see a create, a save, a publish and the other changes once, after they committed, and can stop them
  before they start.
- The History holds only saves that changed something.
- A trashed form or folder cannot be changed by accident through the API or a developer's code; restoring is the way
  back.
- An editor creating "Contact us" gets the alias `contactUs`, and a second "Contact us" gets `contactUs2`.
- The catalogue names every column the entries screen and the exports need, even for fields a later version dropped.
- ADR-0002's "Versions" changes as above, and §4.8, 5.1, 6.1 and 8.3 gain notes (WORKLOG).
- **Revisit** if editors want a version for every save after all (for example a save with only a comment), if the
  consent default needs more than a label and a text, or if 5.1's storage kinds differ from the ones the catalogue
  stores before it, which would need a migration of the stored kinds.
