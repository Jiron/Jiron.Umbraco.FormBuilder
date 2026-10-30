# 0012. Anonymous endpoints, spam protection and challenges

- Status: accepted; rate limits (refines D14), member-bound challenges, the honeypot and `invalid_request`
  amended in 1.8; another origin's requests other than `GET`, the site's own pages behind a proxy that ends TLS and a
  body for an endpoint that takes none amended in 6.1a
  ([ADR-0084](0084-the-public-apis-frame-and-the-form-endpoint.md)); the body of a submission, its limits, the
  receipt and the replay before 7.1 extended in 6.1e
  ([ADR-0088](0088-the-submission-pipeline-and-the-entries-endpoint.md)); what a page's validation checks and the
  receipt before 7.1 extended in 6.1f ([ADR-0089](0089-the-validate-and-receipt-endpoints.md)); a page's places on
  options extended in 6.1g ([ADR-0090](0090-places-on-options.md)); a submission's `uniquePerForm`, which a page's
  validation leaves out, built in 6.1h ([ADR-0091](0091-unique-answers.md)); the honeypot's markup and name rendered
  from 6.3a by [ADR-0105](0105-the-theme-registry-and-the-base-rules.md), its scoring still 7.1's
- Date: 2026-10-23
- Roadmap step: 1.7b (decided); built in 6.1 (the public API and the pipeline), 6.6 (server keys), 7.1 (standard
  protection, content rules and the spam folder), 7.2 (proof of work and captchas), 8.x (mail limits) and 16.1 (the
  security review)

## Context

The public API (`/jfb/api/v1`, §4.6) and the no-JavaScript post (ADR-0004) take submissions from anyone. D14, as first
written, asked for spam protection without third parties by default: a honeypot, a minimum fill time, a signed
challenge, rate limits and content rules feeding a spam score, optional proof of work, and reCAPTCHA or hCaptcha only
when an editor chooses them. Suspicious entries go to **Spam**, where nothing is lost and no action runs, and bots
learn nothing (§4.4). §4.10 adds the rest: Data Protection challenges bound to the form and the time, rate limits per
IP, form and site with bounded partitions that fail closed, size and time limits, `no-store`, origin checks, no
antiforgery token on anonymous forms (so pages can be cached), member-bound challenges instead on members-only forms,
and mail limits for addresses that visitors type.

What is fixed already:

- **Cached pages hold nothing per visitor** (ADR-0004): the runtime fetches the challenge on the first interaction,
  and a no-JS post carries no time signal.
- **Uploads** send the challenge as the first part and are checked before the file's first byte (ADR-0006).
- **Ligata.Forms** (§3.1, §3.2) had the Data Protection challenge, the honeypot, the origin allowlist and `no-store`,
  but in-memory counters that stopped counting once full.
- **Tandem** (Tandem ADR-0016, 0020, 0021) proved, on this machine:
  - limiters of its own over .NET's `PartitionedRateLimiter`, with windows on `TimeProvider`, applied by its own
    middleware, so a site's own `UseRateLimiter()` and CORS policies are neither changed nor needed;
  - CORS, body and time limits as filters and middleware of its own;
  - client addresses from the connection only;
  - captchas that post only the secret and the token;
  - mail caps counted atomically in the queue.

## Options

**Rate limits:**

1. **ASP.NET Core's global `AddRateLimiter` with named policies.** Standard, but the policies live in the site's
   options, a site's own `UseRateLimiter()` counts our endpoints twice or fails on policy names it does not know, and
   the built-in windows cannot run on a test clock.
2. **Counters of our own.** The mistake Ligata.Forms made: unbounded, or bounded and failing open.
3. **.NET's `System.Threading.RateLimiting` limiters, partitioned and bounded, on `TimeProvider`, applied by Jiron
   Form Builder's own middleware** to its own endpoints, plus domain limits in the services where the form and the
   site are known.

**What a suspicious submission gets:**

1. **A refusal.** Honest, but it tells a bot which signal caught it, and a real visitor with an odd answer loses
   their entry.
2. **Silent acceptance into Spam** (D14). Nothing is lost, no action runs, and the bot sees success.

## Decision

