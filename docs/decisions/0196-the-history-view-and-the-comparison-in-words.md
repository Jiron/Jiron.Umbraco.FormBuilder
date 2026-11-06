# 0196. The History view, and the comparison in words

- Status: accepted
- Date: 2026-11-06
- Roadmap step: 10.3a; builds on [ADR-0045](0045-versions-comparison-and-draft-cleanup.md) (the versions, the
  comparison's shape) and [ADR-0052](0052-version-endpoints.md) (the endpoints); IA 5.6, copy.md, the History
  wireframe

## Context

The server has kept every save and publish of a form since 4.5b, with its comment and pin, and compares two versions
element by element: each change names the element, what happened to it, whether it moved, and every property that
differs by a path and both values as JSON (ADR-0045). The History view has been a "coming" screen since 9.3a. IA 5.6
gives it a list of the published versions with "Show draft saves", each row saying when, who and what changed; a
row's "…" with Pin and Add comment…; Compare for two ticked versions; and a clicked row's version compared with the
current draft, in plain language: fields added, removed and changed, then settings, actions and rules. Restoring is
10.3b's.

Open questions: where a row's "who" comes from, as the list carries user keys only; how a comment is written; what
ticking a third version does; how the comparison is grouped and worded, as the server deliberately sends paths and
JSON and never sentences; and how far the words reach, as a type's settings are the type's own.

The design skills shaped it before it was built (ADR-0019, ADR-0020): Operate mode inside Umbraco's design system,
for a designer who wants to know who changed what in a form and when, and what a version held, without reading JSON.
`impeccable` asks its discovery questions of the user; the owner answers none during the run (CLAUDE.md rule 8), so
IA 5.6, copy.md and the wireframe answer them, and the rest is decided here.

## Options

**Who made a version**

1. The backoffice reads Umbraco's user items for the keys: a designer may not see the Users section, and it is one
   more request per page.
2. **The list answers each version's maker by name,** as the library's list does (ADR-0166).

**A comment**

1. In a small sidebar, as an action is edited.
2. **In Umbraco's dialog,** as the publish dialog takes its comment: a comment is one short text.

**A third tick**

1. It replaces the first: the list changes what the user ticked without being asked.
2. **The other boxes rest while two are ticked;** unticking one frees them.

**The comparison's words**

1. Show each path and both values as they are: "settings › maxLength: 100 → 200".
2. **A vocabulary for every property of schema v1** (`schemas/jfb-form.v1.schema.json`), a type's settings named by
   the labels its type serves, values said as the backoffice says them elsewhere, and a rule as the rules builder
   says it.

## Decision

Option 2 each time.

- **The version list** answers `by`, the name of who made each version: for a publish who published it, for a draft
  save who saved it; null when Jiron Form Builder made it or the user no longer exists. The names are read once per
  answer, as the library's list reads them (`UserNames`).
- **The view** (`jfb-form-history-view`, the workspace's History view for who may design the form):
  - a toolbar with **Compare**, shown while two versions are ticked, and the **Show draft saves** switch, which
    adds the draft saves, muted, and the current draft first, as "Current draft";
  - a table in Umbraco's box: a box to tick, the version (its number, the "Published" chip on the version visitors
    get, a pin mark, and when and by whom), what changed against the one before ("4 fields, After sending, 1 rule",
    "First published version" for the first publish, "First version" for the first save), the comment ("none"
    without one, under the changes where the view is narrow), and the row's "…": Pin or Unpin, Add comment… or
    Change comment…;
  - twenty versions a page with Show more, a skeleton while it reads, an error with Try again, and, while nothing
    is published, a sentence saying that publishing keeps the first version here, and that Show draft saves lists
    the saves until then; a page that Show more can't read is said beside it, and Show more tries again;
  - its own words load with it; until they are there a skeleton names nothing, and where they can't be loaded the
    backoffice's start words say so, with Try again;
  - a draft save's controls and what is said of it name it by its moment ("Select Draft save, Today, 09:12"), as
    there may be many; a version's name says that it opens the comparison with the current draft.
- **A pin** acts at once: the mark shows and the view's live region says so; a refused one leaves the row as it was,
  and the notification says why. **A comment** is written in Umbraco's dialog, at most 500 characters, Save and
  Cancel; saving it empty clears it, and the box's own name holds the help. Neither changes the form, so neither
  makes anyone's save stale (ADR-0045).
- **Reading again:** a save moves the draft to a new version, and a publish adds a new live version and leaves the
  draft where it was; the view watches both numbers, so the list reads again after either. Another form starts the
  view afresh.
- **Ticking:** at most two versions; while two are ticked the other boxes rest. **Compare** opens a medium sidebar,
  "Version 12 and version 14", the older first. **The version's name** opens the same sidebar for that version and
  the current draft, "Version 14 and the current draft", with when and by whom; 10.3b puts its restore buttons there.
  The current draft's own row has no sidebar: there is nothing to compare it with.
- **The comparison,** in four parts, each only when it has something:
  - **Fields:** pages, groups and fields added, removed and moved, each named by its title or label, then a table of
    what changed (the element and the property, the older value, the newer value);
  - **Settings:** the settings groups by their names in the Settings view, and After sending's outcomes, each by its
    place ("After sending, Outcome 2"), a redirect's page by its name in the site;
  - **Actions:** each action by its name;
  - **Rules:** rules added, removed and changed, each said as the rules builder says it ("Order number is required
    when Topic is Delivery"), and form-level validation rules.

  Without differences it says "No differences.". A version that cannot be read says so, with what the server says
  of it.
- **The words** (`history/change-words.ts`, pure, English and German):
  - every property of schema v1 has a name; a field type's or an action type's own setting is named by the label
    its type serves, and by its own name only where the type serves none;
  - a text of another language names it ("Label (German)"), an option by its label as each version gave it, the
    order of options as such (options of one value, which the server can't key, by their labels too);
  - values: Yes and No, "none" for none, a text as it is (rich text without its markup, a block's tag a space, a
    long one cut with "…"), numbers in the reader's format, a field named by its label, a list of addresses or
    names by what was added and what was removed; two values that read alike say "(changed)" after the newer;
  - no defaults are filled in: the canonical form writes every switch, number and choice (ADR-0034), so a side
    without one is a side without the object that holds it, and says "none";
  - a Vitest test reads the schema and finds a name for every property it holds.
- **Reading:** the list reads a page at a time; a sidebar reads the comparison and the two versions' definitions at
  once, so rules and the fields they name are said by their labels in each version, and asks Umbraco once for the
  names of the pages their redirects lead to.
- **The sidebar's table** keeps its table roles where a narrow sidebar stacks its rows, so a value is read with its
  version's header; the version's name before it is for sight only.

## Consequences

- A designer sees who changed what and when without the Audit log, and what any two versions differ in, in words.
- The list's answer carries one more property (`by`), in the OpenAPI snapshot.
- A new property of the definition needs a name in the vocabulary, or the schema test fails.
- 10.3b adds Restore as a new draft, Restore and publish… and the row's restore to the same sidebar and menu; 10.3c
  and 10.3d say their differences with the same words.
