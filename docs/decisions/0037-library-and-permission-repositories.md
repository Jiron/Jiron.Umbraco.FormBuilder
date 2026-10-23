# 0037. The library and permission repositories

- Status: accepted
- Date: 2026-10-24
- Roadmap step: 4.3b (decided and built); 4.6 (the permission model on top), 12.1 to 12.3 (the library screens)

## Context

ADR-0036 set the pattern for the repositories: one per aggregate, every state change one conditional statement,
refusals as two exceptions, arguments checked against the columns before the database. Step 4.3b adds the library
(option lists, themes, email layouts, templates) and the permission grants (ADR-0013). Their tables raise questions
that the form repository did not:

- **Three tables, one shape.** Option lists, themes and email layouts each have a key, an alias unique without regard
  to case, a name, content columns of their own and a concurrency version.
- **Aliases that others use.** Forms name a theme by its alias (`appearance.theme`, copied into `jfbForm.themeAlias`),
  while definitions name option lists, and actions and settings name email layouts, by key (ADR-0015, ADR-0033).
- **Deletes.** A library row has no trash. Deleting one that someone just changed, or one that is in use, loses work
  or breaks a form.
- **Grants.** `jfbPermission` has one row per group and target and no concurrency version, but every change is
  audited with the permissions before and after (ADR-0013), and two administrators may edit the same grant.
- **Kinds and flags.** What an option list's kind or a grant's flags mean is decided later (5.5, 4.6).

## Options

**The three aliased tables:**

1. **A repository each, with its own statements.** Three copies of the same create, update, delete and list logic.
2. **One shared table helper** that writes the common columns and takes the content columns as a list, with a typed
   repository per table on top. Chosen.

**Deleting a library row:**

1. **Plain delete by id.** Simple, but it deletes a row someone changed a moment ago without a word.
2. **Against the concurrency version,** like a settings section's delete. Chosen. A theme also checks that no form's
   published version names it, in the same statement.

**Changing a grant:**

1. **Upsert the new permissions.** The audit's "before" would need a read first, and two editors overwrite each
   other unseen.
2. **A version column.** The schema has none, and a migration for it buys nothing the grant's value does not give.
3. **From the permissions the editor saw:** a change names what the grant holds now (0 for none) and what it should
   hold (0 to remove it). The grant's value works as its version. Chosen.

## Decision

**The aliased library rows** (option lists, themes, email layouts) share `AliasedLibraryTable`:

- A create writes version 1 with `INSERT … SELECT … WHERE NOT EXISTS` on the alias and the key, so a taken alias in
  any letter case or a taken key is refused by the statement, not by the unique index.
- An update raises the version against the one the editor read. It also checks that the new alias is free, in the same
  statement.
- A delete is made against the version. A stale one throws `FormBuilderConcurrencyException`.
- Lists sort by name, search name and alias with `LIKE` escaped, and page in the database.
- Lookups by alias are plain equality through the collation (ADR-0033).

**Aliases:** option lists and email layouts may change their alias, because what uses them names them by key. A
theme's alias is fixed once created (ADR-0015), so a theme's update has no alias.

**A theme in use stays.** Its delete carries `NOT EXISTS` on `jfbForm.themeAlias`, which compares through the alias
collation, so any letter case counts, and trashed forms count because they can come back. The General defaults that
name a theme live in settings JSON, so the library service checks them (12.2). A publish does not check that its
theme exists: a form whose theme is gone renders with `default`, and the form check says so (ADR-0015).

**Content columns** are checked against their sizes only: an option list's kind is 1 to 16 ASCII letters, a base
theme 1 to 64 characters, the cache 0 seconds or more, and custom CSS is null or not empty, so an empty text is
never a second way to say "none". What a kind means, and whether the JSON and CSS are valid, is the services' (5.5,
12.1, 12.2).

**Templates** carry no concurrency version: they are saved, renamed and deleted, rarely edited by two people at
once, so the last change wins. A description or category is null or 1 character or more, so an empty text is never a
second way to say "none". Lists filter by kind and category (in any letter case), search the name and the description,
and page in the database.

**Grants** (`IPermissionRepository`):

- **A change** names the group, the target, the permissions the grant holds now and the new ones, 0 standing for no
  grant on either side. It is one statement:
  - an insert-once from 0;
  - a conditional update between two values;
  - a conditional delete to 0.

  When the grant no longer holds what the editor saw, nothing changes and `FormBuilderConcurrencyException` says so.
  The audit (4.6) records exactly the change that happened.
- **No row holds 0:** a grant of nothing is no row.
- **The target:** a global grant's key is `Guid.Empty`, and only a global grant's (§4.5).
- **The flags** are stored as given; their meaning, the dependencies and the global-only permissions are the
  model's (4.6).
- **Reads:** the evaluation reads a user's groups' grants. That read names at most 1,000 keys per statement, below
  SQL Server's 2,100 parameters, and returns rows in the order they were made. A target's grants and all grants are
  read for the editor.
- **Deletes:** a folder or form deleted for good (4.5) and a user group Umbraco deleted (4.6) take their grants with
  them.

## Consequences

- The library screens (12.1, 12.2) map the same two exceptions to `409` answers as the forms do.
- An editor whose grant view is stale gets a conflict, not a silent overwrite, and reloads.
- A theme that a published form names cannot be deleted through the library, and the settings defaults are the
  service's check. A publish does not check its theme, because the built-in themes have no row. So on SQL Server, a
  publish committed while a delete runs may still name a theme that is gone, and that form renders with `default`
  until the form check reports it (ADR-0015). On SQLite the gate serialises the two.
- The shared helper builds SQL from column names the repositories pass in code, never from input.
- **Revisit** if templates gain editing by several people at once (then they need a version), or if a migration adds
  a version column to `jfbPermission`.
