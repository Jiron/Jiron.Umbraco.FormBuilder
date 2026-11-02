# 0120. Sending waits for the runtime's budget

- Status: accepted; follows [ADR-0116](0116-the-runtimes-budget-counts-one-language.md) (which asked each later step
  of 6.4 to make room for what it adds) and [ADR-0119](0119-a-terser-pass-for-the-website-runtime.md); the owner's
  answer on section 4.12's budget, asked in `docs/OWNER-TODO.md`, came on 2026-11-01 and unblocks it
  ([ADR-0146](0146-the-runtimes-budget-grows-to-60-kb.md))
- Date: 2026-10-30
- Roadmap step: 6.4i (built on a branch, measured and blocked); 6.4j and 6.4l to 6.4p are blocked with it, and 6.4k
  is split off from 6.4l as the part that adds nothing to the runtime

## Context

Section 4.12 gives the website runtime 16 KB gzip for its core and 40 KB gzip with everything. After 6.4h the runtime
with every feature is 39.89 KB, with ADR-0116's measure (the largest language's words) and ADR-0119's terser pass, so
0.11 KB is left. The core, what every page with a form loads before its visitor touches it, is 1.09 KB.

6.4i builds sending on the page. Its first version is on the branch `wip/6.4i-sending` (7189d33): a submission id of
the browser's own random numbers, the entries endpoint's body from the page's post, the busy primary button, the
entry's outcome in the form's place with the redirect's countdown, the theme's notice above the buttons for a refused
or failed send, the send events, the server's `422` errors shown as the page's own, and the one audited helper that
parses the server's sanitised HTML (section 4.10). The words and the markup of the notices and the outcome are planned
as the server's render in the form's templates, so the runtime adds code only. Built, it takes the runtime with every
feature to **41.38 KB gzip** (1.49 KB more, about 4.3 KB of minified code), 1.38 KB past the budget.

## Options

1. **Make room by cutting code.** The form chunk is the logic both runtimes share (D10), held to Core by the vectors:
   the messages, the reading, the checks, the rules, the patterns, the dates and decimals. 1.49 KB gzip is about the
   size of a whole module such as the culture formats, and none of that size is spare. Terser's further options take
   less than 0.1 KB off (more passes, `pure_getters`, the unsafe transforms). It would also only move the wall: the
   steps after 6.4i add more runtime code (below).
2. **Raise the budget.** A quality budget of section 4.12 that only the owner may change: the roadmap never weakens a
   check (CLAUDE.md rule 9).
3. **Ship sending outside the measure,** as an add-on file or behind a build flag the size check does not build. The
   check would then measure less than a page loads, which ADR-0116 ruled out; this is option 2 in other words.
4. **Fold the feature chunks into the form chunk,** which saves each file's own compression: every simple form would
   then load the calculation language and the merge tags it never uses, about 9.5 KB more for a contact form, against
   section 4.12's features loaded on demand.
5. **Block 6.4i on the owner's decision and keep its work.** Chosen.

## Decision

- **6.4i is `[~]`** until the owner decides section 4.12's budget. Its work so far stays on `wip/6.4i-sending`, pushed;
  `main` stays within the budget, so every gate passes.
