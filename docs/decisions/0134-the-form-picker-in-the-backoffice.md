# 0134. The form picker in the backoffice

- Status: accepted
- Date: 2026-10-31
- Roadmap step: 6.7f (decided and built); the picker's settings (6.7g) and the demo (6.7h) build on it

## Context

6.7a to 6.7e built the server half of the form picker: the property editor `Jiron.FormBuilder.FormPicker`, whose
value is the picked forms' keys separated by commas (ADR-0129), its settings, and the endpoints that list, search and
look up the forms an editor may pick, within a data type's limits (ADR-0133). The content editor needs its client
half: what a property shows, how an editor picks, and the block label that names the picked form ("Form: Contact us",
information architecture §10).

Umbraco 17 splits a property editor into a schema, named after the server's editor, and a UI that a data type stores;
a data type without a UI gets the schema's default (`defaultPropertyEditorUiAlias`, read in Umbraco's source). The
editor element gets the value, `readonly` and the data type's settings, and fires `UmbChangeEvent`
(research note §11). Umbraco's own pickers look alike: a list of picked items, each with Remove, and a dashed Choose
button that opens a sidebar with a search field and a tree, with Close and Choose at its foot. ROADMAP §4.13 asks the
picker to feel native, and WCAG 2.2 AA holds for the backoffice (CLAUDE.md).

Reading Umbraco's code (the 17.5 package) showed three things that bear on the choice:

- The tree in Umbraco's pickers is built from UUI menu items, which tell assistive technology neither that a folder
  is open nor that an item is selected (no `aria-expanded`, no `aria-selected`), so WCAG 4.1.2 would fail for a
  screen-reader user choosing forms.
- Umbraco's own tree needs its extension machinery (a tree repository, a data source, item repositories and entity
  types) wired to the endpoints, and it shows its own "not found" for an item it cannot read.
- Umbraco's Remove asks first, in a dialog built from a string that holds the item's name, the pattern research note
  §8 found to run HTML.

## Options

- **The list in the sidebar:** (a) Umbraco's tree, wired to the picker's endpoints: native, but with the gap above
  and much machinery; (b) UUI menu items of our own: the same look and the same gap; (c) our own list of plain
  buttons, drawn in UUI's tokens to look like Umbraco's tree: a folder is a button that opens in place and says so
  (`aria-expanded`), a form is a toggle button that says whether it is chosen (`aria-pressed`) and shows a check
  mark, so the state never rests on colour alone (WCAG 1.4.1). (c) is chosen.
- **Level by level:** (a) one level at a time with a breadcrumb back; (b) folders that open in place, as in
  Umbraco's document picker. (b) is chosen: it keeps the levels an editor opened in view, and each level still loads
  only when it is opened, a hundred at a time with "Show more".
- **Remove:** (a) ask first, as Umbraco does; (b) remove at once. (b) is chosen: nothing is saved until the page is,
  picking the form again restores it, and §4.13 keeps confirmation for what cannot be undone. Focus moves to the next
  row's Remove, or to Choose when none is left, so a keyboard user is never left on nothing.
- **A picked form the picker cannot show** (deleted, in the trash, unpublished, outside the data type's limits, or
  restricted for this editor; the endpoint does not say which, ADR-0013): (a) hide it: the page would still render
  it, unseen; (b) show it as Umbraco shows an item it cannot read: the alert icon and "Form not available" in the
  danger colour, the possible reasons in the row's detail line in its own colour (one line, which keeps its contrast
  and is never cut short at the backoffice's narrowest), with Remove. (b) is chosen. The page's own check (ADR-0129)
  refuses it when the page is saved only where it is gone or not allowed; a restricted form stays valid.

## Decision

- **The extensions** (bundle, ADR-0025): the schema `Jiron.FormBuilder.FormPicker` with the default UI
  `Jiron.FormBuilder.PropertyEditorUi.FormPicker` (group `pickers`, read-only supported), the sidebar modal
  `Jiron.FormBuilder.Modal.FormPicker`, and the UFM component `Jiron.FormBuilder.Ufm.FormName` (`jfbFormName`).
- **The property:** the picked forms as Umbraco's reference rows (the form's icon, its name, its alias below) with
  Remove, then a dashed Choose; Choose is gone when a single picker holds its form, and both are gone when the
  property is read-only. A multiple picker keeps the order in which forms were picked; an editor reorders by removing
  and picking again. The picked forms' names come from `GET picker/forms` with the data type's key, which the content
  editor's property context gives (`UMB_PROPERTY_TYPE_BASED_PROPERTY_CONTEXT`, a block's properties included);
  without it nothing is limited. The value it writes is the keys in order, separated by commas, each once.
- **The sidebar:** headline "Choose a form" (or "Choose forms"), a search field that has focus when the sidebar
  opens, and below it the library's top level within the data type's limits. Typing searches forms by name or alias
  (`GET picker/search`, after a pause of 300 ms, the field's search icon turning into Umbraco's small loader
  meanwhile) and shows a flat list in place of the levels; clearing the search brings the levels back as they were.
  A single picker chooses one form at a time, a multiple one toggles each; Choose stays disabled until something is
  chosen, as in Umbraco, and Close changes nothing.
- **States:** rows of the final rows' height stand in while a level, a search or the picked forms load, so nothing
  moves when they arrive. A request that fails says so in place with "Try again" and shows no toast as well (say it
  once). An empty library, a data type whose folders and forms hold no published form yet and a search that finds
  nothing each have their own sentence, the search's with "Clear search".
- **The block label:** `{jfbFormName: <property alias>}` in a block's label shows the picked forms' names, joined as
  the backoffice's language joins a list ("Contact us and Newsletter"), "(not available)" for a form the picker cannot
  show, and nothing for an empty value. Its element looks the names up without a data type's limits, since a label
  names what the block holds.
- **Motion:** none of our own (ADR-0020): Umbraco's sidebar has its own; opening a folder, choosing a form and
  removing one change the screen at once, since editors repeat them all day.
- **Texts:** the area `jfbFormPicker` in English and German, apart from `jfbPicker`, which holds only the messages
  of the server's checks (lang.test.ts holds both); Umbraco's own words where Umbraco has the action (Choose, Close,
  Remove).
- **Code:** Lit through `@umbraco-cms/backoffice`, without decorators (static `properties` and `declare`d fields), so
  the build needs no decorator transform; each element's data comes through one small module over the generated
  client, which the tests replace. Vitest runs the elements in jsdom with Umbraco's packages transformed by Vite.

## Consequences

- The sidebar's list is ours to keep accessible and to keep looking like Umbraco's tree; the review of 6.7f and the
  phase reviews compare the two in Edge in each theme.
- Reordering a multiple picker's forms needs a keyboard path besides dragging (ADR-0016); if editors ask for it, a
  pair of Move up and Move down buttons with an announcement is the way, as in the designer.
- 6.7g adds the settings' own pickers for allowed folders and forms on the same sidebar, which then picks folders
  too, and the endpoint that names folders by key.
- Where UUI's own colours fall short of AA in Umbraco's dark theme, the picker's own parts use tokens mixed from them
  (docs/ux/tokens.md: the danger text and the selected background); Umbraco's own components, such as the positive
  button at the sidebar's foot, keep Umbraco's look and are reported to Umbraco HQ (docs/OWNER-TODO.md).
