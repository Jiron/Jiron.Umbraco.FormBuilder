# 0089. The validate and receipt endpoints

- Status: accepted; extends [ADR-0012](0012-anonymous-endpoints-spam-protection-and-challenges.md) (what a page's
  validation checks, the receipt before 7.1) and
  [ADR-0088](0088-the-submission-pipeline-and-the-entries-endpoint.md) (the pipeline's steps for a page and for a
  receipt); amends ADR-0088 (a UUID with a lone surrogate)
- Date: 2026-10-28
- Roadmap step: 6.1f (decided and built); 6.1g checks the places on options for a page too (ADR-0090), 6.1h leaves
  `uniquePerForm` out of a page's validation, 6.5 calls `validate` between pages, 7.1 binds the receipt to the entry's
  challenge or server key

## Context

Section 4.6 lists two endpoints beside `entries`: `validate`, which checks one page's answers on the server before the
visitor goes on, and `receipt`, which a runtime asks when the answer to a submission was lost and its challenge has
expired since. ADR-0012 fixed that a page's validation runs every check of the page but `uniquePerForm`, so the
endpoint cannot tell whether an answer is already taken, and that every miss of a receipt is the same `404 not_found`,
so submission ids cannot be probed. ADR-0088 built the pipeline both reuse.

Open were: what the validate body holds besides section 4.6's list; which answers the page's rules and checks see;
which of the pipeline's steps a page meets; what a page that passes answers; how a page key of an older revision is
read; which entries a receipt answers for; what it answers for a form that is gone or for members; and both bodies'
limits.

## Options

1. **The answers a page is checked with.**
   - The page's and the earlier pages' only, later ones dropped: a visitor who went back has answers on later pages,
     and a rule of this page that reads one decides otherwise than the submission will.
   - **Every answer posted, read and ruled as the submission reads and rules it, and only the errors of the page's
     fields answered.** The page's verdict is then the submission's for those fields with the same answers. Chosen.
2. **What a page that passes answers.**
   - `200` with a body that says so: a second way to say what the status says.
   - **`204 No Content`.** Chosen.
3. **The steps a page meets.**
   - Reading and checking only: a visitor fills in every page of a form that closed or filled up meanwhile and learns
     it at the end.
   - **Every step a submission meets before its transaction, but idempotency and the empty submission:** the form, the
     gate, the revision and the consent wordings, the availability and the reading. A page of optional fields left
     blank is no error. Chosen.
4. **A receipt for a form that is gone or for members.**
   - `404 not_found` for every refusal.
   - **The form's own codes, as every endpoint of a form answers them (`form_not_found`, `member_required`), and
     `not_found` for every miss of an entry.** Neither form code depends on the submission id, so neither tells a
     prober anything about one. Chosen.

## Decision

### Validate

`POST forms/{key}/validate`, the operation `FormValidate`, policy `submit`, body limit 1 MiB (counted while it is read
when no length is declared, ADR-0084), time limit 30 seconds. The body is a submission's (ADR-0088) with the page's key
and without `submissionId` and `captchaToken`; `revision` and `pageKey` are required:

```json
{
  "revision": 3,
  "pageKey": "<uuid>",
  "culture": "de-CH",
  "contentKey": "<uuid>",
  "values": { "email": "ada@example.com", "files": ["<upload token>"] },
  "consents": { "terms": "<hash>" },
  "query": { "utm": "spring" },
  "context": { "campaign": "2026" },
  "challenge": "<token>"
}
```

`consents` joins section 4.6's list, since a ticked consent on the page is checked with the wording it was shown. The
steps, numbered as in ADR-0088:

1. **The form:** the published form as the site serves it, its row read again; a missing, unpublished or trashed form
   answers `404 form_not_found`.