Option 3 for rate limits, option 2 for suspicious submissions. A refusal is kept only for what a real visitor can fix
or retry: a missing or expired challenge, a failed captcha, a limit, an invalid request. Rate limits therefore refuse
rather than score, which refines D14 (it listed them among the score's signals): a flood must cost nothing to turn
away, and a real visitor behind a busy address keeps every answer and is told when to send again.

### The endpoints

- **Controllers** are internal MVC controllers under `{JironFormBuilder:PublicApi:BasePath}/v1` (default
  `/jfb/api/v1`), never under `/umbraco` (D11), anonymous, with `[MapToApi("jfb-public")]` and JSON options of their
  own: camelCase, string enums only, nulls left out. The OpenAPI document `jfb-public` is snapshotted (ADR-0011).
- **Before routing,** a middleware gives every request under the base path a form feature with no form content, so
  Umbraco's routing never reads a body (ADR-0006).
- **Every response is `no-store`,** except `GET forms/{keyOrAlias}`, which sends `Cache-Control: no-cache` with its
  `ETag` (ADR-0002).
- **Bodies:** JSON only, except uploads (`multipart/form-data`, ADR-0006). A declared length over the endpoint's limit
  is refused before reading, and a body without one is counted while it is read (`413 payload_too_large`,
  `415 unsupported_media_type`). JSON with a lone surrogate, or a body that is not the request's shape, is
  `400 invalid_request` (ADR-0005).
  **Amended in 6.1a ([ADR-0084](0084-the-public-apis-frame-and-the-form-endpoint.md)):** an endpoint that takes no
  body refuses any body, declared or not, before reading it; each endpoint declares its body limit, time limit and
  rate-limit policy, and a test fails for one that does not.
- **Time limits:** each endpoint has one, on the site's `TimeProvider`. Running out answers `504 timeout`; a visitor
  who left gets no answer.
- **Errors** are ProblemDetails with the stable codes of §4.6, plus `origin_denied`, `timeout`, `not_found`, and
  `server_error` for anything unexpected, logged without details. There is no "captcha unavailable" answer: an
  unavailable provider scores the entry into Spam (below). Refusals worded for the team reach visitors in neutral
  words.
- **Request models** seal a `ToString` that prints only their type, because MVC logs action arguments at Trace
  (§4.10).

### Origins

- Same-origin requests are always accepted; others only from `JironFormBuilder:PublicApi:AllowedOrigins`.
- **CORS is Jiron Form Builder's own middleware,** for the base path only: `Vary: Origin` on every answer; a preflight
  from an allowed origin gets `GET, POST`, `Content-Type`, ten minutes and no credentials; any other gets
  `403 origin_denied`. Actual requests from allowed origins get `Access-Control-Allow-Origin` and expose `ETag` and
  `Retry-After`, never `Access-Control-Allow-Credentials`; others get no CORS headers, so the browser hands them
  nothing.
  **Amended in 6.1a ([ADR-0084](0084-the-public-apis-frame-and-the-form-endpoint.md)):** that holds for `GET` and
  `HEAD`; another origin's other requests answer `403 origin_denied` before anything runs, since a browser sends some
  posts without a preflight. When a request came over `http`, an `https` origin on its host counts as the site's own
  (a proxy that ends TLS), never the other way round.
- A request without an `Origin` (a server, a no-JS post) passes the origin check and meets every other check.
- **The preview frame** has the opaque origin `null` (§4.10). The public API accepts `Origin: null` only with a preview
  ticket: a Data Protection token bound to the form's draft, valid for ten minutes, issued by the management API to a
  user who may design the form. Preview entries are test entries.

### Client addresses

Only `HttpContext.Connection.RemoteIpAddress`, never a header Jiron Form Builder parses itself. IPv4-mapped addresses
become IPv4; an IPv6 address counts as its /64 network for limits; no address is the key `unknown`. Sites behind
proxies configure ASP.NET Core's forwarded headers with their known proxies (documented), and tests show that
`X-Forwarded-For` changes nothing without that.

### Rate limits

**On each server, in memory**, applied by `FormBuilderRateLimitMiddleware` after routing, only to endpoints that
carry `[FormBuilderRateLimit(policy)]`. Windows are sliding windows of ten segments on the injected `TimeProvider`. A
refusal answers `429 rate_limited` with `Retry-After`. Jiron Form Builder's policies never enter the site's
`RateLimiterOptions`. On the surface action of a no-JS post, the middleware hands its refusal to the action, which
renders the page again with the answers, a notice saying when to send again, `429` and `Retry-After`. Only refusals of
the per-address and per-form partitions below ten times their limit are rendered; site-wide and overflow-partition
refusals, and an address further over its limit, get a bare `429`, so a flood still costs next to nothing.

| Policy | Partitions (host keys under `JironFormBuilder:Limits`, defaults) |
| --- | --- |
| `read` (definition, render, options) | per address 120 a minute; site-wide 200 a second |
| `challenge` | per address 30 a minute; per address and form 10 a minute; site-wide 100 a second |
| `submit` (entries, drafts, validate, receipt) | per address 20 a minute and 100 an hour; per address and form 5 a minute; per form 60 a minute; site-wide 50 a second |
| `upload` | per address 30 a minute; per address and form 20 a minute; site-wide 20 a second |
| `events` (the insights beacon) | per address 120 a minute; site-wide 500 a second |
| `verify` | per address 10 a minute |

- **Bounded and failing closed:** each server tracks at most `MaxTrackedClients` addresses (default 100,000). Past that,
  new addresses share one overflow partition with the per-address limit, so a flood of distinct addresses is limited,
  never let through, and memory stays bounded. Emptied windows are swept every minute. The site-wide partitions hold
  whatever the per-address ones do.
- **Server keys** (below) are limited per key instead of per address.
- **Per server:** on N servers a client can reach up to N times a per-address limit. That is accepted, because it keeps
  the limits free of shared state; the limits that must hold across the cluster are in the database (below).

**In the database, across the cluster** (domain limits, counted inside the submission's transaction with one
statement each, ADR-0003, ADR-0007):

- a form's entry limit and opening dates (`entry_limit_reached`, `form_closed`);
- one entry per member (`already_submitted`);
- places on options (§4.4);
- **mail to an address a visitor typed** (confirmations, verification, resume links, forwards to visitors): at most 3
  a day per recipient across all forms, and at most 200 an hour per site, both host-configurable. The count and the
  queue insert are one `INSERT … SELECT … WHERE` statement on the recipient key (case-folded, `+tag` removed), with a
  range lock on SQL Server, so parallel requests cannot pass them together. A mail over a limit is stored as
  `Limited` (ADR-0007), shown in the deliveries log, and never sent (§4.10).

### The challenge

`POST forms/{key}/challenge` returns a challenge token and what cached markup cannot carry:

```jsonc
{ "challenge": "<token>", "minFillSeconds": 3, "proofOfWork": { "nonce": "...", "bits": 18 } | null,
  "places": { "<fieldKey>": { "<value>": 3 } }, "member": { "signedIn": true, "groups": ["<key>"] } | null }
```

- **The token** is Data Protection protected (purpose `Jiron.FormBuilder.Challenge.v1`, ADR-0001) and holds the form
  key, a challenge id (128 random bits), the issue time, the expiry (24 hours), on a members-only form the signed-in
  member's key, and, for proof of work, the nonce and difficulty. It is bound to the form and the time, not to the
  address, because phones change networks while a form is open.
- **Renewal:** the runtime may send its previous token, even expired, when it asks for a new one. A valid signature
  carries the original issue time over, so a visitor who took a long time is not scored as too fast.
- **Use:** every JavaScript submission, page validation, draft and upload sends the challenge. A missing, forged or
  expired one is `401 challenge_required`, and the runtime fetches a new one and retries once; a submission that
  retries after a lost answer first asks `receipt` with its old challenge and fetches a new one only after a `404`
  (below).
- **Single use:** the challenge id is stored on the entry (`jfbEntry.ChallengeId`, unique per form), so a solved
  challenge cannot make a second entry. A used challenge is accepted only together with the `submissionId` of its
  entry, and then the same digest returns the original receipt and a different one `409 duplicate_submission` (§4.4,
  step 3); with another entry's `submissionId` it gets `409` with no receipt and no outcome, and with an unknown one
  `401 challenge_required`.
- **The member part** is filled only for a signed-in member, and only with what rules and prefill on this form need.

### Signals and the spam score

Each submission gets a score from 0 up and a list of reasons (`jfbEntry.SpamScore`, `SpamReasons`). A score of 100 or
more is **Spam**: accepted, stored, answered with the form's normal outcome, and no action runs (§4.4).

| Signal | Score | Reason |
| --- | --- | --- |
| The honeypot is filled | 100 | `honeypot` |
| Submitted sooner than `minFillSeconds` after the challenge was issued | 60 | `too_fast` |
| A blocked address range (§4.8) | 100 | `blocked_range` |
| A blocked email domain in an email field | 100 | `blocked_domain` |
| Each blocked word (case-folded as rules compare, ADR-0005) | 40 | `blocked_word` |
| More links in the text than the form's maximum | 40 | `too_many_links` |
| A no-JS post on a form whose protection needs JavaScript (proof of work, a captcha) | 100 | `no_script` |
| A captcha provider that cannot answer | 100 | `captcha_unavailable` |
| Each `IFormBuilderSpamCheck` | its own score | its own reason |

- **Strictness** (`protection.contentRules`): `off` skips the content rules, `normal` uses the weights above, and
  `strict` doubles the content-rule weights and counts any link in an answer where the form's maximum is 0.
- **The honeypot** is a text input whose name is derived from the form key and the public revision, so it is the same
  in every cached copy of one revision and different in the next. The name always contains a `-`, which no field alias
  can (ADR-0002), and never starts with `jfb-`, the prefix of Jiron Form Builder's own inputs, so it collides with
  nothing. It sits in a container that the functional base rules of every theme hide from view (ADR-0015), and that is
  hidden from assistive technology (`aria-hidden`, `tabindex="-1"`, `autocomplete="off"`), with a label that says to
  leave it empty, in case a screen reader reaches it anyway.
- **No-JS posts** carry no time signal: on anonymous forms they have no challenge, and on members-only forms the
  rendered challenge only binds the post to the member (below). They are scored on the honeypot, the blocked ranges
  and the content rules, and limited like every post. On a form whose protection needs JavaScript, they go to Spam
  with `no_script`, so a real visitor without JavaScript is never lost and never trusted.
- **"Not spam"** moves an entry to `Submitted` and runs its `submitted` actions then, or, on a form with email
  verification and no `VerifiedUtc` yet, to `Unverified` with the verification mail (§4.4). Spam keeps no places and
  sends no mail.

### Proof of work

Optional (`protection.level = proofOfWork`). The challenge carries a nonce and a difficulty; the runtime finds a
counter such that SHA-256 of the nonce and the counter starts with that many zero bits, in a Web Worker, with a
progress hint on slow devices. The server checks it once with the challenge. The default difficulty is set in 7.2 so
that the demo machine solves it in under 2 seconds.

### Third-party captchas

- **Providers** implement `IFormBuilderCaptchaProvider`: an alias, whether its keys are configured, the client
  script's settings, and `VerifyAsync`, which returns `Passed`, `Failed` or `Unavailable` and never throws for input or
  provider answers. reCAPTCHA v2, reCAPTCHA v3 and hCaptcha are built in; the last registration of an alias wins, so a
  site can replace one. A provider without keys is greyed out with the reason (§4.3.2).
- **Keys** live only in host configuration (`JironFormBuilder:Captcha`); the projection carries the site key, never the
  secret.
- **Verification** posts only the secret and the token, never the visitor's address, and checks success, the action,
  the host (from the site's configured domains and `AllowedOrigins`, never the request's Host header), the token's age
  (two minutes for reCAPTCHA v3, five for the others, with 30 s of clock skew) and, for v3, the score against the
  form's threshold. A timeout (8 s), an unreadable answer or a non-success status is `Unavailable`.
- **Outcomes:** `Failed` is `403 captcha_failed`, so the visitor can try again; `Unavailable` scores the entry into
  Spam with `captcha_unavailable`, so an outage loses no lead and lets no unchecked entry run actions, and the health
  check reports it.
- **Loading** follows §4.10: only on forms that use a provider, only after consent in the chosen mode (`explicit` by
  default), and reCAPTCHA through `recaptcha.net` when the site chooses it.

### Members-only forms

Their challenge holds the signed-in member's key (ADR-0004). The runtime fetches it from the same origin, and the no-JS
form carries one rendered into its page, which is per member and sent with `no-store`. Every post and API call for
them checks the signed-in member (`member_required` without one) and that the challenge names that member
(`challenge_required` otherwise, so the runtime fetches a fresh one). No cookie is set: the protection holds because no
other origin can read a member's challenge (no credentials across origins, above, and the browser's same-origin policy
for the page), and the page refuses to be framed by another origin (ADR-0004). Across origins and with a server key, a
members-only form always answers `member_required`; in the preview, a valid preview ticket stands in for a test
member, and the test entry has no `MemberKey`.

