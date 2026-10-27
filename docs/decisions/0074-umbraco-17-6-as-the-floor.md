# 0074. Umbraco 17.6.0 as the floor

- Status: accepted; amends D3 (ROADMAP §2), [ADR-0001](0001-solution-structure-and-packages.md),
  [ADR-0010](0010-versioning-branching-and-upgrade-policy.md) and [ADR-0024](0024-transitive-advisories.md) (the
  floor the packages depend on and restore at)
- Date: 2026-10-27
- Roadmap step: 5.5a (found and decided); 18.3 runs its floor checks on 17.6.0, and Phase 19 starts the 18 line at
  18.1.0

## Context

D3 set the packages' range to `[17.5.0, 18.0.0)`, and NuGet resolves a range's lowest version, so the packages
compile and the unit tests run against 17.5.0. In 5.5a a unit test of option lists failed 3 times in 14 runs under
full CPU load: after a refused change dropped a list, the list was still kept. A probe of the same steps kept the
value in 30 of 400 runs on 17.5.0, and in none of 1,200 on 17.6.0.

The cause is Umbraco's runtime cache (`ObjectCacheAppCache`) before 17.6.0. Beside its `MemoryCache` it keeps a set
of the keys it holds, which a clear by prefix (`ClearByKey`, `ClearByRegex`) reads. It also removes a key from that
set in `MemoryCache`'s eviction callback, which runs later on the thread pool, for every reason, a removal included.
When a value is cleared and its key is inserted again before that callback runs, the callback takes the new value's
key out of the set, and the next clear by prefix leaves the value in the cache until it expires. Umbraco 17.6.0 and
18.1.0 fixed it: the callback now ignores removals and replacements (issue 23064).

Every cache of Jiron Form Builder relies on a change clearing its values on every server: the settings
([ADR-0043](0043-backoffice-settings-sections.md)), the access snapshot
([ADR-0048](0048-permission-evaluation-and-visibility-filter.md)) and option lists
([ADR-0073](0073-option-lists-and-sources.md)). On 17.5.x a change could stay unseen for as long as the value is
kept (12 hours for a list), and a revoked permission could stay granted.

## Options

1. **Keep 17.5.0 and key each value by a generation that a change raises on each server,** so a missed clear only
   leaves a value nobody reads. Every cache gets a second mechanism for a fault of the platform, Umbraco's own
   caches on such a site keep the fault, and the unit tests still run on a version whose clearing fails.
2. **Keep 17.5.0 and accept the fault on its releases.** A supported version would break "a change shows at once"
   and the permission model's promise that a revoked grant stops at once.
3. **Raise the floor to 17.6.0,** the first 17.x with the fix. A site on 17.5.x takes a minor update within the LTS
   line, which it needs for its own caches anyway. Chosen.

## Decision

- Every Umbraco reference of the packages is `[17.6.0, 18.0.0)`. The packages compile and the unit tests run against
  17.6.0, which `UmbracoFloorTests` checks; the test and demo hosts keep the latest 17.x (17.7.1).
- The advisory review at the new floor (ADR-0024): Microsoft.OpenApi 2.4.1 and the five System.Security.Cryptography.Xml
  advisories of 10.0.6 come with no restored graph any more and leave `NuGetAudit.props`; the others remain.
- The backoffice's types (`@umbraco-cms/backoffice` 17.5.0) stay as they are: 17.6 has everything they declare, and
  they rise when a step needs a newer client API.
- 18.3's checks of the floor run on a 17.6.0 site, whose package files the manifest's `version` busts (§3.4); the
  upgrade check there stays as written. Phase 19 sets the 18 line's range to `[18.1.0, 19.0.0)` for the same fix.

## Consequences

- D3, ADR-0001, ADR-0010 and ADR-0024, the architecture, the changelog and the listing name 17.6.0.
- Sites on 17.5.x cannot install Jiron Form Builder until they update Umbraco.
- **Revisit** when a later Umbraco fix matters to Jiron Form Builder in the same way, or when the backoffice code
  needs a client API newer than 17.5's types.
