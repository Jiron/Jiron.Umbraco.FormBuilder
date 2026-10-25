# 0048. Permission evaluation, its cache and the visibility filter

- Status: accepted; amends [ADR-0013](0013-permissions.md) (the cache holds the evaluation's inputs, not its results;
  how host grants and the `access` lock combine with stored grants; the audit actions)
- Date: 2026-10-24
- Roadmap step: 4.6a (decided and built); 4.6b builds the policies on it, 4.7 the endpoints with the move rule, 11.3
  the inbox's filter, 12.4a the editor

## Context

ADR-0013 decided who has what: eleven permissions granted per user group globally, per folder or per form, combined
as a union within the boundary of the nearest restricted folder or form, administrators holding everything, and lists
that filter in the database. It left open how the permissions are stored as flags, how the evaluation stays fast and
correct on several servers, how a list filters before paging when restricted folders can sit anywhere in the tree,
how host grants and locks combine with stored grants, and how a grant stays consistent with a folder or form deleted
at the same moment.

The backoffice asks often: the tree, search and every endpoint that acts on a form or folder evaluate the user's
permissions. Folders are few (tens to hundreds, at most 32 levels deep, ADR-0047); forms can be thousands, and so can
entries in the inbox. Grants are few: a handful per group. A change of a grant, a restriction or a folder is rare.

## Options

**What the cache holds:**

1. **No cache:** every check reads the grants of the user's groups and the folder's path, two queries per check.
   Always current, but the tree and the backoffice's many calls pay it every time.
