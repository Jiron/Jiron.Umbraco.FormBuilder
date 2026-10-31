# 0135. The form picker's settings in the backoffice

- Status: accepted
- Date: 2026-10-31
- Roadmap step: 6.7g (decided and built); extends [ADR-0133](0133-the-form-pickers-endpoints.md) by one endpoint and
  [ADR-0134](0134-the-form-picker-in-the-backoffice.md) by the settings and a folder mode of the sidebar

## Context

A data type of the form picker holds five settings (ADR-0129, `FormPickerConfiguration`): `multiple`,
`allowedFolders`, `allowedForms`, `theme` and `redirect`. The server checks them when a data type is saved; the
backoffice needs a screen for them in Umbraco's Settings section. In Umbraco 17 a property editor's settings are
declared in the client manifests (`meta.settings.properties`, each with an alias, a label, a description and the UI
that edits it, read in Umbraco's source): the schema's and the UI's settings merge in the data type's workspace. The
allowed folders and forms are keys, and the screen must show them by name; 6.7e's endpoints name forms by key, but no
endpoint names a folder by key.

## Options

- **Where the settings are declared:** (a) on the picker's UI: a data type that names another UI would lose them;
  (b) on the schema, whose settings every UI of it shows, as the server's configuration belongs to the editor. (b) is
  chosen.
- **The allowed folders and forms:** (a) Umbraco's own pickers: none of them knows the library; (b) a list of keys
  typed by hand: unreadable; (c) the picker's own sidebar, which already shows the library within the user's reach,
  with the chosen folders and forms as reference rows, as the property shows its forms. (c) is chosen, as one UI with
  a setting of its own (`pick`: folders or forms), so both lists look and behave alike.
- **Choosing a folder in the sidebar:** a folder row so far opened its level; in the folder mode it is chosen too. (a)
  one button that both opens and chooses: a click could not mean both; (b) two buttons on the row: the chevron opens
  the folder (`aria-expanded`, named "Show the folders in Events"), the row chooses it (`aria-pressed`, the check
  mark), as forms are chosen. (b) is chosen. The folder mode lists folders only, and has no search: the search finds
  forms.
- **Which folders a user may choose:** the levels are those of 6.7e, so a folder shows once it leads to a published
  form the user may pick. A folder named earlier stays shown by name, also when it leads to no published form any
  more, through the new endpoint, which names any folder the user may see outside the trash.
- **The theme:** (a) Umbraco's dropdown UI with fixed items: the site's themes are known only at run time; (b) a
  select of our own over `GET picker/themes`, first "The form's own theme". (b) is chosen. A theme the settings name
  that the site no longer has is shown as such and kept until the editor changes it; the server refuses it on save
  (`#jfbPicker_noSuchTheme`).
- **The page after a send:** Umbraco's document picker UI (`Umb.PropertyEditorUi.DocumentPicker`) limited to one page,
  whose value is the page's key, as the server reads it.

## Decision

- **The endpoint:** `GET picker/folders?keys` (up to 100 keys, §4.7 "Content"), declared `FormBuilderContentAccess`
  like the four of 6.7e and listed in the permission matrix's exception: the folders of those keys that are outside the
  trash and that the user may see (not restricted and below no restricted folder, or covered by a grant of a group of
  theirs, ADR-0048), in the order asked, each as `key`, `kind`, `name` and `hasChildren` (whether it leads to a form
  the picker may pick); the others are left out.
- **The settings,** on the schema in the server's order, each with an English and a German label and help:
  - `multiple`: Umbraco's toggle, off by default, "Allow several forms";
  - `allowedFolders` and `allowedForms`: `Jiron.FormBuilder.PropertyEditorUi.FormPickerAllowed` with `pick` set to
    `folders` or `forms`; the value is the keys in the order chosen, none written as no value;
  - `theme`: `Jiron.FormBuilder.PropertyEditorUi.FormPickerTheme`, the theme's alias or none;
  - `redirect`: Umbraco's document picker with at most one page.
- **The look** is the property's (ADR-0134): reference rows with Remove, a dashed Choose, the sidebar with Close and
  Choose, rows in their final shape while loading, Try again on a failure, a folder or form the user cannot see shown
  as not available, with its own Remove. The settings' pickers apply no data type's limits.

## Consequences

- A folder can be allowed only once a form in it (or below it) is published. A site that sets the picker up before
  its forms exist publishes one form first or allows the folder later; the folder mode's empty state says so.
- The folder names reach users with Umbraco's Content, Media, Members or Settings section, as form names already do
  (ADR-0133); a restricted folder stays hidden from anyone without a grant.
