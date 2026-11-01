# 0139. The signals and the score

- Status: accepted; builds on [ADR-0012](0012-anonymous-endpoints-spam-protection-and-challenges.md),
  [ADR-0090](0090-places-on-options.md), [ADR-0091](0091-unique-answers.md) and
  [ADR-0138](0138-the-challenge.md)
- Date: 2026-11-01
- Roadmap step: 7.1c

## Context

ADR-0012 set the signals, their weights and silent acceptance into Spam. Building them raised what it left open: how a
JSON body carries the honeypot, whether a spam submission is still validated, what happens to a value kept only until the
actions have run, how the day's counts treat Spam, what a merge tag says of such an entry, and where the blocked ranges
live until the settings screens come.

## Decision

- **The honeypot is a body member of its own,** `honeypot`, on the entries and validate bodies, not a member of `values`.
  An unknown alias in `values` means the form changed (ADR-0088), and a honeypot is no answer. A post without JavaScript
  sends it under the name the page rendered, derived from the revision it posted, and any non-blank value counts,
  whichever way it is posted. A value of the wrong JSON type is `invalid_request`, as with every member.
- **A page's check is not scored.** It stores nothing and already tells places openly (ADR-0090). Only a submission reads
  the signals.
- **A spam submission is still read and validated.** Answers that fail get `422`, as for anyone, which tells nothing of the
  protection, and whatever reaches Spam is readable when someone marks it as no spam. Only the checks of places and unique
  answers are skipped, before the transaction and in it, since Spam neither takes nor holds them. Closed forms and full
  forms still refuse: availability is the same for everyone.
- **The score is stored whatever it comes to.** `too_fast` alone scores 60, so the entry stays where it was going, with its
  score and reason. Reasons are stored comma-separated in the order they were read.
- **What a server key skips:** the honeypot and the time, as a key's request has no page behind it. The blocked ranges
  hold for every request.
- **In Spam, an entry keeps every value, even those kept only until the actions have run,** because marking it as no spam
  runs them then. No action run is queued, and the day counts it under `spam`, not `submissions` (section 4.5).
- **A visitor's texts never tell.** The merge tag `entry.state` reads `submitted` for an entry in Spam, in the outcome and
  in a retry's outcome, or a bot could read its verdict from a form's thank-you.
- **The blocked ranges are a `protection` settings section** (per site, with the global values as its default), read with
  the site of the page. A range is CIDR; it is saved as the host writes it, once, and at most 1,000 per site. An
  IPv4-mapped range is refused, since a mapped address is compared as the IPv4 address it is, so such a range could never
  match. The address is the host's `RemoteIpAddress` (a site behind a proxy configures
  forwarded headers, as for the rate limits, ADR-0137) and is not stored from here. The screen comes with the other
  settings screens.

## Consequences

- The runtime sends `honeypot` when 7.1e gives it a challenge; until then only a hand-made post can score on it through the
  API, and the no-JS post scores it today.
- A bot that always posts within 3 seconds of its challenge lands in the entries with a score of 60 unless something else
  speaks against it; that weight is ADR-0012's, so that a fast human is not lost.
- 7.1d adds the content rules, `IFormBuilderSpamCheck` and the marking as no spam to the same score.
- **Revisit** if a site needs ranges per form, or a range that matches on a proxy's forwarded chain rather than the address
  the host settled on.
