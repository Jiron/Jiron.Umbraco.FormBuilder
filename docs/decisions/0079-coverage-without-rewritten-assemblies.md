# 0079. Coverage without rewritten assemblies

- Status: accepted; amends [ADR-0011](0011-testing-strategy.md) (the coverage tool, and where the coverage runs)
- Date: 2026-10-27
- Roadmap step: 5.5f (decided and built); every later phase review measures with it

## Context

Every phase review measures the line coverage of Core, which must be 85% or more (§4.12 and the phase-review gate).
ADR-0011 chose coverlet's collector, which writes an instrumented copy of each product assembly before the tests load
it. Since 2026-10-26 Windows Smart App Control is enforced on this machine, and its code integrity policy refuses to
load the Core assembly coverlet wrote (event 3077): the test adapter then finds no tests, and the report says 0%,
while the same tests pass without coverage. Phases 5.1a to 5.5e could not measure Core for that reason. Smart App
Control is a security setting of the owner's machine, which the build never changes (CLAUDE.md).

## Options

1. **Switch Smart App Control off.** Refused: it is the owner's security setting.
2. **Measure only in CI.** The gate runs here, and CI's results cannot be read from here; the owner turns CI on.
3. **Another tool that rewrites assemblies** (static instrumentation of any kind): refused by the same policy.
4. **Microsoft's code coverage collector with dynamic instrumentation:** `Microsoft.CodeCoverage` (MIT), which the test
   SDK already brings, instruments each assembly in memory through the runtime's profiling interface as it loads, so
   no file changes and the policy sees the assemblies as built. It writes Cobertura reports, as coverlet did, with the
   same package names. Chosen.

## Decision

- `tests/coverage.runsettings` names the `Code Coverage` collector with Cobertura reports, dynamic instrumentation on
  and static instrumentation off, Core and Web as the only modules, and the files under `obj` left out: the regular
  expression and logging source generators write them, and they are not code anyone writes or tests by hand.
- `scripts/coverage.mjs` reads every `*.cobertura.xml` under its folder, since this collector names each report after
  the machine and the time, and merges them as before: a line counts once, covered when any report hit it.
- `coverlet.collector` leaves the test projects and the notices; nothing else used it.
- The floor stays: Core at 85% or more. The coverage runs at each phase review with `node scripts/coverage.mjs --run`
  (`tests/README.md`), not inside `scripts/ci-local.ps1`, which ADR-0011 had said; the gate stays as fast as it was.

## Consequences

- The phase reviews measure Core again on this machine, and on any machine whose policy refuses rewritten assemblies.
- The figures differ a little from coverlet's (the two tools place lines and sequence points differently, and the
  generated files no longer count); the first figure of this tool is in the Phase 5 handover note.
- The collector runs on Linux and macOS too, so CI can use the same settings once the owner turns it on.
- **Revisit** if the collector's dynamic mode stops working with a new runtime, or if a phase review needs branch
  coverage as well.
