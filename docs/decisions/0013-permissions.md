# 0013. Permissions

- Status: accepted; the cache, host grants and the audit actions amended in 4.6a ([ADR-0048](0048-permission-evaluation-and-visibility-filter.md))
- Date: 2026-10-23
- Roadmap step: 1.7b (decided); built in 4.6a (the model and its evaluation), 4.6b (the policies and handlers), 4.7
  (the permission matrix), 12.4a (the editor), and used by every step that adds a management endpoint or a screen

## Context

§1.1 grants permissions per user group: design, view entries, edit entries, delete, export, manage settings; and
"folders and forms can be limited to certain groups". Step 4.6 lists the permissions (design forms, manage actions,
view entries, view sensitive data, edit entries, delete entries, export, manage library, manage settings, manage
permissions, use privacy tools), granted per user group globally, per folder or per form, combined as a union. §4.5
stores them in `jfbPermission (UserGroupKey, TargetType, TargetKey, Permissions)`. §4.10 asks that every management
endpoint is authorised by these permissions and tested with a `403` for a user without them, that sensitive values
need "view sensitive data", and that views of sensitive entries, exports, deletions and permission changes are
audited. §3.2 adds Ligata's lesson: the UI follows permissions, hiding what a user may not use and explaining what is
disabled.

**What the spikes found** (1.4b): Umbraco's section requirement is internal and its allowed-applications claim is
obsolete in 17, so a handler reads `IUser.AllowedSections` through `IAuthorizationHelper.TryGetUmbracoUser`;
authorization handlers must be singletons.

**The gap in the sketch:** a pure union cannot express "limited to certain groups". If the support team holds "view
entries" globally, a union of grants lets it see the HR folder too, whatever the HR folder's own grants say.

Tandem (Tandem ADR-0072) scoped its roles to sites by letting each group list where it applies, and kept
administrators unrestricted, because they could widen any group anyway.

## Options

1. **Union only.** Simple, but a folder can never be closed to a group that holds a broader grant.
2. **Deny entries** (a group is denied something on a folder). Expressive, but the result of a grant and a denial on
   different levels is hard to read, and every screen has to explain precedence.
3. **Union within a boundary:** a folder or form can be marked **restricted**. Inside a restricted folder or form,
   only grants made on it or below it count; global grants and grants on folders above stop at its edge. Everywhere
   else the union applies.

## Decision

**Option 3.**

**The permissions** (flags, stable names in the API and the audit):

| Permission | Lets a user | Granted |
| --- | --- | --- |
| `design` | see the form in the library, create, edit drafts, preview, publish, unpublish, duplicate, move, trash and restore forms and folders | globally, per folder, per form |
| `manageActions` | add, change and test actions, and retry deliveries | globally, per folder, per form |
| `viewEntries` | list and open entries, notes and the timeline, download files, see insights and answer summaries | globally, per folder, per form |
| `viewSensitive` | see sensitive values unmasked; every reveal is audited | globally, per folder, per form |
| `editEntries` | change values (audited), change states, add notes, forward | globally, per folder, per form |
| `deleteEntries` | delete entries and empty Spam | globally, per folder, per form |
| `export` | export entries and download the ZIP of files | globally, per folder, per form |
| `manageLibrary` | option lists, themes, email layouts and templates | globally |
| `manageSettings` | the settings sections, the audit log and the deliveries log | globally |
| `managePermissions` | this model: grants and restrictions | globally |
| `privacyTools` | find, export and erase a person's data across every form, and the retention dry run | globally |

- **Dependencies:** `viewSensitive`, `editEntries`, `deleteEntries` and `export` need `viewEntries`, and
  `manageActions` needs `design`; the editor ticks the dependency with them and saving refuses a grant without it.
- **Global-only permissions** cannot be granted on a folder or form; saving refuses them there.

**Who has what:**

- **The section comes first.** Every endpoint of the Forms section needs the Forms section in the user's allowed
  sections (`IUser.AllowedSections`, read through `IAuthorizationHelper`). The migration grants it to the
  administrators group once (4.1).
