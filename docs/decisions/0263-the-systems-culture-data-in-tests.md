# 0263. The system's culture data in tests: the fixtures render with data of their own

- Status: accepted; adds to [ADR-0064](0064-the-merge-tag-engine.md) (culture formats written as data for the shared
  vectors) and [ADR-0065](0065-the-merge-tag-service-and-its-sources.md) (the formats from .NET's culture data), and
  to [ADR-0128](0128-the-element-writes-the-servers-view-model.md) (the rendering fixtures)
- Date: 2026-11-23
- Roadmap step: 26.1

## Context

The product reads the operating system's culture data: the date and time patterns, the month and day names and the
number separators that `DotNetCultureFormats` writes for both runtimes, the countries' names (`RegionInfo`), and the
order of names in a culture (`StringComparer.Create`). .NET takes them from ICU, and Windows and Linux carry different
ICU versions, so the same server writes them differently on each, and again after either system's updates. That is the
platform's and stays: a site shows what its server's system says.

Until the first run on GitHub every test ran on one Windows machine. On Linux, four rendering fixtures and three unit
tests failed on exactly these values:

- the American time pattern: Windows 11 writes `h:mm tt` with an ordinary space, Linux with a narrow no-break space
  (U+202F) in its place, so the formats in the model data and the date in the notice of a form that opens later
  differ;
- the countries: Linux names `EH` "Western Sahara" where Windows has no name and shows the code, says "Palestinian
  Territories" for "Palestinian Authority", "Cape Verde" for "Cabo Verde" and more, so the names, and with them their
  order, differ in English and German;
- the British long date: `dddd, d MMMM yyyy` on Windows, `dddd d MMMM yyyy` on Linux.

The fixtures (`tests/fixtures/rendering/*.json`) are one file that the server's tests write and the client's tests read
as the server's rendering, so they must be the same file on both systems and still be what the server renders.

## Options

1. **Record the fixtures per system.** Two files, each true on its system; the client's tests would read one of them,
   and a change on one system would go unseen on the other. No.
2. **Compare through a normaliser** that replaces the system's values (the time patterns, the country options) with
   placeholders before comparing. The file would no longer be what the server renders, and the client's tests, which
   compare the element's markup with the server's, would read placeholders.
3. **Render the fixtures with culture data the test host fixes**, through the seam the product already has for the
   shared vectors: `MergeTagCultures`, the server's one source of the formats per culture, which takes formats given as
   data (ADR-0064). Extend it, at no cost to production, to the rest of the culture data the rendering reads.

## Decision

Option 3. `MergeTagCultures` is the culture data the server prints with: the formats, as before; the countries' names
and their order (`CountryName`, `CountryOrder`), which the countries option list, the country and address fields'
select, the recall labels and `FieldDisplay.CountryName` now ask it for; and the culture the server formats its own
texts with (`Info`: the date in a notice, a count, a file's size). In production it reads .NET's data exactly as
before; the order of country names is the culture's own, letter case aside, in both places that sort them (one of them
sorted case sensitively before, which no country's name tells apart).

Given data replaces it for the cultures and languages it names. The four fixture tests register
`tests/fixtures/rendering/culture-data.json` (`FixedCultureData`): the formats of `en-US` and `de-DE` and the
countries' names in English and German, in their order, recorded from the existing fixtures, so the fixtures did not
change. A test fails when the product offers a country the data does not name.

Unit tests of formatting state what the product does with the data it is given: they build the expectation from the
same `CultureInfo`, or give the code a culture they made (a cloned culture with its patterns set, or `CultureFormats`
written in the test), and test each form both systems write, such as the time with either space.

## What still comes from the system in the fixtures

- The views' current culture, set from .NET for the element endpoint: it writes only whole numbers there, which no
  culture of the fixtures writes with separators.
- The numbers a post without JavaScript reads with a comma in cultures whose decimal separator is one
  (`FormPostReader`): `,` for German and `.` for English in every ICU.
- The order of an option list's own labels (content, data types), which the fixtures do not sort.

Each of these is the same in every ICU version for `en-US` and `de-DE`; a fixture that came to depend on one of them
would fail on one system first and is then given the data here.

## What keeps a real change visible

- The rendering itself is the product's: the fixed data goes through the same code a site's data goes through, so a
  change to how the server renders, writes or orders anything shows in the fixtures on every system.
- `DotNetCultureFormatsTests` holds the formats .NET's data gives to what .NET prints for every culture the system has,
  on each system, so a pattern the formatter cannot print is found wherever it appears.
- The browser takes either space in the time pattern as a literal: both runtimes copy any character that is no pattern
  letter, and a visitor never types a time in a culture's pattern (a time field is the browser's own time input, which
  sends the wire form), so the server's system cannot break what a visitor enters. Tests of both forms hold it in C#
  and TypeScript.

## Consequences

- The rendering fixtures are one file on every system, and stay so when either system's ICU changes.
- A new culture or language in a fixture needs its data in `culture-data.json`; a country added to ISO 3166 needs its
  name in each language there, which the test that loads the data says.
- `MergeTagCultures` keeps its name (it began with the merge tags' formats); its summary says what it now holds.
