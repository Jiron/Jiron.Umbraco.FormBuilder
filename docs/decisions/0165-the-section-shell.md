# 0165. The section's shell: the whole sidebar at once, one source of health, and what the Overview reads

- Status: accepted; the whole sidebar as a list of items is superseded by
  [ADR-0257](0257-five-places-before-the-tree.md), in which `section/sidebar.ts` names five places and their tabs and
  every screen keeps its address. The one source of health and what the Overview reads stay as decided here
- Date: 2026-11-03
- Roadmap step: 9.1 (decided and built); 9.2 to 13.3 replace the screens that stand in; 12.5 adds the mirror's check; 13.4 checks that nothing is left

## Context

Step 9.1 builds the Forms section around the structure of ADR-0021. A few things the design left open came up as it was built:

- The step says that items whose screens come in later phases appear with those phases. Its own check says that each role sees exactly
  its menus, and that a privacy officer sees People and Retention without Data collection, which come in Phase 13.
- Each health check has two readers: Umbraco's Health Check dashboard, and the Overview's Health line for any user of the section.
- The Overview's checklist has to know, on the server, when a step is done, and it should not come back when the last form is deleted.
- Recent entries reads across forms, which no index supports: `IX_jfbEntry_formId_submittedUtc` starts at the form.

## Options

For the sidebar:

1. Add each item with its screen, and test the roles only for the items that exist. It leaves the check of Phase 9 unable to say what it
   promises, and a role's menu changes with every phase.
2. Register the whole sidebar now. An item whose screen is still to come opens one sentence in its place, and each screen's step replaces it.
   The table of items and permissions is whole and tested from the start.

For health:

1. One Umbraco health check each, with its own code, and something else for the Overview. They would drift.
2. One probe per subject, which says what is wrong in words, and two thin readers.

## Decision

- **The whole sidebar from 9.1 (option 2).** `section/sidebar.ts` holds every group and item of `docs/ux/information-architecture.md` with
  the permission that shows it, and every item has a workspace. `coming-screen.element` stands in for each screen that a later step builds;
  no release ships with one, which 13.4 checks. A role's menus are real from the first day, and the check the step asks for runs in full.
- **`GET me`** says what a user holds: `global`, and `anywhere`, also on one folder or form. The sidebar's items name which they need, and a
  condition reads that once per page load. The Inbox needs `viewEntries` anywhere; a setting needs its permission globally, since settings are
  granted globally only. It only spares a user the menus that would refuse them: each endpoint still checks for itself.
- **Health:** a `HealthProbe` per subject answers with findings: a stable code and a sentence in the user's language. `GET setup/health` reads them
  all (the answer stands for a minute per server, as the start page opens often and the probes read the database and the registered servers; a user who may not read site names hears of "a site"), a probe that fails counts as a finding of its own, and one `[HealthCheck]` class per probe shows the same on Umbraco's dashboard. Words are
  translated on the server, because that dashboard shows them as written. A finding names the screen that fixes it, and the Overview links to it
  for a user who may open it, and otherwise says who can.
- **The jobs' health** needs a time the database holds, as any server may ask and one server runs them: `JobHeartbeat` writes `jfb.jobs.last-run` at
  most every five minutes from the outbox's job, and the check speaks after 15 minutes without a run. Until a first run, a server's own start counts.
- **The Data Protection key ring** has no public way to ask where it lives. A store the site set counts, and so does Azure App Service, whose instances share the
  folder. Otherwise ASP.NET Core keeps it in the account's profile, so the check says so when no such folder exists, in a container, whose disk goes with it, and
  when the site runs on more than one server. It can't see the registry on IIS, so a site there hears only about the second.
- **The "used on" index is never a warning.** A rebuild delays what the Info view shows and repairs itself, so the Overview leaves it out, and Umbraco's
  dashboard says so while one is under way, and carries the action otherwise.
- **The mirror's check** comes with the mirror (12.5, which already says to warn in the health check); there is nothing to look at before.
- **The checklist's detectors look at the site, not the user:**
  - email: Umbraco's gate passes and a From address is set, by the rule mail itself follows, as the health check says; while it is open, the step says what the health check found, and Health leaves it out;
  - form: a form exists outside the trash;
  - test entry: any form ever received an entry (`lastEntryNumber`), so deleting a test entry leaves the step done;
  - form block: the element type exists, or a page uses a form through a site's own type;
  - access: a group other than the administrators that has the Forms section holds `viewEntries` anywhere.

  Once all five were done, `jfb.setup.complete` says so, and the checklist stays away whatever happens to a form later. "Show the setup checklist" shows
  it again with the steps as they are.
- **Recent entries** are what has arrived: `Submitted`, `Approved` and `Rejected`, no tests, no form in the trash, through the access filter of ADR-0048.
  An index on `submittedUtc` (migration step 9) means the list reads ten rows and sorts nothing; 11.3's Inbox reads the same way.
- **While the migrations are pending**, every endpoint answers `503`, so the Overview shows one sentence that says so and why, from its own words, for that code only: a gateway's 503 is a failure like any other. The health
  check says the same from the server, since Umbraco's dashboard still works.
- **`confirmText`** is the only way to Umbraco's confirm modal, with its source test, and the section's first caller is "Trash" on a form in the tree.
  9.2 builds the rest of the tree's actions on it.

## Consequences

- The sidebar shows items that lead to one sentence until Phase 13. That is true only of a build in progress: no package is published before 13.4 has found none left.
- Each later screen's step replaces a `comingScreen` line in `section/manifests.ts`, and nothing else in the shell.
- Until 9.2, "Choose a template…" opens the Templates item; 9.2 points it at the gallery.
- A health check that needs a new subject adds a probe and a `[HealthCheck]` class; `SetupApiTests` lists the checks Umbraco finds, so neither can be forgotten.
- Revisit the key ring's check if ASP.NET Core offers a way to ask where its keys live.
