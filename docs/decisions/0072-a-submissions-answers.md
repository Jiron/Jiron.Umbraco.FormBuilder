# 0072. A submission's answers

- Status: accepted; amends [ADR-0070](0070-storing-answers-and-reading-them-back.md) (the values the server works
  out, and a repeatable group's stored rows) and [ADR-0061](0061-the-form-rules-engine.md) (the rules run twice in a
  submission)
- Date: 2026-10-27
- Roadmap step: 5.4d (decided and built); 6.1's pipeline and 6.2's posts without JavaScript call it, 6.5 adds the
  member and context prefill, 5.5's sweep checks that 6.1 wires it

## Context

Each piece of a submission's handling exists on its own: a field type limits, reads and checks one field's answers
(ADR-0058, ADR-0062, ADR-0071), the rules engine decides what is shown, required and run over the answers
(ADR-0061), merge tags render a hidden field's default (ADR-0066), and the entry's writer stores the answers
(ADR-0070). The pipeline (6.1) needs them put together over a whole form, and a few questions only come up there:

1. **Order.** The rules need the answers read, and the checks need the rules: a hidden field is never checked, and a
   field may be required by a rule.
2. **Rows.** A repeatable group's rows are posted as a list; their number is the group's to check (ADR-0071), and a
   post can send any number.
3. **Values the server works out.** A calculation's posted value is whatever the browser sent; a hidden field's value
   may come from the member, which no page holds (ADR-0002).
4. **What is stored.** Which fields store a value, in which row, and which files a file field takes.

## Options

1. **Where the step lives.**
   - Inside 6.1's endpoint: the posts without JavaScript (6.2) would need a second copy.
   - **A service of Core's entries (`SubmissionReader`),** which takes the post in one shape whatever the transport.
     Chosen.
2. **What runs first.**
   - The checks before the rules: every hidden field would be checked, and a rule's `require` could not count.
   - **Read, then the rules, then the checks:** each answer read by its type, the rules over what was read, then the
     shown fields checked. Chosen.
3. **Counting rows.**
   - Only the rows that hold a value: a visitor who sees three rows, one of them empty, would be told about two.
   - **Every posted row, an empty one included,** as the runtime shows them (ADR-0071 names this condition). Chosen.
     A group whose count fails is not read at all, so a post with a thousand rows costs no more than its count.
4. **A calculation.**
   - The posted value, checked against the server's: two results for one field, and a rounding difference fails a
     visitor who did nothing wrong.
   - **The server's result alone,** whatever was posted. Chosen.
5. **When a hidden field's value is resolved.**
   - Before the rules: its default could not show an answer's merge tag over the answers the rules leave shown.
   - **After the first evaluation,** over the shown answers, and then the rules once more. The rules the browser runs
     never look at a value only the server has (ADR-0035), so the second evaluation shows and hides exactly what the
     first did, and only the actions and the outcome can change. Chosen.
6. **A repeatable group's stored rows.**
   - The posted row numbers: an empty row in the middle would leave a gap that means nothing to a reader.
   - **Closed up:** the rows that hold no value are left out, the rest numbered from 0 in their order, as a ranking's
     positions are (ADR-0070). Chosen. An error keeps the row as posted, which is the row the visitor sees.

## Decision

- **The input** (`PostedSubmission`): the scope merge tags render in (its form is the version the visitor submits),
  the instant from the server's clock, the site's time zone (an unknown one is refused; the pipeline falls back to UTC,
  ADR-0043), the member's groups, each field's posted texts and parts by its key (`PostedAnswer`), each repeatable
  group's rows by its key, and the uploads the posted tokens stand for (6.1 finds them).
- **The output** (`SubmissionAnswers`): what the entry stores (`EntryFieldAnswer`, ADR-0070), the errors
  (`SubmissionError`: the field or grid row, the row of a child as posted, and the error kind, the index of the
  field's own check or the key of the form-level validation rule), the rules' `FormState`, and the stored answers as
  merge tags show them, with the fields the rules hid (ADR-0066). The submission is valid when it has no error.
- **`ReadAsync`, in this order:**
  1. Every field of the version whose type the site has, in the form's order; keys the version lacks, fields of types
     the site lacks and a child's key outside its rows are ignored. Every text is cleaned as rules clean it (ADR-0005)
     and empty texts and parts without a name are left out (ADR-0058); the answer is held to its type's limits and,
     within them, read by its type.
  2. A repeatable group's posted rows are counted and held to its limits (`FieldValues.CheckRows`); only within them
     are its rows read, each child row by row.
  3. A field whose type keeps its value on the server takes nothing from the post, except a hidden field prefilled
     from the query, whose value only the visitor's page has (ADR-0002).
  4. The rules run over what was read, with the query values the form allows, the member's sign-in and groups, the
     instant and the time zone; the server works out every calculation, its own rows' too.
  5. Each shown hidden field without a value renders its default where hidden values render, over the stored answers
     so far (so a later default can show an earlier one), cleaned, held to its type's limits and read. A value over
     the limits is left out with a warning that names the field's alias, never the value. When any value was
     resolved, the rules run once more.
  6. **Stored:** each shown field's values, a calculation's as the server worked it out; a group's children in its
     shown rows, closed up. A file field stores its values only when every token stands for an upload of that field,
     and then with those uploads.
  7. **Errors**, in the form's order, of the shown fields only: a group's row count, then its children row by row;
     for each field the kind its limits fail, `invalid` for a file token without an upload of the field, or else its
     type's checks and its own (`FieldValues.Check`, required as the rules decide, the day in the site's time zone, the
     files, and the other answers a `matchesField` check compares with: a child's own row); then the form-level
     validation rules whose message shows at the field or one of its grid rows.
- **What the rules hide is gone:** neither checked, nor stored, nor counted (a hidden group's rows), and merge tags
  see it as hidden.
- `AddFormBuilderEntries` registers the reader as a singleton.

## Consequences

- ADR-0070's writer stores the answers as they come out, with the calculations and hidden values filled in; its row
  numbers are the closed-up ones. ADR-0061's engine runs once or twice per submission.
- 6.1 turns the JSON post into a `PostedSubmission`, resolves the upload tokens, turns the errors into messages, and
  stores the answers when the submission is valid; 6.2 does the same from a form post. Until then nothing in the
  product calls the reader, which 5.5's sweep notes.
- 6.5 adds the member and context prefill as further server-side sources of step 5; the projection never holds a
  hidden field's default or source (ADR-0035), which 6.2's markup tests check.
- The browser counts rows the same way, so no check of rows joins `packages/logic`.
- **Revisit** if a hidden field's default ever needs the value of a hidden field after it, or if rows are ever
  counted otherwise.
