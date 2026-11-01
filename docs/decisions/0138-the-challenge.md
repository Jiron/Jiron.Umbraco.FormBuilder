# 0138. The challenge

- Status: accepted; builds on [ADR-0012](0012-anonymous-endpoints-spam-protection-and-challenges.md),
  [ADR-0004](0004-rendering-and-submission-paths.md) and [ADR-0006](0006-file-storage-and-uploads.md)
- Date: 2026-11-01
- Roadmap step: 7.1b

## Context

ADR-0012 fixed the token, the endpoint, the single use, the receipt's grace and the member binding. Building it raised
what that ADR left open: where the check lives, what a request without JavaScript carries, how a race on one submission
answers, what the endpoint says about places, and what becomes of the requests that pipeline tests and a page's future
sender write.

## Decision

- **The check is the pipeline's gate, the token the transport's.** `FormSubmissions` asks an `IChallengeReader` the
  transport hands it, as it does for upload tokens, so the core knows no Data Protection. A transport with no reader has
  no challenge to check: that is the post without JavaScript, whose page may come from a cache (ADR-0004), and which
  posts no submission id, so it has no earlier entry to hear of. A request with a server key skips the challenge, and only
  that key hears of its entries (ADR-0126).
- **The order of section 4.4 holds:** the form, the gate (`401 challenge_required`), idempotency, then everything else.
  Only an entry's own challenge or key gets its receipt; any other post with that submission id gets `409` with neither
  receipt nor outcome.
- **A used challenge is found before the answers are read,** so a replay with another submission id costs one indexed
  read and learns nothing from the answers' checks. It is still held by the unique index on (form, challenge) when two
  requests race.
- **A race on one submission is a retry.** When a read of the id misses an entry another request is storing with the same
  challenge, the challenge's own lookup or the conflict on the id finds it, and the request gets the retry's answer rather
  than `401`. A conflict on the challenge alone means another submission id, so it is `401`.
- **A page check does not use a challenge up,** and a solved one still passes it: nothing is stored by a check, and a
  visitor may check a page again after a send that lost its answer.
- **An upload's token names its challenge,** so only the submission with the same one claims the file (ADR-0006). A
  post without JavaScript and a server key have none, and their tokens claim only with none.
- **A renewal gets a new id and expiry and keeps the first issue time.** An earlier challenge counts at any age for this,
  since the time it carries is signed. Uploads made under the earlier id are not carried over; their tokens expire within
  the same day anyway.
- **On a form for members** the challenge names the signed-in member, and the endpoint gives one only to a member the form
  admits, never to a server key. Entries, page checks and uploads refuse another member's, a visitor's or none. The form
  takes no entry until 14.3 admits its members, so the right challenge meets `member_required` there, and 14.3's checks
  cover both paths.
- **The endpoint's places are of every option known without a visitor's answers:** the form's own, and those of a list or
  a source with no parent, resolved in the site's default culture with no page, which only a source that changes its
  values by page or culture would notice. A source that fails tells none rather than failing the challenge. An option
  that hangs on a parent's answer is not told, since the endpoint does not have that answer; the server refuses a full
  one there as everywhere, on `validate` and on the entry, so the visitor still learns of it on the page.
- **What it tells a member is what the form's rules ask:** `member` is left out for a visitor and when no rule of a page,
  group or field looks at the member, and `groups` holds only the groups the rules name that the member is in.
- **The body may be left out,** so a page can ask with a bare `POST`. One given is read strictly, like every other body.
- **Tests:** a test of a body or a file signs with a test server key, since the challenge is beside the point there; the
  shared helper otherwise does what a page does, holding one challenge per form until it makes an entry.

## Consequences

- Every request but a server key's and a post without JavaScript's needs a challenge from now on. The website runtime does
  not send yet (ADR-0120), so nothing on a visitor's page changes until 7.1e.
- A headless site's browser code must ask for a challenge first; a headless server signs with its key.
- Fetching a challenge reads the entries' counts when the form has options with places, which the `challenge` policy
  limits.
- Resolving a source can make a challenge as slow as the source; the `challenge` policy limits how often, and the
  endpoint's 10 second time limit holds.
- **Revisit** if options a parent filters come to need their places ahead of the check.
