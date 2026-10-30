# 0029. The Testing project: the stub, the clocks, the people and the test-only endpoints

- Status: accepted
- Date: 2026-10-23
- Roadmap step: 3.4a (decided and built); 3.4c's browser host turns these on, and later steps add their fakes and
  test-only endpoints here
- Amended in 6.4k by [ADR-0121](0121-a-strict-site-in-the-browser-tests.md): `Csp` gives every response a strict
  Content Security Policy, with a nonce of its own for a request that asks for one

## Context

ADR-0011 sets the test layers and names what `tests/Jiron.Umbraco.FormBuilder.Testing` holds: the HTTP stub on 5560
(webhook calls recorded, URL option lists, captcha verification answers), fake time, the user and member seeders, the
mail reader and the test-only endpoints under `/jfb-testing/`, guarded to test hosts. ADR-0001 keeps the project out of
every package. Step 3.4a builds it, with the unit and integration projects around it. What had to be decided:

- **The stub:** in Node beside the browser tests (Tandem's shape), or in C# in the Testing project, where ADR-0001 and
  ADR-0011 put it. Integration tests need it as much as the browser tests do: webhook signatures, retries and timeouts
  are proven in C# against a real Umbraco.
- **Where it runs:** a process of its own, which the browser tests' global set-up would start and stop by PID, or
  inside the test host.
- **How a host turns things on,** without the demo or any site that references the project ever doing so by accident.
- **Reading mail:** Umbraco writes `.eml` files with MimeKit (MIT). A reader of our own would have to decode MIME
  (multipart, quoted-printable, base64, encoded words) correctly; MimeKit is already in every Umbraco site's dependency
  graph.

Building it showed three facts about Umbraco 17.7.1's test hosts, now in §3.4:

- `UmbracoIntegrationTest.Setup` is not virtual, so a fixture sets configuration on `builder.Config` in
  `CustomTestSetup`, before `AddComposers`. `UmbracoTestServerTestBase.Setup` is virtual and takes
  `InMemoryConfiguration`.
- The test server keeps front-end routing in its start-up window: every page request gets the maintenance result,
  whose `Upgrading.cshtml` the test host does not have, so the request throws. A test of middleware puts a stand-in for
  the site right behind it rather than relying on Umbraco's front end.
- The test server's `IHostEnvironment` is "Production" and its `IWebHostEnvironment` Umbraco's "Tests", and the web
  host registers its `IWebHostEnvironment` again after Umbraco's set-up. A fixture that needs Development registers
  `IHostEnvironment` itself in `CustomTestSetup`.

## Options

1. **A Node stub started by the browser tests' global set-up** (Tandem). The browser tests get it, the integration tests
   do not, and a second implementation would be needed for them.
2. **A C# stub as an executable of its own.** Usable from both, but another project, another process to track by PID,
   and a build before every browser run.
3. **A C# stub as a class** (`StubServer`, a small Kestrel server on 127.0.0.1), which integration tests start
   in-process on a port the system chooses, and which a test host runs on 5560 as a hosted service when its settings
   ask for it.

## Decision

**Option 3**, with these rules:

- **One switchboard:** the `JironFormBuilderTesting` section (`FormBuilderTestingOptions`), off by default. `Endpoints`
  opens the test-only endpoints, `Clock` puts the host on `TestClock`, `StubPort` starts the stub, and `UserGroups`,
  `Users` and `Members` give the host its people. The browser tests' host passes them as environment variables, so no
  password sits in a file.
- **The stub** records every request (method, path, query, headers, body up to 1 MiB, the stub's time) and answers:
  `/webhooks/*` with `200`; `POST /captcha/siteverify` and `/captcha/{name}/siteverify` by the token alone
  (`StubCaptcha`: pass, fail, expired, or a score), so a browser test picks the outcome through its fake widget; any
  other path with `404` unless a scripted answer (method, path, status, body, delay) matches. The control paths under
  `/_stub/` read and clear the calls and add or remove answers, and are not recorded. It listens on loopback only, and
  in a host only in Development.
- **The test-only endpoints** (`/jfb-testing/status` and `/jfb-testing/clock` now) answer only when all three hold: the
  switch is on, the host runs in Development, and the request comes from this machine. A refused request reaches the
  site as if they did not exist. Unit tests cover each condition, and two integration fixtures prove it on a real
  Umbraco site: outside Development the paths are not there, in Development they answer, and from another address they
  are not there again.
- **Time:** `TestTimeProvider` (moved by hand), `ManualTimers` (timers that fire when told) and `TestClock` (the real
  time plus an offset that the test-only endpoints move).
- **People:** `TestPeople` creates user groups, users, member groups and members through Umbraco's services and leaves
  existing ones alone; `TestPeople.NewPassword()` makes passwords per run. `TestPeopleSeeder` runs it at start-up and
  logs "Jiron Form Builder test people ready", or "failed" with Umbraco's status and never a password.
- **Mail:** `TestMailbox` reads a pickup folder with MimeKit, retrying a file that is still being written, and
  `WaitForAsync` polls with a deadline. MimeKit is used as Umbraco already brings it; it is listed among the
  development-only packages in THIRD-PARTY-NOTICES.
- **Files of the repository:** test assemblies carry the repository root as assembly metadata, set by
  `tests/Directory.Build.props`, and `RepositoryRoot` reads it, so tests read files in place (ADR-0005).
- **The integration harness** follows the lessons file: `appsettings.Tests.json` (package migrations unattended off,
  ModelsBuilder off, quiet logging, the SQLite pool), the copied `Umbraco.Tests.Integration.deps.json`, the set-up
  wrapper, `LogCapture` through `ILogger<>`, `dotnet test --blame-hang-timeout 10m`, and Umbraco's `TEMP/` working
  folder git-ignored.

## Consequences

- One stub serves the integration and browser tests, and no extra process needs tracking: it stops with its host.
- Each feature step adds what its tests need here: the captcha and AI fakes, the test transport that alone reaches the
  stub (§4.10), test-only endpoints for seeding, running jobs and ageing entries, and the builders.
- The demo references the Testing project from 3.4c, with everything off, so it behaves as a site does.
- Revisit if a browser suite needs the stub to outlive a host restart (the resilience suite, 5524): it can then run
  the same class in a host of its own.
