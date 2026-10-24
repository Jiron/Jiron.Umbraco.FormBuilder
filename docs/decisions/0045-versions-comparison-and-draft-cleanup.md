# 0045. Versions: the list, the comparison, restore, pins and the draft cleanup

- Status: accepted; amends [ADR-0002](0002-form-definition-model-versions-and-public-projection.md) (where a restore
  records the version it came from, pins and comments outside the form's concurrency version, the diff's shape and the
  draft cleanup's setting)
- Date: 2026-10-24
- Roadmap step: 4.5b (decided and built); 4.7 maps it to the management API, 10.3 builds the History view and its
  settings, 13.x adds the other parts of the retention job

## Context

ADR-0002 keeps every save and publish as a row of `jfbFormVersion`, lets a version be restored, pinned and commented,
has the retention job keep published, pinned and entry-bound versions plus the last 50 drafts and the drafts of the
last 90 days, and says that a comparison matches elements by key and reports elements added, removed and moved, and
properties changed, per culture for texts. ADR-0044 built the form service on which this step adds the versions. §4.8
makes how long draft saves stay a `general` setting for every site at once, and ADR-0007 sets the rules every job
follows. The History wireframe (IA 5.6) asks for more than ADR-0002 settles:

- **The list** shows the published versions, with the draft saves on request, and for each row what changed
  ("4 fields, After sending, 1 rule"), so a row says something without being opened.
- **The comparison** of two versions, and of a version with the current draft, is in plain language: fields added and
  removed, a property of a field with both values, a rule added, a recipient added to an action.
- **Restore** acts at once, from a row's menu, and Undo restores the draft before it.
- **Pin and comment** sit in the same menu.

Open questions: what a row's "changed" compares with; how the comparison names properties so that the backoffice can
word them; whether a pin or a comment is a change to the form, which would make an editor's next save fail as stale;
how the cleanup stays within a job's 10 seconds on a site with a long history; and where "restored from version N"
(ADR-0002) is kept.

## Options

**What a row's changes compare with:**

1. **The row below it in the list.** In the list of publishes that is the previous publish; with draft saves shown, a
   publish would show only what changed since the last draft save, often nothing.
2. **A publish with the previous publish, a draft save with the version before it,** whatever the list shows: a
   publish says what visitors got that is new, a draft save what that save did.

**The comparison's shape:**

1. **A text diff** of the two JSON documents. It knows nothing of pages, fields or rules, so a moved field reads as a
   removal and an addition, and nothing can be worded per field.
2. **A change per element,** matched by key: what happened to it (added, removed, changed), whether it moved, and each
   property that changed with both values, by a path the backoffice words.

**Pins and comments:**

1. **A change to the form** that raises `ConcurrencyVersion`, like every other change (ADR-0002). An editor whose
   colleague pins a version then gets `concurrency_conflict` on their next save, for something that changed nothing
   they edit.
2. **A change to the version only:** one statement on the version's row, which the form's concurrency version does not
   see.

**The cleanup:**

1. **One statement** that deletes every draft the rules allow. On a site with thousands of old drafts it runs long and
   holds every distributed job of its server meanwhile (ADR-0007).
2. **A read that nominates drafts, and deletes in bounded units,** each of which checks again what can change after
   the read, until the time budget is used up; the next run continues.

**"Restored from version N":**

1. **A column on the version row.** The schema gains a column the History design does not show.
2. **The audit entry and the notification** of the restore carry it; the row is a draft save like any other.

## Decision

Option 2 in each case. In detail:

### The service

The form service of ADR-0044 gains, on the same rules (its own unit, checks, a cancelable notification, one unit with
the audit entry, the notification after the commit; a missing form or version gives null):

- `ListVersionsAsync(key, includeDrafts, page)`: the form's versions, newest first, the publishes only unless
  `includeDrafts`, at most 50 per page. Each item has its number, kind, the revision it published, comment, pin, times
  and users, whether it is the form's draft or the version visitors get, and its changes: for a publish, against the
  previous publish; for a draft save, against the version before it; none for the first of either (the backoffice says
  "first version").
- `GetVersionAsync(key, number)`: one version with its definition, read with `ReadStored` (ADR-0042), and the problems
  a save would find in it now (a version above a lowered limit opens with `limit_exceeded`), or those of a stored
  document that cannot be read.
- `CompareAsync(key, fromNumber, toNumber)`: the comparison of two versions, both read and upgraded to the current
  schema first, so a version stored in an older schema compares by what it means. A stored document that cannot be read
  is refused with its read problems. Comparing a version with the current draft names the draft's number, which
  `GetDraftAsync` gives.
- `RestoreVersionAsync(key, expectedConcurrencyVersion, number)`: copies the version's definition into a new draft
  row. The current draft stays in the History as the row it already is, so a restore loses nothing, and Undo is another
  restore. It is a draft save: a stale concurrency version and a form in the trash are refused first, a definition
  whose key is not the form's with `invalid_value`, the save checks run with the site's limits (a version stored above a lowered limit is refused with `limit_exceeded`), and a version
  whose definition is the draft's writes nothing. It raises `FormSavingNotification` and `FormSavedNotification` with
  `RestoredFromVersion` set, so a handler that follows draft saves sees restores too.
- `PinVersionAsync(key, number, pinned)` and `CommentVersionAsync(key, number, comment)`: one column each, so a pin and
  a comment at the same moment both stay. They change the version's row only: the form's concurrency version and its
  last change stay as they are. A form in the trash refuses them with `trashed`, also in the statement itself. A
  comment is at most 500 characters; an empty or blank one clears it. A pin or a comment that is already so writes
  nothing. Each raises `FormVersionUpdatingNotification` (cancelable) and `FormVersionUpdatedNotification`, with the
  version's number, comment and pin.

**Audit actions:** `form.versionRestored` (details: the new version and the one it came from), `form.versionPinned`,
`form.versionUnpinned` and `form.versionCommented` (details: the version, and whether it has a comment now, never its
text), and `form.draftsPruned` (the versions the cleanup removed). This also settles "restored from version N"
(ADR-0002): the audit entry and the notifications carry it, not the row.

### The comparison

`DefinitionDiff.Compare(from, to)` (`Core.Definitions.Comparison`) compares the canonical forms of two definitions
(ADR-0034), so a default left out and a default written compare alike.

- **Elements:** the settings groups (`submission`, `availability`, `protection`, `privacy`, `appearance`, `developer`),
  pages, groups, fields (a repeatable group's children included), the rules of pages, groups, fields, actions and
  outcomes, form-level validation rules, actions and outcomes. Elements with keys are matched by key, settings groups
  by name.
- **Each change** names the element's kind and key (or the settings group), its parent (the page of a group, the group
  or repeatable group of a field, the element a rule belongs to), its default-culture label (a page's or a group's
  title, a field's label, an action's name), a field's alias and type and an action's type, and one of `added`,
  `removed` or `changed`. Its own properties come along before and after, without its key and without the elements it
  holds, so the backoffice can word a rule's conditions or an added field without another request.
- **Moved:** an element moved when its parent changed, or when its order among the siblings that both versions share
  changed. Of those, only the fewest that explain the new order count as moved (the ones outside the longest run that
  kept its order), so moving one field does not mark every field after it. The order of an element's rules has no
  meaning and is not compared.
- **Properties:** each change has a path of segments from the element (`["label", "de"]`, `["settings", "maxLength"]`)
  and both values as JSON, null where one side has none. Objects are compared property by property, so a text is
  compared per culture and a type's settings per setting. A field's option items are matched by `value` and a grid's
  rows by `key` (`["options", "items", "phone", "label", ""]`). When the newer list is not the older one's kept
  items followed by the new ones, their order comes as one more change: the list of values or keys before and after
  (`["options", "items"]`). Other lists (conditions, checks, recipients, former aliases) are compared
  as whole values, and the backoffice words what was added or removed.
- **Order of the changes:** the settings groups, then pages, groups and fields as the newer version shows them with
  each element's rules after it, then validation rules, actions and outcomes, then what the newer version no longer
  has, in the older version's order.

### The draft cleanup

`general.history` is `[GlobalOnly]`: `{ "draftDays": 90, "draftCount": 50 }`. A draft save stays while it is one of
the form's `draftCount` newest draft saves (0 to 1,000) or younger than `draftDays` (1 to 3,650). The cleanup never
removes a published version, a pinned one, the form's draft, or one an entry points at (preview entries point at
drafts).