2. **The gate:** a members-only form answers `403 member_required` until 14.3.
3. **Compatibility:** the revision and its aliases as for a submission (`409 form_changed`), and the hash of every
   ticked consent against its wording now. The page key names a page of the revision the visitor's page showed or of
   the live version; for the current revision one that names neither answers `400 invalid_request` at `pageKey`, for
   an older revision `form_changed`.
4. **Availability:** `403 form_closed` and `403 entry_limit_reached` as for a submission, so a visitor learns it on the
   page they are on.
5. **Reading:** every answer posted, the earlier pages' and any later page's a visitor came back from among them, read
   by its type, ruled and checked as the submission's are, a file field's tokens by the uploads they stand for, every
   check but `uniquePerForm`, which only the submission's transaction runs (6.1h).
6. **The page's errors:** those at the fields of the page with that key, in the revision the visitor's page showed and
   in the live version, their repeatable groups' children and grid rows among them. Errors at other fields are not
   answered: the earlier pages were checked when the visitor left them, and the submission checks every page again.
   They answer `422 validation_failed` as the entries endpoint lists them; for an older revision, an error at a field
   that revision did not have answers `form_changed`.
7. **Otherwise `204`.** There is no `empty` error: a page may be all optional.

Nothing is stored, counted or claimed: no entry, event, count or action run, and an upload stays its field's until a
submission claims it.

### Receipt

`POST forms/{key}/receipt`, the operation `FormReceipt`, policy `submit`, body limit 16 KiB (a submission id and the
longest challenge, 4,096 characters, with room for escapes), time limit 30 seconds, the entries endpoint's, as it
renders the same outcome. The body is `{"submissionId": "<uuid>", "challenge": "<token>"}`; `submissionId` is required,
and the challenge is read and not checked until 7.1.

1. **The form** as for a submission (`404 form_not_found`), and members-only forms `403 member_required` until 14.3.
2. **The entry:** the form's entry with that submission id, unless it is a draft (8.6), answers `200` with its receipt
   and outcome as a retry of the same answers gets them (ADR-0088): rendered with the version it was submitted with,
   its stored answers and context values, and the outcome its `submitted` event names. A query value, which no entry
   keeps, renders empty in a receipt as in a retry. An entry in Spam answers as any other, so a bot learns nothing
   (ADR-0012).
3. **Every miss** answers the same `404 not_found`: no entry with that id, an entry of another form, a draft.

Until 7.1, the submission id alone names the entry. It is the visitor's random UUID, or the one the server made and
answered only to them, and the answer holds what that submission was answered with; 7.1 adds the challenge or server
key that made the entry, as ADR-0012 says.

### In the pipeline

`FormSubmissions.ValidateAsync` and `ReceiptAsync` share the steps of `SubmitAsync`: the form, the gate, and for a page
the reading. A page that passes is `SubmissionResult.Valid()`, neither an entry nor a refusal; a receipt's miss is the
refusal `EntryNotFound`. The bodies are read as strictly as a submission's (`EntryBodies`): an unknown member, a value
of another shape, a lone surrogate, bytes that are not UTF-8 or a body that is not a JSON object answer
`400 invalid_request` with the paths.

### Found while building it

- **A UUID with a lone surrogate.** The body reader read a UUID member (`submissionId`, `contentKey`, and now
  `pageKey`) with the plain string reader, which throws on an escaped lone surrogate, so the entries endpoint answered
  such a body with `500` instead of `400 invalid_request` (ADR-0005). It reads them as it reads every other string
  now, and the member is named; the unit and integration tests of the entries body cover it.

## Consequences

- 6.5's runtime checks a page on the server before it moves on, with the codes and paths of a submission, and shows a
  page's `422` at that page's fields only.
- A page's check costs a submission's reading without its transaction; it is limited as a submission is (`submit`).
- A receipt answers what a retry would, so a runtime that lost an answer shows the visitor the outcome they would have
  seen.
- Section 4.6's validate row gains `consents` and the `204`; the receipt row names the form's codes.
