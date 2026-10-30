# 0110. The runtime's frame, its chunk and the reading of the page

- Status: accepted; carries out [ADR-0004](0004-rendering-and-submission-paths.md)'s error display on demand as one
  chunk with the logic, loaded on a form's first interaction; extends
  [ADR-0109](0109-reading-answers-in-the-browser.md) (the table of the built-in types says which hold child fields and
  which the server resolves)
- Date: 2026-10-30
- Roadmap step: 6.4b (decided and built); 6.4c applies the rules and 6.4d the checks to what the frame reads, 6.4g
  sends it, 6.4h swaps a changed form in, 6.4i adds `get(formEl)` and the other events

## Context

Step 6.4b starts the website runtime. Its entry had only set `window.JironFormBuilder`; it now has to find the forms
of the markup contract ([ADR-0015](0015-themes-and-the-markup-contract.md)), read each one's model data (the form
endpoint's answer, [ADR-0093](0093-the-razor-rendering.md)), and read the page's controls into the answers the
server would take, so that 6.4c's rules and 6.4d's checks decide what the server decides.

Two facts shaped it. First, the size: section 4.12 gives the runtime's core 16 KB gzip, and the shared logic that
validation and rules need (the reading stage, the checks, the rules, the form engine and the messages) bundles to
about 15.9 KB gzip by itself, measured with the bundler the build uses, before calculations (3.8 KB more) and before
any code that touches the page. ADR-0004 already loads the runtime's error display on demand and fetches the
challenge on the visitor's first `focusin` or `input`. Second, the reading: the server reads a post without JavaScript
by the names the controls carry (Web's `FormPostReader`, [ADR-0100](0100-the-post-without-javascript.md)): a field's
parts, a repeatable group's rows up to the count the page shows, a consent's wording, a switch that posts nothing. The
browser shows the same markup, so its controls carry the same names.

## Options

**Where the logic loads.**

1. In the entry: the page then loads everything the moment it shows a form, and the core is over its budget.
2. As a chunk loaded at once on every page with a form: the core check passes, but in name only, since every such
   page loads the chunk anyway.
3. As a chunk loaded when the browser is idle: the same cost for every visitor, a little later.
4. As a chunk loaded on a form's first interaction: a visitor who reads the page and never touches a form loads the
   frame alone. Nothing is lost while it loads: the server renders the form in the state its rules give, and the
   post without JavaScript still works.

**How the page is read.**

1. Control by control, per field type: a second reading of the page to keep equal to the server's, by hand.
2. Through `FormData`: the browser's own list of what the form posts (enabled controls with a name, the chosen
   options, a checkbox only when ticked), read by its names exactly as the server reads the post, by a twin of
   `FormPostReader` that one vector suite holds to it.

**Where that reader lives.** In the runtime, with the DOM; or in `packages/logic`, which has no DOM, takes the names
and values as data and is what the vector runner already runs.

## Decision

Option 4 for the logic, option 2 for the page, and the reader in `packages/logic`.

### The frame

- **The entry is the frame,** about 1 KB gzip. Once the document is parsed, and for any root a site passes to
  `JironFormBuilder.init(root)` (the root itself when it is a form), it takes every `form[data-jfb-form]` whose
  `data-jfb-contract` is `1`, reads its `script[type="application/json"][data-jfb-model]`, and enhances the form once:
  a form already enhanced, of another contract version, without model data, or with model data that is not the shape
  this runtime reads (an object with the alias, the fields' logic, the consents, and the form's pages, groups and
  fields, each with its key and type) is left alone, and so to the post without JavaScript. An outcome or a notice in
  place of a form is a `div` and is never enhanced. A second copy of the runtime on the page keeps the first one's
  global and enhances through it.
- **`jfb:ready`** is dispatched on the form when it is enhanced, once per form, bubbling, with `{ key, alias }` and
  never an answer (section 4.9). It fires as the frame runs, so a site listens from a script that runs before the
  runtime (the head, or a classic script); a form the site adds later fires it when the site calls `init`.