2. **Results per user and form path** (ADR-0013's sketch): many entries, and each change has to find the ones it
   touches; a folder's move touches every path below it.
3. **The inputs, once per server:** every grant and the folder tree (no forms), evaluated in memory. One entry, which
   every change drops whole.

**How a list filters:**

1. **After reading:** breaks paging and counts, which ADR-0013 rules out.
2. **In SQL:** a recursive walk over `jfbFolder` in every list query, with the boundary rule written a second time,
   in SQL.
3. **In memory, sent into the query:** the folders where the user holds the permission come from the snapshot, as a
   list of ids in the query; the forms' own grants are a subquery on `jfbPermission`.

## Decision

**The inputs, once per server**, and **the folders in memory, sent into the query**: one implementation of the rule,
and a list query that stays one statement.

**The permissions** are flags with stable names (the API, the audit, the host configuration) and stable values (the
`permissions` column of `jfbPermission`), never renumbered:

| Name | Value | Granted |
| --- | --- | --- |
| `design` | 1 | globally, per folder, per form |
| `manageActions` | 2 | globally, per folder, per form |
| `viewEntries` | 4 | globally, per folder, per form |
| `viewSensitive` | 8 | globally, per folder, per form |
| `editEntries` | 16 | globally, per folder, per form |
| `deleteEntries` | 32 | globally, per folder, per form |
| `export` | 64 | globally, per folder, per form |
| `manageLibrary` | 128 | globally |
| `manageSettings` | 256 | globally |
| `managePermissions` | 512 | globally |
| `privacyTools` | 1024 | globally |

- **Saving refuses** (`422 validation_failed`, ADR-0043's errors on `access.permissions`): a value without its
  dependency (`needs`: `viewSensitive`, `editEntries`, `deleteEntries` and `export` need `viewEntries`,
  `manageActions` needs `design`), a global-only permission on a folder or form (`globalOnly`), an unknown value, and
  a user group Umbraco does not know (`unknownGroup` on `access.group`). A value a later version adds and an earlier
  one reads is ignored by the evaluation.
- **Names** compare in any case in the host configuration, like `LockedSections`; the API and the audit write them as
  above.

**The evaluation** of a user, who brings their user key, their groups, whether they are in Umbraco's administrators
group (alias `admin`, as Umbraco decides it) and whether they hold the Forms section:

- **Without the Forms section:** nothing, whatever their groups hold. The section comes first (ADR-0013).
- **An administrator:** every permission, everywhere.
- **Everyone else**, with `own(x)` the union over the user's groups of their grants on `x`, and only the seven
  permissions that can be granted on folders and forms flowing down:
  - **globally:** `own(global)`, all eleven, for what has no form or folder (the library, settings, privacy tools);
  - **the root:** the folder-level part of `own(global)`;
  - **a folder:** `own(folder)`, plus what its parent (or the root) gives unless the folder is restricted;
  - **a form:** `own(form)`, plus what its folder (or the root) gives unless the form is restricted.

  This is ADR-0013's rule: with no restriction on the path, the global grants, every folder above and the form; with
  one, only the nearest restricted folder or form and what lies between it and the form. A folder the snapshot does
  not know yet (made on another server a moment ago) gives only its own grants and inherits nothing, so the rule
  fails closed; so does a folder whose parent it does not know, and a folder in a ring, which no tree may hold.
- **Moves:** for an item that is not restricted, a group gains what its new parent gives and its old one did not.
  The evaluation lists the groups that gain something; 4.7 refuses such a move without `managePermissions`. A
  restricted item, and everything below it, gains nothing from a move.

**The snapshot** holds every grant (`jfbPermission`), every folder with its key, id, parent and restriction (`jfbFolder`,
the trash included, since items in the trash keep their place) and the host grants resolved to group keys. Forms are
not in it: a check of a form brings the form's own row (key, folder, restriction), which the endpoint has read anyway.

- **Cached** in Umbraco's runtime cache under one key, with the loading token of ADR-0043: a load that a change
  overtakes is used once and not kept. It expires after an hour as a safety net.
- **Dropped** after every committed change: a grant, a restriction, a folder created, moved, trashed, restored or
  deleted, a form deleted (its grants go with it), a user group saved (a host grant names it by alias) or deleted. The
  changing server drops its copy at once; the access cache refresher (`RefreshAll`) drops the others' on their next
  sync, usually within seconds. Each operation drops it once, before it publishes its notification, so handlers see
  the change; a permanent delete that stops part way drops it too, for what went before it stopped.
- **On another server, until then,** a check can be a few seconds behind. The lists fail closed: a folder the
  snapshot does not know lists none of its forms.

**The "who may see this form" filter** is a scope computed per user and per set of permissions (by default any of the
seven): everything (administrators), nothing (no section, no grant), or:

- the ids of the folders where the user holds one of the permissions, whose forms count unless restricted;
- whether the root does, for the unrestricted forms at the root;
- the user's groups, for a subquery on `jfbPermission` that adds the forms with their own grant to one of them.

The query builder turns it into one clause on `jfbForm`, before paging and counting. The folder ids go into the
statement as integer literals: they are numbers the snapshot read from the database, so nothing a user typed reaches
the text, and SQL Server's limit of 2,100 parameters cannot cut a large tree. The folder list keeps the folders where
the user holds something, the folders above them, and the folders on the path to a form with its own grant, so a
restricted form granted to a group is reachable; folder lists are not paged, so this filter runs on the read list. The
inbox (11.3) applies the same clause with `viewEntries`.

**Host grants and locks** (§4.8, ADR-0008):

- `JironFormBuilder:Access:Grants`, a list of `{ "group": "<alias>", "permissions": [ ... ] }`, replaces the stored
  global grants while it holds at least one entry: the evaluation reads the host's, and a change of a global grant is
  refused with `409 setting_locked` naming the key. Start-up refuses an entry without a group, with no permission, with
  an unknown name or without a dependency, and a group listed twice. A group the site does not have grants nothing,
  and the snapshot's load logs it.
- `access` in `LockedSections` makes the whole section read-only: every grant and every restriction.

**Writes** go through the access service, each a unit of work of its own with its audit entry, then drop the
snapshot:

- **A grant** changes from what the editor saw to what they chose, in one conditional statement (4.3), which on a
  folder or form also reads the target with a lock: a grant never outlives a delete that runs at the same moment. A
  missing target answers as missing (null), like every service.
- **A restriction** of a folder changes only its flag, outside the trash; of a form, it goes through the form's row
  with the concurrency version the caller read, like a rename (ADR-0044).
- **The audit** records `access.grantChanged` (the target, the group, the permissions before and after),
  `access.restricted` and `access.unrestricted`, and `access.groupRemoved` (how many grants a deleted user group took
  with it).

**Amended in 4.8c** (the Phase 4 review):

- **Removing a deleted group's grants** is a unit of work of its own, as every other change of access is, so the
  snapshot drops only after the commit. Umbraco publishes `UserGroupDeletedNotification` after its own scope ends,
  so the handler runs outside any unit.
- **A folder's rename keeps the snapshot,** which names no folder; every other change of the tree drops it.

## Consequences

- One rule, in one place, decides every check and every list; the lists stay single statements with exact counts.
- A change costs one reload of a small snapshot per server; a site with thousands of folders sends long but valid
  lists of ids, and the query plans vary with the user. Revisit with a closure table if trees grow that large.
- Another server can be a few seconds behind after a change, as Umbraco's own caches are; lists fail closed.
- Host grants lock the global grants as a whole; a host that wants none locks the section with `access`.
- 4.6b's policies, 4.7's endpoints, 11.3's inbox and 12.4a's editor build on the service without another rule.
