# 0261. Where the test time goes: the same checks, run side by side

- Status: accepted. Changes how the gate of [ADR-0011](0011-testing-strategy.md) runs its two long stages; keeps
  every check of it, the one host per run of [ADR-0031](0031-browser-harness.md) as the least a run needs, and "Flaky
  means broken"
- Date: 2026-11-21
- Roadmap step: 24.1

## Context

**The owner, on 2026-11-21,** before the first release and before the repository is opened: the tests take hours,
there seem to be about ten thousand unit tests and a couple of thousand browser tests, and that may be more than is
needed. Go through them, throw away what is not needed or combine it, and narrow the time down, but only where a
broken product is still flagged.

**Measured first,** on the development machine (8 cores with 16 threads, 28 GB), from the gate's own log of 2026-11-19
(`ci-local.ps1` on f9eda3e5, 5,375 s) and a result file with every integration test's time:

| What | Tests | Time | Per test |
| --- | --- | --- | --- |
| Unit tests (.NET) | 9,169 | 59 s | 6 ms |
| Client tests | 6,342 | 86 s | 14 ms |
| AI add-on (a real Umbraco) | 56 | 94 s | 1.7 s |
| Integration tests (a real Umbraco) | 2,313 | 37 min 28 s | 1 s |
| Browser tests, one run | 590 | 23 min | 7 s of test time, four at once |
| Checks, builds, packing | | 6 min | |

So the 15,500 unit tests take two and a half minutes together, and removing every one of them would save 3% of the
gate. The time is in two places. Each integration test starts an Umbraco of its own, and the 2,313 of them run one
after the other in one process: the median test takes 0.6 s, ten tests take more than 5 s and together 166 s, so no
few slow tests carry it, the count times the start does. And the browser tests run four at once against one site on
SQLite, whose backoffice tests take 17 s each on average. A phase review ran this up to three times (the clean-clone
verification, the coverage run, the Umbraco 18 line's gate), which is where the hours came from; and when the site's
database stalled, the browser stage ran a second time.

**Two more facts.** The workflow's jobs stop after 45 minutes, and its SQLite job and its SQL Server job each run the
integration tests in one process, which took 37 minutes on a machine with four times a hosted runner's cores. The
release calls that workflow, so the first release would have met the limit. And in the integration tests 264 of the
2,313 are rows of 36 tables, each row starting its own Umbraco.

**Tried on 2026-11-21:**

- The integration tests in eight processes at once, each with a working folder of its own (Umbraco's harness takes
  its folder from the process's temporary folder when `System_DefaultWorkingDirectory` is set, and its SQLite
  databases are in memory, each with a name of its own): all 2,313 ran, each once, in 7 min 19 s. The tests' own time
  rose from 2,250 s to 3,320 s, as the processes share the cores; the two tests that hold 100,000 entries to a time
  budget took 70 s and 39 s where they take 46 s and 24 s alone, and passed.
- The browser tests with eight workers on the one site: 18.5 minutes where four take 23, the tests' own time up from
  4,324 s to 7,081 s, and two failures that four workers do not have. Read at first as "the site is the limit"; the
  next measurement showed it is the processor.
- The browser tests against two and three sites of their own, four workers each: 19.4 and 18.9 minutes, the tests' own
  time 6,665 s on two sites, the processor at 96 to 99% throughout on three. A third site gains half a minute and
  brings failures of tests that wait on timing. Eight browsers and their hosts fill the eight cores whichever site
  they ask.
- The same 111 browser tests with and without Playwright's trace recorder (`retain-on-failure` records every test and
  keeps the failed ones'): 1,070 s of test time with it, 848 s without. A fifth of the stage went into a file that is
  read only after a failure.

## Options

1. **Remove tests until the time fits.** The time follows the count only in the two layers that start a site, and
   there a test is the one place a behaviour is run against a real Umbraco or a real browser. To halve the gate this
   way would take half of those tests with it. Ruled out as the way to the time; kept, narrowly, as the last part of
   the decision.
2. **Fold tests together** (one Umbraco per table instead of per row, one browser test per screen). It saves a start
   per row, 264 at most of 2,313, and a failure then names the table and not the row. Ruled out as a rule; a table may
   be folded where its rows share one arrangement and the failure message still names the row.
3. **One schema or one site for a whole fixture.** Umbraco's harness builds a host per test whatever the database
   option says, so it saves the smaller part and makes every test depend on the ones before it. Ruled out.
4. **Run the two long stages side by side** (chosen): the integration tests in several processes, the browser tests
   against more than one site.
5. **Run the test projects' own tests in parallel inside one process.** Umbraco's integration harness keeps state
   per process and is not written for it. Ruled out.

## Decision

**The checks stay; how they run changes.**

- **Integration tests in shares.** One script runs the integration assembly in several processes at once, each with
  its own working folder and results. The tests are shared out by class. The last share names no class: it takes
  every test the other shares do not name, so a new class is run without anyone listing it, and no class can be in
  two shares or in none. After the run the script adds up what ran and fails unless it is the number of tests the
  assembly holds. The gate, the coverage run and the workflow's jobs all go through this script; the workflow gives
  each share a job of its own.
- **Tests that measure time run alone.** A test that holds the product to a time budget is marked as such and runs
  after the shares, with nothing beside it, as the browser tests' `budgets` project already does.
- **Browser tests against two sites.** A run starts two sites (one to three by its argument), each with its own
  ports, database, mail folder, sign-in lock and credentials, and gives every spec file to exactly one of them; four
  workers per site as before. Two is what the development machine's cores carry; more sites need more cores, not
  more ports. `budgets` runs once, after everything, alone. The tests that ran are checked against the tests the
  projects hold, as for the shares.
- **The gate records no trace.** The gate's runner turns the recorder off and prints, for a failed test, the command
  that runs it again with its trace; a developer's own run records as before, and the screenshot of a failure stays.
  A failure that does not come again leaves its message, its screenshot and the host's log, and no trace: that is the
  price, taken because the recorder slowed every passing run by a fifth and changed the timing the tests met.
- **The test hosts do Umbraco's start-up work at once.** A site's first test started 79 s after its host: 17 s to
  start, the rest the start-up wait idling through the minute after which Umbraco registers its distributed jobs
  (`Umbraco:CMS:DistributedJobs:Delay`, a setting) and rebuilds its indexes (a minute fixed in its handler of the
  started notification). Every host the harness starts sets the first to zero, and the Testing project takes
  Umbraco's handler out and rebuilds the same indexes itself with no delay, once per host; a host whose Umbraco no
  longer has that handler stops at start-up rather than rebuild twice. The wait stays as strict, both lines and the
  settle time (ADR-0011, ADR-0253): only the idling goes. One site's set-up went from 81 s to 22 s.
- **Three workers per site on two or three sites.** Four each put eight browsers and two hosts on eight cores: the
  tests' own time rose to 5,100 s, and three of five whole runs failed on tests that read a page too early (each fixed
  at its cause). Three each took 4,355 s and passed, 70 s longer. One site keeps the config's four.
