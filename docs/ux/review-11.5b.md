# Step 11.5b: answer summaries

ROADMAP 11.5b, part of the cluster 11.4 (ADR-0197), shows in a form's Insights view what its entries answered
([ADR-0212](../decisions/0212-answer-summaries.md)):

- the segmented switch Overview · Answers at the toolbar's end, where it stays in both; in Answers the date range, the
  entries list's Filter with its own dialog and chips, and one switch "Show as tables" in the place of Compare, which
  with Group by is the Overview's;
- a box per field in the form's order: options as bars of their share of the answers, an older version's option marked
  and the visitors' own "Other" words as one line; a ranking by its average position; a grid row by row, its rows side
  by side; a number's average, median, lowest and highest, and for a scale from 0 to 10 its three groups and
  recommendation score; a checkbox ticked or not, of the entries; free text listed newest first, each opening its entry,
  five and then twenty more; any other field how often it was answered; the fields nobody answered in one line;
- computed in the database over the entries list's own query, never for a draft, a test entry, an entry in Spam, a
  sensitive field or one kept only until the actions run.

Per ADR-0019 and ADR-0020 the three design skills shaped it before it was built and ran again on the result; the design
comes from Phase 2 ([information-architecture.md](information-architecture.md) 5.5, the wireframe's `answers` and
`answers-table` states, and [copy.md](copy.md)). The owner answers no questions during the run (CLAUDE.md rule 8), so
every choice the skills would have asked about was decided here or in the ADR.

## What was looked at

Edge, through a scratch spec that is never committed, on a form the test host seeds with an answer of every kind and
120 entries, the first twelve sent before an option was taken away: the toolbar in both modes, the Answers whole in one
tall shot, the range menu, the filter menu and dialog, the filtered Answers with their chip, the free texts after Show
more, and every box as its table; at 1440 px light in English and dark in German, at 820 px and at 390 px. At 390 px
Umbraco's own shell is wider than the screen (it lays out at about 920 px, as earlier reviews found); inside it the boxes
go to one column and the switch to its own line. The toolbar's controls and the menu were measured, not only looked at.

The rounds of shots found these, each changed and shot again:

- **The switch stood 3 px taller than the buttons beside it** (36 px against 33): its buttons were 34 px with a border
  around them. They are 31 px now, so the switch with its border is as tall as the range and Filter buttons.
- **A grid's bars ran the width of the page,** twelve of them one under another for three rows of four columns, too
  long to compare. A grid's rows now stand side by side, each with its own bars, and go one under another where the box
  is narrow.
- **The first "tables" shots still showed bars:** the four shots ran side by side as one user, and the switch was
  pressed before the view had drawn. The scratch spec now waits for a table before its shot; the journey presses the
  switch and checks it is on.
- **The filter menu's "Fields" heading** measured 4 px left of its items' text. Their computed styles showed every item
  at 97% at rest: `pressOf`, given a list of selectors, put `:active` on the last of them only, so the others shrank
  for good. The entries list's chips and menu items and the entry panel's links had sat that way since 11.1, and the
  switch's buttons were 3% short too. A list is one `:is()` now, with a test, in its own commit (`5946a73e`).

## design-taste-frontend

Read as: Operate-mode product UI inside Umbraco's design system, for an editor who wants to know what people answered
without reading every entry. The skill's own scope ends at dashboards and data tables, so only its rules for every
interface apply, as in 11.5: dials 2 / 1 / 5, Umbraco's tokens and components, one look.

- **One look:** `uui-box` per field with its count in the header actions, Umbraco's toggle for the tables, the entries
  list's own Filter, dialog and chips (one way to filter entries anywhere), the Overview's bars and tables.
- **Every state composed:** loading as a skeleton; another range or filter loading as the answers dimmed and marked
  busy, "Loading the answers" said; a read that failed with Try again in their place; no entry in the range, said in
  words that tell whether a filter is set; the fields nobody answered as one line, not a box each; Show more that could
  not read says so beside the button it keeps.
- **Pre-flight:** no colour carries a number alone (every bar has its share and count in words); one accent; no
  gradient, no new motion but the dimming; tabular numbers in the figures and tables; the copy self-audit found no
  string that says a thing twice. The skill bans the en dash, which copy.md uses for "no value" in a table cell
  (a ranking's option nobody placed); copy.md is Phase 2's and wins (ROADMAP "How to use").

## impeccable

Operate mode, a refinement inside Umbraco's design system, shaped before it was built (`shape`, no human to ask, so its
assumptions were written down: the reader has the right to view entries, knows the form, and wants each question's
picture first and the exact numbers one switch away). The critique it guides kept:

- **One toolbar, nothing new in it but what Answers need:** Compare and Group by leave, the Filter and the switch of
  tables take Compare's place, and the segmented switch stays where it was, so nothing moves under the pointer when the
  mode changes.
- **Say it once:** the number of entries once at the top; each box's count once in its header; a checkbox's ticks in
  its bars and not again in its header; a field in a repeatable group says "one answer for each row of Guests" once,
  under its box; one switch for every table, not one per box.
- **Shares that look like what they say:** an option's bar is its share of the answers, so 42% is 42% of the box's
  width, not a share of the largest option; a ranking's bar is how near the top it stands, which a line under it says.
- **Free text read, not counted:** two lines of each answer, the entry one click away, the newest first.

## emil-design-eng