- **Blocked with it:** 6.4j (`form_changed` is the answer of a send), 6.4l (fitting the page and the developer
  surface: the ground check, step folding, the JS API with `submit()` and the events add code, and `submit()` sends),
  6.4m (uploads, sent with the entry), 6.4n (the signature pad), 6.4o (the other field behaviours, each sent and
  stored) and 6.4p (the review of the runtime's states that these add). Each also adds runtime code past the budget on
  its own.
- **Split off and open:** 6.4k, the CSP nonce, no cookies and the chunk names with a content hash, which check the
  runtime as it is and add nothing to it (WORKLOG 2026-10-30).
- **Later steps** whose runtime parts need room (6.5 moving through pages in the browser, 6.6 the element's sending,
  7.1 the challenge's use, 7.2 proof of work and the captchas) are split when they come up: their server parts go on,
  their runtime parts wait with the same reason, each split in WORKLOG and no check weakened.
- Without JavaScript every form works as before (ADR-0100): the posts, the server's errors, notices and outcomes,
  uploads, rows and pages. The runtime keeps what 6.4a to 6.4h built: the rules, the checks and their errors, the
  calculations, answer recall and the defaults.

### Sending as built so far, for when the step resumes

- The sender takes over the press that the page's checks pass (`errors.ts` gives it the press); a press while one is
  on its way sends nothing. `Next` posted as before until 6.5d paged in the browser (ADR-0155).
- The body is the page's post (`readPage`): each answer's texts by alias, each part as `alias.part`, a consent's
  wording among them, a group's rows as objects; the query values as the rules read them; the context values; the
  form's revision and culture; the page's content key; one submission id per page load.
- `200` puts the outcome in the form's place and focuses it; a redirect without a message goes at once; a message
  with a redirect counts down, with "Go now" and "Stay here". `422` shows the server's errors at their fields, kept
  while the field is unchanged, unless one is on another page. `429`, `5xx` and no answer in 40 s (past the endpoint's
  own 30) show the notice above the buttons, which takes the focus; every answer stays and the id is kept for the
  retry. Any other answer, and a form that posts files, goes the way without JavaScript, which the server answers in
  full.
- Still to build then: the server's templates, `data-jfb-content`, the tests, axe, the look in a real browser and the
  design skills' pass; built in 6.4i, below.

## Consequences

- The runtime's steps wait; the server, the backoffice and every later phase go on.
- The owner decides one number. The plan's remaining runtime features, estimated from what 6.4a to 6.4i cost: sending
  1.5 KB (measured), `form_changed` 0.8, fitting the page and the developer surface 1.5, uploads 3, the signature pad
  3, the other field behaviours 3, paging in the browser 1.5, the challenge's use 1, proof of work and the captchas
  2.5: about 18 KB, so about 58 KB with everything. The core stays near 1 KB of its 16 KB, and a page loads only the
  features its form uses.
- **Revisit** when the owner answers, and at each phase end, where every blocked step is retried.

## Sending as it resumed (2026-11-01, step 6.4i)

The owner raised the budget (ADR-0146), and the branch's work came back on `main`. What the steps
since then changed, and what the step finished:

- **The challenge came forward from 7.1e.** Since ADR-0138 every request but a server key's and a post without
  JavaScript's needs a challenge, so a send without one could never pass: the page asks for one as it wakes, on the
  visitor's first touch, so the form's fill time counts from there; it asks again at the send when that failed, and on
  a `401` it renews with the one it holds, which carries the first time over, and sends once more, with the same
  submission id. A second `401` goes the way without JavaScript. The honeypot's value goes as the body's `honeypot`,
  and a `429` says when to send again from its `Retry-After`. 7.1e keeps what stays its own: a challenge for each
  upload, the places and the member's values from the challenge's answer, the receipt for a send whose answer never
  came, and its checks.
- **The placement's page** (ADR-0129) reaches the entries endpoint in a token of its own, not in Umbraco's route token:
  `data-jfb-placement`, written only for a placed form that names a page, encrypted and signed through Data Protection
  (`PlacementTokens`, purpose `Jiron.FormBuilder.Placement.v1`) with the form's key, so a visitor can neither name another
  page nor bring one from another form's placement. The body's `placement` takes it. Anything else, an empty text among
  them, answers `400 invalid_request` naming `placement`, and the page's post then goes by the route token, which is
  intact. Umbraco's route token was the other way: it names no form, and the endpoint would have had to take a surface
  controller's token.
- **The server's part:** the form's templates hold what a send shows, in the theme's views and the page's language
  (`FormViewModel.Sending`): the notices of `rate_limited`, `server_error` and `timeout`, the outcome without its
  message, with `data-jfb-reference`, `data-jfb-countdown`, "Go now" and "Stay here", and the words as attributes of the
  template. `data-jfb-content` names the page. The headless element writes the same (ADR-0128).
- **A site's own views keep working.** A form view that writes none of this has no `data-jfb-sending` on its templates,
  and its form posts as without JavaScript; a theme's notice view that leaves a code out does the same for that send.
  So the markup contract stays at version 1.
- **An entry is never made twice.** A send whose answer was lost leaves an entry the page does not know of. Its retry with
  the same answers gets that entry's answer; with others, or past the challenge's life, it gets `409 duplicate_submission`,
  which must not go on to a post, as a post carries no id and would store a second entry. The page asks `receipt` with
  each challenge it sent with and shows that outcome, or its thank-you when none comes. A retry and a receipt carry the
  placement too, which no entry keeps. No other button leaves the page while a check or a send is on its way.
- **A form that stores files does not send through the page** until 6.4m, by its fields' kind as well as its encoding,
  since the element writes none and its files would be lost without a word.
- **A form with no address to post to** (the element, ADR-0128) has nothing to fall back on, so what a form with one posts
  shows the notice of a failed send.
- **A challenge made at the send counts as filled in no time** (ADR-0139). So when the first asking failed and this one
  succeeds, a form that can post goes that way, where no time counts; only the element sends with it.
- **The countdown** runs for at least ten seconds, whatever shorter delay the outcome names: a shorter one leaves a visitor
  on a keyboard or a screen reader no time to reach "Stay here" (WCAG 2.2.1). Focus on the outcome reads its line once, and
  the line changes without being said again. A message with a redirect and no delay waits at "Go now", as after a post.
- **The ring** turns in both themes that draw a button; the two steps it turns by moved to the base rules, and an
  unstyled form says "busy" by its cursor and the live region.

## `form_changed` on the page (2026-11-01, step 6.4j)

ADR-0004 says what happens; three things it left open:

- **What it carries.** Answers go by each control's name, which holds for every control a page has now but for rows and
  a ranking's order, which a form's first render does not hold. So a form with those, and a form the server pages, whose
  visitor would land on its first page, goes by post, whose answer keeps all of it (ADR-0100) until 6.5d and 6.4o bring
  them to the page; so does a form that takes no entry now, or a render that fails. The honeypot starts over, and the
  context and query values the page gave its form are carried, as a render from elsewhere lacks them.
- **The same submission, on.** The new form keeps the submission id and the challenge: no entry used either, and a new
  challenge would count as filled in no time (ADR-0139). The frame leaves the new form alone (`data-jfb-woken`).
- **The placement** reaches `render` as its `placement` query value, the same token, so the new form's route token and its
  own `data-jfb-placement` hold the page too (ADR-0129). A token this site did not write for the form is `400`.

The notice's words are in the form's templates like the other send notices, with both sentences about consents on its
line (`data-jfb-one`, `data-jfb-other`). It is no `jfb-notice-error`, as nothing went wrong. The element has no address
to post to and renders otherwise (ADR-0128), so its turn comes with 6.6g.
