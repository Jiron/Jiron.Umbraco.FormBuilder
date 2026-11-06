# 0197. Steps are clustered, and never split

- Status: accepted; amends the roadmap's "How to use" (splitting a step) and CLAUDE.md rule 10
- Date: 2026-11-06
- Roadmap step: during 10.3a; the owner's messages of 2026-11-06

## Context

Splitting a step that turned out bigger than written was the roadmap's way to avoid cutting corners (CLAUDE.md rule
10). It kept each part's checks small, but it made the list of open steps longer as the run went on: 10.3 became six
parts on the day, and the open steps went from 46 to 51. The owner wrote, the same day: "We have to start to come to an
end there is no way that there is more tasks again than there was before we have to cluster more or something", and
then: "Id still like to keep whatever is in those tasks ofc but just make sure that wr dont keep making more and more
small steps and we never come to an end like that" (sic: "wr dont" in the original). The owner's message beats the plan (CLAUDE.md rule 8).

## Options

1. Merge 10.3's six parts back into one, and keep splitting other steps when they turn out big.
2. **Put the open steps together, one or two a phase, each keeping every item and check of the steps it holds, and
   never split a step or add a small one again.**

## Decision

Option 2.

- **The clusters.** Each keeps the number of its first part, and holds its parts word for word under their old numbers
  and titles, each part with its own "Done when"; the cluster's own "Done when" is that every part's passes. A phase's
  last cluster carries the phase review and the phase-review gate. 51 open steps became 14:

  | Step | Holds |
  | --- | --- |
  | 10.3 Preview, publishing and history, and phase review | 10.3a to 10.3f, 10.4 |
  | 11.1 Entries: the list, the entry panel, the inbox and Spam | 11.1, 11.2, 11.3 |
  | 11.4 Exports, insights and answer summaries, and phase review | 11.4, 11.5, 11.5b, 11.6 |
  | 12.1 The library: option lists, themes, email layouts and templates | 12.1, 12.2, 12.3 |
  | 12.4 Global settings and access, deliveries and the audit log, forms as files, and phase review | 12.4a, 12.4b, 12.5, 12.6 |
  | 13.1 Privacy: retention, data subject tools, sensitive data and IP addresses, and phase review | 13.1 to 13.4 |
  | 14.1 Languages, members and multi-site, and phase review | 14.1 to 14.4 |
  | 15.1 Extensibility, samples and the AI add-on, and phase review | 15.1 to 15.3 |
  | 16.1 Hardening: security, performance and load, accessibility and resilience | 16.1 to 16.4 |
  | 16.5 Upgrade tests, the UX polish pass and code quality, and phase review | 16.5, 16.6, 16.7 |
  | 17.1 Logo and icon, documentation and screenshots, and phase review | 17.1, 17.2a, 17.2b, 17.3 |
  | 18.1 Packaging, the release workflow, the install test and the first prerelease, and phase review | 18.1 to 18.4 |
  | 19.1 The Umbraco 18 line: branch, build and everything on 18, and phase review | 19.1, 19.2 |
  | 20.1 Clean-clone verification and the status report | 20.1, 20.2 |

  A step number named elsewhere before this decision (in ADRs, docs and comments: "11.2", "18.3") points into the
  step that now holds it, by this table. 8.6b stays blocked as it was (ADR-0146).
- **No more splits, no more small steps.** A step that turns out bigger than written stays one step, worked in as many
  commits as it needs; something clearly missing joins the step it belongs to. The roadmap's "How to use" and CLAUDE.md
  rule 10 say so.
- **Within a cluster,** the parts are built in the order that suits the work (10.3 keeps the order its split chose:
  History, then conflicts, then the publish summary, then the preview), each with its design skills, journeys, reviews
  and tamper rounds as before; the cluster is ticked once, with the hash of its last work commit.

## Consequences

- The roadmap shows 14 open steps and one blocked one, and the count only goes down from here.
- Every item and check of the old steps is still there, word for word; none is weakened.
- A cluster takes longer to tick than one of its parts, so its work is committed and pushed as it goes, and WORKLOG
  notes each part as it is finished.
