# 0064. The merge-tag engine

- Status: accepted; amends [ADR-0014](0014-merge-tag-language.md) (the values tags give, the `json` context, where a
  tag may stand in each context, the problems and what each renders) and
  [ADR-0005](0005-shared-logic-and-test-vectors.md) (the `template` case, the cultures in a vector file's defaults, and
  the case mappings in the tables check); the problems of a place and the form check's messages amended by
  [ADR-0065](0065-the-merge-tag-service-and-its-sources.md) in 5.3c
- Date: 2026-10-26
- Roadmap step: 5.3b (decided and built); 5.3c to 5.3f add the sources, answer recall and the culture formats from
  .NET (ADR-0065), 6.x renders texts, outcomes and redirects with it, 8.x mail and webhooks, 9.4 and 10.2 the picker
  and the form check's messages

## Context

ADR-0014 fixed the merge-tag language: `{{ source.path | filter: arg }}`, no loops, no conditions, eleven filters,
seven escaping contexts, unknown tags rendered empty with a warning and malformed ones rendered as written with an
error, and culture formats that the server writes as explicit patterns so both runtimes print the same text. Step 5.3b
builds the language in both runtimes over sources given as data; 5.3c adds the real sources. Building it raised
questions ADR-0014 does not answer:

1. **What a tag's value is.** `field.qty` is the answer as displayed, "1'234.5" in de-CH, but `number: 2` and `json`
   need the number behind it, and `date: "long"` the date behind "15.06.2026".
2. **Where a tag may stand.** Escaping keeps a value inside the place it stands in, but the place itself can be the
   danger: a value in an `href` attribute can be `javascript:alert(1)` however well its quotes are escaped, a value
   inside a `<script>` element is code, a value right after `https://` is the host a visitor is sent to, and a value
   right after a backslash in a JSON string consumes the escape and ends the string.
3. **JSON bodies.** ADR-0014's `jsonString` covers tags inside the string literals of a JSON body, and `json` "outside
   a JSON string literal writes a complete value", so one body holds tags of both kinds.
4. **What each problem renders, and where it points.**
5. **The case mappings.** `upper` and `lower` use "the simple case mappings of the folded blocks", but the runtimes'
   mappings differ there: JavaScript maps the dotless i to "I", .NET does not; .NET maps the Greek letters with a
   ypogegrammeni (U+1F80 to U+1FAF, U+1FB3, U+1FC3, U+1FF3) to their capitals, JavaScript's full mappings make two
   letters of them; Node's newer Unicode maps U+019B to U+A7DC, outside the folded blocks, and .NET does not. Folding
   agrees on all of them, so the tables check did not see it.

## Options

1. **Values.**
   - **Text only:** every value is a string. `number` would read display text, which is culture-formatted, so it
     reads nothing; editors would have to know to write `field.qty.value`.
   - **Typed items:** a source gives text, and with it the wire number or the wire date behind the text, so the number,
     currency, date and json filters read what they need. Chosen.
2. **Where a tag may stand.**
   - **Anywhere, escaped:** no breakout of a quoted attribute or a string, but a value can still choose a link's
     scheme, a script, a redirect's host or a JSON escape.
   - **Only where a value is data:** each context scans the template's own text and refuses a tag where a value could
     become code, markup or the address's host. Chosen: an editor's text rarely needs a tag in those places, and a
     refusal is a form-check error before anything is published.
3. **JSON bodies.** A context per tag chosen by the editor, which the editor can get wrong, or **one `json` context
   whose scan knows** whether each tag stands inside a string literal. The scan is chosen; `jsonString` stays as the
   context of a template that is itself the content of one string.

## Decision

### The engine

Each runtime parses a template once for its context and renders it as often as needed (answer recall renders a label
at every key stroke): `parseTemplate(text, context)` and `renderTemplate(parsed, values, culture)` in
`packages/logic/src/template.ts`, `MergeTags.Parse` and `MergeTags.Render` in Core. Parsing finds every tag, its place
in the context, its path or string, its filters and every problem that does not depend on values. Rendering asks the
sources for each path and writes the text. Neither throws for any template, any value or any culture data, and both
are linear in the template's length and the values' sizes.

### The grammar as built

The grammar is ADR-0014's. Letters and digits are ASCII; names start with a lowercase letter.

