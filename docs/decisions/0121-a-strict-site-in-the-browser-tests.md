# 0121. A strict site in the browser tests

- Status: accepted; amends [ADR-0029](0029-testing-project.md) (a setting of the Testing project) and
  [ADR-0026](0026-client-workspace-and-asset-delivery.md) (what the size check holds the runtime's build to); checks
  what [ADR-0004](0004-rendering-and-submission-paths.md) decided for the CSP, cookies and chunk names
- Date: 2026-10-30
- Roadmap step: 6.4k (decided and built)

## Context

ADR-0004 decided that the website runtime needs nothing of a Content Security Policy but `script-src 'self'` on a
page behind the output cache, that a nonce works on a page that is never cached and reaches the chunks the runtime
imports, with or without `'strict-dynamic'`, that Jiron Form Builder sets no cookie, and that the runtime's chunks are
named by their content, since the entry's `?v=` never reaches an `import()`. The spike of 1.4a showed it in a
sandbox; 6.4k shows it on the demo, with the chunks 6.4a to 6.4h built. The browser tests' host runs without the
output cache, since a form published during a run would leave cached pages stale until 6.7 evicts them, and sets no
policy at all.

## Options

1. **Turn the output cache and a policy on for the run's host.** Every other spec would then meet stale pages and a
   policy it was not written for.
2. **Check the markup only:** no inline script, no style attribute, no nonce on a cached page. That never runs the
   browser's policy, nor the chunks' loading under it.
3. **A host of its own for the strict site,** started by its spec: the output cache on, and a test-only policy from
   the Testing project, as strict as a careful site sets one. Chosen.

## Decision

- **The Testing project's `Csp` setting** (`JironFormBuilderTesting:Csp`, off by default, in Development only, as the
  test-only endpoints are) puts `TestCspMiddleware` in front of the pipeline. Every response gets
  `default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; object-src 'none'; base-uri 'self';
  form-action 'self'; frame-ancestors 'self'`, set as the response starts, so a page the output cache serves carries
  it too. A request with `jfb-test-nonce=nonce` (or `=strict-dynamic`) gets 16 random bytes as its nonce instead of
  `'self'` (with `'strict-dynamic'` beside it), its response is `no-store`, and `TestCspNonces`, the host's
  `IFormBuilderCspNonceProvider`, gives `<jfb-assets>` the nonce.
- **The strict-site spec** (`tests/e2e/specs/website/strict-site.spec.ts`) starts the demo as a second host on 5525
  (section 4.11's extra hosts), with its own database, the output cache on for five minutes, the policy on and no stub,
  whose port the run's host holds. It counts the policy's violations from the page's first script on
  (`securitypolicyviolation`), and first shows that the count works: an inline script that the page did not bring is
  refused and counted. Then the room hire form (the calculation language) runs from a page the cache serves (an `Age`
  header) with `script-src 'self'`; the workshop booking (the merge tags, the demo's one form with texts that recall
  answers, for members, so its page is never cached) runs with `script-src 'self'` once a member signs in; and both
  run from a page with a nonce in either mode: focused, sent empty, every chunk loaded, the error summary shown, no
  violation, axe clean. The contact form, sent in full, leaves no cookie, and no response of the visit sets one.
- **The chunk names:** the size check holds every file of the runtime's build but its entry to a name with the
  bundler's content hash (`<name>-<eight characters>.js`), and a build test of the shared configuration shows that the
  same sources give the same names, a changed module a new one, and the entry its own.
- `scripts/gate-clone.mjs` reads every port the harness exports, so a gate refuses to start while the strict site's
  host listens.

## Consequences

- A change that adds an inline script, a style attribute, a resource from another host or a cookie fails on a page
  as a strict site serves it, the chunks loaded on demand included.
- The spec costs a second host's start, about two minutes of the browser projects' run.
- The policy is the Testing project's, not a recommendation: the docs (15.x) tell sites what the runtime needs.
