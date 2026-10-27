# 0066. Answers as merge tags

- Status: accepted; amends [ADR-0014](0014-merge-tag-language.md) (the shapes of the `field` source's values, its
  parts and groups, and a sensitive field without an answer), [ADR-0055](0055-field-type-framework.md) and
  [ADR-0058](0058-field-answers-and-values.md) (a type also writes its values' display texts), and
  [ADR-0065](0065-the-merge-tag-service-and-its-sources.md) (the scope carries the answers; the `field` source and
  `entry.answers`); the display texts in TypeScript, a site's type by its rule kind in visitor texts and the trimmed
  prefix and suffix added by [ADR-0068](0068-answer-recall.md) in 5.3f; the labels of options from a list or another
  source added by [ADR-0077](0077-answers-and-labels-from-resolved-options.md) in 5.5d
- Date: 2026-10-26
- Roadmap step: 5.3d (decided and built); 5.3e resolves dictionary texts in labels, 5.3f gives answer recall and the
  display texts to the browser's runtime, 5.5 the labels of options from a list, 14.2 the answer words in five more
  languages

## Context

ADR-0014 promises `field.<alias>` as "the display text of the answer" (option labels, numbers and dates in the
culture's formats, file names), `.value` and `.label`, a repeatable group's child as a list, and `entry.answers` as a
table in HTML and lines elsewhere, with rules for sensitive fields, hidden fields and former aliases. ADR-0065 built
the service without them. Six questions were open:

1. who writes an answer's display text: the `field` source, from what it knows of each type, or the type;
2. the words a ticked box or a yes shows as, and the names of countries, in which languages;
3. what shape a tag's value has: one item, a list, the rows of a repeatable group;
4. how `entry.answers` can be a table in HTML when a source gives text and the context escapes it;
5. what each place shows of a sensitive field, a field the rules hid, and a field not for mail;
6. what a type that fails to write its texts does to a mail.

## Options

1. **Display texts.**
   - The source switches on the type's alias: a site's own type could never show more than its wire value, and every
     new built-in type would touch the source.
   - The type writes them, as it reads and checks its answers (ADR-0058), with a default by its rule kind that a type
     overrides. Chosen.
2. **Words and country names.**
   - The words in the backoffice's localisation: they are visitor-facing texts (mail to a visitor, the outcome), not
     the editor's.
   - A small table of the server's own, English and German now, the other five languages with 14.2's visitor texts.
     Chosen. Country names come from .NET's culture data, which 5.4 needs anyway.
3. **Shapes.**
   - Always a list: `{{ field.email | json }}` would give an array for an email address.
   - Always one item: a checkbox list would lose its items for `join` and `json`.
   - A list where the type takes several values (several choices, files, a grid's rows), one item otherwise; a
     repeatable group's child is a list of its rows. Chosen.
4. **The table.**
   - A public `Html` value any source may give: a site's source could then put markup into a page, which ADR-0014
     rules out.
   - A built-in source's own markup, written only where the context is HTML and no filter changes the text, every text
     in it escaped by the source. Chosen.
5. **Places.** ADR-0014's rules, applied per place in one table (below).
6. **Failures.**
   - Let it through: the service would render the whole `field` or `entry` tag empty, so one broken type of a site
     would empty the list of the answers in every mail.
   - Log it and show that field as the default display would. Chosen.

## Decision

### A type's display

`IFormBuilderFieldType.Display(FieldDisplay)` writes the texts a field's values show as, in the culture the template
renders in. `FormBuilderFieldTypeBase<TSettings>` passes the field's settings to `Display(TSettings, FieldDisplay)`.
`FieldDisplay` holds the field, its values (ADR-0058), the file each value stands for, the culture and whether the
texts are for the list of the answers (`IsListing`), and gives the helpers a type writes with:

- `Add(text)`, and `AddNumber(text, number)` and `AddDate(text, date)`, which keep the wire number or date behind the
  text so the `number`, `currency`, `date` and `json` filters work on it; an empty text shows nothing;
- `Text(localizedText)`, an editor's text in the culture; `Number(number, decimals)`, a wire number as the culture
  writes it, rounded half away from zero and padded when the places are given (0 to 15); `Date(date)`, a wire date in
  the culture's `short`, a time in its `time`, a date-time in its `dateTime` format; `OptionLabel(value)`, the label of
  the field's option with that value, or the value (a visitor's "Other" text); `CountryName(code)`; and `Yes`, `No`.

**The default**, by the type's rule kind: a text as it is; a choice by its option's label; a ticked box as yes; a file
by its name; a number and a date in the culture's formats with the wire form behind; a value with parts (JSON
storage) as its parts' texts apart by ", ". A type that holds no value shows nothing, and in the list of the answers
neither does a type whose default value stays on the server (the hidden field, which the visitor never saw).

**The built-in types** that show more than the default:

| Type | Shows |
| --- | --- |
| `number` | the number in the culture's format between the field's prefix and suffix, each trimmed with the set both runtimes share and apart by a space: `CHF 1,234.5 net` |
| `slider` | the number with the field's unit: `75 %` |
| `calculation` | the result with the format's places (two by default) between its prefix and suffix; in the list of the answers nothing when visitors do not see it |
| `grid` | each answered row as `Statement: column`, the columns' labels or the scale's numbers, several columns of a row apart by " / " |
| `yesno` | the field's label for the answer, or the word for yes or no |
| `country` | the country's name in the culture's language |
| `name` | the parts apart by spaces: `Dr Anna Muster` |
| `address` | street, second line, postcode and city, region, country, apart by ", ", a country given by its code by its name |

**Words:** yes and no are `Yes`/`No` in English and `Ja`/`Nein` in German; a culture takes its own, then its more
general culture's, then English. 14.2 adds French, Italian, Dutch, Spanish and Danish with the other visitor texts.

**Country names** come from .NET: the name of a country in a language is the native name of the region of that
language's culture for the country (`de` and `FR`: `de-FR`, "Frankreich"), which ICU gives for any pair. The invariant
culture and a culture .NET does not know name countries in English; a code .NET has no region for shows as it is.

**A type whose display throws** is logged with its alias and the exception's type and stack (never its message, which
may hold an answer), and its field shows as the default display shows it. A field whose type the site no longer has
shows its values as they are.

