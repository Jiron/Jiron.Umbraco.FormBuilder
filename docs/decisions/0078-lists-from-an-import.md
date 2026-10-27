# 0078. Lists from an import

- Status: accepted; amends [ADR-0046](0046-copies-templates-and-import.md) (an import creates the option lists its
  file carries and the site lacks)
- Date: 2026-10-27
- Roadmap step: 5.5e (decided and built); 9.x shows the notes, 12.1 builds the library's own screens and endpoints

## Context

An export file carries the library option lists its form names (ADR-0046). Until 5.5e an import used a list the site
has by key, moved the fields to the site's list with the same alias and contents, and reported every other list as
missing: the kinds and their checks did not exist yet, so a list from a file would have reached the library unchecked.
5.5a to 5.5c built the kinds, and `OptionListService.CheckAsync` holds a list to its kind's checks on the site (allowed
hosts for a URL list, a file's items, a content root the site has, a code source it registers). Open were:

1. **Who may create lists.** Only a user who may manage the library (`manageLibrary`, a global permission, ADR-0048);
   the request names the importing user by key only.
2. **The alias** of a carried list when another list of the site has it with other contents.
3. **A refused list.** The checks of the site refuse what the file's site allowed (a host this site does not call).
4. **Atomicity.** The form and its lists are written by different services, and the form's create can still be
   refused (its alias taken, a handler cancelling it) after the lists are checked.
5. **Two imports at once** of the same file, which both plan to create the same list.

## Options

1. **Who may create lists.**
   - A flag on the request that the caller sets from its own check: every caller of the service would decide it, and
     one that forgets gives no lists rather than too many, but the rule would live outside Core.
   - The request carries the user's access as well as its key: the request then names the user twice, and the two can
     disagree.
   - The service reads the user the request names from Umbraco and asks the access service for its global permissions.
     Chosen: one fact in the request, and the rule of ADR-0048 in one place.
2. **The alias.**
   - Report the list as missing: the editor then rebuilds by hand a list the file holds in full.
   - The file's alias, or the next free number when another list has it (`meals2`), as a form's alias takes one
     (ADR-0044). Chosen.
3. **A refused list.**
   - Create it anyway and let it offer nothing: the library would hold a list its own checks refuse.
   - Refuse the whole import: one list from a site with other hosts would keep the form out.
   - Keep the key and report `option_list_missing`, as for a user who may not manage the library, and log which checks
     refused it by their codes and paths. Chosen.
4. **Atomicity.**
   - Create the lists first in their own units, and delete them when the form's create fails: a crash in between
     leaves lists nobody asked for.
   - Write the lists in the form's unit of work, after the form, so the form and its lists commit together or not at
     all. Chosen.
5. **Two imports at once.**
   - The second import fails with a conflict, and the editor imports again.
   - The unit rolls back, and the import plans its lists again with what the site now has, so a list the other import
     created is used by key; up to three attempts, as a form's create tries its alias (ADR-0044). Chosen.

## Decision

**Which lists an import creates:** each list a field of the file names that the site lacks by key, that the file
carries, and that no list of the site takes the place of (the same alias and contents, `option_list_reused`, ADR-0046),
when the user the request names may manage the library: a user Umbraco has, whose global permissions (ADR-0048) hold
`manageLibrary`. An import without a user, or by a user Umbraco lacks or who may not manage the library, creates none.

**What it creates:** the list with the file's key, so the fields keep naming it and an import of the same file later
finds it by key; its name, kind, configuration, items and cache time as the file carries them; and the file's alias
(or one from its name when the file's is not valid, as for a form), or the first free one with a number from 2 when
another list has it in any letter case (lists the same import creates included). A kind this site does not have
refuses the list.

**Through the kind's checks:** each list is held to `OptionListService.CheckAsync`, the checks the library's own
changes use: the alias, the name, the cache time, the kind's configuration on this site (a URL list's `https` and
allowed hosts, a content root the site has, a code source it registers) and the items (a file list's lines as a manual
list's options). A list the checks refuse keeps its key in the fields and is reported `option_list_missing`; the log
names the list's key and the problems' codes and paths, never the configuration or the items.

**In the form's unit of work:** the lists are written after the form in the unit that writes the form and its audit
entry, so a create that is refused or cancelled leaves no list, and an import never leaves half its lists. Once the
unit has committed, every server forgets what it kept of each new list's key (a page may have asked for it while it was
missing). When another change takes a planned list's key or alias between the checks and the write, the unit rolls
back, and the import checks its lists again with what the site then has, up to three times; after that the import is
refused with the conflict.

**The note** of a created list is `option_list_created`, with the alias it got, at the first field that names it, and
the import's audit entry counts it among its notes.

## Consequences

- A form moves between sites with its lists for an editor who may manage the library; another editor gets the form and
  the notes, and someone who may manage the library makes the lists.
- The library never holds a list from a file that its own checks refuse, on the day of the import.
- A list from a file is the same list on both sites (one key), so later imports of the form use it.
- **Revisit** if editors want to choose, while importing, which lists to create, or to see in the note why a list was
  refused.