**Their no-JS posts** carry the rendered challenge:

- the post's `submissionId` is derived from the challenge id, so a repeated post (a double click, a reload) finds the
  entry and redirects to its outcome instead of making a second one;
- a missing, expired or foreign challenge renders the page again through `CurrentUmbracoPage()` with the answers, a
  fresh challenge and a notice to send again;
- the minimum fill time does not apply, as for every no-JS post (a re-render would restart it);
- **the origin comes first:** before any re-render (a bad challenge, `form_changed`, a `429`, validation errors), the
  surface action accepts only an `Origin` that is one of the site's own origins, and a missing or `null` `Origin` only
  with `Sec-Fetch-Site: same-origin`. Anything else gets a plain `403 origin_denied` page that shows none of the posted
  answers, so no other site can have a member's page filled in and handed a fresh challenge.

### Server keys

Headless servers may send `Authorization: Jfb-Key <key>`. Host configuration (`PublicApi:ServerKeys`) holds a name and
the SHA-256 of each key, never the key, compared in fixed time. A server-key submission skips the challenge, the
captcha and the honeypot, never validation, the content rules or the domain limits, and is limited per key. The entry
records which key's name submitted it in its context, under the reserved name `jfbServerKey`, which
`developer.contextValues` cannot list.

### The other tokens

