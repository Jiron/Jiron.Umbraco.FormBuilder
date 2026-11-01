# 0146. The runtime's budget grows to 60 KB

- Status: accepted; the owner's decision on section 4.12, asked in `docs/OWNER-TODO.md`; ends the wait of
  [ADR-0120](0120-sending-waits-for-the-runtimes-budget.md) and changes the number in
  [ADR-0116](0116-the-runtimes-budget-counts-one-language.md), whose way of counting stands
- Date: 2026-11-01
- Roadmap step: between 7.3d and 7.4 (the owner wrote during the run, so the message beats the plan)

## Context

Section 4.12 gave the website runtime 16 KB gzip for its core and 40 KB gzip with everything. The runtime with every feature
it has stands at 39.98 KB, and sending (41.38 KB once built on `wip/6.4i-sending`) and the rest of the plan's runtime features
come to about 58 KB. Eleven steps waited on this: 6.4i, 6.4j, 6.4l, 6.4m, 6.4n, 6.4o, 6.4p, 6.5d, 6.6g, 7.1e and 7.2c.

## Decision

- **The owner raised the budget to 60 KB gzip with everything,** on 2026-11-01, on the recommendation put to them: a few
  kilobytes more are no issue, as long as it works as intended afterwards. Everything else in section 4.12 stands.
- **The core stays at 16 KB,** and it is what every page with a form loads before its visitor touches it, about 1 KB today.
  Features still load on demand, so the 60 KB is the most any one form can ask for, not what a page loads. ADR-0116's count
  stands too: every feature and the largest language's words.
- **The eleven steps resume at 7.4i,** this phase's retry of what was blocked, with every check they had. The security review's
  fixes (7.4a to 7.4g) go first, since the runtime's sending builds on the server code they change. Their work so far waits on
  `wip/6.4i-sending`.
- **Bytes need no more golfing for their own sake.** ADR-0116 asked each later step of 6.4 to make room for what it adds; the
  steps now stay within this budget and keep their code clear.

## Consequences

- `scripts/size-check.mjs` holds the new number, and the gate still fails past it.
- The plan's estimate leaves about 2 KB, so 6.4p's review measures what the runtime came to. Past 60 KB is the owner's to decide
  again, and the step goes to `docs/OWNER-TODO.md` as before.
