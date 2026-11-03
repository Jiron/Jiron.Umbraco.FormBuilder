# 0166. The form library: its endpoints, its lists, the Recycle Bin, and what a new form needs where it lands

- Status: accepted
- Date: 2026-11-03
- Roadmap step: 9.2 (decided and built); 9.3a, 9.3b and 10.2 honour the links it hands them; 12.3 adds Save as template…, 12.4a Permissions…

## Context

Step 9.2 builds the form library of `docs/ux/information-architecture.md` (sections 3 and 4) on the services of 4.5 and the exchange service of
ADR-0046. The services were there; what the screens need around them was not:

- The folder list shows each item's entries of the last 30 days and who changed it last, and the Recycle Bin when and by whom each item went
  there. The tree's endpoints answer what the tree needs and nothing more.
- The confirmations of a permanent delete and of emptying the bin say what goes ("118 entries and 9 uploaded files"), before it goes.
- The import sidebar says what a file holds and what it can't bring before the editor imports it.
- The tree marks a form with unseen entries for a user who may view them.
- A folder has Delete… only while it is empty, and no Trash; the server deleted a folder only from the trash.
- Duplicate, import and a template can bring actions into a folder where the user may design but not manage actions; a draft save that adds
  actions needs `manageActions` there (ADR-0013).
- The header search finds fields across forms "by label or alias (from the field catalogue)", which holds what was published.

## Options

For the import's notes:

1. Import, then show the notes. The editor learns of a missing theme once the form exists.
2. A check that runs the import's own reading and planning and writes nothing, then the import. Same code, so the notes can't differ.

For a folder's Delete…:

1. Trash it and delete it from the trash, two calls from the browser. A folder that looks empty to its user may hold a restricted form they
   can't see, which would go to the trash with it.
2. A delete of an empty folder outside the trash on the server, refused while it holds anything, in the trash or out, seen or not.

For new forms with actions:

1. Let `design` be enough, as for any new form.
2. Ask for `manageActions` where the form lands when it comes with actions, as a save that adds them does.

## Decision

- **Endpoints** (the permission matrix holds them):
  - `POST form/{key}/duplicate` (`design` on the form; the copy lands in its folder, where it needs `design`), `POST form/{key}/copy-actions`
    (`design` on the source; `design` and `manageActions` on the target, which the user must see), `POST form/from-template`, `POST
    form/import/check` and `POST form/import` (`design` on the folder), `GET form/{key}/export` (`design`: the file carries the actions);
  - `GET template` (the gallery: built-in first, then the site's saved form templates by name, each with what it holds and its definition for
    the preview; a saved one that does not read is left out with a warning);
  - `GET library/items?folderKey=` and `GET library/trash` (the screens' rows: the item, when and by whom it last changed, as a name, and its
    entries of the last 30 days that the user may view, a folder summing the forms below it; null where the user may view none);
  - `GET form/{key}/holdings` and `GET trash/holdings` (what a permanent delete takes; the trash's is what emptying would delete for this user,
    through the same partition emptying uses, with how many top items would stay);
  - `GET form/search/field` (fields of live versions, of forms outside the trash that the user sees, by label or alias).
- **New forms with actions need `manageActions` where they land (option 2):** a duplicate with actions, an import of a file with actions, and
  a form from a template with actions. Otherwise the answer is `403` naming the permission, before anything is written.
- **The import's check (option 2)** returns the file's name, alias, version and date, what it holds (pages, fields with a repeater's
  children, rules, outcomes, actions) and the notes. A new note, `page_missing`, says that an outcome sends visitors to a page this site lacks or
  holds in its recycle bin; the outcome keeps the key until the editor chooses one. Every note's path now points into the file
  (`$.definition.actions[0]`), as the record always said; the actions' notes pointed at `$.actions[0]`.
- **A folder's Delete… (option 2):** `DELETE folder/{key}` deletes a folder outside the trash at once when it holds no folder and no form,
  in the trash or out, and needs `design` on it; otherwise `409 folder_not_empty`. A folder in the trash goes as before, needing `design` and
  `deleteEntries` on all of it, the folder itself named first.
- **Unseen entries:** a tree item says `hasUnseenEntries` for a form whose entries the user may view, with an arrived entry (submitted,
  approved or rejected, no test) whose first view is not on record.
- **The field search reads the catalogue (ADR-0003)**, so a field only a draft has is found once it is published. The header search's Forms tab
  lists the forms the text names, then the fields ("Email, in Contact us"); a field's result opens its form with the field selected
  (`?field=<key>`), which the designer honours from 9.3a.
- **The Recycle Bin** is a tree of its own under the forms tree, as Umbraco's own recycle bins are, with entity types of its own for what it
  holds, so their menus (Restore…, Delete…) never mix with the library's. A restore puts the form back where the server says: its folder, or
  the top of Forms when that folder is in the trash. Folders have no Trash in the backoffice, so a form's folder can't go while the form is in
  the bin, and the picker IA 3 mentions has nothing to ask.
- **Forms are renamed in their workspace's header (9.3a)**, as IA 3 gives a form no Rename…; folders by Rename… and the folder list's header.
- **Save as template…** (12.3) and **Permissions…** (12.4a) join the menus with their steps.
- **The copy-actions result** links each reference it could not map to the target's Actions view with the copy selected (`?action=<key>`),
  which 10.2 honours.
- **The gallery's preview** draws the template's pages and fields from its definition with plain controls; 9.3b's preview elements replace them.

## Consequences

- The screens never guess: counts, names and permissions come from the server, filtered by what the user sees.
- A folder that looks empty to its user but holds something they can't see is refused with `folder_not_empty`, and nothing moves.
- 9.3a's, 9.3b's and 10.2's checks include the links this step hands them.
