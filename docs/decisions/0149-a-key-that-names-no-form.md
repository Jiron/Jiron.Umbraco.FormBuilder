# 0149. A key that names no form

- Status: accepted; amends [ADR-0012](0012-anonymous-endpoints-spam-protection-and-challenges.md) (the `submit` policy's
  per-form default) and [ADR-0137](0137-the-rate-limiter.md) (what a request that names a form counts)
- Date: 2026-11-01
- Roadmap step: 7.4e

## Context

Phase 7's review found four ways to spend limits that belong to someone else, and two costs before any limit:

- **Made-up keys spent the site's limit** and each made a window per form until the bound on windows was reached, because the
  limiter counted before anything knew the form exists.
- **Twelve addresses could shut one form,** as each may send 5 a minute and the form took 60.
- **A form read by its alias counted no form,** because the middleware read only the route value `key`.
- **An invalid server key wrote a warning with a path the caller chose,** before any limit, so a flood chose what the log held.
- **A body's list of errors cost quadratic time,** each path compared with the ones before it, and nothing could cancel it.

## Options

**When to learn that a form exists:**

1. **Before any limit.** Each made-up key reads the database, so the flood reads as much as it likes.
2. **After every limit, as before.** The limits are spent before anything can tell.
3. **In between:** the address's own limits first, which need no form, then the form, then the form's limits and the site's.

**Where to read the form.** The first build read it in the rate-limit middleware, and the whole integration suite refused that: the
middleware runs before the check of the migrations, so a site being set up answered `500`, and before the request's time limit, so
a slow database held the request without one. The filter of the public API comes after both.

**Reading a form this server does not keep,** with no limit of the site's in front of it: unbounded across many addresses. A
site-wide window of its own, as large as the site's, bounds those reads and leaves the site's limit alone. A real form only
passes through it once per server, as the cache then keeps it.

## Decision

Option 3, with `FormRateLimits` over the limiter's three stages, the first in the middleware and the rest in the public API's filter:

- **`Start`** counts the address's windows, and a refusal ends there, touching no table.
- **A form this server keeps** goes on to `Finish`, reading nothing.
- **Any other** spends the `Lookup` window (bare `429` past it) and is read. A form that exists goes on to `Finish`. One that does
  not answers from its endpoint, with no form window and no count against the site.
- **A route value that can be no key or alias** spends only its address and reads nothing.
- **An alias counts as its form:** `keyOrAlias` resolves to the same key, so both spellings share one window.
- **The `submit` policy's per-form default** is 600 a minute: 120 addresses at their limit, not twelve. It still stops a flood from
  many addresses well before the site's 50 a second adds up.
- **An invalid server key's warning** has a fixed text, no path, and at most one per minute.
- **A body's list of errors** holds each path once and at most 100, which also makes building it linear.

## Consequences

- A made-up key costs at most two reads of the form's row, the limiter's and its endpoint's, as a key that names nothing is never
  cached (ADR-0084). Both are bounded by the address's limits and the `Lookup` window.
- A request refused for its form now meets the body's limit first (`413`), and a site being set up answers `503` first, as both come
  before the form's limits. A post without JavaScript takes all the stages at once, before its body or the form's own read.
- A flood of made-up keys can still refuse lookups of forms this server has not read yet, only until each has been read once.
- A flood with a real key still spends that form's limit and the site's. Nothing can tell it from visitors.
- A caller with more than 100 errors in a body sees the first 100, and the next request shows the rest.
- **Revisit** if the limiter comes to share its counts across servers.
