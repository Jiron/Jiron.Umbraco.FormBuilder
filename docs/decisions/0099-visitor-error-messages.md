# 0099. Visitor error messages

- Status: accepted; extends [ADR-0022](0022-default-theme-and-tokens.md) (the error summary's heading and links) and
  [ADR-0005](0005-shared-logic-and-test-vectors.md) (a vector suite for messages); a summary link after a label that
  ends in its own mark takes no colon since 6.4a ([ADR-0106](0106-the-default-theme-ships.md))
- Date: 2026-10-29
- Roadmap step: 6.2g (decided and built); 6.2h shows the messages after a post without JavaScript, 6.4 shows them in
  the browser from the same words, 14.2 adds the other five languages

## Context

The pipeline answers a refused submission with problems: a code and a path each (ADR-0088). A code is a field's kind
of error (`required`, `maxLength`, ... the eighteen of ADR-0002), `check` with the index of the field's own check
that fails, `rule` with the key of the validation rule whose message shows, or `empty` for a submission without an
answer. The visitor needs words: under the field and in the error summary, which names each problem as its fix and
links to the field (ADR-0022). A post without JavaScript (6.2h) renders them on the server, the runtime (6.4) in the
browser, and D10 says the two never disagree about a message.

The definition holds an editor's own words: a field's `messages` per kind, each check's `message`, each validation
rule's `message`, all visitor texts in the culture (D20). Nothing holds the package's words for an error the editor
wrote nothing for, and nearly every field has none. `docs/ux/copy.md` sets the tone: an error says what to do ("Enter
a number between 1 and 20"), never blames, and the summary's heading counts ("This answer needs a change", "These
answers need a change").

Open were: what a default says and which values it shows; where the words live so both runtimes show the same; how a
default reads in the summary, where several fields may share one; and what the empty submission says.

## Options

**Where the defaults live:**

1. **Each runtime its own**, the server's in C#, the runtime's in TypeScript, kept alike by review. They drift, as
   every pair of hand-kept lists does.
2. **The server writes every field's messages into the form endpoint's answer**, so the runtime only shows them. One
   writer, but every message of every field travels with every form (a 50-field form about 150 texts), a limit
   relative to today goes stale on a cached page, and a message that depends on the answer (below) cannot be written
   ahead.
3. **The same words and the same rules in both runtimes, held by a vector suite**, as the rules, checks and merge
   tags already are (ADR-0005): each case names a field, a kind, the answers and a culture and expects the text. The
   words are data a test can compare; the values are formatted with the culture formats both runtimes already share
   (ADR-0064), so a date or a number prints the same.

**How a default reads in the summary:**

1. **The message alone,** as under the field. Two fields left empty give two links that both say "Enter an answer",
   which fails WCAG 2.4.4 (link purpose): the list says nothing more.
2. **The field's label in every message** ("Enter First name"). Labels are often questions or nouns with their own
   capitals, so no sentence holds every label in every language.
3. **The message under the field, and in the summary the field's label before it** ("First name: Enter an answer"),
   for the package's defaults only. An editor's own message names its field already, as `docs/ux/copy.md` asks
   ("Enter your first name"), and shows as written in both places.

## Decision

Option 3 for both.

- **Which message shows,** first found: a field's own message for the kind, in the culture; for `check`, the check's
  own message; for `rule`, the validation rule's message; otherwise the package's default for the kind and the
  field's type, in the language of the page's culture (its own words, else its more general culture's, else English,
  as the rendering's words, ADR-0093). An editor's text that is empty counts as none.
- **The defaults** say what to do, in the patterns of `docs/ux/copy.md`, and the copy guide lists every one in
  English and German:
  - `required` by what the control asks: enter a text, an email address, a phone number, a web address, a number, a
    date, a time, a date and a time; choose an answer, at least one, a rating, a point on the scale, a value, a
    country, `Yes` or `No` in the field's own words; an answer in each row of a grid and in a grid's row; put the
    options in order; tick the box, tick it to agree; enter the name and the address in full; choose a file; sign or
    type the name. A site's own type says "Answer this question".
  - `invalid` by type, with an example where one helps: an email address in full (`name@example.com`), or, for a
    well-formed address, one at another domain (the domains a field takes stay on the server, ADR-0062, so no message
    names them and the browser can tell the two apart by the address alone); a web
    address in full (`https://example.com`); a number with an example in the culture's format; a real date, time or
    date and time; a rating from 1 to its count; a point from the scale's start to its end; a value of a slider's
    range; one of the options given; a country from the list; sign again. A site's own type says "Check this answer".
  - `minLength`, `maxLength`, `minWords`, `maxWords` with the field's limits; `min` and `max` with both limits when
    the field has both ("Enter a number from 1 to 20"), else the one it has; `step` with the field's step and its
    start, or a whole number, or the decimal places a number's decimals allow, or a time's minutes; `dateWindow` with
    the window's dates (one relative to today counted from the site's today), Monday to Friday in the culture's names
    for a field that takes weekdays only, and "This date is not available" for an answer the window would take
    that the field excludes; `selections` with the fewest and most; `fileType` with the field's formats, `fileSize`
    with its limit in KB or MB, `fileCount` with its most; `full` with the option's label when the answer holds one
    option; `confirmation`, `digits`; `rowCount` with the group's least or most. Only `dateWindow`, the email
    address's `invalid` and `full` read the answer.
  - `check` without its own message: the answer in the format asked for (`pattern`), the same as the other field,
    named by its label (`matchesField`), an answer already given (`uniquePerForm`); `rule` without one: "Check this
    answer"; `empty`: "Answer at least one question before you send the form".
  - **Values** print with the shared culture formats (ADR-0064): numbers as the culture writes them, as written
    (no places added or cut), dates in the culture's `medium` pattern, times in its `time` pattern, sizes in KB
    below a megabyte and in MB from one (1,024 based, one place at most), lists of formats with commas. Counts take
    the language's singular or plural ("1 character", "2 characters").
- **One set of words:** the defaults are a table per language (`VisitorMessageWords` beside
  `Jiron.Umbraco.FormBuilder.Logic.VisitorMessages` in Core, `src/lang/messages.<language>.ts` beside `messages.ts` in
  `packages/logic`), each a whole sentence with named values, never joined from pieces.
  `tests/vectors/messages.json` (suite `messages`) holds them: each case gives a field as the form endpoint shows it
  (its type, its public settings with their effective values, its options' labels), the kind, the answers, the
  culture and the site's today, and expects the text, in English and in German, with each culture's formats from
  `tests/vectors/cultures.mjs`, which the merge tags' suite now shares. Core and `packages/logic` pass it now, 6.4 shows the runtime's messages on the page, and
  14.2 adds the other five languages to both (ROADMAP 6.4 and 14.2, strengthened in 6.2g's split). A test lists every
  kind each built-in type declares (`FieldTypeDeclaration.Messages`) and finds a default of its own for it in both
  languages; another finds every word of each language in the copy guide's table.
- **The error summary** (amends ADR-0022's "the same words under the field"): its heading says "This answer needs a
  change" for one error and "These answers need a change" for several, in German "Diese Antwort muss noch geändert
  werden" and "Diese Antworten müssen noch geändert werden", as the copy guide has it. A link to a package default
  puts the field's label first, "{label}: {message}" ("{label} {message}" after a label that already ends in a
  question mark, an exclamation mark, a full stop or a colon, since 6.4a), and a field in a repeatable group's row
  adds the row's legend, "{label} ({row}): {message}"; a field without a label, or one whose label is hidden, shows
  the message alone, as does every editor's own message. The error under the field is always the message alone. The empty submission's
  message stands first in the summary, linked to the first field of the page that takes an answer; a form whose
  summary is off still shows it there, alone, since it belongs to no field.
- **On the page** a post leaves each error as its kind (`FormPostError`: the code, the check's index, the rule's key
  and a grid's row), never as text, and the page works out the message in its culture, so a post's errors read as
  the runtime's would. A grid shows its errors below its rows, so a default at one of its rows names the row there
  ("Morning: Choose an answer in this row") and in the summary in the row's place ("Times (Morning): ...").

## Consequences

- 6.2h renders these messages for the pipeline's problems on a post without JavaScript, at their fields and in the
  summary, and 6.4's runtime renders the same from the same words; a change to a default is a change to the vectors,
  which both runtimes then fail until they agree.
- The messages read only what the form endpoint already gives (the public settings with their effective values, the
  options' labels, the culture's formats and the site's time zone), so the browser has every value a message needs.
- A cached page's dates relative to today go stale at midnight as its window does (ADR-0097); the runtime works the
  window out again.
- A site's own type gets the generic defaults without values; its editors give it messages of their own.
- **Revisit** when a language needs more than a singular and a plural (14.2 checks the five it adds), or when a type
  needs a default that depends on the answer beyond the three above.
