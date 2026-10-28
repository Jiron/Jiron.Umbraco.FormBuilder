# 0068. Answer recall

- Status: accepted; amends [ADR-0014](0014-merge-tag-language.md) (visitor texts name fields by their current aliases
  only, a field whose value stays on the server shows nothing in them, and a site's type shows there by its rule
  kind), [ADR-0064](0064-the-merge-tag-engine.md) (the problem `field_after_text`, and the parser's check of a path
  where the text stands), [ADR-0065](0065-the-merge-tag-service-and-its-sources.md) (the scope and the check know
  whose text it is, and a source knows its paths per place) and [ADR-0066](0066-answers-as-merge-tags.md) (the
  answers' display texts in TypeScript too, and a prefix and a suffix trimmed with the set both runtimes share); the
  `field` source without an alias and the website's reading of a value with parts amended by [ADR-0083](0083-merge-tags-and-the-two-runtimes-after-the-phase-5-review.md) in 5.5h
- Date: 2026-10-26
- Roadmap step: 5.3f (decided and built in both runtimes); 6.1 gives the browser what recall needs, 6.2 renders
  visitor texts on the server, 6.4 updates them as the visitor types, 10.x reports the problem in the form check

## Context

ADR-0014 lets a visitor text (a label, a help text, a placeholder, a page or group title, a text block) use
`field.*` of the fields above it or on earlier pages, and calls a text that names a later field an error (§4.3.3).
The browser renders these texts as the visitor types, and the server renders the same text for a post without
script and for the summary page. ADR-0066 made each field type write its answers' display texts, in C#. Six questions
were open:

1. what "above the text" means for a field in a repeatable group, a group's title and a page's title;
2. what a tag that names a later field renders, and where its problem points;
3. how the browser shows an answer as the server does, when a site's own type writes its display in C# only;
4. whether a visitor text may name a field by a former alias;
5. what a field whose value stays on the server, a sensitive field and a field the rules hid show;
6. what the browser is given, and in which form the answers reach the runtime.

## Options

1. **Before the text.**
   - Only the fields on the text's own page above it: a page could not greet the visitor by the name given on the
     first page.
   - The fields that come before the text's owner in the order of the form and do not hold it. Chosen. The order is
     the pages, each page's groups, each group's fields, and a repeatable group's children right after it. A field
     holds the texts of its children, so a child's label cannot recall its own repeatable group, nor a group's title
     the group's fields. A text without an owner, or with one the form lacks, comes after every field.
2. **A later field.**
   - Render it empty: the editor would never learn why the text shows nothing.
   - The problem `field_after_text`, an error, at the tag's path; the tag is written as its text, as for every
     error (ADR-0064). Chosen. The form check reports it through `Check` with the text's owner.
3. **The answers' texts in the browser.**
   - The server sends each answer's display text: the answers being typed are not on the server yet.
   - `display.ts` ports each built-in type's display to TypeScript, and a site's own type shows by its rule kind in
     visitor texts in both runtimes, even one that replaces a built-in alias, since its display runs in C# only.
     Chosen: the page shows the same text with and without script. A site's type keeps its own display everywhere
     else (mail, outcomes, entries).
4. **Former aliases.**
   - As in mail: the browser would need every field's former aliases, which tell visitors how the form was built.
   - Current aliases only in visitor texts, in both runtimes; a former alias there is `unknown_path`. Chosen. Mail
     and the other places keep finding fields by their former aliases (ADR-0014).
5. **What shows.**
   - A field the rules hid, or a child of a hidden repeatable group, is empty, and a hidden group is the empty
     text, not an empty list.
   - A field whose value stays on the server (`ServerOnlyDefault`, the hidden field type) is empty in visitor texts:
     its value never reaches the page (ADR-0014). Its label shows.
   - A sensitive field, or a child of a sensitive group, shows `•••` once answered and nothing before (ADR-0066).
6. **The browser's input.**
   - The definition: it holds settings and texts visitors may not see (ADR-0002).
   - A `RecallForm` the projection writes (6.1): the pages, groups and fields in their order with each field's id,
     alias, label, kind, whether it holds parts, options' labels (a country's and an address's countries named as
     the server names them), a grid's rows and its public settings, every text in the page's culture; whether its
     tags show masked or nothing; and the culture's words for yes and no. Chosen. The projection names no field as
     sensitive; it gives the recall mode of the fields a visitor text recalls.

## Decision

### Before the text

`MergeTagScope.TextOwner` is the key of the page, group or field whose text is rendered, null for a text after every
field. In the visitor-text place a field may be named only when it comes before the owner in the order above and is
not one of the owner's holders. `MergeTagRecall.IsBefore` decides this in C#, `recallAnswers` in TypeScript.

### The problem

`field_after_text` (error, at the path) joins ADR-0064's problems. The parser takes an optional path check
(`parseTemplate(text, context, pathProblem)` in TypeScript); the merge-tag service checks in the same place in C#,
and `IFormBuilderMergeTagService.Check` takes the text's owner. A later field's tag is written as it stands.

### The display in both runtimes

`displayAnswer` writes what each built-in type's display writes: a number between its trimmed prefix and suffix, a
slider with its unit, a calculation in its format (two places unless it says otherwise), a grid's rows as
`Statement: column`, a scale's columns as numbers, yes and no by their labels or the culture's words, a country by
its name, a name's parts apart by spaces, an address on one line. Every other type, and every type of a site's own,
shows by its rule kind as ADR-0066's default: an option by its label, a ticked box by the word for yes, files by
their names, numbers and dates in the culture's formats with the wire form behind, parts apart by commas. The flag
`builtIn` says which a field's type is. Both runtimes trim a prefix and a suffix with the set the text module shares
(U+FEFF included).

### What visitor texts show

| Field | Its tags in a visitor text |
| --- | --- |
| before the text, answered | the display texts, or with `.value` the wire form, a number with the number behind |
| before the text, not answered | empty |
| after the text, or holding it | `field_after_text`, written as it stands |
| hidden by the rules, or in a hidden group | empty; a hidden group is the empty text |
| a hidden field (value on the server) | empty; `.label` shows |
| sensitive, or in a sensitive group | `•••` once answered, empty before |
| named by a former alias | `unknown_path` |
| a repeatable group | its answered rows, each as `Label: answer` apart by `; ` |
| a repeatable group's child | its answers in every row, as a list |

### The browser's input

`recallAnswers(form, owner, answers, hidden, words, culture)` gives the values of a text's `field.*` paths and its
path check. The rest of a text's paths (`form`, `page`, `site`, `date`, `dictionary`) are the same for every visitor
of the page, so 6.1 sends each visitor text that holds tags as its template with those values resolved, and the
runtime renders it with `renderTemplate`. The answers are in the wire form, as Core's types read them (ADR-0058);
the runtime reads them so in 6.4.

## Consequences

- A label such as "Thanks, {{ field.name.first }}." shows the same text in the browser, after a post without script
  and on the summary page.
- A site's own field type shows plainly in visitor texts while its display shapes mail and outcomes.
- Renaming a field breaks the visitor texts that use the old alias, and the form check says so; mail keeps working.
- The vectors' recall cases (`tests/vectors/merge-tags.json`) hold both runtimes to this, with site types in the C#
  runner that write displays the visitor texts must not use.
- **Revisit** if visitors need a repeatable group's child to recall the answer of its own row only, or if sites need
  their own displays in the browser (a client-side extension point).