- **The retention job** `Jiron.FormBuilder.Retention` is an `IDistributedBackgroundJob` with a period of one hour. It
  runs only while Umbraco runs and no Jiron Form Builder migration is pending (ADR-0007), then runs the parts of
  retention in order within a budget of 8 seconds, so `ExecuteAsync` returns within 10. The draft cleanup is its first
  part; entries, mail and files join it in later steps. A part that fails is logged and the next one runs; the job
  runs again after its period.
- **The cleanup** reads the general settings once per run. One read nominates up to 200 drafts, by form and number: those past the newest `draftCount` of their form, older than the cutoff, not pinned, not a form's
  draft and without entries. Each form's nominees are then removed in a unit of their own with its `form.draftsPruned`
  entry, each by the conditional delete of 4.3a, which checks again with a locking read what can change after the read:
  the pin, the form's draft and published versions, and the entries. Which drafts are the newest only ever grows past a
  nominee, and a draft's age only grows, so a nomination never becomes wrong by the time of the delete. A change to the
  setting during a run applies from the next run.
- The run continues with the next nominations while the budget lasts and a round removes something, and leaves the
  rest to the next run. It removes only rows of `jfbFormVersion`; nothing else refers to a draft save.

## Consequences

- The History rows say what changed without a request per row, and the comparison can be worded field by field in
  the backoffice's languages, since it carries paths and values, never English sentences.
- Restoring is a draft save that handlers already understand, and nothing is lost by it.
- A pin or a comment never makes another editor's save fail.
- A site with a long history is cleaned up over several hourly runs, each holding Umbraco's job runner for at most a
  few seconds.
- A comparison of large forms reads both definitions in full; the list reads the definitions of at most 51 versions
  per page, plus the publish before the oldest one.
- ADR-0002's "Versions" now points here for where a restore records its source, and §4.8 names `general.history`
  (WORKLOG).
- **Revisit** if editors want to compare more than two versions at once, if a site's History is so long that the
  nominating read becomes slow, or if the backoffice needs a summary the element changes cannot give.
