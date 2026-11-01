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
  on its way sends nothing. `Next` posts as before until 6.5 pages in the browser.
- The body is the page's post (`readPage`): each answer's texts by alias, each part as `alias.part`, a consent's
  wording among them, a group's rows as objects; the query values as the rules read them; the context values; the
  form's revision and culture; the page's content key; one submission id per page load.
- `200` puts the outcome in the form's place and focuses it; a redirect without a message goes at once; a message
  with a redirect counts down, with "Go now" and "Stay here". `422` shows the server's errors at their fields, kept
  while the field is unchanged, unless one is on another page. `429`, `5xx` and no answer in 40 s (past the endpoint's
  own 30) show the notice above the buttons, which takes the focus; every answer stays and the id is kept for the
  retry. Any other answer, and a form that posts files, goes the way without JavaScript, which the server answers in
  full.
- Still to build: the server's templates (the theme's notice for each code, its outcome with `data-jfb-reference`,
  `data-jfb-countdown` and "Stay here", the words "Sending your answers" and the page's thank-you), `data-jfb-content`
  on the form, the tests, axe, the look in a real browser and the design skills' pass.

## Consequences

- The runtime's steps wait; the server, the backoffice and every later phase go on.
- The owner decides one number. The plan's remaining runtime features, estimated from what 6.4a to 6.4i cost: sending
  1.5 KB (measured), `form_changed` 0.8, fitting the page and the developer surface 1.5, uploads 3, the signature pad
  3, the other field behaviours 3, paging in the browser 1.5, the challenge's use 1, proof of work and the captchas
  2.5: about 18 KB, so about 58 KB with everything. The core stays near 1 KB of its 16 KB, and a page loads only the
  features its form uses.
- **Revisit** when the owner answers, and at each phase end, where every blocked step is retried.
