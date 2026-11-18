# 0021. The Forms section's structure

- Status: accepted; how the left side shows the structure is superseded by
  [ADR-0257](0257-five-places-before-the-tree.md): the groups of items under their headings are five places before
  the forms tree, each one screen whose tabs are the items. What each group holds, its names and who sees it stay as
  decided here
- Date: 2026-10-23
- Roadmap step: 2.1 (decided); built in 9.1 (the section and its menus) and by every later step that adds a screen to it

## Context

Step 9.1 sketched the sidebar: the forms tree; Entries with "all entries" and spam; Library with option lists, themes,
email layouts and templates; and one Settings group of nine items (general, email, spam, files, privacy, access,
deliveries, audit log, privacy tools). Step 2.1 designs the section before any of it is built
(`docs/ux/information-architecture.md`).

Four things pull at that sketch:

- **§4.13 and D18:** calm screens, say it once, one vocabulary.
- **The 1.2a spike:** Umbraco's sidebar header repeats the section's label above the tree, with the root's "+" and "…"
  (`docs/research/umbraco-backoffice.md`, section 4). §4.13 allows a name in two places, never in three.
- **ADR-0013's permissions:** the privacy settings need `manageSettings`, while the privacy tools need `privacyTools`,
  a permission a privacy officer holds without `manageSettings`.
- **§4.8 has a seventh settings section, Integrations,** which the sketch leaves out.
- **Retention had three homes:** §4.8 put the spam retention under Spam protection and the other states' defaults
  under Privacy, while a form's own values sit in its settings.

The sketch also puts "privacy" and "privacy tools" next to each other, which read as one thing twice. In German both
are "Datenschutz…".

A first-time-editor check of a first draft (2.1) placed the privacy tools as views inside a Settings › Privacy item.
It found that a privacy officer had to look for a tool under "Settings", the one Settings item they could see.

## Options

1. **Keep the sketch:** one Settings group of nine items, plus Integrations as a tenth. It is the easiest to build,
   but the group is long and mixes three jobs: configuring, watching what was sent and done, and answering a person's
   request. Two privacy items would sit side by side.
2. **Group by job, and give each subject one place:**
   - **Entries** (Inbox, Spam) and **Library** as sketched;
   - **Settings** with the sections of §4.8 except privacy;
   - **Privacy** as a group of its own, holding the privacy tools and the privacy settings as separate items;
   - **Activity** with Deliveries and the Audit log.

   Each group stays short, and each subject is named once. Each item needs one permission (Retention two: one to read,
   one to change).
3. **A start page per persona** instead of shared menus. It is tailored, but it repeats places, and a user with two
   roles meets two maps of the same section. Umbraco's sections don't work that way.

## Decision

**Option 2.**

- **The forms tree sits under Umbraco's own sidebar header,** "Forms", which carries the root's "+" and "…", where
  Umbraco users look for them. The first draft left the header out; the check found that the "+" then had no place a
  newcomer would look. With the top bar, the header is the second place the section's name appears, and the Overview
  adds no title, so nothing makes a third.
- **"All entries" becomes "Inbox"** (de: Posteingang), so the item doesn't repeat its group's heading. Its badge
  carries the unseen count.
- **Settings** holds General, Email, Spam protection, Files, Access and Integrations.
- **Privacy** holds three items (de: Datenschutz with Personen, Aufbewahrung, Datenerfassung):
  - **People:** find, export or erase one person's data (`privacyTools`).
  - **Retention:** the defaults per state, Spam included, and what will be deleted soon. `privacyTools` or
    `manageSettings` reads it; changing the defaults needs `manageSettings`. This is the one home of the site's
    retention defaults; Spam protection no longer holds the spam retention.
  - **Data collection:** IP handling, sensitive-field defaults, the captcha consent mode and the insights switch
    (`manageSettings`).
- **Activity** holds Deliveries and the Audit log.
- **A group heading** appears only above items the user can see.

## Consequences

- **9.1's sidebar** is reworded to this structure (WORKLOG). Its check, that each role sees exactly its menus, stays,
  and now names the Privacy items too. The screens of 12.4a, 12.4b and 13.1 to 13.3 sit in the places named here.
- **§4.8** moves the spam retention from Spam protection to Privacy's default retention per state.
- **The permission matrix** (4.7) gains no endpoint from this: it is a matter of placing screens. 9.1's Playwright
  check covers the Privacy group for the privacy officer (`privacyTools` only) and the administrator (both).
- **Integrations gets a screen** in 12.4a, as §4.8 already requires ("a test … fails unless each has a screen").
- **Revisit** if the section gains a fourth job (for example a payments add-on, §7) or a settings group grows past
  seven items.