- **Administrators** (the user is in Umbraco's administrators group) hold every permission everywhere, restricted
  folders included. They could grant themselves anything, so a restriction would protect nothing and only surprise
  them. The documentation says so, and the audit log records what they do.
- **Everyone else:** the effective permissions of a user on a form are the union, over the user's groups, of the
  grants that reach the form:
  - with no restricted folder or form on the path from the root to the form: the global grants, the grants on every
    folder above it, and the grants on the form;
  - with one: only the grants on the nearest restricted folder or form and on everything between it and the form.
- **Folders** work the same way for what happens in them (creating a form needs `design` on the folder or above).
- **Nothing reaches a restricted folder by accident:** moving a form or folder into or out of a restricted folder
  shows how the move changes who sees it, and needs `managePermissions` when it widens access.

**Storage:**

- `jfbPermission` rows hold the grants (`TargetType` `Global`, `Folder` or `Form`; `TargetKey` `Guid.Empty` for
  global, §4.5).
- **The restriction** is a flag on the folder or form, `IsRestricted` on `jfbFolder` and `jfbForm`, set with
  `managePermissions`. Restricting a folder that has no grants of its own asks first, because only administrators
  would see it then.
- Every change is audited with the group, the target and the permissions before and after. **Amended in 4.6a
  (ADR-0048):** so is each restriction set or lifted, and the grants a deleted user group took with it.

**Host configuration** may set the global grants (`JironFormBuilder:Access:Grants`, a list of
`{ "group": "<alias>", "permissions": [ … ] }`). Global grants then show read-only with the key (ADR-0008). With
`LockedSections` including `access`, folder and form grants and restrictions are read-only too. **Amended in 4.6a
(ADR-0048):** the host's grants replace the stored global grants rather than adding to them, and a group they name
grants nothing until the site has it.

**Enforcement:**

- **Policies:** one authorization policy per permission, with a singleton requirement handler (§3.4). Endpoints that
  act on a form or folder evaluate the permission against that resource: the handler resolves its path and the
  user's effective permissions.
- **The evaluation is cached** per user and form path, and dropped by a cache refresher when a grant, a restriction, a
  move or a user's groups change. **Amended in 4.6a (ADR-0048):** each server caches the evaluation's inputs instead
  (every grant, the folder tree and the host's grants by group) as one snapshot, which every committed change of a
  grant, a restriction, a folder, a permanent delete or a user group drops; a user's groups come with the user.
- **What a user cannot see does not exist for them:** a form or folder the user holds no permission on, and its
  entries, answer `404 form_not_found` (or `entry_not_found`), the same as a missing key, so keys reveal nothing. A
  form the user can see but not act on answers `403 forbidden` with the missing permission's name.
- **Lists filter in the database:** the tree, search, the all-forms inbox, badges and counts include only forms the
  user may see (any permission on them), through a filter the query builder applies (4.3), never after paging.
- **The form picker** in the content editor lists forms for users who may use it on content, without any Jiron Form
  Builder permission: it shows names and keys of published forms that are not restricted, plus restricted ones the user
  holds a grant on. It never shows entries, settings or actions.
- **Privacy tools** work across every form, restricted ones included, because a person's request covers everything
  the site holds. Each search, export and erasure is audited with the user and the counts.
- **Sensitive values** are masked in every response unless the user holds `viewSensitive` on the form; an unmasked
  read is audited.
- **The UI follows permissions** (§3.2): a screen shows only what the user may use; a disabled action says which
  permission it needs. The management API returns the user's effective permissions with each form and folder, so the
  backoffice never guesses.

**Test personas** (3.4, 4.6), each a user group seeded by the Testing project:

| Persona | Grants |
| --- | --- |
| admin | Umbraco's administrators group |
| designer | `design`, `manageActions`, `manageLibrary` globally |
| entry handler | `viewEntries`, `editEntries`, `export` globally |
| viewer | `viewEntries` globally |
| privacy officer | `privacyTools`, `viewEntries`, `viewSensitive`, `deleteEntries` globally |
| no access | the Forms section, no grants |
| outsider | a backoffice user without the Forms section (for the section requirement, 4.7) |

A restricted folder with its own grant to a separate group is part of the seed, so every list test also proves that
the restriction holds.

**Tests:** the permission matrix calls every management endpoint as each persona and expects the documented answer
(4.7, ADR-0011); unit tests cover the evaluation, including restrictions, dependencies, moves and administrators.

## Consequences

- A folder for HR or legal can be closed to everyone but its own group without touching the global grants.
- Every new management endpoint names its permission and joins the matrix; every new list applies the visibility
  filter in its query.
- Administrators see everything, which is honest about what they can do anyway.
- §4.5 gains `IsRestricted` on `jfbFolder` and `jfbForm`; §4.8's Access names restrictions and host grants (WORKLOG).
- **Revisit** if sites need a permission per action type (for example webhooks only), or per entry state.

## Amendment (2026-10-23, step 2.4)

**Who can retry a delivery.** The table gives "retry deliveries" to `manageActions`, while the information
architecture (`docs/ux/information-architecture.md`, §11) says the designer persona cannot retry. Both hold: a retry
needs `manageActions` for the run's form, and a screen that shows the run. Those are the entry's timeline, which
needs `viewEntries` for that form, and Activity › Deliveries, which needs `manageSettings`. The designer persona
holds `manageActions` but neither of the others, so it tries an action with **Test** instead, and the failed run's
details in the Actions view say who can retry it. The management API checks `manageActions` on every retry, whichever
screen asked.
