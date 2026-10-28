# 0083. Merge tags and the two runtimes after the Phase 5 review

- Status: accepted; amends [ADR-0065](0065-the-merge-tag-service-and-its-sources.md) (when a site source's traits are
  read), [ADR-0066](0066-answers-as-merge-tags.md) and [ADR-0068](0068-answer-recall.md) (the `field` source without
  an alias, and how the website reads a value with parts), [ADR-0077](0077-answers-and-labels-from-resolved-options.md)
  (when the labels of resolved options are read) and [ADR-0072](0072-a-submissions-answers.md) (the scope a
  submission's hidden fields render over)
- Date: 2026-10-28
- Roadmap step: 5.5h (decided and built); 5.5i (the second review's fixes: parent answers held in merge tags,
  options kept per parent answers, traits read once, a raw lone surrogate)

## Context

The Phase 5 review read the merge tags in both runtimes once more and found five things:

1. `{{ field }}`, the `field` source with no alias after it, threw in a visitor text instead of rendering empty with a
   warning, as every other unknown path does.
2. A site source's `IsPerVisitor` and `Places` were read each time a place was checked, and its `Name` in every loop.
   A source that computes them could answer differently from one call to the next, and one that throws stopped a
   rendering instead of the start.
3. The website's reader of a value with parts (a name's, an address's, a ranking's) used `JSON.parse`, which keeps
   the last of two members with the same name and puts names that look like numbers first. Core's `FieldPartsJson`
   reads the members in the order the text writes them and refuses a name written twice, an empty name or text and a
   lone surrogate, so a value a client stored by hand showed differently on the page and in an email.
4. The labels of resolved options were read for every answered field whenever a tag needed any text of the scope,
   even a tag that shows a value or a field's label; and a submission rendered each hidden field's default over a
   scope of its own, so a source was asked for its labels once per hidden field.
5. Option labels, month and weekday names, country names and the order of labels come from each server's culture
   data; nothing said that every server of a site needs the same.

## Decision

- **The field source alone** is an unknown path: `{{ field }}` renders empty with an `UnknownPath` warning in every
  place, and the publish check reports it at its position like any unknown path. The website renders it the same, as a
  shared vector shows.
- **A site source's traits are read once.** The registry reads each site source's `Name`, `IsPerVisitor` and `Places`
  when it is built, copies `Places` to a set of its own, and every check reads that copy. The start-up check reads them
  inside a guard: a source whose traits throw, or a filter whose `Name` throws, stops the start with a message naming
  its type and the exception's type, and a registry built anyway leaves it out.
- **The website reads parts as Core does.** `valueParts` lets `JSON.parse` check the text, then reads the members from
  the text in their written order, with JSON's whitespace and escapes: no parts (so the text shows as it is) for a name
  written twice, an empty name or text, a text that is no string, an empty object, an array, or a name or text with a
  lone surrogate after its escapes are decoded. Shared vectors hold each case.
- **Labels are read when a tag shows them.** The scope keeps the dictionary's texts, read once per scope, and the
  options each list or source resolved, read the first time a tag shows a field's answers (`field.<alias>`, a
  repeatable group's rows, `entry.answers`) and kept for the scope's other tags under the field and the parent answers
  they were asked for. A tag that shows a value or a label asks no source.
- **Parent answers are held, in merge tags too** (5.5i, after the second review). A cascading field's options are
  asked for the parent's answers that are options the parent offers (its own, or those its list or source resolves,
  in turn), as a submission's check holds them (ADR-0081); with the parent's options unknown, the field shows its
  values as they are. A hidden field's default renders before the answers are checked, and without this a visitor's
  text would reach a source and become a cache key there.
- **A submission's hidden fields render over one scope.** The reader makes the scope at the first hidden field with a
  default and gives it the answers again after each value it resolves, so a later default still shows an earlier
  one's value, and the rows of a repeatable group are counted as before. What the scope keeps stays true: its options
  are kept for the parent answers they were read for, so when a site's type that keeps its value on the server and
  has options is a parent and resolves after them, the next tag reads the options for its value (5.5i; this step first
  held that a resolved value is never a parent's, which is so for the built-in types only). Only the answers the
  reader makes to be replaced change, between two renderings and never during one, as `MergeTagAnswers` says; every
  other answers, those every scope without any shares among them, refuse it, so no answers kept in a static field can
  change.
- **Culture data is the same on every server.** The texts a source of options gives (months, weekdays, countries),
  the order of labels in a culture, and the date and number patterns of the public projection come from the culture
  data of the server that answers, which each server keeps. A site's servers run the same .NET version over the same
  ICU version (on Windows the one the operating system ships, on Linux the `libicu` package); the start-up check
  (ADR-0042) checks that ICU is there, not that it is the same everywhere.

## Consequences

- No visitor text can stop a rendering by naming the `field` source alone.
- A site source cannot change where it is offered after start-up; one whose traits fail is found before the site
  takes a request.
- A value with parts shows the same in both runtimes, whatever a client stored.
- A rendering asks each source at most once for each field whose answers it shows, and a submission at most once for
  all of its hidden fields together; a template of values and labels asks none.
- Operating several servers needs the same runtime and culture data on each, written in the architecture's settings.
