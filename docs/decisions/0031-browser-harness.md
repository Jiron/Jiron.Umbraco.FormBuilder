# 0031. The browser harness: the test host, its start-up wait and its people

- Status: accepted
- Date: 2026-10-23
- Roadmap step: 3.4c (decided and built); 4.6 gives the personas their permissions; 6.2 and 6.5 complete the form
  helpers on real forms; 6.6 adds the static site on 5521

## Context

ADR-0011 fixes the shape of the browser tests from Tandem's lessons (ROADMAP section 3.3): a fresh demo host on 5520
per run, a global set-up that waits for Umbraco's start-up work, the personas, the sign-in lock and the hang marker.
Three things had to be decided in 3.4c that Tandem's harness either got wrong or never needed.

**The start-up wait.** About a minute after it starts, Umbraco 17 registers its distributed jobs and rebuilds its
search indexes, and every SQLite hang of Tandem's browser tests began in that minute. Tandem's wait looked for
Umbraco's "The Delivery API is not enabled" line, which never appears once the Content Delivery API is on (6.6 turns
it on). Umbraco 17.7.1 logs nothing when the rebuild ends and publishes no notification: `ExamineIndexRebuilder` runs
the registered index populators one after the other, each on every index it is registered for, and returns.

**The order of populators.** A first attempt registered the marker populator in the Testing project's composer. The
integration test showed it running before Umbraco's own: Umbraco registers its populators in `AddExamineComposer`,
and composers with no order between them run in any order.

**The demo and the Delivery API.** The demo was created without the template's "use Delivery API" option, so its
`Program.cs` never called `AddDeliveryApi()`. With `Umbraco:CMS:DeliveryApi:Enabled` set, the API's controllers were
found but its services were missing, and every request failed with 500.

## Options

1. **The wait:**
   - a fixed delay of 70 seconds: simple, but slow on a quiet machine and too short on a loaded one;
   - polling `IIndexRebuilder.IsRebuildingAsync`: it reports false during the minute before the rebuild and again after
     it, so a poll can miss a short rebuild;
   - a test-only index populator that runs after all the others and logs a line. It needs the order to hold, and it
     indexes nothing.
2. **The order:** a composer that runs after `AddExamineComposer` (`[ComposeAfter]`), or a check at run time alone.
3. **When the wait fails:** start the tests anyway and warn, as Tandem did, or fail the set-up.
4. **The members:** through the Testing project's people seeder, or through the demo's own member seeder.

## Decision

- **The start-up marker:** `StartupMarkerPopulator` in the Testing project, registered for Umbraco's standard indexes
  only, so the set of indexes a rebuild clears does not change. It logs `Jiron Form Builder test host: indexes rebuilt`
  with the index names. `StartupMarkerComposer` registers it only when `JironFormBuilderTesting:StartupMarker` is on,
  and runs after `AddExamineComposer`.
- **A check at run time as well:** before it logs, the populator checks that it is still the last registered
  populator. When it is not, it logs an error instead, and the wait fails with that reason instead of starting early.
- **The wait** (`waitForStartupWork` in `tests/e2e/support/host-health.ts`) ends when the log holds both Umbraco's
  `Completed registering distributed background jobs` and the marker, plus five seconds. It ends at once on a hung
  database or a misplaced marker. A test checks that the lines it waits for are the ones the C# code writes.
- **A failed wait fails the run:** the set-up stops the host, writes the hang marker when the database hung, and fails
  with the reason and the end of the host's log. A run that starts its tests in the stall would only fail later and
  further from the cause.
- **The Delivery API:** the demo calls `AddDeliveryApi()` as the template's option does, and the API stays off until
  `Umbraco:CMS:DeliveryApi:Enabled` turns it on. `JFB_E2E_DELIVERY_API=on` runs the test host with it on, and a smoke
  test checks the switch reached the host. While it is off, Umbraco 17.7.1 answers its requests with 401.
- **The host's settings come only from the harness:** inherited variables that would configure it (`Umbraco__*`,
  `JironFormBuilder*`, `Demo__*`, `ConnectionStrings__*`, logging and `ASPNETCORE_*`) are dropped. A stale host is
  stopped only when its command line still names this host's content root, and always with its whole process tree.
- **The people:**
  - the personas of ADR-0013 are user groups created by the Testing project: `jfbDesigners` (Content and Forms),
    `jfbEntryHandlers`, `jfbViewers`, `jfbPrivacyOfficers` and `jfbNoAccess` (Forms only). The outsider is one of
    Umbraco's writers (Content only), and the admin is the unattended install's user;
  - the two members come through the demo's own member seeder, in its member group, with access to the members
    area, as the demo creates its members;
  - passwords are new for every run and live only in `.local/credentials.json` and the host's environment.
- **The form helpers** (`fillForm`, `submitForm`) work through the markup contract (ADR-0015). They find fields by
  alias and controls by name, and refuse a field that a rule hides. They wait for the outcome, or for the error
  summary after a post renders the page again or after the runtime moves focus to it (ADR-0004). Until 6.2 renders
  forms, a smoke spec runs them on a contract page served with `page.route`.
- **What waits for its first spec:** each Playwright project joins with its first spec (ADR-0011).
- **The static site on 5521** (added in 6.6f): `support/static-site.ts` serves one page from the test's own process,
  made from its query, that loads `<jfb-form>` from the host with the attributes given, as a site that does not run
  Umbraco would. The host's public API allows its origin, `http://127.0.0.1:5521` (`JironFormBuilder:PublicApi:
  AllowedOrigins` in the harness's settings); `http://localhost:5521` is the same page on an origin it does not allow.
  The spec that uses it starts it and stops it. The stub on 5560 runs inside the host (ADR-0029), so the set-up starts no server of its
  own for it.

## Consequences

- The wait holds with the Delivery API on and off. Two real runs of the smoke project showed both logs, and the
  host-health tests replay them.
- A later registration that puts a populator after the marker shows as a failed set-up that names the cause, not as
  hangs.
- `loginAs` waits for the backoffice header, not for a section address. Until 9.1 adds the Forms section, the Forms-only
  personas see a backoffice without sections.
- Umbraco's populators can be registered twice. The integration host's base class and `AddExamineComposer` both
  register all six, and the demo logs Umbraco's "not enabled" line twice per rebuild. The marker still comes last in
  both.
- Revisit the marker when Umbraco publishes a notification or a log line when a rebuild ends: then the wait can look
  for that, and the populator can go.
