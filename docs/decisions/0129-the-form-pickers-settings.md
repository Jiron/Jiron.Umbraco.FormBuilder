# 0129. The form picker's settings

- Status: accepted; extends [ADR-0127](0127-the-form-picker-in-the-delivery-api.md) (the picker's value and its
  Delivery API output) and [ADR-0105](0105-the-theme-registry-and-the-base-rules.md) (a placement's theme)
- Date: 2026-10-31
- Roadmap step: 6.7a (decided and built); 6.7e builds the settings' screen and the picker in the backoffice

## Context

The data type "Jiron Form Builder form picker" picks one form or several, may limit the picks to some folders or
forms of the library, and may set a theme and a page to go to after a send for every form it places. 6.6c stored
one form's key as text and gave Razor a `FormBuilderFormReference` and the Delivery API `{ key, alias, name }`.

Three questions follow from the settings:

1. **Where a pick the settings do not allow is stopped.** Umbraco checks a property's value with
   `IValueValidator.Validate`, which is synchronous, when a page is saved through the management API (the
   backoffice and `IContentEditingService`); a check there that awaited the library's asynchronous reads would block
   on a task. A notification handler on saving is asynchronous, but it sees the stored values without each nested
   property's settings, so it cannot check a picker inside a block.
2. **How the page after a send reaches the visitor without JavaScript.** The post goes to the page and the post
   handler redirects to the outcome (ADR-0100). The placement's page must reach the handler without a visitor being
   able to change it, or the form becomes a redirect to anywhere the visitor names.
3. **How the overrides reach a headless site.**

## Options

- **The check:** (a) only in the backoffice's picker, which offers only allowed forms: a save through the API or an
  older value passes; (b) a notification handler: no settings for nested pickers; (c) the value editor's validator
  with a synchronous read of the library's folders through Umbraco's scope, as Umbraco's own validators read
  content: the save is refused wherever the picker is, at the price of one small query per save of a picker. (c)
  is chosen.
- **The page after a send:** (a) a hidden input with the page's key: a visitor can change it; (b) look the
  placement up again on the post: a page may place the same form in several blocks; (c) Umbraco's encrypted route
  token (`ufprt`), which the form already carries for its surface action and which takes additional values that only
  the server can write and read. (c) is chosen.

## Decision

- **The settings** (`FormPickerConfiguration`): `multiple` (one form or several, default one); `allowedFolders` and
  `allowedForms` (keys; a form is allowed when both are empty, when it is listed, or when it lies in or below a
  listed folder); `theme` (a theme's alias, which must be a theme of the site); `redirect` (a content key). The
  configuration editor refuses a theme the site lacks, keys that are not keys, and more than 1,000 allowed folders or
  1,000 allowed forms (`#jfbPicker_tooMany`, 6.8f): the picker's queries name the allowed forms in one list, which
  carries `SqlArgs.MaxListLength` values, and past it every call of that picker would fail. Folders are held to the same
  number, so both lists read alike; a folder also takes in everything below it, which is the way out the message names.
  Umbraco's data type endpoint answers a refused save with a general message ("Invalid data type configuration") and
  only logs the reasons, so the setting's element shows that message itself once it holds more (ADR-0135); the other
  settings cannot reach a refusal from their own screens, as they offer only valid values.
- **The value** stays text: the picked forms' keys with hyphens, separated by commas, in the editor's order. A
  value of 6.6c, one key, reads the same. The server reads a key in any format .NET does; the backoffice reads the same
  formats, keeps the hyphenated one and writes that (`picked-keys.ts`, 6.8f), so a value imported in another format is
  named and marked chosen, and cannot be picked twice.
- **The check on save:** the value editor reports, as Umbraco reports its own properties' problems when a page is
  saved, and Umbraco then never publishes the value: a value that is not keys, several keys in a single picker, a key
  that names no form of the library or a trashed one, and a form the settings do not allow, each as a
  `#jfbPicker_<key>` message the backoffice puts in the editor's language. It reads the forms' folders and the folders' parents synchronously through Umbraco's
  scope; the forms' data stays where it is. A settings change does not touch saved values: the next save checks
  them, as Umbraco's own pickers do.
- **Razor:** a single picker gives a `FormBuilderFormReference`, a multiple one an
  `IReadOnlyList<FormBuilderFormReference>` (empty when nothing is picked); each reference carries the placement's
  `Theme` and `Redirect`. The view component and `<jfb-render picked="...">` render a reference in its theme unless
  the page names one itself, and put its redirect into the route token as `jfbRedirect`. `picked` is code, as
  MVC's typed tag helper attributes are; `form` stays text, so `form="@reference"` still names the form by its key
  alone (ADR-0093), without the settings.
- **After a send:** a post whose route token holds `jfbRedirect` goes, once sent, to that page's address in the
  page's culture instead of the outcome; a page that is gone or not published leaves the outcome as it is. A send
  with JavaScript goes the same way, by a token of its own (6.4i, ADR-0120). The render endpoint (ADR-0104) takes no
  placement: the page's refresh of a form (6.4j) carries the theme it has and must carry the placement's token too.
- **The Delivery API:** a single picker answers `{ key, alias, name }` and a multiple one an array of them in the
  editor's order (`null` in the place of a form that is not published or not headless), with
  `theme` and `redirect` (the page's key, which a headless site resolves through the Delivery API) beside them when
  the settings set them; expanded, each form is the definition endpoint's answer with the same two beside it.

## Consequences

- One query per save of a page with a picker: the forms named and the library's folders, a small table.
- A theme or a page removed after the data type named it renders the form in its own theme, or leaves the outcome
  as it is; neither breaks a page.
- 6.7e's screen writes the same settings; a data type made in code or by a package writes them as JSON.
