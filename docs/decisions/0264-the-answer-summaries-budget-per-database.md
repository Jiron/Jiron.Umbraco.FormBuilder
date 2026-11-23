# 0264. The answer summaries' budget is per database: a second on SQLite, eight on SQL Server on a hosted runner

- Status: accepted; adds to [ADR-0212](0212-answer-summaries.md) (the summaries and their budget on SQLite) and
  [ADR-0261](0261-where-the-test-time-goes.md) (the timed tests run alone)
- Date: 2026-11-23
- Roadmap step: 26.1

## Context

ADR-0212 holds the answer summaries of a form with 100,000 entries to a second on SQLite, and
`AnswerSummaryBudgetTests` measures it: the median of five loads, for every entry and for a filter. Until 2026-11-22
that test had only ever run on SQLite on the development machine. The release run of `v17.0.0-beta.1` was its first
run on SQL Server, in the workflow's timed job, alone on a hosted runner with SQL Server 2022 in a container beside
it: 3.95 s against the second, so the job failed. The two other timed tests, the entries list's first page (300 ms)
and the export, held their budgets in that same job, and the same test held its second in the SQLite job.

So the query is correct on SQL Server and about four times over a budget that was set for another database on
another machine. What is not known is why. Read from the statements, not measured: each summary is about nine
statements that each join the form's 100,000 entries to their values; the index the entries are found by
(`formId`, `state`, `submittedUtc`) does not hold `isTest`, which every one of them tests, and the indexes of the
values do not hold every column the counts read, so SQL Server may look each row up or scan. SQLite, in the same
process as the tests, pays far less for that than a database server reached over a socket.

## Options

1. **Make the statements meet a second on SQL Server.** The likely means are indexes that hold more columns, which
   is a migration in a schema that two prereleases have shipped. It cannot be measured here: this machine may not run
   a SQL Server (CLAUDE.md rule 6), so each try is a run of the workflow, about 4 h 20 min of runner time, and a
   migration written without a measurement is a guess.
2. **Leave the test out on SQL Server.** Step 26.1 skips no test on a system to make it pass, and the test's counts
   are worth having there.
3. **A budget per database**, each from a measurement on the system it runs on, with the measured times in the run's
   log every time.

## Decision

Option 3.

- **SQLite: a second**, as ADR-0212 says, on the development machine and on the hosted runner alike.
- **SQL Server on the workflow's hosted runner: eight seconds**, about twice the 3.95 s measured there. It is not a
  promise to a site; it is what the test can hold on that hardware. It still fails on what a budget is for: a lost
  index or a plan that reads the entries once per option costs many times that, not twice.
- The test writes its medians and its budget to its output, and `scripts/test-integration.mjs` prints what the
  timed tests wrote into the run's log, so every run on a hosted runner shows them without its artifacts.
- The dry run of step 26.1 measures again; `docs/STATUS.md` holds what it showed.

## Consequences

- A site on SQL Server with about 100,000 entries in one form can expect its Answers view to take seconds, not a
  second, on hardware like a hosted runner. That is a known limit of this prerelease, said in `docs/STATUS.md`.
- Making it faster needs a SQL Server to measure on, which only the owner can allow or provide
  (`docs/OWNER-TODO.md`). With one: read the plans of the summaries' statements, add what the indexes lack as a
  migration, and bring this budget down to what is then measured.
- A budget that runs only on GitHub is changed only with a measurement from there.
