# 0112. Checks and messages on the page

- Status: accepted; builds on [ADR-0062](0062-the-built-in-types-checks.md) (the checks both runtimes share),
  [ADR-0099](0099-visitor-error-messages.md) (the messages both runtimes share) and
  [ADR-0111](0111-rules-on-the-page.md) (the state the checks run in)
- Date: 2026-10-30
- Amended in 6.4g by [ADR-0115](0115-answer-recall-on-the-page.md): a summary link names a label that recalls answers as
  it renders for the answers checked, an empty one as none
- Roadmap step: 6.4d (decided and built); 6.4e shows the errors on the page, 6.4g names a label that recalls answers
  as the page shows it, 6.4i sends what passes

## Context

Without JavaScript the server checks a post as Core's pipeline does (`SubmissionReader`): each shown field in the
form's order, a repeatable group's row count and then its rows' children, a field over its type's limits alone, its
type's checks, its own checks and the validation rules whose message shows at it. The post handler keeps a field's
first error, and the page shows its message under the field and in the error summary, the editor's own message where
there is one and otherwise the default in the visitor's language (ADR-0099), the summary's link putting the field's
label, and in a row the row's legend, before a default. A send with no answer at all is refused as empty, its message
linked to the page's first field.

The browser has the pieces: the reading stage (6.4a), the page's answers (6.4b), the rules' state (6.4c), and in
`packages/logic` the built-in types' checks (`checkField`), the default messages (`defaultMessage`, `summaryEntry`)
and the words of English and German (`lang/`). What it lacks is the pipeline's order and scope, the choice of the
message, and a way to load only the page's language. The question is also how to prove that the result is the
server's, beyond the shared vectors each piece already passes.

## Options

**Where the order lives.** (1) In `packages/logic`, as a twin of `SubmissionReader`: it would need the page's reading,
which is the runtime's. (2) In the runtime, over the reading and the rules' state it already has, with the pieces
from `packages/logic`.

**What the browser leaves to the server.** (1) Everything it cannot check exactly. (2) A guess for the rest (an upload
it has not sent, options a source gives): a guess the server then contradicts is worse than none.

**The proof.** (1) Unit tests of each rule: they state what the code says. (2) A fixture of the server's own pages:
forms posted without JavaScript, the page the server renders again with its errors saved by an integration test, and
the runtime's errors and messages on that same page compared with the server's, field by field and link by link.

**The words.** (1) Both languages in the chunk: every page pays for both, and 14.2 adds five more. (2) One file per
language, loaded for the page's culture.

## Decision

Option 2 each time.

### The errors (`packages/runtime/src/checks.ts`)

- **Each shown field in the form's order,** as `SubmissionReader` checks it: a field the rules hide, or one in a hidden
  group or page, has none; a repeatable group gets its row count's errors (`checkRows`) and the validation rules' at
  it, then each row's shown children with that row's state; a field over its type's limits has that error alone
  (`maxLength` or `invalid`, from the reading); otherwise `checkField` with the field's kind, whether the rules require
  it, its settings, its own options with the parent values they belong to, a grid's rows, its own checks, today in the
  site's time zone and the other fields' values as the rules see them (in a row, the row's children's); then the
  validation rules whose message shows at the field or one of a grid's rows.
- **Left to the server:** a calculation (its value is the server's), the `hidden` type (its value is resolved there), a
  type that stores files (uploads and signatures, 6.4m and 6.4n), options from a list or a source (any answer is taken,
  as on the server when a source cannot give them), the checks that compare with other entries (`uniquePerForm`, the
  places an option has left) and a site's own type's check (only `required` is the browser's).
- **What the page shows:** a field's first error, as the post handler keeps it; on Next, the errors of the page's
  fields; on Send, every page's. A Send with no answer at all is the empty submission, as `FormSubmissions` decides it
  (`isEmptyPost` in `posts.ts`: every posted text blank once cleaned, a consent's wording aside).

### The messages (`packages/runtime/src/messages.ts`)

- **An editor's own message** as written: the check's for a failing check, the validation rule's for a rule, otherwise
  the field's for the kind; an empty one is none. Otherwise **the default** (`defaultMessage`) for what the server's
  render gives it: the field's type, whether it is the built-in one, its settings, its own options' labels, the check's
  kind, the other field's label for `matchesField`, whether the error is at a grid's row, the answers as posted and a
  group's rows.
- **Labels as the page shows them:** a field's label or legend (`.jfb-label`) without its mark; none when it is hidden
  from sight; a box's field label, which the page may not show, from the model data. A row's legend from the page, a
  grid row's label from the model data. A grid row's default names its row under the grid; a summary link puts the
  label and the row's legend before a default (`summaryEntry`), and an editor's own message stands alone in both.
- **The words** of English and German are files of their own (`@jfb/logic/lang/en`, `@jfb/logic/lang/de`), which the
  runtime imports for the page's culture only, the first time a message is needed: the culture's language, else
  English, as Core's `VisitorMessageWords` falls back. 14.2 adds five more files and pays nothing on the others' pages.

### The proof

`Rendering/ErrorFixtureTests` (integration) publishes a form with a field of every kind the browser checks and a form
of two pages, posts them as a browser without JavaScript does, on a site in English and on one in German, and saves
each page the server renders again (the form element, its route token and honeypot left out and its key fixed) with
the clock it used in `tests/fixtures/rendering/errors-en.json` and `errors-de.json` (`JFB_UPDATE_SNAPSHOTS=1` writes
them and fails). The posts: every required field left empty with one answer given; answers each check refuses, a grid
row's validation rule without a message and a row's answer that must match another among them; nothing at all; and
Next on the first page of two. `checks.fixture.test.ts` loads each page in jsdom, reads it, applies its rules and
checks it as the runtime does, and expects every error the server wrote (`[data-jfb-error]` by its id, with its text)
and every link of the summary (its target and its text), in the server's order. What no such post reaches (an answer
over its type's limits, an editor's empty message, options a parent's answer filters, the empty submission's link past
what the page hides, what is left to the server) has unit tests in `validation.test.ts`.

## Consequences

- The browser and the server decide a field's errors and their words alike, held by the shared vectors piece by piece
  and by the server's own pages as a whole; a change to either that the other lacks fails the fixture.
- The messages cost a request the first time a page needs one, a small file cached for a year under its content hash.
- A label that recalls answers is read from the page; a box's field label that recalls answers waits for 6.4g.
- **Revisit** if a field type gets a check the browser cannot make exactly; it then joins what is left to the server.
