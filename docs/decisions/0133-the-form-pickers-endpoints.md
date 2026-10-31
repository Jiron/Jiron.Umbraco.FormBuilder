# 0133. The form picker's endpoints

- Status: accepted; builds the picker rule of [ADR-0013](0013-permissions.md), and adds a third access declaration to
  [ADR-0050](0050-management-api-base.md); a fifth endpoint, the folders of given keys, added in 6.7g
  ([ADR-0135](0135-the-form-pickers-settings-in-the-backoffice.md))
- Date: 2026-10-31
- Roadmap step: 6.7e (decided and built); the picker (6.7f) and its settings (6.7g) in the backoffice use them

## Context

The form picker runs in the content editor, and ADR-0013 lets anyone who may use it on content pick a form without
any Jiron Form Builder permission: it shows names and keys of published forms that are not restricted, plus restricted
ones the user holds a grant on, and never entries, settings or actions. Every endpoint of the management API so far
needs the Forms section (ADR-0050), so none of them can serve the picker, and the library's lists show what a user
holds permissions on, which an editor without the section has none of. The picker's data type settings choose allowed
folders and forms and a theme, in Umbraco's Settings section, from the same library.

## Options

- **Who may call them:** (a) the Forms section, as everything else: an editor without it could not pick a form;
  (b) Umbraco's Content section only: the settings of the data type live in Settings, and a member or media type may
  hold a picker too; (c) any of Umbraco's Content, Media, Members and Settings sections, where a property with a picker
  is edited or configured. (c) is chosen.
- **How the rule is applied:** (a) read every form and filter in memory; (b) the library's own visibility filter
  (ADR-0048), widened by the forms nobody's grants are needed for. (b) is chosen: the database filters and pages, as
  for every list (ADR-0036).
- **What a data type's settings limit:** the settings decide what a page may hold (ADR-0129), so with `dataTypeKey`
  every answer keeps to them; without it (the settings' own picker, a block's label) nothing is limited.

## Decision

- **The endpoints** (`/umbraco/jfb/management/api/v1/picker`, §4.7 "Content"): `GET children?parentKey&dataTypeKey&skip&take`
  (a level of folders, then forms, by the tree's order and by name), `GET search?text&dataTypeKey&skip&take` (forms by
  name or alias), `GET forms?keys&dataTypeKey` (up to 100 forms by key, in the order asked, the others left out) and
  `GET themes` (every theme with its alias and label, in the backoffice's order). Each item is `key`, `kind`, `name`,
  `alias` (a form's) and `hasChildren` (a folder's): names and keys only.
- **Access:** the declaration `FormBuilderContentAccess` replaces the Forms section and every permission: the access
  filter lets a user with one of the four sections through, refuses the others with `403 forbidden` naming `content`,
  and still answers `503 migrations_pending` and `400 invalid_request` as for every endpoint. The permission matrix
  names these four endpoints as its one reviewed exception and checks that each carries the declaration; their own
  tests hold the access rule.
- **What they show:** published forms outside the trash, neither restricted themselves nor below a restricted folder,
  in any folder outside the trash; plus every form the user sees through the library's evaluation (a grant of a group
  of theirs on a restricted folder or form, ADR-0048), still published and outside the trash; and the folders that lead
  to one of those, so a level never opens onto nothing. Technically, the user's form visibility is widened by the root
  and the folders below no restriction, and the folders are those above a folder that holds such a form.
- **A data type's limits:** the allowed folders and every folder below them, and the allowed forms wherever they are.
  The top level shows the allowed folders that no other allowed folder holds, then the allowed forms outside them; a
  folder outside the limits answers `404 folder_not_found`. A `dataTypeKey` that names no form picker's data type
  answers `404 data_type_not_found`.

## Consequences

- An editor sees the names of forms they may otherwise know nothing about. That is the intent of ADR-0013: a name and
  a key say nothing about entries or settings, and restricted forms stay hidden.
- The settings' allowed forms are at most 1,000 keys, the limit of one list in a statement (`SqlArgs.MaxListLength`).
- A group that holds a grant has the Forms section too (12.4a), so the evaluation counts its grants; a user without the
  section sees the forms nobody's grant is needed for.
