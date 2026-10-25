# 0052. The version endpoints of the management API

- Status: accepted
- Date: 2026-10-25
- Roadmap step: 4.7c (decided and built)

## Context

ADR-0045 gave every form a history of versions: draft saves and publishes, each with its comment, pin and who made
it, a comparison of two versions element by element, a restore that copies a version into a new draft, and pins and
comments that leave the form's concurrency version alone. 4.7c puts them in the management API on the base of
[ADR-0050](0050-management-api-base.md), next to the forms of [ADR-0051](0051-form-endpoints.md). Three questions
were open: what each endpoint needs, how a restore relates to the actions, and how the comparison's kinds and
values travel.

## Decision

- **Every version endpoint needs `design` on the form** (ADR-0013): the history shows definitions, and only a
  designer works with them. A form the user does not see answers `404 form_not_found`, a version that does not exist
  `404 version_not_found`.

  | Endpoint | What it does |
  | --- | --- |
  | `GET form/{key}/versions` | the publishes, newest first, and the draft saves too with `includeDrafts`; at most 50 a page; each with the summary of what changed, null when it is not known |
  | `GET form/{key}/versions/{number}` | the version with its definition document (ADR-0051) and why a restore of it would be refused now |
  | `GET form/{key}/versions/compare?from=&to=` | what changed from one version to the other, element by element, each property before and after |
  | `POST form/{key}/versions/{number}/restore` | copies the version into a new draft; it sends the `concurrencyVersion` it read |
  | `PUT form/{key}/versions/{number}/pin` | pins or unpins it, so the cleanup keeps it |
  | `PUT form/{key}/versions/{number}/comment` | sets its comment, or clears it |

- **A restore is a save of the version's definition**, so a restore that changes the actions needs `manageActions`
  as a save does (ADR-0051): the version's definition is compared with the draft's after the concurrency check. A
  version whose definition cannot be read is left to the restore, which refuses it with its problems.
- **Pins and comments** send no concurrency version, because they do not change the form (ADR-0045).
- **Kinds travel as stable names**, the camel case of the model's names: a version is a `draft` or `published`; an
  element of a comparison a `settings` group, `page`, `group`, `field`, `rule`, `validationRule`, `action` or
  `outcome`; a change `added`, `removed` or `changed`. A property's path is its list of segments, and its values
  before and after are JSON, as in ADR-0045.

## Consequences

- The History of the designer (Phase 9) reads one list endpoint and opens a version or a comparison only when the
  editor asks.
- A designer without `manageActions` can restore any version that keeps the actions as they are now.
- A new kind of element or change in the comparison is a new name in the API, never a renumbered value.
