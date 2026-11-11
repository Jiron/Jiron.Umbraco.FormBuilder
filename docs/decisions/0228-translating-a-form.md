# 0228. Translating a form: the marks, the dictionary and the emails

- Status: accepted; amends [ADR-0170](0170-the-inspector.md) (a field's translation chip) and
  [ADR-0067](0067-the-umbraco-sources-and-the-base-url.md) (Umbraco's dictionary in editors' texts)
- Date: 2026-11-11
- Roadmap step: 14.1 (part 14.1, culture variants and backoffice localisation)

## Context

Every text an editor writes is a map from language to text (section 4.3.1), and the designer already shows any of the
site's languages: the canvas, the inspector, the Settings and Actions views and their sidebars follow the language
chosen, and a box in another language shows the default text as its placeholder. Three things were still missing.

- Only a field said that it lacked the language shown ("No German text yet"). A page's or a group's title, an
  outcome's message, an email's subject and body, a button's label and the closed message lacked it in silence, so a
  translator could not find what was left without opening everything.
- A text of exactly `#key` takes Umbraco's dictionary item of that key, but it could only be typed from memory, and the
  designer showed `#key` itself where visitors see the item's text.
- The server resolved such a text everywhere visitors see one, but an email's subject and body went out as `#key`.

## Options

**Where a missing language shows:**

1. **One count in the toolbar** ("12 texts without German"). It says how much, not where, and repeats what the marks
   below it would say.
2. **The field's chip on every element that holds texts,** and on a closed group, the line of what it holds. Chosen:
   the marks sit where the work is, as the field's already does.

**How a dictionary item is chosen:**

1. **A link under every text box.** It doubles the boxes' lines for a feature few sites use.
2. **Suggestions while typing `#`.** Nothing tells an editor it exists.
3. **A button in the box's row, shown when the row is hovered or holds the focus** (section 4.13: contextual actions
   appear in context, on hover and keyboard focus), which opens Umbraco's own dictionary picker. Chosen.

## Decision

1. **The translation mark.** Where the language shown is not the site's default, an element with a text written in some
   language but not in this one carries the chip "No German text yet" (`jfbDesigner_untranslated`), and a screen reader
   hears it with the element's name:
   - on the canvas: a field (as before), a page (title, description) and a group (title, description);
   - in the Settings view: the box of the submit, next and back labels and of the closed message, beside its label, and
     a closed group's line says it once for the texts inside it; an outcome's row in the outcome list;
   - in the Actions view: an action's card, for any text of the action's settings (an email's subject and body).
   The marks read the draft only; they change nothing and need no server. A text whose default is exactly `#key` lacks
   no language: the dictionary holds its text in each one, and the line under its box (3.) says what visitors see, so
   the chip would say something untrue (found in the German walk of 14.1).
2. **The dictionary picker.** A plain text box of an element's own (a field's label, help and placeholder, a page's or
   group's title, a button's label, an email's subject) has the button "Use a dictionary item" at the end of its row,
   shown while the row is hovered or holds the focus, and only to a user whose groups open Umbraco's Translation
   section, where the dictionary lives. It opens Umbraco's dictionary picker (`UMB_DICTIONARY_PICKER_MODAL`), and the
   item chosen becomes the box's text in the language shown, `#` and its key, as typing it would.
3. **What a `#key` shows.** Under a box holding `#key`, a line says what visitors in the language shown see: the
   item's text, found as the server finds it (the language, its more general ones and Umbraco's fallbacks, then the
   default language), or that the dictionary has no such item, so visitors see the text as written. The canvas shows
   the item's text in place of `#key`. Both come from `GET designer/dictionary-texts` (the culture and up to 100 keys;
   each key's text, or none), open to any user of the section, as the texts are the site's public ones.
4. **Emails.** An email action's subject or plain-text body of exactly `#key` is the item's text in the entry's
   language; an HTML body's item text is sanitised as an editor's body is. Merge tags inside the item's text render as
   in the action's own text.
5. **Exports.** An entry keeps the language it was sent in, and an export writes its answers' numbers and dates in that
   language, as it did; a new column, Language (`language` in JSON, `FormBuilderExportColumnKind.Language` for a format
   of a site's own), names it, so a reader of a file with entries in several languages knows how each row is written.
   The received time stays `yyyy-MM-dd HH:mm` in the site's time zone in every language, so the column sorts.

## Consequences

- A translator switches to a language and finds every text left by its chip, in each view, without opening an element.
- A rich text (a description, an outcome's message, an email's body in the editor) is HTML, which a whole-text `#key`
  can't be in the rich text editor; such texts take an item through the merge tag picker's Dictionary group
  (`{{ dictionary.key }}`), as before.
- Option labels and the error messages take `#key` typed, without the button, so the option editor's rows and the
  Validation section keep one line each.
- The German walk of 14.1 (docs/ux/review-14.1.md) brought two more rules for words the screens build. A rule's
  condition inside a sentence takes its language's clause order (`jfbRules_clause`): German puts the verb last and quotes
  a field's name ("Pflicht, wenn „Gäste“ kleiner als 7 ist"), English keeps its order. And the package's default theme,
  whose name is a word rather than a name, is said in the backoffice's language wherever a theme is named
  (`jfbSection_defaultTheme`: Default, Standard); a site's theme keeps the label it registers.