| Token | Form | Lifetime and use |
| --- | --- | --- |
| Resume (save and resume) | 256 random bits, base64url; only its SHA-256 is stored (`ResumeTokenHash`) | in the URL fragment (`#jfb-resume=…`), so it never reaches server logs; valid until the draft is submitted or the link expires; compared in fixed time; an unknown or expired token answers like a missing one |
| Verification (double opt-in) | Data Protection (`Jiron.FormBuilder.Verify.v1`) with the entry key and expiry | `GET verify?t=` only shows a confirm button, because mail scanners follow links; `POST verify` confirms once, by the entry's state |
| Upload | Data Protection (`Jiron.FormBuilder.Upload.v1`) with the form, the challenge id and the file key | 24 hours; only the submission with the same challenge can claim the file (ADR-0006) |
| Preview ticket | Data Protection (`Jiron.FormBuilder.Preview.v1`) with the form and its draft version | ten minutes |
| The copy of the answers after a no-JS post | Data Protection (`Jiron.FormBuilder.Copy.v1`), single use | minutes (6.5) |

Servers in a cluster must share the Data Protection key ring and application name, as Umbraco itself needs; the
health check warns when a token from another server cannot be read.

### The receipt

The answer to a submission carries a receipt: the submission id, the entry's reference (or number) and the time. When
a response was lost, the runtime repeats the same `POST` with the same `submissionId` and body, which returns the
original receipt and outcome. If its challenge has expired by then, the post meets `401 challenge_required`, and the
runtime asks the receipt route instead, which checks the challenge's signature and accepts it for up to 24 hours past
its expiry; only a `404` there makes it post again with a fresh challenge. `POST forms/{key}/receipt` takes
`{ submissionId, challenge }` (or the server key that submitted the entry) and returns the receipt and the outcome
only when an entry with that submission id was made with that challenge or key; any miss is the same `404 not_found`,
so submission ids cannot be probed. The post with a fresh challenge keeps the same `submissionId`; past the grace it
meets `409 duplicate_submission`, which the runtime reports as already sent, without the outcome.

### What the validate endpoint does not check

`POST forms/{key}/validate` runs every check of one page except `uniquePerForm`, which runs only on submission, so the
endpoint cannot be used to ask whether an address is already registered.

## Consequences

- A site's own rate limiting and CORS neither help nor hinder the public API; operators tune Jiron Form Builder's in
  `JironFormBuilder:PublicApi` and `JironFormBuilder:Limits`.
- Every new public endpoint must declare its rate-limit policy, body limit and time limit; a test lists the endpoints
  from the OpenAPI document and fails when one is missing (6.1).
- Bots that fill the honeypot or post too fast learn nothing; the team finds their entries in Spam, which is purged
  after 30 days.
- A captcha outage or a visitor without JavaScript costs a check in the Spam folder, never a lost lead.
- A mailbox gets at most three mails a day from one site because of what strangers typed.
- §4.6 gains `origin_denied`, `timeout` and `not_found`, and the receipt route takes the challenge; §4.10 names the
  middleware, the overflow partition, the preview ticket and the spam-score table (WORKLOG).
- **Revisit** if a shared rate-limit store becomes available without extra infrastructure, if measured abuse needs
  per-address limits across servers, or if bots learn to wait out the minimum fill time at scale.
