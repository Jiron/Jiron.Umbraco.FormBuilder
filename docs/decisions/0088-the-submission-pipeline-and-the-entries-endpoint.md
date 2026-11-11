# 0088. The submission pipeline and the entries endpoint

- Status: accepted; extends [ADR-0012](0012-anonymous-endpoints-spam-protection-and-challenges.md) (the body of a
  submission, its limits, the receipt and the replay before 7.1),
  [ADR-0002](0002-form-definition-model-versions-and-public-projection.md) (how an older revision is mapped),
  [ADR-0084](0084-the-public-apis-frame-and-the-form-endpoint.md) (the form endpoint's `open` at the entry limit) and
  [ADR-0087](0087-the-upload-endpoint.md) (an upload checks the form's availability); amends
  [ADR-0061](0061-the-form-rules-engine.md) (which fields can be required); a UUID with a lone surrogate amended,
  and the pipeline's steps for a page and a receipt extended, in 6.1f
  ([ADR-0089](0089-the-validate-and-receipt-endpoints.md)); the transaction's last check amended in 6.1g
  ([ADR-0090](0090-places-on-options.md)) and extended in 6.1h ([ADR-0091](0091-unique-answers.md))
- Completed by: [ADR-0231](0231-members-entries.md), which builds what this left to step 14.3 (members)
- Date: 2026-10-28
- Roadmap step: 6.1e (decided and built); 6.1f adds `validate` and `receipt` over the same pipeline, 6.1g the places
  on options, 6.1h `uniquePerForm`, 6.2 the post without JavaScript, 7.1 the gate and the spam score, 8.1 the processor
  that runs the queued actions, 8.5 email verification and approval, 11.5 the insights metrics beyond `submissions`,
  13.1 the purge times, 13.3 the IP address, 14.3 members-only forms

## Context

Section 4.4 lists the steps of a submission: the form and its version, the gate, idempotency, compatibility,
availability, protection, reading and checking the answers, one transaction, and waking the action processor. The
pieces exist: the published form cache with the designer's locks (ADR-0044, ADR-0060), the reader of a submission's
answers (ADR-0072, ADR-0080), the consent wordings and their hashes (ADR-0085), the store of an entry's answers
(ADR-0070), the entry, event, action-run and daily-stat repositories (ADR-0038), and the upload tokens (ADR-0087).

Open were: where the pipeline lives, so 6.2's post without JavaScript runs the same one; the JSON body and how it names
fields, parts, rows, consents and uploads; the body limit; what the digest of a submission covers; what a replay
answers before 7.1 binds it to its challenge; how an older revision is mapped; what counts toward the entry limit and
how two submissions at once cannot both take the last place; what the transaction holds before 8.1 and 8.5 exist;
which status each refusal answers with; what a validation error says; and how an outcome is rendered, for a new entry
and for a replay.

## Options

1. **Where the pipeline lives.**
   - In the controller: 6.2 would run another copy.
   - **A Core service over a request that names fields by alias,** which the JSON endpoint and 6.2's form post both
     build; the transports read their own bodies and turn the result into their own answers. Chosen.
2. **Upload tokens in the body.** Section 4.6 lists an `uploads` member beside `values`.
   - A separate member: two places name one field's answer, and a field could hold a token in one and text in the
     other.
   - **A file field's values are its tokens,** as the reader already takes them (ADR-0080); there is no `uploads`
     member. Chosen; section 4.6 is corrected.
3. **Query values.** Section 4.6 lists none, yet rules read `query.<name>` and the page's address is the browser's,
   not the API request's.
   - Read them from the API request's own query: it is not the page's.
   - **A `query` member,** held to the names the form lists (`developer.queryValues`). Chosen; section 4.6 is
     corrected.
4. **The digest.**
   - The body's bytes: a client that orders its members otherwise gets `409` for the same answers.
   - **The request as the pipeline reads it, written canonically:** members sorted, the challenge, the captcha token
     and the submission id left out, so the same answers give the same digest whatever the transport. Chosen.
5. **The entry limit under load.**
   - Count before the transaction only: two submissions at once can both take the last place.
   - **Count once early, so a full form costs nothing, and again inside the transaction, right after the form's
     counter,** which every submission to the form updates first, so they take turns there. Chosen.
6. **What a validation error says.**
   - The visitor's message in the culture: the defaults are the runtime's (6.4) and the page's (6.2), and the form's
     own messages are already in the visitor's form.
   - **A stable code, where it is and a neutral line for developers,** with the check's index or the rule's key, so a
     client shows the form's own message from the visitor's form or its default for the code. Chosen.

## Decision

### The service

`FormSubmissions.SubmitAsync(SubmissionRequest)` runs the pipeline and answers a `SubmissionResult`: accepted (a new
entry, or the one a replay names) with its receipt and outcome, or refused with one reason, the paths a request could
not be read at, or the validation errors. The request names the form by its key and every answer by its field's alias:
each field's texts and parts, each repeatable group's rows of its children's answers, the consents' hashes as the
`wording` part (ADR-0058), the query and context values, and the uploads the posted tokens stand for (each token
with the key of the file it names, as the transport read it). It is registered in the core composer, as it uses the
forms, the entries and the files.

### The steps, in order

1. **The form:** the cached published version with the designer's locks (ADR-0044, ADR-0060); one that is missing,
   unpublished, in the trash or unreadable answers `404 form_not_found`, and the form's row is read again, so a form
   trashed a moment ago takes nothing even before the cache hears of it (ADR-0047).
2. **The gate:** a members-only form answers `403 member_required` to every public submission until 14.3 binds it to
   the signed-in member (ADR-0012); the origin is the frame's (ADR-0084); rate limits and the challenge come in 7.1.
3. **Idempotency:** without a `submissionId` the server makes one. When the form has an entry with the posted one,
   the same digest answers that entry's receipt and outcome, a different one `409 duplicate_submission`, and nothing
   below runs, so a retry after a lost answer meets neither a closed form nor a full one (section 4.4). Until 7.1 binds
   a replay to its challenge or server key, anyone who sends the same answers with the same id gets the receipt back;
   the id is the visitor's random UUID and the answers are their own, so this tells them nothing new. Two submissions
   with one id at once: the unique index refuses the second, which is then answered as a replay or a `409` in the same
   way.
4. **Compatibility** (ADR-0002): a revision newer than the form's or that no published version had answers
   `409 form_changed`. An older one is read through the newest published version that had it: its aliases name the
   fields, so a field renamed since keeps its answer, and a posted alias that version lacks, or a field it had and the
   live version does not, answers `form_changed`. For the current revision an alias that names no field answers
   `400 invalid_request` at its path. The hash posted for every ticked consent must be the one its wording has now,
   in the request's culture ([ADR-0085](0085-visitor-texts-and-consent-wordings-on-the-form-endpoint.md)); any other
   answers `form_changed` and stores nothing.
5. **Availability:** before the opening time or after the closing time by the site's clock answers
   `403 form_closed`, and a form whose entries reached `availability.maxEntries` `403 entry_limit_reached`. Entries
   count toward the limit unless they are test entries, drafts, spam or rejected, so a flood of spam cannot close a
   form. The count runs once here and again inside the transaction, right after the form's counter (option 5), as a
   locking read (ADR-0038), so on SQL Server no entry moves into a state that counts until the unit ends; there the
   refusal is `FormBuilderConflict.EntryLimitReached` and rolls everything back.
6. **Protection:** none until 7.1; every entry scores 0.
7. **Reading** (ADR-0072, ADR-0080): the request's culture among the site's languages, the site of the content page
   and its time zone (ADR-0043), the scope the form's merge tags render in, then the reader. A submission without a
   single answer fails with the form-level error `empty`; any other error answers `422 validation_failed`. For an
   older revision an error at a field that revision did not have answers `form_changed` instead (ADR-0002).
8. **One transaction** (`EntryAnswerStore`, whose unit the others join after the form's counter):
   - the entry with its values, uploads and consent wordings; its state is `unverified` when the form verifies an
     email address (8.5 sends the mail) and `submitted` otherwise (8.5 adds automatic approval);
   - a value kept only until the actions have run is stored only when a run applies (section 4.4): a run was queued,
     or the form has an enabled action whose trigger can still fire for the entry (any while it is unverified,
     `approved` or `rejected` while it waits for approval, `approved` when it is approved at once, which 8.5 queues);
   - the `submitted` event, whose details name only the outcome the rules chose (`{"outcome":"<key>"}`), so a replay
     and 6.1f's receipt render the same one;
   - one action run for each enabled `submitted` action whose rule matches, in the form's order, in one batch, none
     for an unverified entry;
   - the daily `submissions` count; 11.5 adds the page, the hour and the time it took, with the opt-out;
   - the search text of the answers that are not sensitive, the page's path from the content's address in the
     culture (never the visitor's), the context values the form lists, and no IP address (13.3 adds the setting); the
     purge time is 13.1's.
9. **After the commit:** 8.1's processor will be woken here; until then the runs wait.

### The outcome

The outcome the rules chose, or the first one without a rule; none is `{kind: "message"}` without `html`, which the
runtime shows as its own thank-you.

- A message renders as the consent wordings do: the text in the culture, a dictionary item through the dictionary,
  the merge tags in the HTML context where outcomes render them, then the one sanitiser.
- A redirect renders its URL in the URL context and checks the result again with the publish rule (ADR-0035); a
  content redirect takes the content's address in the culture. A redirect that fails the rule, or content without an
  address, is not followed: a message outcome keeps its message without it, a redirect outcome answers the default
  message, and the site logs a warning that names the form and the outcome, never an answer.
- `showAnswers` is answered only when true; 6.5 shows the copy.
- The scope holds the entry (its key, number, state and time) and the stored answers. A replay renders with the
  version the entry was submitted with, its stored answers and the outcome its event names.

### The endpoint

`POST forms/{key}/entries`, the operation `FormEntries`, policy `submit`, body limit 1 MiB (counted while it is read
when no length is declared, ADR-0084), time limit 30 seconds. The body:

```json
{
  "submissionId": "<uuid>",
  "revision": 3,
  "culture": "de-CH",
  "contentKey": "<uuid>",
  "values": {
    "email": "ada@example.com",
    "age": 42,
    "name.first": "Ada",
    "topics": ["a", "b"],
    "files": ["<upload token>"],
    "items": [{ "title": "One" }, {}]
  },
  "consents": { "terms": "<hash>" },
  "query": { "utm": "spring" },
  "context": { "campaign": "2026" },
  "challenge": "<token>",
  "captchaToken": "<token>"
}
```

- `revision` is required, every other member optional. A value is a string, a number (kept as written, so `1.5e3`
  reads as 1500, ADR-0058), `true` or `false` (`"true"`, `"false"`), `null` (no answer) or an array of strings. A part
  is `<alias>.<part>`; a grid's row is its part named by the row's key. A repeatable group's alias holds an array of
  rows, each an object of its children's values.
- Query values hold a string or an array of at most 20 strings, context values a string, each of at most 2,000
  characters; names the form does not list are left out, never stored or rendered, since every value of a public post
  is the visitor's to set.
- `challenge` and `captchaToken` are strings, read and not verified until 7.1.
- `placement` (added in 6.4i, ADR-0120) is the token a placed form's markup carries in `data-jfb-placement`; an entry
  whose token names a page goes there. Anything this site did not write for this form answers `400 invalid_request`.
- An unknown member, a value of another shape, a body that is not JSON, an escaped lone surrogate, more than 64
  levels, or a `submissionId` or `contentKey` that is not a UUID answers `400 invalid_request` with the paths.
- The answer is `200 {receipt: {submissionId, reference, submittedUtc}, outcome: {kind, html?, url?, delaySeconds?,
  showAnswers?}}` for a new entry and a replay alike; the reference is the form's format of the entry's number.

### The refusals

| Code | Status |
| --- | --- |
| `form_not_found` | 404 |
| `member_required` | 403 |
| `duplicate_submission` | 409 |
| `form_changed` | 409 |
| `form_closed` | 403 |
| `entry_limit_reached` | 403 |
| `validation_failed` | 422, `errors: [{code, path, message, check?, rule?}]` |
| `invalid_request` | 400 |

A validation error's `code` is the field's kind of error (`required`, `maxLength`, … as `messages` names them), `check`
for one of the field's own checks (with its index), `rule` for a form-level validation rule's message (with its key)
and `empty` for a submission without an answer. Its `path` is the field's alias, `<grid>.<rowKey>` for a grid row,
`<group>[<row>].<child>` for a repeatable group's child, or `""` for the form.

### Uploads and the form endpoint

An upload checks the opening and closing times and the entry limit as step 5 does, after the field and before a byte
of the file is read, with the same codes; a members-only form takes uploads until 7.1, as ADR-0087 says. The form
endpoint's `open` is false once the entry limit is reached too.

### Found while building it

- **Fields that can be required.** The rules engine took a field without a rule kind for one that holds no value, so
  it never made a grid, a name or an address required and refused a `require` rule on one with `invalid_rule`, which
  a published form could hold, so every submission to it failed. The logic model now says of each field whether it
  can be required (`CanBeRequired`, from its type's `Required` feature; one with a rule kind always can), the engine
  in both runtimes holds both its own requirement and `require` rules to that, and the shared vectors and the mutation
  check carry it. A grid that is required reports `required` at the grid, as its type checks it; a rule's message
  shows at a grid row's path.
- **Optional members as `null`.** The document marks `values`, `consents`, `query` and `context` as nullable, so the
  endpoint takes `null` for each as none, as it does for the other optional members.
- **An outcome's parts.** A redirect at once carries no `delaySeconds`; only a message with a URL does.
- **The frame's refusals in the document.** An endpoint that declares `403` or `413` for its own refusals keeps the
  frame's reason in that status's description (`origin_denied`, `payload_too_large`), which the document dropped
  before.
- **The count inside the unit is a guard.** The source scan of ADR-0038 found it as a plain read; it now carries
  `SqlNames.LockingRead`, so a review that moves an entry into a state that counts waits for the submission's unit,
  or the submission for the review's, instead of reading the row's last committed version.

## Consequences

- One pipeline serves the JSON endpoint now and 6.2's post later; 6.1f's `validate` and `receipt` reuse its steps.
- Members-only forms take no public submission until 14.3, which is safer than taking one from anyone.
- Entries of a form with email verification wait as unverified until 8.5 sends the mail; none runs an action before.
- A body above 1 MiB is refused; a form whose answers could be larger (many long texts in many rows) needs a larger
  limit, which a host setting would give. **Revisit** when a form meets it.
