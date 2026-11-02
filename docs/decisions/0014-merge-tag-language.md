# 0014. The merge-tag language

- Status: accepted; consent texts and sensitive fields amended in 1.8; the values tags give, the `json` context, where
  a tag may stand and the problems amended in 5.3b ([ADR-0064](0064-the-merge-tag-engine.md)); a site's sources and
  filters amended in 5.3c ([ADR-0065](0065-the-merge-tag-service-and-its-sources.md)); the `field` source's values,
  parts and groups and a sensitive field without an answer amended in 5.3d ([ADR-0066](0066-answers-as-merge-tags.md));
  `page.url`, a member's properties and the dictionary's fallback amended in 5.3e ([ADR-0067](0067-the-umbraco-sources-and-the-base-url.md));
  answer recall amended in 5.3f ([ADR-0068](0068-answer-recall.md))
- Date: 2026-10-23
- Roadmap step: 1.7b (decided); built in 5.3 (both runtimes and the vectors), used by 6.x (texts, defaults, outcomes),
  8.x (emails, webhooks, content), 9.4 and 10.2 (the picker) and 11.x (entry titles)

## Context

D24 gives Jiron Form Builder one small template language of its own, `{{ source.path | filter: arg }}`, for defaults,
emails, messages, redirects and webhooks: values HTML-escaped in HTML, never evaluated as code, inserted from a picker
so nobody has to memorise the syntax. §4.3.3 lists the sources, the filters, escaping per context, unknown tags that
render empty with a form-check warning, answer recall in visitor texts, and the extension interfaces
`IFormBuilderMergeTagSource` and `IFormBuilderMergeTagFilter`. D10 makes merge tags part of the logic both runtimes
share, so the vectors (`merge-tags.json`) run in C# and TypeScript. Step 5.3 asks for a fuzz test (10,000 templates,
never an unhandled exception, never over 50 ms) and an injection test (no HTML, URL or JSON breakouts).

Open points:

- **Escaping:** what each context does, and how an explicit filter (`url`, `json`, `html`) combines with it.
- **What a tag may use where:** cached markup holds nothing per visitor (ADR-0004), so a label cannot show a member's
  name; a redirect must not let a visitor pick the host (ADR-0002).
- **Culture formatting** of dates and numbers: .NET and the browser carry different culture data, so the same filter
  could print "23.10.26" on one side and "23.10.2026" on the other.
- **Lists:** several choices, files, repeater rows.

## Options

1. **An existing template language** (Liquid, Handlebars, Mustache). Familiar, but each needs a library in two
   runtimes with the same behaviour (none exists for every feature in both), brings loops and conditions that belong
   to rules, and its documentation would define our semantics.
2. **A minimal language of our own**: paths and filters only, no loops, no conditions, no expressions, one grammar
   implemented twice and pinned by shared vectors.

## Decision

**Option 2.**

### The grammar

```text
template  = { text | tag } ;
tag       = "{{" , ws , ( path | string ) , { ws , "|" , ws , filter } , ws , "}}" ;
path      = name , { "." , segment } ;            (* field.email, page.up.heroTitle, dictionary.Forms.Thanks *)
filter    = name , [ ws , ":" , ws , argument , { ws , "," , ws , argument } ] ;
argument  = string | number ;
string    = '"' , { character - ( '"' | "\" ) | "\" , ( '"' | "\" ) } , '"' ;
number    = the wire number of ADR-0005 ;
name      = lowercase letter , { letter | digit } ;
segment   = ( letter | digit | "_" | "-" ) , { letter | digit | "_" | "-" } ;
ws        = { " " | tab } ;
```

- Text outside tags is literal. A lone `{` or `}}` is literal. A tag whose content is a string prints the string, so
  `{{ "{{" }}` prints two braces; there is no other escape.
- Source and filter names are lowercase; aliases and property names keep their case.
- `dictionary.` takes the rest of the path as one key, dots included.
- **Limits:** at most 200 tags per template, 10 filters per tag and 100,000 characters of output; the text limits of
  ADR-0002 bound the template itself.
- **Errors never throw** in either runtime. A malformed tag renders as its literal text and is a form-check error; an
  unknown source, path or filter renders empty and is a form-check warning; a limit is a form-check error and the
  output stops at the limit.

### Sources

