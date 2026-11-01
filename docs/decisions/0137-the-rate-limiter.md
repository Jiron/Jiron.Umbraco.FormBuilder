# 0137. The rate limiter

- Status: accepted; builds on and amends [ADR-0012](0012-anonymous-endpoints-spam-protection-and-challenges.md)
  and [ADR-0126](0126-server-key-submissions.md); a window the sweep ends counts nothing more, found by 7.1c's gate
- Date: 2026-11-01
- Roadmap step: 7.1a

## Context

ADR-0012 fixed the policies, their partitions, the overflow partition and the bare `429` for a flood, and named .NET's
partitioned limiters on `TimeProvider` as the way to build them. Building it raised four questions that ADR left open.

## Options

**The partitions.**

1. **.NET's `PartitionedRateLimiter`,** with a window on `TimeProvider` inside each partition, as Tandem does. It has
   no bound of its own and tells nobody how many partitions it holds, so the bound would need a second dictionary kept
   in step through each limiter's disposal. Its idle sweep runs on a real timer once a second over every partition
   whatever the site's clock says, so 100,000 partitions cost 100,000 locks a second under the very flood they guard
   against, and a test on a stopped clock cannot see a sweep.
2. **The window ported from Tandem in a bounded dictionary of its own.** One dictionary, one counter, a sweep once a
   minute on the site's clock. Chosen. It gives what ADR-0012 asks of the limiter, sliding windows on `TimeProvider`
   and bounded partitions that fail closed, with less machinery, and a test can see all of it.

**What a post without JavaScript counts as.** Every post costs a page, but `submit`'s 5 a minute per address and form
would stop a real visitor who adds a few rows of a repeatable group or goes back and forth.

**Where such a post is counted.** The limiter's middleware runs after routing and knows no form, since the form's key
is in the post's body.

**How much a server key gets.** One server sends for all its visitors, so an address's limits would stop a busy
headless site.

## Decision

- **Option 2 for the partitions.** `FormBuilderRateLimiter` keeps at most `Limits:MaxTrackedClients` windows across
  every policy and partition, so that one number bounds its memory. Past it, a new client or form shares one overflow
  window per partition. A window that still counts is never dropped, so nobody gets a fresh limit by flooding. A form
  key is counted toward the bound too, because a route can name any key.
- **Partitions count cheapest first.** A request one refuses is not counted by those after it, and those before it give
  it back, so one flooding client uses up no one's site-wide limit.
- **A post counts as `submit` when it runs the pipeline or holds files:** Send, Next, or any post of a form that takes
  files. Back, an answer's "Change", and a row's or a ranking's button only render the page again, and count as `read`.
- **A post is counted where its form is known, before anything that reads a table.** `FormPostHandler` counts it after
  reading the form's key. `FormPostFilesMiddleware` counts a post that names its form in its address before reading a
  byte of it, and leaves the decision on the request, so it counts once. ADR-0012's "the middleware hands its refusal to
  the action" becomes this. The order of ADR-0012 holds: a bare `429` renders nothing, and a refusal that renders waits
  for the origin's check, so nothing is rendered for another site.
- **A refused post stores nothing.** Its page shows its answers and when to send again, in seconds, or in minutes from
  two minutes on. Files it held are not kept, and the notice says to choose them again.
- **A server key gets `Limits:ServerKeyFactor` times an address's limits,** 10 by default, in its own partitions. The
  limits of a form and of the site stay as they are.
- **Limits are host settings under `JironFormBuilder:Limits`:** `Read`, `Challenge`, `Submit`, `Upload`, `Events` and
  `Verify`, each with `PerAddressPerMinute`, `PerAddressPerHour`, `PerAddressAndFormPerMinute`, `PerFormPerMinute` and
  `SitePerSecond`, where 0 leaves a limit out, with `MaxTrackedClients` and `ServerKeyFactor`. A value out of range stops
  the site at start-up with a message that names the key (ADR-0042).
- **The test hosts turn the limits off** and each test of a limit names its own: their requests all come from one
  address. The browser suite's host keeps one limit per address and form, which the spec of a `429` uses up on a demo
  form no other spec posts to.

- **The sweep ends a window under its lock.** A request may hold a window the sweep is about to remove, and a count made on it would be lost, which lets a client send one request too many. So the sweep retires a window only while it is empty and unused for a whole length; a request that meets a retired window counts on a new one.

## Consequences

- A flood of distinct addresses is refused once the bound is reached, new visitors with it, until the flood's windows
  empty. That is ADR-0012's "limited, never let through", and the site-wide limit holds either way.
- §4.10's wording ".NET's partitioned limiters" now means this limiter (WORKLOG).
- A post of a form with files counts as `submit` even when it only adds a row, since its address is all that is known
  before it is read.
- Each limit counts on each server, as ADR-0012 accepted.
- **Revisit** if a real site's visitors meet `submit`'s limits on a long form without JavaScript.
