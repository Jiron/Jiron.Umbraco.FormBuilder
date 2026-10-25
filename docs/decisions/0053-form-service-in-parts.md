# 0053. The form service in parts, and one change pipeline

- Status: accepted; amends [ADR-0044](0044-form-service.md) (the form service in three parts),
  [ADR-0045](0045-versions-comparison-and-draft-cleanup.md) (the versions in a service of their own) and
  [ADR-0046](0046-copies-templates-and-import.md) (copies, templates, imports and exports in the exchange)
- Date: 2026-10-25
- Roadmap step: 4.8d (decided and built); 15.1 makes the parts public

## Context

The Phase 4 review found the form service of ADR-0044 at 23 operations over 15 dependencies in about 1,600 lines:
the lifecycle, the trash, the versions of ADR-0045, and the copies, templates, imports and exports of ADR-0046. Three
of its dependencies (option lists, themes, templates) served only the copies and imports. The steps of a change (the
refusal inside a caller's unit, the cancelable notification, the write with its audit entry, the notification after
it) were written out about twelve times; the check that a call runs in its own unit four times (forms, folders,
access, settings); the notification helpers twice; and the refusal of something in the trash in three services and
four repositories, worded three ways. 15.1 makes the service public, after which its shape cannot change in a minor
version.

## Options

1. **One service in partial files, as it was.** One interface for every consumer, and the duplication stays.
2. **Split it by job, with shared parts inside Core.** Three interfaces: the lifecycle with the trash, the versions,
   and the exchange. What they share moves to internal helpers, and one change pipeline serves every service of Core.
3. **A class per operation** behind a dispatcher. Fine-grained, but a public surface of twenty types to learn, and a
   dispatcher that nothing else in Jiron Form Builder needs.

## Decision

Option 2.

### The parts

| Interface | Operations | Rules from |
| --- | --- | --- |
| `IFormBuilderFormService` | a form by key or alias, the draft opened for editing, create from blank, save, publish, unpublish, rename, move, trash, where a restore lands, restore, delete for good | ADR-0044, ADR-0047 |
| `IFormBuilderFormVersionService` | the History, one version, the comparison of two, restore a version, pin, comment | ADR-0045 |
| `IFormBuilderFormExchangeService` | duplicate, copy actions, create from a template, import, export | ADR-0046 |

Each is a singleton with its own registration, and the boot test resolves each. The limits and audit action names move
with their operations (`FormBuilderFormVersionService.MaxVersionsPerPage`, `FormBuilderFormExchangeService.ImportedAction`
and the others); the stored audit actions, the notifications and the error codes stay as they are.

What the parts share stays internal:

- `FormDocuments`: a definition as the stored document and back, the draft, the site's limits, a form as the services
  answer with it, and the folder a form may go to;
- `FormChecks`: the concurrency version, the name, the alias, the comment and the trash;
- `FormCreation`: a blank design, the new-form defaults, and the create with the first free alias. A create is the form
  service's change wherever it starts, so it logs as the form service.

### One change pipeline

`FormBuilderChanges<TService>` holds the steps every change of Core's services takes:

- `EnsureOwnUnit`: refused inside a caller's unit of work or an Umbraco scope, with the service's own reason;
- `CancelableAsync`: a handler that cancels stops the change with `cancelled` and its messages;
- `WriteAsync`: the write and the audit entries made of its result in one unit of work, none for a change that found
  nothing to change; an entry that cannot be made rolls the write back;
- `PublishedAsync`: the notification after the commit; a handler that fails is logged with what changed, and the
  change stands.

It is registered as an open generic singleton, one per service, so each logs under its own category as before. The
form parts, the folder service, the access service and the settings service use it; the access changes and the settings
save have no cancelable notification and take the steps they have. A form's permanent delete keeps its own batches in
`FormRemoval` (ADR-0047), each a unit of work with its audit entry.

### One trash check

`LibraryTrash` (`Core.Persistence`) words the refusals once, for the services before a change and for the repositories
inside the write: `trashed` ("The form is in the trash; restore it first.") and `not_trashed` ("Only a form in the
trash is deleted; put it in the trash first."), for forms and folders alike.

## Consequences

- The form service has 10 dependencies, the version service 4 and the exchange 10, and a consumer depends only on the
  part it uses: the version endpoints on the version service, and on the form service only for the draft a restore is
  compared with.
- A new change in any service of Core takes its steps through the pipeline, so none forgets its unit, and a failing
  handler is logged the same way everywhere, naming the form, "the form library" or the settings section.
- 15.1 makes the three interfaces public with their records, each with its snapshot test.
- **Revisit** if a part grows past one job again, or if a service needs a step the pipeline lacks, such as a change
  that spans units, which today only the permanent delete makes.