| Source | Gives |
| --- | --- |
| `field.<alias>` | the display text of the answer: option labels in the entry's culture, numbers and dates formatted for the culture, file names; several answers joined |
| `field.<alias>.value` | the stored value in its wire form (option values, ADR-0005) |
| `field.<alias>.label` | the field's label in the culture |
| `field.<repeater>.<child>` | the child's answers across the rows, as a list |
| `entry.number`, `entry.reference`, `entry.date`, `entry.state`, `entry.url` | the entry's number, reference, submission time, state, and its link in the backoffice (for team mail) |
| `entry.answers` | every visible answered field: a table in HTML, `Label: answer` lines in text |
| `entry.table` | the same with the fields without an answer, unless the action leaves them out ([ADR-0160](0160-the-email-action.md)) |
| `form.name`, `form.alias` | the form |
| `page.name`, `page.url`, `page.<propertyAlias>`, `page.up.<propertyAlias>` | the content page the form is on: its name, its absolute URL (Umbraco's address of the page, a domain's for a page under one, otherwise after the site's base URL; ADR-0067), a property's value, or the nearest ancestor's |
| `member.name`, `member.email`, `member.<propertyAlias>` | the signed-in member; a property only when the member type lets the member see it and does not mark it sensitive (ADR-0067) |
| `query.<name>` | a query value, only for names the form allows (`developer.queryValues`) |
| `context.<key>` | a value the developer passed when rendering or submitting |
| `site.name`, `site.url` | the site's root node name and its base URL (ADR-0008) |
| `date.now` | the current time in the site's time zone |
| `dictionary.<key>` | an Umbraco dictionary item in the culture, falling back through its parent cultures, Umbraco's fallback languages and the default language (ADR-0067) |

A field's former aliases (ADR-0002) resolve to the field. Sources added through `IFormBuilderMergeTagSource` declare
their name, their paths for the picker, the contexts they may be used in, and whether their values are per visitor.
**Amended in 5.3c (ADR-0065):** a site's source declares the places it may be used in rather than contexts (a place
fixes its contexts), may not take a built-in source's name, and gives the empty text when it fails.

**Sensitive fields** (ADR-0009), one rule per output:

- entry titles and the search text leave them out (their tags render empty);
- a `field.<alias>` tag renders `•••`, except in an action that names the field to include it;
- `entry.answers` leaves them out, except in an action that names them to include them; in mail it also leaves out
  every field whose `includeInEmails` is false (ADR-0002);
- the copy of the answers and the outcome message that a visitor sees show them masked.

**Fields that rules hid** for the entry render empty, and `entry.answers` leaves them out.

**Amended in 5.3d (ADR-0066):** each field type writes its answers' display texts; `field.<alias>` is a list where the
type takes several values and one item otherwise; `field.<alias>.<part>` gives a part of a name or an address and
`field.<repeater>` the group's rows; `.value` is the wire form rules compare (a ticked consent is `true`); a
sensitive field without an answer renders empty rather than `•••`; and ADR-0066 lists what each place shows of a
sensitive field.

**Amended in 5.3f (ADR-0068):** a field is above a visitor text when it comes before the text's page, group or field
in the order of the form and does not hold it; visitor texts name fields by their current aliases only; a hidden
field, whose value stays on the server, is empty in them; and a site's own type shows there by its rule kind, since
the browser has no display of its own.

### Filters

| Filter | Effect |
| --- | --- |
| `default: "text"` | the text when the value is empty |
| `upper`, `lower` | the simple case mappings of ADR-0005's folded blocks; other characters unchanged, so both runtimes agree |
| `truncate: 50` (and an optional suffix, `"…"` by default) | at most that many code points |
| `date: "short"` | a date or time in one of the named formats `short`, `medium`, `long`, `time`, `dateTime` or `iso` |
| `number: 2` | a number with that many decimals, rounded half away from zero, with the culture's separators |
| `currency: "CHF"` | a number as an amount in that ISO 4217 currency, in the culture's pattern |
| `join: "; "` | a list joined with that separator (", " when not given) |
| `html` | text as HTML: escaped, with line breaks kept as `<br>`; only in HTML contexts |
| `url` | percent-encoded as one URI component |
| `json` | a complete JSON value (a string, a number or an array) |

- Filters on a list apply to each item, except `join`, `default` and `json`, which take the list.
- Filters added through `IFormBuilderMergeTagFilter` are server-only unless they ship a TypeScript twin and pass the
  vectors; the picker offers server-only filters only where the server renders. **Amended in 5.3c (ADR-0065):** a
  site's filter is server-only, with no TypeScript twin, so the places the browser renders too refuse it.

**Culture formats are resolved by the server.** The named date formats, the number separators and grouping, and the
currency patterns come from the server's culture data, written into the public projection as explicit patterns
(`dd.MM.yyyy`, the month names, the separators). The browser formats with those patterns, so both runtimes print the
same text for the same culture, and the vectors pin the pattern formatter rather than either platform's data.

