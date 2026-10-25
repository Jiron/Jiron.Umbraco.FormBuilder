# 0051. The form endpoints of the management API

- Status: accepted; amends [ADR-0050](0050-management-api-base.md) (a folder's restore that lands somewhere else)
- Date: 2026-10-25
- Roadmap step: 4.7b (decided and built); 4.7c adds the versions on it

## Context

4.7a built the management API's base and its first endpoints, the library tree and folders ([ADR-0050](0050-management-api-base.md)).
4.7b adds the forms over the form service of ADR-0044: search, get, create, save the draft, publish, unpublish,
rename, move, trash, restore and delete for good. Four questions were open:

- **How a definition travels.** The definition is a versioned JSON document (ADR-0002, ADR-0034) with its own reader,
  which upgrades older schemas, applies the site's limits and names every problem by its JSON path. An OpenAPI model of
  the C# records would describe a second shape, written by other converters than the reader's.
- **Who may read a draft.** ADR-0013 gives `design` the drafts, and every permission lets a user see the form. A user
  who handles entries needs the form's name and state, not its draft.
- **A save that changes the actions.** ADR-0013 gives adding and changing actions to `manageActions`, which needs
  `design`. A draft holds its actions, so a save with `design` alone could change them.
- **A restore that lands somewhere else.** A form or folder in the trash goes back to its folder, or to the root when
  that folder is in the trash too (ADR-0047). At the root other grants reach it than in its folder, so a restore can
  give groups more access, as a move does, which ADR-0013 lets only `managePermissions` do. 4.7a's folder restore did
  not ask.

## Options

For the definition:

1. **An OpenAPI model of the definition.** Typed in the generated client, but it is a second description of the
   document that can drift from the reader, and the reader's problems would no longer point into what the client sent.
2. **The document itself:** the request and the answer carry the definition as a JSON object, which the reader reads
   and writes.

## Decision

**Option 2**, and the rules below, in `Web/ManagementApi/Library`:

- **The definition** travels as the document itself, in a property `definition` that the OpenAPI document leaves as
  any JSON value (`{}`). A save reads it with the site's limits; a refusal answers `400` with every problem, its `path`
  being a JSON path into the document (`$.pages[0]…`). The draft answers with the canonical document. The designer's
  own types for it come with the designer (Phase 9).
- **Concurrency:** every change sends the `concurrencyVersion` it read, in the body, or in the query for a delete; a
  stale one answers `409 concurrency_conflict`.
- **What the form endpoints need:**

  | Endpoint | Needs |
  | --- | --- |
  | search | the section; forms outside the trash whose name or alias contains the text, that the user sees, by name, paged in the database, each with the user's permissions |
  | a form | seeing it; its name, state and the user's permissions on it |
  | a form's draft | `design` on it |
  | create | `design` on the folder it goes in (at the root: the folder-level part of the global grants) |
  | save the draft | `design` on it; a save that changes the actions also `manageActions` |
  | publish, unpublish, rename, trash | `design` on it |
  | move | `design` on it and on the new folder; `managePermissions` and the confirmation `widenAccess` when it widens access |
  | restore | `design` on it; when it lands somewhere else, also what a move there needs |
  | delete for good | `design` and `deleteEntries` on it |

- **A save that changes the actions** is one whose comparison with the stored draft (ADR-0045) adds, removes, changes
  or moves an action, or a rule of one. It is compared after the concurrency check, so a stale save answers
  `409 concurrency_conflict` rather than naming a permission for a change someone else made; without `manageActions`
  it answers `403 forbidden` naming it. A draft that cannot be read counts as one without actions.
- **A restore that lands somewhere else** (a form whose folder is in the trash, a folder whose parent is) is checked
  as a move to the root: `design` there, and when it widens access `managePermissions` and the confirmation
  `widenAccess`, with the same answers. A folder's restore takes an optional body for the confirmation; this amends
  ADR-0050, whose restore did not ask.
- **The answers:** a form names its key, alias, name, folder, `concurrencyVersion`, publish state and revision,
  restriction, trash time, creation and change times, and the user's permissions. The draft adds the definition, its
  problems under the current limits, the draft's and the published version's numbers and whether there are
  unpublished changes. A save or a publish answers the form and the number of the version it wrote, null when nothing
  changed. A delete answers what went, as a folder's does.

## Consequences

- The client sends and receives the document the reader reads, so a problem's path always points into what was sent,
  and the document's schema version travels with it.
- The generated client types the definition as `unknown`; the designer narrows it with its own types.
- A designer without `manageActions` can change everything in a draft but its actions; the backoffice can tell before
  saving, because the form names the user's permissions.
- A restore can no longer widen access past `managePermissions`; restoring into the folder it came from asks nothing
  more than before.