| Before | After | Why |
| --- | --- | --- |
| the segmented switch 36 px tall beside 33 px buttons | 31 px buttons in a 1 px border, 33 px in all | one toolbar line; a 3 px step reads as a mistake |
| a grid's twelve bars at full width, one under another | each row's bars in its own column, wrapping where narrow | shorter bars compare at a glance |
| a free text's underline the same at rest and on hover | the underline quiet at rest and full on hover and focus, 120 ms `ease`, hover only on devices that hover | a colour change uses `ease`; no sticky hover on touch |
| Show more leaving the focus on a button that the last page removes | the focus on the first new answer | a keyboard user reads on where the list grew |
| the menu items, chips and segments at 97% at rest, the list of selectors pressed for good | `:is(...)`: `scale(0.97)` on `:active` only, nothing under reduced motion | a press answers at once, and nothing looks pressed that is not |
| the answers swapped at once when another range came | dimmed to 0.6 opacity over 150 ms, `ease`, and marked busy | the old numbers must not pass for the new range's |
| new free texts fading in after Show more | considered and left out: they appear at once and the focus moves to them | the focus says where the list grew; motion would add nothing on a screen read often |
| bars growing from nothing when the Answers open | left as drawn, nothing moves | a view people come back to does not animate its data (as in 11.5) |

## The words

English and German for every new string, in `lang/insights.*.ts`, which load with the Insights view; the Filter, its
dialog and its chips use the entries list's own words in `lang/entries.*.ts`, loaded when Answers first show. copy.md
has each new line.

## A reader's review

A reader who was given the part's code and ADR-0212, not these notes, found these, each held in the code and each fixed
with a test:

1. **A repeatable group kept only until the actions run** had its children summarised: the summaries checked the
   field's own keep, not its group's. They check both now; a group kept so, and a sensitive group, keep their children
   out of the summaries and the texts endpoint.
2. **Show more pressed while another range was loading** put the old range's texts back once both answered. A page now
   applies only to the texts it was asked for.
3. **The visitors' own "Other" words became counted lines** where a list or a source gave the options, and where only
   an older version offered Other. Every value no version's options and no list offer is now one Other line; a list
   that cannot give its options leaves the values as they are, as nothing tells them apart (ADR-0212 4).
4. **A grid ignored older versions:** a row a later version removed lost its answers, an older column was not marked,
   and the grid's answers were its busiest row's. A grid now keeps every version's rows, marks removed rows and older
   columns, and counts each entry or row once over all its rows (`DISTINCT`, ADR-0212 5).
5. **A share far under 1% still read as 0%** (1 of 2,500). It shows to its first figure now (0.04%).
6. **Tests that did not test their names:** the Spam rule was never reached (no Spam entry where the filter named
   Spam); no test had a group kept until the actions run or a sensitive group, a page of texts ending inside an entry's
   rows at one instant, or the race in 2. Each has one now.
7. Smaller: a scale's groups came from its newest version only (a 5 of a five-point scale read as a low ten); they now
   need every version to ask from 0 to 10. A texts cursor could place a page by another form's entry, as the list's
   cursor could not; it is held to the form now.

It also checked and found fine: the statements' placeholders, grouping and counting, the median, the cursor's order,
SQLite against SQL Server, every left-out entry and field, both endpoints' permission, and each share's whole.

## The tamper round

50 tampers, each taking back one rule of the part in the working tree after the reader's review, building what it
needs and running the tests that hold the rule; a tamper counts as caught only when a test fails, and each file comes
back as it was read. 46 were caught at once. Four were missed, and each has a test now:

- **An answer of several values counted per value:** no test answer held two values, as only a choice of several or a
  file upload stores them. A test stores an answer of two values and finds it counted once.
- **A scale's top group taking 8:** the tests' scale answers had no 8. A test answers 6, 7, 8 and 9 and finds the
  groups' edges where their names put them.
- **The texts endpoint reading any field:** no test asked for a field that is not free text. One asks for an email and
  a number and gets none, and a line of text it reads.
- **A long text shown whole:** no test sent one past 500 characters. One does, and finds it cut with an ellipsis.

That leaves 50 tampers, all caught.

## What the journeys and the tests found

`insight-answers.spec.ts` in Edge, against the demo built from the working tree, as a user who may only view entries:
the host seeds a form with an answer of every kind and 120 entries; the switch shows the Answers in the same range
without Compare; each kind's numbers are the ones the seed's rule gives (a friend 26%, the radio advert of the twelve
before it went 3%, the visitors' own words 8%, content first at 1.4, the scale's groups 17% / 25% / 58% and its score
+42, a checkbox's 80 not ticked of the entries, a group's 90 meals per row, an email given in 80% of the entries); the
free texts read on five, then twenty, then the last five, the focus on the first new one; the entries list's dialog
filters by an answer and its chip takes it away; the switch shows every chart as its table, kept across a reload; a
free text opens its entry; and axe finds nothing inside the view either way.

- **The journey could not press the toggle:** Umbraco's toggle keeps its input out of sight, so Playwright waited for it
  to show. The press goes to the toggle, as a pointer's does, and the journey checks the switch is on.
- **A checkbox's share was of its own ticks** while the journey's first run was being written: an unticked box stores
  nothing, so its answers were all ticked. Its share is of the entries now (ADR-0212 4), and the box no longer says its
  count twice.
- **A test outside the step failed under load:** pasting 1,000 options in the designer compared each line with every
  option before it and took 3.6 s, near vitest's 5 s. The paste looks values up in a set now and takes 20 ms
  (`4af6a0ff`).
- **Two tests of 11.5 did not typecheck** (an instant that may be undefined), which vitest does not see; fixed in
  `b57e34c0`.

Checks this session: 8,554 unit tests; the summaries, their 100,000-entry budget, the insight API, the permission
matrix, the API snapshot, the entries query and list and the public insights integration tests (83); 5,627 client tests;
the size budgets (the backoffice bundle at 479.10 of 512 KB); `dotnet format`; and the journey above with its four look
rounds.
