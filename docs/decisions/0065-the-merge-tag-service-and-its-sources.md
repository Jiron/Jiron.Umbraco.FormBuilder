# 0065. The merge-tag service and its sources

- Status: accepted; amends [ADR-0014](0014-merge-tag-language.md) (a site's source declares the places it may be used
  in rather than contexts, and a site's filter is server-only) and [ADR-0064](0064-the-merge-tag-engine.md) (the
  problems a place adds, and the form check's messages move to the form check); the scope's answers, the `field`
  source and `entry.answers` added by [ADR-0066](0066-answers-as-merge-tags.md) in 5.3d; the scope's page and member
  and the Umbraco sources added by [ADR-0067](0067-the-umbraco-sources-and-the-base-url.md) in 5.3e
- Date: 2026-10-26
- Roadmap step: 5.3c (decided and built); 5.3d adds the answers (`field.*`, `entry.answers`), 5.3e the Umbraco
  sources (`page`, `member`, `site`, `dictionary`, `entry.url`), 5.3f answer recall and the copied-actions report

## Context

Step 5.3b built the merge-tag language in both runtimes over values given as data (ADR-0064). The values have to come
from somewhere: the form, the entry, the page, the visitor, the developer, the site's own systems. §4.9 promises sites
their own sources and filters through `IFormBuilderMergeTagSource` and `IFormBuilderMergeTagFilter`, and ADR-0014's
table says which sources each place may name. Step 5.3 turned out larger than one step and became four (WORKLOG
2026-10-26); 5.3c builds the service, the extension points, the sources that need nothing from Umbraco's content, and
the culture formats from .NET. Five questions were open:

1. how a site adds a source or a filter, and what happens to one whose name is taken;
2. how a place's rules (ADR-0014's table) reach the engine, for built-in sources and a site's alike;
3. what a source or filter that fails does to the mail or page it is part of, and what the log may hold;
4. how .NET's culture data becomes the formats ADR-0064 describes, including the `medium` date that .NET lacks;
5. which time zone an entry's date and reference year are in.

## Options

1. **Registration.**
   - The service collection (`services.AddSingleton<IFormBuilderMergeTagSource, …>()`): no order the picker could
     use, and a site cannot tell which of its registrations won.
   - Umbraco's ordered collection builders behind `builder.JironFormBuilder()`, as the field types use (ADR-0055):
     ordered, open to every composer before and after the core one, and created by the container. Chosen.
   - Letting a site replace a built-in source: tempting for `member`, but every place rule, the cache rules and the
     picker trust the built-in sources' meaning. Not offered; a site names its own source instead.
2. **Place rules.**
   - Each source checks the place itself: every site's source would repeat the cache and consent rules, and one that
     forgets them would leak per-visitor data into cached pages.
   - The service checks them from the source's traits (per visitor or not, its places) and a table per place for the
     built-in sources, and tells the engine which paths and filters to refuse. Chosen.
3. **Failures.**
   - Let an exception through: one broken CRM call would stop a confirmation mail or a page.
   - Render the tag empty and log it. Chosen; the log keeps the exception's type and stack, never its message, which
     may quote a visitor's answer (D13).
4. **Culture formats.**
   - The browser's `Intl` and .NET each formatting for themselves: different data, different text (ADR-0014).
   - The server writes .NET's patterns into ADR-0064's subset, and both runtimes format with them. Chosen; a test
     compares the result with .NET's own output for every culture .NET knows.
5. **Time zone.**
   - UTC: a reference written on New Year's night in Zurich would carry the old year.
   - The site's time zone from the general settings (ADR-0043), UTC when the server cannot resolve it. Chosen.

## Decision

### The service

`IFormBuilderMergeTagService` (namespace `Jiron.Umbraco.FormBuilder.Core.MergeTags`) has three operations:

- **`RenderAsync(text, place, context, scope)`** parses the template with the place's rules, asks each source once
  per distinct path, and renders with the culture's formats. The scope (`MergeTagScope`) carries the form's definition,
  name and alias, the culture name, the site's root key, the entry once there is one, and the query and context
  values.
- **`Check(text, place, context, form)`** gives a template's problems before any value: the language's, those of the
  place, and an `unknown_path` warning for a path no source knows. A built-in source whose paths are all known in
  advance (`form`, `entry`, `query`, `date`) warns of any other path; `context` and a site's source know any path, so
  only their name is checked.
- **`Paths(place, form)`** gives the picker's paths, of the sources the place takes, the built-in ones first.

A context the place does not render in is a programming error and throws `ArgumentException`; nothing else in a
template throws. The form check (10.x) turns the problem codes into messages, as it does for formulas and rules; the
service gives codes and positions only.

### Places

`MergeTagPlace` names where a template is used, following ADR-0014's table:

| Place | Contexts | Built-in sources | A site's source | Per visitor | A site's filter |
| --- | --- | --- | --- | --- | --- |
| `VisitorText` | `html`, `text` | `field`, `form`, `page`, `site`, `date`, `dictionary` | yes | no | no |
| `ConsentText` | `html` | `form`, `page`, `site`, `dictionary` | no | no | no |
| `DefaultValue` | `text` | `form`, `page`, `site`, `date`, `dictionary`, `query` | yes | no | no |
| `HiddenValue` | `text` | all | yes | yes | yes |
| `Outcome` | `html` | all | yes | yes | yes |
| `Redirect` | `url` | all | yes | yes | yes |
| `EntryTitle` | `text` | `field`, `entry`, `form` | no | no | yes |
| `Mail` | `header`, `html`, `text` | all | yes | yes | yes |
| `Action` | every context | all | yes | yes | yes |

- A built-in source a place's list lacks, and a site's source the place does not take, are `source_not_for_place`
  errors at the path: the tag renders as written, so the form check reads it.