- **The gate's cheap stages side by side.** The repository checks, the client's build and the browser tests'
  set-up need nothing of each other, nor do the client's lint and tests and the .NET build, which needs only the
  client's build: `scripts/lanes.mjs` runs each group as lanes at once, every line marked with its lane, the first
  failure stopping the rest. Every command of the gate still runs and still fails it.
- **What may go.** A test is removed or folded into another only where the same behaviour is still run by a test
  that fails when it breaks, at the same layer or a cheaper one, and the record below names both. Unit and client
  tests are left alone: they cost nothing worth a risk.
- **Proof that a break is still flagged.** One fault is put into the product for each layer (shared logic, a
  repository, a page a visitor gets, a backoffice screen) and the new runners must fail on each, in the stage that
  failed before.

## Consequences

- The gate's time is the longest share's and not the sum; the target on the development machine is under 30 minutes
  where it was 67 without a second browser run. `docs/STATUS.md` says what it measures.
- A phase review's three long runs shrink with it.
- The processes share the cores, so a single test takes longer than alone. Nothing but the marked tests may depend on
  how long something takes; a test that does and is not marked is a fault of the test. The first runs on two sites
  showed such tests, and one fault of the product behind them (an answer given and left before a form's logic had
  loaded got no error at its field); 24.1 fixes each at its cause.
- The workflow's test jobs become a matrix, each well inside its 45 minutes. Hosted runners have fewer cores, so the
  number of shares per job is the script's argument and not a constant.
- More than one site at once needs more memory and ports: the port table in `tests/e2e/support/paths.ts` stays the
  one place they are given out, inside 5500 to 5599.
- **Removed or folded:** kept current by 24.1 as the list of each test that went, with the test that still runs its
  behaviour.
