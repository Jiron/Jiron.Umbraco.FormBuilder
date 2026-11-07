# 0212. Answer summaries

- Status: accepted
- Date: 2026-11-07
- Roadmap step: 11.4, its part 11.5b (ADR-0197); builds on [ADR-0211](0211-insights.md) (the Insights view, its date
  range and its charts), [ADR-0207](0207-the-entries-list.md) (the list's filters and its query),
  [ADR-0070](0070-storing-answers-and-reading-them-back.md) (how answers are stored), [ADR-0066](0066-answers-as-merge-tags.md)
  (how a field's type shows a value) and [ADR-0009](0009-privacy-defaults.md) (sensitive data); IA 5.5, copy.md

## Context

Step 11.5b shows what people answered, per field, in the form's Insights view: counts and shares per option, the average
position of a ranking's options, average, median, lowest and highest for numbers, the three groups and the
recommendation score of a 0-to-10 scale, and free-text answers listed, never summarised. It is computed in the database,
over the date range and the entries list's filters, never for test entries, entries in Spam, sensitive fields or values
kept only until the actions run; values of older versions count under their field, marked where the options changed; and
100,000 entries load in under a second on SQLite. The owner answers no questions during the run (CLAUDE.md rule 8), so
each choice below is this record's.

## Decisions

1. **Where.** The Insights view's toolbar gets the segmented switch Overview · Answers (IA 5.5), at its end in both, so
   it never moves. In Answers the date range applies, by when entries were received in UTC days as the Overview counts
   them; Compare and Group by, which only the Overview's numbers have, are not shown, and Compare's place holds the
   Filter button and the switch of tables (7). The Filter takes the entries list's parts (the states, the actions and a
   field's test) with the list's own dialog and chips, in the list's words; Received is not among them, as the range
   says it, nor the search, which would read the texts the Answers list rather than summarise them, nor the list's
   saved filter sets, which can hold either. The filter is the view's, for this visit, and so is the switch between
   Overview and Answers; a range chosen in Answers reads the Overview's report once the Overview shows again.
2. **Which entries.** Those the entries list would hold with that filter in that range: never a draft, a test entry or
   one in Spam; by default every other state, as the list's default is.
3. **Which fields.** Every field a published version had that holds answers, in the live version's order and those it
   no longer has after them, as the list offers them. Left out: a sensitive field or one in a sensitive group, and a
   field the live version keeps only until the actions run, or one in a repeatable group kept so, whose values exist
   only for a while.
4. **What each shows,** by what the field's type says its answers are (its rule kind), so a site's own types summarise as
   the built-in ones do:
   - **Options** (one choice or several): each option's count and share, the live version's options first in their
     order, those nobody chose with 0, then values no live option has, marked "no longer an option". Shares are of the
     entries that answered (of the rows, for a field in a repeatable group), so several choices add up past 100%, which
     the box says. Where any version offered "Other", every value no version's own options and no list or source offer
     now is a visitor's own words, and they are one line at the end, never counted apart; a list or source that cannot
     give its options, or whose options hang on a parent's answer, leaves every value an option, as nothing tells them
     apart.
   - **A ranking:** each option's average position (1 is first) and count, best first.
   - **A grid:** each row's count and share per column, the live version's rows and columns first and then those only
     an older version asked, marked; the grid's answers are the entries (or rows) that answered any of its rows, each
     once.
   - **A number** (a number, a rating, a scale, a slider, a calculation): count, average, median, lowest and highest; a
     scale from 0 to 10 adds its three groups (0 to 6, 7 and 8, 9 and 10) and the recommendation score, the share of the
     top group minus that of the bottom one, from -100 to 100, where every version asked from 0 to 10, so a 5 of five
     never reads as a low ten.
   - **A checkbox:** how many ticked it, of the entries: an unticked box stores nothing, so its answers are its ticks,
     which its box says once, as Ticked and Not ticked; in a repeatable group, whose rows are not counted, the number of
     rows that ticked it.
   - **Free text** (a line or a paragraph of text): the newest answers, five at a time with Show more, each linking to
     its entry; never counted or summarised. An email, a phone number, a web address and a hidden field are not free
     text to read through, and say how often they were answered, as dates, times, files, signatures, names and
     addresses do.
5. **In the database.** One grouped statement per kind across all its fields, over the values of the matching entries
   (`entryId IN` the list's own query); an answer is counted by its first value (`valueIndex` 0), so an entry, or a row
   of a repeatable group, counts once without `DISTINCT`, and an option once per entry or row that chose it; only a
   grid, whose answer spreads over the fields of its rows, counts its entries or rows with `DISTINCT`. A median
   is two ordered reads of the field's number index; the newest texts walk the entries by submission. All statements
   run in one read within the query budget ([ADR-0039](0039-entries-query-builder.md)).
6. **Labels** are what the field's type shows for the value (ADR-0066): an option's label, from a list or a source what
   it resolves, a country's name, yes or no, in the form's default language as the entries list's columns are. A value
   no live option has is labelled as the newest published version that offered it labelled it, and otherwise shown as
   stored.
7. **Tables.** Every answer's chart is bars, so one choice serves them all: a single "Show as tables" in the Answers
   toolbar, kept per user as the box `answers` of `insights.tables`, where the wireframe drew a button in each box; one
   control says it once.
8. **Endpoints.** `POST form/{key}/insights/answers` with the span and the filter answers every field's summary and the
   number of matching entries; `POST form/{key}/insights/answers/{field}/texts` with the same and a cursor answers the
   next free-text answers. Both need `viewEntries` on the form; a sensitive field or one kept only until the actions
   run is refused as not found.
9. **Insights off.** The switches of Privacy › Data collection and of the form turn off the counting of what visitors do;
   the Answers come from the entries, which the Entries view shows anyway, so they are there while the Overview says
   where to turn the counting on.
10. **Shares and bars.** An option's share is of the answers (of the rows, in a repeatable group) and its bar is that
    share of the box's width, not of the largest option, so 42% looks like 42%; a ranking's bar is how near the top an
    option stands on average, the first place a whole bar, which a note under it says. Shares are whole percents, but
    one above none and below 1% keeps a decimal, so it never reads as none. The fields nobody answered in the range
    share one line under the boxes instead of a box each.

## Consequences

- Positive: no entry is loaded to summarise them; the summaries are as fast as the indexes ADR-0003 measured.
- Positive: the same filter reads the same entries in the list, the export and the summaries.
- Negative: an option list whose labels hang on another field's answer shows its stored values, as no single parent
  answer applies to a whole summary.