- **The chunk** (`chunks/form-<hash>.js`, named by its content, ADR-0004) loads on a form's first `focusin`,
  `pointerdown`, `input` or `change`: a field reached, pressed or changed, a browser's autofill included, which changes
  fields without focusing them. It loads once for the page, and the frame then gives it each form once
  (`attach(form, model)`). When it cannot load, the frame does not try again and the forms stay with the post without
  JavaScript. In 6.4b the chunk reads the page (6.28 KB gzip); 6.4c to 6.4e add what the visitor sees.

### Reading the page

- `entriesOf(form)` is `new FormData(form)` with its texts: a file is the upload's to read (6.4j), a disabled control
  (one a rule hides) and a button are left out, as a browser's post leaves them out.
- `readPost(fields, entries)` (`packages/logic/src/posts.ts`) reads them as `FormPostReader` does, over the fields'
  aliases in the projection (the page and its model data are one published version, so a former alias never names a
  field there):
  - `<alias>` and `<alias>.<part>` (the part after the first dot; an empty part is the field's own text), a repeatable
    group's `<group>[<row>].<child>` with a row in ASCII digits as .NET reads them (leading zeros, trailing NULs) and
    below 100; any other name is left out;
  - a group's rows as many as `jfb-rows-<alias>` says (its last value, at most 100), or as far as a name of a child
    goes, a row past the count left out and a row without answers kept in its place;
  - `jfb-consent-<path>` as the consent's `wording` part, `jfb-order-<path>` naming the ranking's row but no answer,
    `jfb-context-<name>` (the last value) and `jfb-query-<name>` (every value);
  - a yes or no switch that posts nothing as `no`, in each row the page shows too, and a signature's typed name left
    out, as the server draws it;
  - each CRLF as LF in a field's texts and a consent's wording, the package's other names as posted.
- **Which fields are what:** a repeatable group is a built-in type that holds child fields, or a site's own type with
  child fields; a consent, a signature and a switch are only the built-in types of those aliases, as the form
  endpoint's `fields[key].builtIn` says (ADR-0109). `BUILT_IN_TYPES` gains `childFields` and `serverOnly`, which
  `reading.test.ts` holds to the served declarations' `features` and `serverOnlyDefault`, and `MOST_ROWS` (100) is
  held to the most a repeatable group's `maxRows` takes.
- **Then each field as Core's `SubmissionReader` reads a submission:** a repeatable group's rows are read only while
  their number keeps the group's limits (`checkRows`); a value the server resolves (the `hidden` type) is never taken
  from the page, unless the field is prefilled from the query; a built-in type's answer goes through 6.4a's
  `readField`, so an answer over its limits or unreadable gives the rules no value; a site's own type gives its
  cleaned texts as they are (ADR-0109). The rules' answers are each value by its field's key, a grid's by its rows'
  keys, and each group's rows by the group's key, only for fields with values, as Core puts them.
- **The check:** `tests/vectors/posts.json` (suite `posts`, 85 cases: texts and parts, names of no field, rows by count
  and by name with malformed names, consents, switches, context and query values, rankings' orders and signatures)
  passes in NUnit through `FormPostReader` (`PostVector`) and in Vitest through `readPost`. A site's own type under a
  built-in alias is the browser's decision alone, so `posts.test.ts` tests it.

## Consequences

- The core is the frame, so the 16 KB check measures what every page with a form loads before the visitor does
  anything; the logic counts in the 40 KB of everything, which the later parts of 6.4 fill.
- A visitor's first touch waits for one request from the site's own host, cached for a year under its content hash.
  Until it arrives the form is the server's: the rules' first state is rendered, and an answer typed meanwhile is
  read from the controls when the chunk is there.
- A change to the names the views give controls, or to how the no-JS post reads them, is a change to the vectors,
  which both readers then fail until they agree.
- `jfb:ready` fires before the scripts that run after the runtime, so they miss it; from 6.4i, `get(formEl)` answers
  for a form that is already enhanced.
- **Revisit** if the chunk with the logic grows past what a first touch should wait for, or if browsers give a way to
  load a module when a form is first seen that costs nothing before then.
