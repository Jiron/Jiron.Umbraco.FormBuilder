# 0264. The timed tests' budgets are per database: SQLite's as they were, SQL Server's from the hosted runner

- Status: accepted; adds to [ADR-0212](0212-answer-summaries.md) (the summaries and their budget on SQLite),
  [ADR-0207](0207-the-entries-list.md) (the list's first page), [ADR-0003](0003-entry-storage.md) (the export while
  submissions arrive) and [ADR-0261](0261-where-the-test-time-goes.md) (the timed tests run alone)
- Date: 2026-11-23
- Roadmap step: 26.1

## Context

Three integration tests hold the product to a time (the category `Timed`): the answer summaries of a form with
100,000 entries (a second, ADR-0212), the first page of its entries list (300 ms, ROADMAP §4.12) and the CSV export of
10,000 entries while submissions arrive (5 s, and 150 ms at p95 for a submission meanwhile). Each budget was set and
measured on SQLite on the development machine, and until 2026-11-22 the tests had run nowhere else.

The workflow runs them on SQL Server too, in a job of their own on a hosted runner, with SQL Server 2022 in a
container beside the tests. Three runs there, on 2026-11-22 and 2026-11-23:

| Measured on SQL Server, hosted runner | First run | Second | Third | SQLite's budget |
| --- | --- | --- | --- | --- |
| Answer summaries, every entry (median of five) | 3.95 s | 4.02 s | 5.28 s | 1 s |
| Answer summaries, a filter | not read | 0.79 s | 1.02 s | 1 s |
| First page of entries, the slowest of three lists (median) | not read | 105 ms | 164 ms | 300 ms |
| CSV export of 10,000 entries | not read | 3.78 s | 5.50 s | 5 s |
| A submission during the export, p95 | not read | 25 ms | 89 ms | 150 ms |

The first run failed on the summaries, the third on the export. On SQLite the same jobs held every budget each time.
So the statements are correct on SQL Server and slower there than budgets set for another database on another
machine; and one hosted runner is a third slower than the next, so a budget close to one measurement fails on the next
run for no change in the product.

What is not known is why the summaries are four to five times over. Read from the statements, not measured: each
summary is about nine statements that each join the form's 100,000 entries to their values; the index the entries are
found by (`formId`, `state`, `submittedUtc`) does not hold `isTest`, which every one of them tests, and the indexes
of the values do not hold every column the counts read, so SQL Server may look each row up or scan. SQLite, in the
same process as the tests, pays far less for that than a database server reached over a socket.

## Options

1. **Make the statements meet SQLite's budgets on SQL Server.** For the summaries the likely means are indexes that
   hold more columns, which is a migration in a schema that two prereleases have shipped. It cannot be measured here:
   this machine may not run a SQL Server (CLAUDE.md rule 6), so each try is a run on GitHub, and a migration written
   without a measurement is a guess.
2. **Leave the tests out on SQL Server.** Step 26.1 skips no test on a system to make it pass, and their counts and
   contents are worth having there.
3. **A budget per database**, each from measurements on the system it runs on, with the measured times in the run's
   log every time.

## Decision

Option 3.

- **SQLite: the budgets as they were**, on the development machine and on the hosted runner alike: 1 s, 300 ms, 5 s
  and 150 ms.
- **SQL Server on the workflow's hosted runner: about twice the highest value measured there**, rounded up: 12 s for
  the summaries, 600 ms for the first page, 12 s for the export and 300 ms for a submission during it. They are not
  promises to a site; they are what the tests can hold on that hardware from one runner to the next. They still fail
  on what a budget is for: a lost index or a plan that reads the entries once per option costs many times a
  budget, not a third more.
- Each test writes what it measured, its database and its budget to its output, and `scripts/test-integration.mjs`
  prints the lines of the timed tests' output that name a time into the run's log, so every run on a hosted runner
  shows them without its artifacts.

## Consequences

- A site on SQL Server with about 100,000 entries in one form can expect its Answers view to take seconds, not a
  second, on hardware like a hosted runner. That is a known limit of this prerelease, said in `docs/STATUS.md` and
  in the release's upgrade notes.
- Making it faster needs a SQL Server to measure on, which only the owner can allow or provide
  (`docs/OWNER-TODO.md`). With one: read the plans of the summaries' statements, add what the indexes lack as a
  migration, and bring these budgets down to what is then measured.
- A budget that runs only on GitHub is changed only with a measurement from there, added to the table above.
