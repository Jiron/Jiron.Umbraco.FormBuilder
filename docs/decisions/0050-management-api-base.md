# 0050. The management API's base

- Status: accepted; amends [ADR-0013](0013-permissions.md) (what a permanent delete and emptying the trash need) and
  [ADR-0049](0049-authorization-policies.md) (a policy for what a user sees)
- Date: 2026-10-25
- Roadmap step: 4.7a (decided and built); 4.7b adds forms, search and versions on it

## Context

ADR-0013 decided who may do what, and that what a user cannot see answers `404` while what they see but may not do
answers `403` with the permission's name. ADR-0049 built the policies, which check globally or against a folder or
form and say why they refused. ADR-0033 lets a site run while Jiron Form Builder's tables are missing or old, and the
management API must then answer `503 migrations_pending`. ADR-0036 and ADR-0044 gave every refusal of the services a
stable code (`FormBuilderErrorCodes`). §3.4 has the attributes of a management API controller, and §3.3 its lessons:
Umbraco documents `401` and `403` itself, a permission sweep must fill route parameters by type, Umbraco's error
handling can drop a refusal's body, and the client is generated from a committed snapshot, never from a running site.

Four questions were open:

- **Where the checks run.** ASP.NET Core checks an endpoint's `[Authorize]` policies in the authorization middleware,
  before any filter. There a policy has no folder or form to check against, a refusal is a `403` without a body, and a
  permission policy reads Jiron Form Builder's tables, which may not exist yet.
- **How an endpoint says what it needs,** so that one test can read it for every endpoint, and an endpoint that says
  nothing is caught.
- **How refusals become answers** that keep their code.
- **What a permanent delete needs.** ADR-0013's table gives `design` the trash and the restore, but not the delete,
  which removes entries. A folder in the trash takes everything below it into the delete, and emptying the trash takes
  everything in it, also a restricted folder that the user cannot see.

## Options

1. **A policy on every action** (`[Authorize(Policy = …)]`). Declarative and readable, but checked in the middleware:
   globally only, a bare `403`, and before the migration check, so a pending plan answers `500`.
2. **Checks inside each action** through `IAuthorizationService`. Any check is possible, but nothing declares it: the
   permission matrix cannot know what an endpoint needs, and an action that forgets its check is open.
3. **A declaration on each action, and one filter that reads it:** the section, then the migration state, then the
   permissions against the folder or form the declaration names.

## Decision

**Option 3**, in `Web/ManagementApi`:

- **Route and document:** `/umbraco/jfb/management/api/v1/…` (`[BackOfficeRoute]` with `[ApiVersion("1.0")]` on every
  controller, §3.4), the backoffice's JSON options, and Umbraco's `BackOfficeAccess` policy on the base controller, so a
  call without the backoffice's token answers `401`. The OpenAPI document `jfb` is registered in one composer (Umbraco
  18 replaces Swashbuckle); its operations are named `<controller><action>` for the generated client, and it carries
  the backoffice's security requirements. Actions document only their own answers, never `401` or `403`, which
  Umbraco adds. The controllers are internal, like the services they use, and a feature provider adds them to MVC.
- **The declaration:** every action carries exactly one of two attributes.
  - `[FormBuilderPermission(permissions, On = …, Key = …)]` needs every permission given, globally, or on the folder
    or form whose key the named value holds: a route value, a query value or a property of the request's body. An
    empty folder key is the root.
  - `[FormBuilderSection(On = …, Key = …)]` lets any user of the Forms section through, and, when it names a folder or
    form, only one who sees it.

  The reviewed exception list is empty; the form picker (6.7) will be its only entry.
- **The filter** runs before the request body is validated, so a refusal never reveals what the body should have held:
  1. a user without the Forms section: `403 no_section`;
  2. while the migration plan is behind (`IFormBuilderMigrationState`): `503 migrations_pending` with `Retry-After`;
  3. a form key that names no form, or a form the user does not see: `404 form_not_found`; a folder the user does not
     see: `404 folder_not_found`;
  4. each permission through its policy against the resource (ADR-0049): a denial `NotFound` answers as above, and a
     denial `MissingPermission` `403 forbidden` with the permission's name in `permission`;
  5. a section declaration that names a resource asks the new policy `Jiron.FormBuilder.Policy.Visible`, which holds
     for a resource the user sees in the sense of ADR-0049.

  The filter keeps what it read for the action: the user, and the form. An action without a declaration answers
  `403 forbidden`: it fails closed, and the permission matrix fails before it ships.
- **Refusals** become ProblemDetails in one exception filter on the base controller, each with its `code` from
  `FormBuilderErrorCodes`, a title written for editors as a complete sentence (the backoffice shows it as a toast,
  1.2b) and a detail that names no internals:
  - a validation refusal: `400`, with every problem in `errors` (`code`, `path`, `message`);
  - `concurrency_conflict`, a conflict (`alias_taken`, `folder_not_empty`, `not_trashed`, …) and `cancelled` (with the
    handlers' messages): `409`; `setting_locked`: `409` with the configuration key;
  - `query_timeout`: `503`;
  - an item that does not exist: `404 <kind>_not_found`, the same as one the user cannot see.
- **What the library's endpoints need** (`design` and the others as in ADR-0013):

  | Endpoint | Needs |
  | --- | --- |
  | the tree: the root and a folder's children | the section; they list what the user sees (ADR-0048), each item with the user's effective permissions; a folder the user does not see is not found |
  | a folder | seeing it |
  | create a folder | `design` on the parent (at the root: the folder-level part of the global grants) |
  | rename, trash, restore a folder | `design` on it |
  | move a folder | `design` on it and on the new parent; see below when the move widens access |
  | delete a folder for good | `design` and `deleteEntries` on it and on every folder and form below it |
  | the trash | the section; it lists the trash's top items the user sees |
  | empty the trash | the section; it removes only what the user may delete for good |

  - **A move that gives a group more access** (ADR-0048's gains) needs `managePermissions` and a confirmation in the
    request: without the permission it answers `403 forbidden` naming `managePermissions`, and with it but
    unconfirmed `409 widens_access`; both carry the gains, so the backoffice can show who would gain what.
  - **A permanent delete** removes entries, and a folder's delete removes everything below it, which may hold a
    restricted folder the user does not see. So it needs `design` and `deleteEntries` on everything it removes;
    otherwise it answers `403 forbidden` with the first missing permission and removes nothing. Nothing new can arrive
    below a folder in the trash, so what was checked can only shrink before the delete runs.
  - **Emptying the trash** removes the trash's top items (a folder with everything below it, or a form trashed on its
    own) that the user may delete for good, and leaves the others; the answer counts what went and what stayed.
- **The client** is generated from the committed snapshot `docs/api/jfb.json`, which the snapshot test writes with
  `JFB_UPDATE_SNAPSHOTS=1`. The generated client in `packages/backoffice/src/api` is committed, and a client test
  regenerates it into a temporary folder and compares, so neither the snapshot nor the client can drift unnoticed.

## Consequences

- One reflection test finds every endpoint with its declaration, and calls it as each persona who must be refused
  (4.7a's permission matrix); a new endpoint joins it by being written.
- The migration check needs no table and no policy: a pending plan never reaches a query.
- A designer who holds `design` but not `deleteEntries` can trash and restore but not delete for good; that takes a
  group granted both, or an administrator. ADR-0013's table says so from now on.
- An administrator may still empty everything, and a user who sees only part of the trash empties only that part.
- Changing a declaration changes the matrix's expectations by itself, so the matrix tests the declarations against
  ADR-0013's table rather than against themselves (a table of what each endpoint needs is written out by hand).
