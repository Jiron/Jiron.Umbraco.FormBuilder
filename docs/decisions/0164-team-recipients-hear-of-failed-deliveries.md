# ADR-0164: The team recipients hear of failed deliveries

Status: accepted, 2026-11-03 (ROADMAP 8.6a, its wiring sweep). Builds on ADR-0007 (the outbox) and ADR-0159 (visitor mail).

## Context

Section 4.8 gives the email settings team recipients "for system notices (failed deliveries)". The setting was stored and validated from 8.2 on, and nothing read it. No later step built the notice.

## Decision

- **An action that fails for good, or ends unsure whether it arrived, and a mail that fails for good, tell the team recipients of the entry's site.** An attempt that will be tried again tells nobody.
- **At most once an hour per form,** as a host that is down fails every entry, and a mailbox that fills up helps nobody. The mail says more may have failed since. Its time sits in the key and value store, so two servers can at worst send one extra, which costs less than a lock on the path of a failure.
- **It names the form, the action type and the code, and no answer.** The team's addresses may be broader than who may read entries.
- **A notice is a mail of its own kind, `notice`.** One that fails tells of nothing, and neither does a test mail, so a failure cannot feed itself.
- **A failure to tell stops nothing:** it is logged, and the run or mail goes on as it would have.

## Consequences

- With no team recipients set, nothing changes.
- Someone reading the deliveries list sees the notices there, under their kind.