- **A tag's extent:** `{{` starts a tag; it ends at the first `}}` outside a string literal, where a string runs from
  `"` to the next `"` not escaped by `\`. When a string does not close, the tag ends at the first `}}` after its start
  instead, so one missing quote breaks one tag, not the rest of the template. With no `}}` after it, `{{` is an
  unclosed tag and the rest of the template is text.
- Inside a tag, spaces and tabs may stand around `|`, `:` and `,` and at the ends, nowhere else; a path has no spaces
  around its dots. A string's only escapes are `\"` and `\\`. A number is the wire form of ADR-0005, minus included.
- `{{ "{{" }}` prints two braces; a lone `{` and any `}}` outside a tag are text.

### Problems

A problem is `{ code, at }`, `at` the UTF-16 index where it starts. A tag with an error renders as it is written; a
tag with warnings only renders empty; a limit ends the output where it is reached. Problems are listed in the order of
the template.

| Code | Kind | Where | When |
| --- | --- | --- | --- |
| `malformed_tag` | error | where the grammar fails | the tag does not follow the grammar |
| `unclosed_tag` | error | the `{{` | no `}}` follows |
| `tag_in_markup` | error | the `{{` | `html`, `xml`: the tag is not in text content |
| `tag_in_escape` | error | the `{{` | `json`, `jsonString`: the tag follows a backslash inside a string literal |
| `tag_before_path` | error | the `{{` | `url`: the tag comes before the address's path |
| `invalid_argument` | error | the argument, or the filter for a wrong count | a filter's arguments do not fit it |
| `filter_not_for_context` | error | the filter | `html` outside the `html` context, `json` inside a JSON string |
| `filter_after_encoding` | error | the first filter after it | a filter follows `html`, `url` or `json` |
| `too_many_tags` | limit | the 201st tag | more than 200 tags |
| `too_many_filters` | limit | the eleventh `\|` | a tag with more than 10 filters |
| `too_long` | limit | the text or tag that reached it | the output would pass 100,000 UTF-16 code units; it stops there, never inside a surrogate pair |
| `unknown_path` | warning | the path | no source knows the path |
| `unknown_filter` | warning | the filter | no filter has the name |

A misplaced or malformed tag reports only that problem. A well-formed, well-placed tag reports an unknown path first,
then its filters' problems from left to right.

### Values

A source gives a **value** for a path: one **item** or a **list** of items. An item is text, or text with the value
behind it: `{ text, number }` with a wire number, or `{ text, date }` with a wire date, time or date-time. A path a
source does not know has no value (`unknown_path`); a known path without an answer is the empty text or the empty list.
A string literal is a text item.

- The `number` and `currency` filters read an item's `number`, the `date` filter its `date`, and both read a text
  item's text in the wire form; a value a filter cannot read passes through unchanged.
- Every other filter reads the text, and the text filters (`upper`, `lower`, `truncate`) give text items.
- Filters apply to each item of a list, except `default`, `join` and `json`, which take the list.
- **Empty** is the empty text, or a list whose items are all empty text (or none).
- A list that reaches the context unjoined is joined with ", ". When its items were encoded by `html` or `url`, the
  separator is encoded the same way, so `url` in the `url` context never leaves a raw space.

### Filters

| Filter | Arguments | Gives |
| --- | --- | --- |
| `default` | a string or number | the argument as text when the value is empty |
| `upper`, `lower` | none | each character's simple case mapping when it is one character in the folded blocks of ADR-0005; the Turkish i's (U+0130, U+0131) and, for `upper`, the letters with a ypogegrammeni (U+1F80 to U+1FAF, U+1FB3, U+1FC3, U+1FF3), whose full uppercase is two letters, stay as they are |
| `truncate` | a whole number 0 to 100,000, and a string (`"…"` when not given) | the first that many code points and, when it cut, the kept part without trailing spaces (the trim set of ADR-0005) and the string |
| `date` | `"short"` (when not given), `"medium"`, `"long"`, `"time"`, `"dateTime"` or `"iso"` | the date, the time or both in the culture's pattern; `iso` the wire form with seconds; a value without what the format needs passes through |
| `number` | a whole number 0 to 20 (its own decimals when not given) | the number rounded half away from zero and padded to that many places, with the culture's separators, grouping and negative pattern |
| `currency` | three capital letters, and a whole number 0 to 20 | the amount in the culture's currency pattern, rounded to the currency's minor unit (0, 2, 3 or 4 places as ISO 4217 has them, 2 for any other code) unless the places are given; the culture's symbol for its own currency, otherwise the code with a no-break space wherever the pattern puts the symbol next to the number |
| `join` | a string (", " when not given) | a list's items' texts with the string between them; one item as it is |
| `html` | none | the text escaped for HTML with each line break (CRLF, CR or LF) as `<br>`; only in the `html` context |
| `url` | none | the text percent-encoded as one URI component |
| `json` | none | a complete JSON value: a string, a number for an item with a wire number, or an array for a list |

`html`, `url` and `json` encode, so they come last. A value they encoded is not encoded again by the context of the
same kind (`html` in `html`, `url` in `url`, `json` outside a JSON string); any other context encodes it again, which
is safe.

### Contexts

| Context | A value's encoder | Where a tag may stand |
| --- | --- | --- |
| `html` | `&`, `<`, `>`, `"`, `'` as `&amp;`, `&lt;`, `&gt;`, `&quot;`, `&#39;` | in text content, as the next section reads it: not inside `<…>`, a comment, a `<!…>` or `<?…>` declaration, the content of `script`, `style`, `textarea`, `title`, `xmp`, `iframe`, `noembed`, `noframes`, `noscript` or `plaintext`, or right after a `<` |
| `xml` | the same with `&apos;`; characters XML 1.0 cannot carry become U+FFFD | in text content: not inside `<…>`, a comment, a CDATA section, a processing instruction or a document type declaration, or right after a `<` |
| `text` | none | anywhere |
| `header` | a CRLF, each other line break and each control character becomes a space, then the trim set of ADR-0005 is trimmed | anywhere |
| `url` | UTF-8 percent-encoding of everything but `A-Z a-z 0-9 - . _ ~`, lone surrogates as U+FFFD | after the path starts: after the authority of an `http:` or `https:` address (`https://host/` and on), after a leading `/`, `?` or `#`, or after the first `/`, `?` or `#` of a relative address; never in an address with any other scheme |
| `form` | `application/x-www-form-urlencoded`: everything but `A-Z a-z 0-9 * - . _` percent-encoded, a space as `+` | anywhere |
| `jsonString` | escaped as JSON string content: `"`, `\`, control characters, `<`, `>`, `&`, U+2028 and U+2029; lone surrogates as U+FFFD | not right after a backslash; a tag after a quote of the template's own text that ends the string writes a complete JSON value |
| `json` | a tag inside a string literal is in `jsonString`; any other writes a complete JSON value | as `jsonString` inside a string literal, anywhere else |

- The scans read only the template's own text, never a value, and treat anything that could open markup as markup,
  so a doubtful place is refused rather than allowed.
- The `url` scan reads the address as browsers do: tabs and line breaks are dropped, leading spaces and control
  characters skipped, and `\` counts as `/`, so `https:\\{{ … }}` is refused like `https://{{ … }}`.
- **A tag written as it stands is read by the scan**, because the output then holds its text: after a malformed,
  misplaced or unclosed tag, or a tag with an error, the scan goes on from the end of that text, so
  `{{ x | truncate: "<script>" }}` opens a script for the tags after it. A tag that writes a value, or nothing, leaves
  the scan where it was, since every encoder keeps its place.

### Reading markup

The `html` scan reads the template as the HTML tokenizer does, because a place the scan reads differently from a
browser is a place where a value lands somewhere else: `<!--><a onclick="-->{{ … }}">` puts the value in an event
handler if the scan thinks the comment ends at `-->`.

- **Tags:** a quote opens an attribute value only after `=`, so a quote in an element or attribute name, or in an
  unquoted value, is a character like any other. The scan keeps no more than ten characters of an element's name, more
  than any raw element's, so a long name is none of them.
- **`</`** and a letter open an end tag (which may hold attributes, as the tokenizer reads them); `</>` is nothing;
  `</` and anything else open a comment that ends at the first `>`.
- **Comments** end at `-->` and `--!>`, and at a `>` right after `<!--` or `<!---`. Any other `<!` and any `<?` open a
  comment or document type declaration that ends at the first `>`.
- **Raw elements** (the list in the table) end at `</`, the element's name in any case, and a space, `/` or `>`.

Where a browser's reading depends on the whole document, which the scan does not build, the scan refuses every tag
that follows instead of guessing:

- `<!--` inside a `script`, since it may start an escape that `</script>` does not end; and a `plaintext` element,
  which nothing ends.
- A raw element whose content holds a `<` other than its end tag's, when a browser may read that content as markup:
  `noscript` (raw only while scripts run), and every raw element after an `svg`, `math`, `select` or `frameset` start
  tag (foreign content does not make them raw, and the select and frameset modes may drop their start tags). Without
  another `<`, both readings end at the same end tag.
- A CDATA section after an `svg` or `math` start tag, which a browser ends at `]]>` inside foreign content and at the
  first `>` outside it.

The `xml` scan reads XML's well-formed markup the same way, with XML's differences: every `<` opens markup, a comment
ends only at `-->`, CDATA sections and processing instructions end at `]]>` and `?>`, a document type declaration's
quoted literals may hold a `>`, and an internal subset (`[…]`), whose markup declarations the scan does not read,
refuses every tag after it.

### Culture formats

The culture's formats are data the server writes (5.3c reads them from .NET): the patterns of `short`, `medium`,
`long`, `time` and `dateTime`; the month names, short, and in their genitive forms; the day names and short, Sunday
first; the AM and PM designators; the minus sign; the number's decimal and group separators, group sizes and negative
pattern; and the culture's currency code and symbol, its separators, group sizes and positive and negative patterns.

- **Date patterns** use a subset of .NET's custom format strings: `d dd ddd dddd`, `M MM MMM MMMM`, `y yy yyy yyyy`,
  `h hh H HH m mm s ss t tt`, quoted text in `'…'` or `"…"`, and `\` before a literal character; every other
  character is literal, so the server writes the culture's date and time separators into the pattern rather than `/`
  and `:`. A month name takes its genitive form when the pattern also has the day of the month (`d` or `dd`), as
  Slavic and Greek dates need.
- **Group sizes** are read as .NET reads them: from the decimal point leftwards, the last size repeating, a 0 ending
  the grouping (`[3]`, `[3, 2]`, `[3, 0]`).
- **Number patterns** use `n` for the digits, `-` for the culture's minus sign and, in currency patterns, `$` for the
  symbol; every other character is literal.
- An incomplete list of names falls back to the number, so wrong data prints numbers instead of failing.

### Vectors

`merge-tags.json` holds `template` cases: `{ "template": { "text", "context", "sources" } }`, where `sources` maps
each known path to its value (a string, `{ "text", "number" }` or `{ "text", "date" }`, or a list of them), and
`expect` is `{ "text", "problems" }`. A file's defaults gain `cultures`, the culture formats by name, and a case
formats with those of its context's culture. The cultures in the vectors are data written for the vectors, so they pin
the formatter, not .NET's or a browser's culture data.

### Other tests

- **Fuzz:** 10,000 random templates in each runtime, built from the grammar and broken by chance, rendered in every
  context over random values; each renders without an exception and in at most 50 ms (the fastest of three runs), and
  the templates reach every problem code.
- **Injection:** hostile values (markup, quotes, scripts, line breaks, addresses, JSON) through every filter in every
  context and every place a tag may stand; a check of each output shows that no value adds an element, an attribute,
  a header line, a URL's scheme or host, or a JSON token outside its string. The places a tag may not stand, the
  tokenizer's corners above among them, must render as written.
- **Lone surrogates**, which no vector can hold (.NET's JSON reader refuses one), have a test of their own in each
  runtime: wherever the output must be well formed, they become U+FFFD.
- **The tables check** gains `upper` and `lower`: what `upper` and `lower` do to every code point, compared between
  the runtimes like folding.
- **Mutation:** the mutation script covers `template.ts`, `culture.ts` and the case mappings of `text.ts` in
  TypeScript, and in C# the places where .NET differs (the case mappings, UTF-8 and hexadecimal output, comparers,
  character classes). Each survivor is killed by a vector or written down as an equivalent change. The equivalent
  ones are explicit case rules that one runtime's own mappings already keep: in TypeScript those for the dotted
  capital I and the letters with a ypogegrammeni, whose full mappings are two characters; in C# the one for the
  Turkish i's, which .NET's invariant casing leaves alone, and the capitals of the ypogegrammeni range, which map to
  themselves. Both runtimes state the whole rule, so neither depends on the other's mappings.

## Consequences

- Escaping and placement are properties of the place: an editor's template cannot let a value become code, markup,
  a header or another host, only a doubly encoded text or a form-check error.
- A tag inside an HTML attribute is refused, so a link to an entry in a mail body is the mail layout's job (8.x), not
  a merge tag's.
- 5.3c to 5.3f give each source's values as items (an answer's display text with its wire number or date), write the
  culture formats from .NET with the separators resolved, and check the paths each place may use (ADR-0065). The form
  check (10.x), not the service, turns each problem code into a message (amended in 5.3c).
- ADR-0014 gains the `json` context, the value model and the placement rules; ADR-0005 gains the `template` case,
  `defaults.cultures` and the `upper` and `lower` tables (WORKLOG).
- **Revisit** if editors need a tag inside an attribute (a URL attribute would need a scheme check of the value), or
  per-culture currency symbols beyond the culture's own currency.