### The answers in the scope

`MergeTagScope.Answers` (`MergeTagAnswers`) holds each field's answer (`MergeTagAnswer`: the field's key, its values,
the file each stands for, and the row of a repeatable group's child, from 0) and the keys of the fields the rules hid
for the entry. It refuses two answers for one field in one row; an answer without values counts as none.
`MergeTagScope.IncludedSensitive` holds the sensitive fields an action names to include. The submission pipeline (6.1)
and the actions (8.x) fill both; before then, and where no entry is rendered for, they are empty.

### The `field` source

- **`field.<alias>`**, the answer as its type shows it. A list where the type takes several values (several choices,
  files, rows), so `json` gives an array however many were chosen; one item otherwise; empty without an answer.
- **`.value`**, the values in the wire form as rules compare them (a ticked consent is `true`, not its wording's hash),
  with the wire number or date behind each; the same shape. **`.label`**, the field's label in the culture, or its
  alias without one. **`.<part>`**, for a type that stores parts, a part's text, such as `field.name.first`.
- **A repeatable group's child**, `field.<group>.<child>` or by the child's own alias, is a list with an item for each
  row it answered, a row's several texts apart by ", "; `.value`, `.label` and parts work on it as on any field.
  **`field.<group>`** is a list of the answered rows, each its children's `Label: answer` apart by "; ". A group has
  no `.value`.
- **Aliases** match exactly. A field's former aliases resolve to it (ADR-0002), after the fields whose alias it is.
- **The check** knows every path that names a field, its `.value` (not a group's), `.label` and, for a type that
  stores parts, any part; anything else is `unknown_path`. **The picker** offers each field by its label, a group's
  child under the group's alias.

### `entry.answers`

Every answered field the rules did not hide, in the order of the form, with the texts its type shows in the list of
the answers, apart by ", ":

- **Text:** `Label: answer` lines; a repeatable group a `Label:` line, then a numbered line for each row.
- **HTML:** `<table class="jfb-answers">` with a row for each field, its label in a `th` of `scope="row"`, its answer in
  a `td`, a group's rows in an `ol`; every text escaped, line breaks kept as `<br>`. The table is the value's internal
  markup, written only in the HTML context by a tag without filters; with a filter, or in another context, the text
  lines are used, escaped as usual. A site's source cannot give markup.
- The output limit (ADR-0064) may cut a very long table; nothing of the template follows the cut, so what an HTML
  reader closes is all that is open.

### What each place shows

A field the rules hid, or a child of a hidden group, is empty in its tags and left out of `entry.answers`. A sensitive
field (or a child of a sensitive group) shows as follows; without an answer it is empty everywhere, since there is
nothing to mask.

| Place | Its tag | `entry.answers` |
| --- | --- | --- |
| Visitor texts | `•••` | the place takes no `entry` |
| Consent texts, default values | the place takes no `field` | the place takes no `entry` |
| Hidden field values, redirects | `•••` | left out |
| Outcomes and the copy of the answers | `•••` | masked (`Label: •••`, a group as one line) |
| Entry titles | empty | left out |
| Mail | `•••`, its answer when the action names it to include it | left out, listed when named; fields with `includeInEmails` off are left out, though a tag that names one shows it |
| Other actions | as mail | as mail, `includeInEmails` aside |

## Consequences

- A site's field type shows its answers as it decides, through the same kind of override as its reading and checks,
  and shows sensibly without one.
- Mail, outcomes and webhooks get readable answers in the visitor's culture without a template per type.
- No source can put markup into a page; only the answers' table, which the server writes and escapes, is markup.
- Labels and option labels that name a dictionary item show it since 5.3e (ADR-0067); options from a list show their labels in
  5.5; the browser's runtime gets the same display texts for answer recall in 5.3f.
- **Revisit** if editors need the answers' table styled per form (themes, 6.3, and mail templates, 8.x, may add
  classes), or a ranking shown with its positions.