### Contexts and escaping

Every place that renders a template has one context. The context's encoder runs last, on every value a tag produces,
never on the template's own text.

| Context | Encoder | Used by |
| --- | --- | --- |
| `html` | HTML-escapes `&`, `<`, `>`, `"` and `'` | rich texts, outcome messages, HTML mail bodies, labels and help as rendered |
| `text` | none | plain-text mail bodies, Slack and Teams text, entry titles, content-action values |
| `header` | line breaks become spaces, then trimmed (ADR-0007) | mail subjects and other header values |
| `url` | percent-encodes each value as a URI component | redirect URLs, after the host only (ADR-0002) |
| `jsonString` | escapes as JSON string content | tags inside string literals of a JSON webhook body |
| `form` | `application/x-www-form-urlencoded` encoding | form-encoded webhook bodies |
| `xml` | escapes `&`, `<`, `>`, `"` and `'` | XML webhook bodies |

- **Explicit filters combine safely:** a value that `html` turned into HTML is not escaped again in an HTML context;
  a value that `url` encoded is not encoded again in a URL context; `json` outside a JSON string literal writes a
  complete value, and inside one is refused at publish. Any other combination is encoded twice, which is safe.
- **No context accepts markup from a value.** Visitor text in mail to a visitor is plain text: links a visitor typed
  never become clickable (§4.10).
- **Amended in 5.3b (ADR-0064):** a JSON body has one `json` context whose scan knows whether a tag stands inside a
  string literal; each context refuses a tag where a value could become code, markup or an address's host (in markup
  outside text content, before a URL's path, after a backslash in a JSON string); and a tag's value is text, or text
  with the wire number or date behind it.

### Where each source may be used

| Where | Context | Sources |
| --- | --- | --- |
| Labels, help, placeholders, page and group titles, text blocks | `html` or `text` | `field.*` (answer recall) of fields above the text or on earlier pages, by their current aliases (ADR-0068); `form`, `page`, `site`, `date`, `dictionary` |
| Consent texts | `html` | `form`, `page`, `site`, `dictionary` only, so the stored wording is the same for every visitor and holds no personal data (ADR-0009); any other source is a form-check error |
| Default values | `text` | `form`, `page`, `site`, `date`, `dictionary`, `query` (allowed names) |
| Hidden field values | `text` | all, `member` and `context` included, resolved when the entry is submitted and never rendered into the page (§4.3.2) |
| Outcome messages and the copy of the answers | `html` | all |
| Redirect URLs | `url` | all, after the host only |
| Entry titles | `text` | `field.*` (sensitive fields render empty), `entry`, `form` |
| Mail subjects and bodies | `header`, `html` or `text` | all |
| Webhooks, Slack, Teams, the content action | their body's context | all |

- **Visitor texts before submission are cache-safe:** they use only what is the same for every visitor on that page.
  The server resolves everything but `field.*`, and the runtime fills in `field.*` as the visitor types (answer
  recall, §4.3.3). A text that names a later field (`field_after_text`, ADR-0068), or a per-visitor source, is a
  form-check error.
- **Query values on cached pages** are filled in by the runtime in the browser, because Umbraco's cache key ignores the
  query string (§3.4); the server fills them in only on renders that are not cached.
- **The picker** (9.4, 10.2) offers exactly the sources and filters that the context allows, with the form's fields
  above the text first.

### Tests

The vectors (`tests/vectors/merge-tags.json`, the `template` case of `jfb-vectors/1`) cover the grammar, every
source kind, every filter, every context's encoder, explicit filters in each context, lists, answer recall with
hidden fields, the culture patterns, the limits and every error path. 5.3 adds the fuzz test and the injection test,
and the mutation sweep of ADR-0005 covers the template module.

## Consequences

- One grammar of about a dozen rules, implemented twice and pinned by vectors, so a label previews in the browser
  exactly as the server renders it.
- Escaping is a property of the place, not the editor's job; an editor cannot produce a breakout by choosing the wrong
  filter, only a doubly encoded value.
- Culture formatting depends on the server's culture data alone, at the price of a few patterns in the projection.
- No loops and no conditions: lists render joined, and anything conditional is a rule (an outcome per case, an action
  rule) rather than template logic.
- §4.3.3 gains `field.<alias>.value`, `entry.answers`, the contexts and the cache-safe rule (WORKLOG).
- **Revisit** if editors need per-row formatting of repeaters in mail, or a conditional sentence often enough that
  rules feel heavy.
