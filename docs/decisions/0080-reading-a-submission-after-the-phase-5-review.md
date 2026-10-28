# 0080. Reading a submission after the Phase 5 review

- Status: accepted; amends [ADR-0058](0058-field-answers-and-values.md) (a checkbox's longest answer, and part names
  held to a type's longest text), [ADR-0006](0006-file-storage-and-uploads.md) (one token stands for one value of a
  submission) and [ADR-0072](0072-a-submissions-answers.md) (a hidden field's query value that does not fit); the
  upload taken once by any token and one an entry holds refused added in 5.5i, after the second review, which also
  logs a query value that fails any of its type's limits as one its type cannot take (5.5f already counted it as
  none); a query value its type cannot read counting as none added in 5.5j, after the third
- Date: 2026-10-27
- Roadmap step: 5.5f (decided and built); 6.1 runs the reader in the submission pipeline

## Context

The Phase 5 review read the submission reader (ADR-0072) against posts that a client can send but the browser's own
runtime never does. It found four places where a post fared differently from what the ADRs say:

1. **A token named twice.** Two rows of a repeatable group whose file field posted the same upload token each found
   the token's upload of that field, so both rows kept it, and the entry's writer then refused the whole entry with an
   exception, because it never stores one upload twice. The visitor got a server error instead of an error at the field.
   One field's answer naming one upload twice would have done the same; the built-in file type already read the same
   token twice in one answer as unreadable, but two tokens of one upload (the website signs a new token each time it
   shows a kept file, ADR-0006) passed both checks until 5.5i.
2. **A ranking's part names.** A ranking posted without JavaScript names each option in a part and gives its position
   as the part's text (ADR-0069). The reader cleaned the texts but not the names, and the answer limits counted texts
   only, so a name of any length reached the stored values whenever the field's source could not give its options:
   ADR-0077 then takes the answers without holding them to the options.
3. **A query value that does not fit.** A hidden field prefilled from the query (ADR-0002) takes the query value as its
   answer. A value longer than the type takes failed the submission at a field the visitor can neither see nor change.
4. **A checkbox's `false`.** ADR-0058 reads anything but `true` as unticked, but gave the checkbox a longest answer of
   4 characters, so `false` was refused as too long before it was read.

It also found that a cascading choice's check compared each option's parent values with a list of the parent's
answers, which costs the number of options times the number of answers.

## Options

- **A token named twice:** let the writer refuse it, as before (a server error, and nothing tells the visitor what is
  wrong); keep the upload once at its first value and drop the later ones silently (a row's file disappears without a
  word); or refuse the later value at its field and row. The last is chosen: the visitor is told where.
- **Part names:** refuse every part a type does not know, in the reader (the reader does not know which parts a type
  reads: the types read them); have the ranking refuse long names itself (other types with parts would still take any
  name); or count part names among the texts the limits hold. The last is chosen: it bounds every type at once.
- **A query value that does not fit:** refuse the submission, as before (the visitor cannot fix it); cut the value to
  the limit (the entry would store a value nobody gave); or treat it as no value, so the default stands in. The last
  is chosen.

## Decision

- **One upload, one value.** In the form's order, a repeatable group's rows included, an upload is taken only by the
  first shown value whose token stands for it, whichever token that is. A later value whose token stands for the same
  upload, by the same token or another, makes its field's answer invalid, as a token of no upload does, and so does a
  token of an upload of another field or one an entry already holds; that field stores nothing. An answer takes its
  uploads only when every one of its tokens stands for an upload it may take, so a token that fails takes nothing
  from a later field, not even the uploads of the answer's other tokens. An answer whose tokens stand but whose type's
  check then fails (too many files, a type or size it refuses) has still taken them, so a later value naming one is
  `invalid` too; the submission fails either way. The stored answers and the errors walk the shown fields in the same
  order, so the same values fail in both.
- **Part names are cleaned and counted.** The reader cleans each part's name as it cleans texts (ADR-0005), leaves out a
  name that cleans to nothing, and keeps the texts of names that clean to one name under that name, in their order. A
  ranking that names an option twice that way gets two positions for it, which it reads as unreadable (ADR-0069).
  `FieldAnswers.LongestText` counts the names of the parts that have texts, so a ranking's name longer than an option
  value is refused before the type reads it, as any of its answers over the limits is (`invalid`, as a ranking has no
  message of its own for a length). The part names of a name, an address or an email are short words.
- **A query value that does not fit counts as none.** A field that keeps its value on the server and is prefilled
  from the query, whose value its type cannot take (longer than it takes, more texts or parts than it takes, or one it
  cannot read, which a site's type may refuse), reads as no value, so its default stands in, as it does when the query
  gives none: the visitor can neither see nor change the field, so no error could tell them anything. The server logs
  a warning that names the field, never the value, once for each value, so at most once for each row of a repeatable
  group.
- **A checkbox takes `false`.** Its longest answer is 5 characters, so `false` reads as unticked, as ADR-0058 says.
- **The parent filter** holds the parent's answers in a set.

## Consequences

- A client that posts one upload twice, by one token or by two, gets `invalid` at the later field or row, never a
  server error. The pipeline gives the reader only the form's uploads (6.1), which the reader cannot tell by itself.
- Every part's name is held to its type's longest text, for every type with parts.
- A link whose query value is too long for its hidden field still submits, with the field's default.
- The browser's runtime posts neither form, so nothing changes there; the shared vectors do not cover the reader.
