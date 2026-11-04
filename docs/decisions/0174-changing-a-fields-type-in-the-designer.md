# 0174. Changing a field's type in the designer

- Status: accepted; builds on [ADR-0059](0059-changing-field-types.md) (the map of changes),
  [ADR-0060](0060-designer-locks-of-field-settings.md) (the host's locks), [ADR-0169](0169-the-designers-moves.md) (the element's
  "...") and [ADR-0170](0170-the-inspector.md)
- Date: 2026-11-04
- Roadmap step: 9.4d

## Context

ADR-0059 worked out, for every pair of the site's types, what a field keeps, narrows, loses and needs when it changes
from one to the other, and left three things to the designer: serving the map, showing a field's preview before the
change, and making the change on the draft. ADR-0060 adds that a setting the host locks on the new type is set by the
lock, so a change neither carries it over nor asks for it. The canvas element's "..." lists "Change type..." first
(IA 5.1), and the inspector edits the field.

Five questions came up:

1. **Where the changes are served.** ADR-0059 had them come with the field types. Served there, the 26 built-in types
   that change bring 650 changes: the field types answer grows from 68 KB to 287 KB, read on every opening of a form,
   for a dialog most visits never open.
2. **What a lock does to a change**, on the server or in the browser.
3. **What the change does to the field**, beyond the map's lists: a value of a narrowed setting, the rules that make a
   field required, a condition elsewhere whose operator the new type doesn't take, a setting the field needs.
4. **Where the editor starts a change**, without saying it twice on a screen (section 4.13).
5. **How the editor chooses**, among up to 25 types, and sees what goes before anything changes.

## Options

1. **Serving.** With every type's list (four times its size, read at every opening); one request per preview (refused
   by ADR-0059: a round trip for every type looked at); or **one request per type the field leaves:
   `GET field-type/{alias}/changes`**, a route of the field types endpoint, read when Change type is asked for and kept
   while the form is open, with each type in the list saying whether its fields may change at all
   (`mayChangeType`), so the "..." and the inspector offer it only where there is something to choose. Chosen. A type
   the site lacks answers `404 field_type_not_found`; any user of the Forms section may ask, as for the list.
2. **Locks.** The browser could read the new type's locks from the list and take them out itself, but the map is the
   one source of what a change keeps (ADR-0059). **The server applies them** (`FieldTypeChanges.UnderLocks`): a
   locked setting leaves the kept, narrowed and needed lists, and one the change would have carried is named among
   `lockedSettings`. Chosen.
3. **The change.** The browser carries the kept settings as they are and a narrowed one only when the new type's rules
   take its value, checked kind by kind as the server's settings check holds a value (a text per culture with its
   default text, a list's items each once as they compare; a value of any JSON, which only the type can check, is not
   carried). It takes away the common properties the new type lacks, with what belongs to them: being required with
   the field's rules that require it, showing and hiding with its rules that do that, places with its options' places;
   and its own messages for errors the new type never raises. A setting a lock sets is left to the lock. **Conditions
   stay**, whatever their operator: removing one would change what a rule does (a rule left without its condition
   shows its element always), so the preview names each with what holds it, and the form check names it until it is
   changed, as publishing refuses it (ADR-0059). So do the other fields that name it in a way the new type can't
   keep: one whose options follow its answer, where the new type has no options, and one that must match its answer,
   where the new type has no rule kind or keeps its value on the server; the preview names them too. Everything else stays: the key, alias, label, help and the field's
   place. Beyond the map's needed settings, the preview also asks for a required setting the change would carry that
   the field has no value for, or whose value the new type's rules refuse: a checkbox without its text becoming a
   consent needs the consent's wording.
4. **Where it starts.** In the canvas element's "..." (first, as IA 5.1 lists it) and **in the inspector's header, a
   "Change type" button beside the type's name**, in every way the inspector shows; the bar the inspector carries over
   a narrow canvas gives it no second entry. Chosen.
5. **Choosing.** A list of cards for 25 types is long and repeats the palette; **the browser's own select, grouped by
   palette group, with "Now: Short text" under it**, and below it the preview of the type chosen, said as it changes:
   "What goes" (each setting with the field's value, a text property with its text, a message by what it answers),
   "Set by the site instead" (with the lock's value where the site shows it, and only where it differs from the
   field's), and "Needed before it publishes" (options, settings, and each condition with what holds it). A change that
   takes nothing says so in one line. Where the form has entries that hold the field and the new type keeps its
   answers in other columns, one line says that the entries screen's filters and sorting pass older answers by
   (ADR-0058). A type the site lacks what it needs for, or that the form would refuse (one per form, not in a
   repeatable group, a rule waiting on itself), is listed as unavailable; chosen, the preview says why and Change type
   stays off. A disabled option would be skipped by the arrow keys, so a keyboard or screen reader user would never
   hear why. Change type stays off until a type is chosen. Chosen.

## Decision

- **`GET field-type/{alias}/changes`** answers `{ from, changes }`, each change with its new type, its kept, narrowed,
  lost, needed and locked settings, the properties and message names it loses, whether it needs options, the operators
  it loses, whether conditions compare anew and whether the columns stay. Held to `docs/api/field-type-changes.json`
  for a text, a long text, a number, checkboxes and a single checkbox; the permission matrix names the route. The list
  says `mayChangeType` for each type.
- **The designer** (`change-type/`): `previewChange` and `changedField`, pure; the dialog `Jiron.FormBuilder.Modal.
  ChangeType`; the Design view checks each type against the form as a change of the editor model, makes the change
  chosen on the form as it is when the dialog closes, says it ("Changed Your name to Dropdown"), offers Undo in a
  toast, and gives the focus back to the field; Cancel too. The workspace reads a type's changes once while the form
  is open, ahead as soon as a field of the type is selected so the dialog opens at once, and again after a read that
  failed, which a toast says. A type the dialog shows as refused is never changed to.
- **The words of operators** live in their own area, `jfbRules_op_*`, in English and German, for the rules builder
  (9.5a) to say its conditions with. An action without a name of its own is named by its place ("action 2"), as an
  outcome is.
- **A value from before a lock** (ADR-0060) follows the field's key whatever type the stored draft gives it, so a draft
  saved after a change of type and then taken back with Undo saves again; a value the field never held stays refused.

## Consequences

- ADR-0059's "served with the field types" is served beside them; the map, and what it decides, is unchanged.
- A change can leave a condition the form check names, and a setting or options the field needs; both are said before
  the change and stay visible in the form check afterwards.
- A default value that no longer fits the new type (a list for a type that takes one answer, a text for a number), and a
  merge tag or formula that reads the field as its old kind, are left to publishing, as ADR-0059 decided; the preview
  does not name them.
- A lock of the type the field leaves ends with the change: the field then has its own value, or the new type's
  default, as any field of that type.
- Should a value from before a lock be lost by a change (the new type lacks the setting) and come back with Undo after
  a save, the next save is refused with the setting and the lock's key; changing the type again through the dialog
  leaves the value to the lock.
- The rules builder (9.5a) is where a condition named by the preview gets changed.