- A site's source is taken where the place takes a site's sources, its values are the same for every visitor or the
  place takes per-visitor values, and its `Places` (null for all) includes the place.
- A site's filter runs only on the server, so the places the browser renders too refuse it with
  `filter_not_for_place` at the filter. Arguments its `Accepts` refuses are `invalid_argument` at the filter.
- A built-in name this version has no source for yet (`page`, `member`, `site`, `dictionary` until 5.3e; `field`
  until 5.3d) is not refused by a place that lists it; nothing knows its paths, so it renders empty with `unknown_path`.
  Since 5.3e (ADR-0067) every built-in name has its source.

`source_not_for_place` and `filter_not_for_place` are problems of the server's service only; the browser's runtime
receives templates that the server has checked, so its engine needs no place rules.

### Sources and filters of a site

- A composer adds them with `builder.JironFormBuilder().MergeTagSources().Append<T>()` and
  `.MergeTagFilters().Append<T>()`, in any order relative to the core composer. Each is a singleton the container
  creates, so it may take singleton services.
- A name is a lowercase ASCII letter, then ASCII letters and digits. The ten built-in source names (`field`, `entry`,
  `form`, `page`, `member`, `query`, `context`, `site`, `date`, `dictionary`) and the eleven built-in filter names are
  reserved, also for the sources later steps add.
- A source gives the value of the path after its name (empty for the name alone) as a `MergeTagValue`: items of text,
  optionally with the wire number or date behind each (ADR-0064), or null for a path it does not know. A filter
  checks its arguments (`Accepts`) and maps a value (`Apply`), and never encodes: the context escapes what it gives.
- Start-up refuses (ADR-0042) a name outside the grammar, a built-in name, and a name two of a site's sources or two
  of its filters share, each problem naming the type; a source or filter the container cannot create is reported
  with the container's message. The registry leaves such a one out, so the built-in one stays.
- A source or filter that throws gives the empty text (an `Accepts` that throws counts as arguments that do not fit),
  and the service logs the source's or filter's name, the exception's type and its stack trace, at error level. A
  cancelled rendering cancels; a source's own timeout is a failure like any other.

### The built-in sources of this step

- **`form.name`, `form.alias`.**
- **`entry.number`** (with the number behind its text), **`entry.reference`**, **`entry.date`** (in the culture's
  `dateTime` format, with the wire date-time behind it) and **`entry.state`**; all empty before the visitor submits.
- **`query.<name>`** only for a name in the form's `developer.queryValues`, spelt exactly; several values are a list,
  none is empty. Any other name is unknown, whatever the address holds.
- **`context.<key>`**: a value the developer passed; a key the form lists in `developer.contextValues` is known without
  a value (empty), any other only when passed.
- **`date.now`**: the clock's time in the site's time zone, in the culture's `dateTime` format.

`entry.reference` is the form's prefix, then the year and a dash unless the format leaves the year out, then the
number padded with zeros to the format's digits (clamped to 1–10, five by default): `REQ-2026-00042`. The year and
`entry.date` are in the site's time zone (`general.timeZone`, ADR-0043); a zone the server cannot resolve counts as
UTC, as `today` does (ADR-0030).

### Culture formats from .NET

`MergeTagCultures` writes each culture's formats once from .NET's data and keeps up to 256 cultures; only cultures
.NET has data for count, and any other name formats as the invariant culture.

- **Calendar:** wire dates are Gregorian, so a culture whose default calendar is another (Thai, Arabic in Saudi
  Arabia) uses its Gregorian calendar's patterns and names, or the invariant culture's when it has none.
- **Patterns:** `short`, `long` and `time` are .NET's short date, long date and short time; `dateTime` is short date,
  a space and short time, .NET's `g`. `medium` is the long date without the day's name and the separators that tie it
  to the rest, with the month's short name: `dddd, d. MMMM yyyy` becomes `d. MMM yyyy`, `dddd, MMMM d, yyyy` becomes
  `MMM d, yyyy`.
- **Writing:** `/` and `:` become the culture's separators; quoted and escaped text becomes literal text, and every
  literal character that the subset or .NET would read is escaped with `\`, so a written pattern prints the same in
  .NET as in both runtimes.
- **Outside the subset:** a pattern with fractions of a second, an era or an offset (`f`, `F`, `g`, `K`, `z`) takes the
  invariant culture's pattern for that format. On .NET 10 with this machine's ICU, 14 cultures have one: in the
  short and long dates (and so `dateTime`) of `ar-SA`, `ckb-IR`, `lrc`, `lrc-IR`, `mzn`, `mzn-IR`, `ps`, `ps-AF`,
  `uz-Arab` and `uz-Arab-AF`, and in the long date of `lo`, `lo-LA`, `th` and `th-TH`. The test lists them from the
  data rather than from a fixed list, since CI's ICU differs.
- **Numbers and amounts:** .NET's separators, group sizes and its tables of negative number and positive and negative
  currency patterns; the currency is the region's ISO 4217 code, none for a culture without a region.

## Consequences

- A site extends merge tags the way it extends field types, and the cache and consent rules hold for its sources
  without the site knowing them.
- A broken source or filter costs a value, never a mail or a page, and its log entry holds no answer.
- The patterns the browser receives are .NET's, so both runtimes print what .NET prints; the test shows it for every
  culture on the machine that runs it.
- The form check, not the service, owns the messages of problem codes (ADR-0064's consequence said 5.3c would).
- **Revisit** if a site needs its own filter in visitor texts (it would need a TypeScript twin that passes the
  vectors), wants to replace a built-in source, or if editors in the cultures whose patterns leave the subset need
  the parts it lacks.
