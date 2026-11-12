# 0238. The load test

- Status: accepted
- Date: 2026-11-12
- Roadmap step: 16.1 (part 16.2, performance and load)

## Context

Section 4.12 sets server budgets at a stated concurrency (rendering, a submission, the entries list over 100,000 entries,
a 10,000-entry export, the first mail), backoffice budgets (a 300-field designer, a tree of 1,000 forms), file budgets,
and that a page with a form scores in Lighthouse as the page without it. Part 16.2 asks for a Node load script without
copyleft tools that covers them, on SQLite, and reports where SQLite stops keeping up. The file budgets are held by
`scripts/size-check.mjs` on every client build, the designer's by `specs/budgets/designer.spec.ts`, and the entries
queries' plans by `EntryQueryPlanTests` and `IndexPlanTests` (ADR-0003, ADR-0039).

## Decision

- **A script of the browser tests', `tests/e2e/load/load.ts`** (`npm run load`), so it starts its host with the harness's
  own code: the demo on port 5523 (section 4.11) with a fresh SQLite database, the test endpoints and the test people,
  and no limit of an address or a form, as one machine plays every visitor.
- **Seeded through the test endpoints** (`LoadEndpoints`, on a test host only): the load's forms, the entries sent
  through the submission pipeline as a server key sends them, four at a time, so 100,000 entries are what a site's
  visitors would leave, and a folder of 1,000 forms through the form service.
- **One part at a time,** measured from Node over HTTP on the same machine, so each number is what a client waits for,
  ASP.NET Core's pipeline included: medians, p95 and the highest, each part against its budget, and the run fails when
  one is missed. The visitors' sends go as a page sends them: a challenge, the three seconds a person takes at least, the
  upload, the send. The first mail is timed to its file in the host's pickup folder.
- **Where SQLite stops keeping up** is a part of its own: bursts of 10, 20, 40, 80 and 160 sends arriving at the same
  moment, reported without a budget, with the size at which a burst's p95 first passes the submission budget.
- **The tree in the browser too:** Edge, signed in as the admin, from expanding the folder to its first form drawn.
- **Lighthouse from a throwaway folder** (`%TEMP%\jfb-lighthouse`, installed by the script on first use), never one of
  the repository's packages. Lighthouse 12.8.2 is the newest whose whole tree holds only licences rule 4 allows: from 13
  on, `@sentry/node` brings a package under a source-available licence (FSL-1.1), which rule 4 forbids anywhere. Its tree
  carries advisories that would otherwise sit in the browser tests' lock, though it only ever runs against the load host
  on this machine. One package in it, `parse-cache-control` 1.0.1, names its licence in an old field that its BSD-3-Clause
  licence file confirms.
- **The two pages Lighthouse compares** share the demo's layout: the Contact page, with its form and the layout's
  newsletter form, and the Privacy page without the newsletter (`?newsletter=off`, a switch of the demo's), which holds
  no form, so nothing of Jiron Form Builder loads on it. The demo sends its pages compressed (ASP.NET Core's response
  compression, over HTTP only, as a site's host would); uncompressed, a page's form markup alone could cost it a round
  trip that no site with compression pays.
- **Lighthouse measures both ways,** three runs of each page each way: its simulation (its default), which works a slow
  phone's load out from a load on this machine, and the same network and processor applied to a real load in the
  browser. The budget is held on the real load. The simulation's numbers are reported beside it with what they leave
  out: it counts the stylesheets the forms bring into the body as holding the first paint, served one after another on
  the local server's single connection, although a browser draws the page's content above them without waiting, and it
  put the page with forms at 99 against 100 in three of five runs while a real load on the same network differed by
  19 ms of first paint. Meeting the simulation's model would have meant writing the forms' styles into every page or
  merging the base rules into each theme, which costs every visitor bytes their cache now saves them, against a delay a
  browser does not have.
- **The numbers go to `docs/performance.md`** with the machine they were measured on, and `.local/load/results.json`.

## Consequences

- A run takes about 25 minutes, half of them the seeding; the database is fresh each time, so every run
  measures the same.
- The sends leave a warm-up round out of their numbers, as rendering, the list and the tree leave their first requests:
  the first full run counted the burst of ten sends cold, and its first round, the server's first sends after its start,
  took 290 ms each.
- The budgets are checked on the machine that runs the script; a slower machine may miss one a faster one meets, which
  the numbers' page says.
- Lighthouse needs the network on its first use, to install into its folder; the rest of the run needs none.
