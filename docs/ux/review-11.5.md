# Step 11.5: insights

ROADMAP 11.5, part of the cluster 11.4 (ADR-0197), counts what visitors do with a form and shows it
([ADR-0211](../decisions/0211-insights.md)):

- a form whose insights are on carries the address to count at; the runtime sends a view, a start at the first press or
  answer, each page reached and each field that stopped the visitor, once each in a page's life, as beacons that hold no
  answer and no person; a page the server renders for a post says so in its address, so its visit counts once;
- each server adds the beacons up in memory and writes them every 10 seconds and when it stops; a stored entry adds its
  website page, its hour and its time to complete in the unit that stores it; a post refused without JavaScript counts
  the fields the server refused;
- a form's Insights view: the numbers with each change said in words, entries per day, week or month, who reaches each
  page with the median time to complete, the fields that stop visitors most, the top website pages, entries by hour and
  the members; every chart can show as its table, kept per user;
- the Overview's Forms at a glance, Privacy > Data collection with the site's switch, and the Privacy group of a form's
  Settings view with the form's own.

Per ADR-0019 and ADR-0020 the three design skills shaped it before it was built and ran again on the result; the design
comes from Phase 2 ([information-architecture.md](information-architecture.md), sections 2, 5.5 and 9, and
[copy.md](copy.md)). The owner answers no questions during the run (CLAUDE.md rule 8), so every choice the skills would
have asked about was decided here or in the ADR.

## What was looked at

Edge, through a scratch spec that is never committed, on a form the test site seeds with 60 days of numbers: the Insights
view without and with the comparison, its range menu, every box as its table, the form's Settings view with the Privacy
group open, Data collection, and the Overview with Forms at a glance; at 1440 px light in English and dark in German, at
820 px and at 390 px. At 390 px Umbraco's own shell is wider than the screen (it lays out at about 920 px, as earlier
reviews found); inside it the view goes to one column and the numbers two by two. The German headings are the longest
("Felder, an denen Besucher am häufigsten hängen bleiben") and wrap beside "Als Tabelle zeigen" without crowding it.

The first round of shots found four things, each changed and shot again:

- the range button's chevron pointed right while its menu was open; it now turns down with the menu, as Umbraco's own
  menus do;
- the form names in Forms at a glance were the browser's blue underlined links; they now read as the Overview's other
  table does, the text colour with the underline on hover and focus;
- in the table of the fields that stop visitors, "Mostly" stood at the end of its column as the numbers do; it is words,
  and now starts its column;
- the four shots ran side by side as one user, and one's "Show as table" reached another's view: right, as the choice is
  the user's, but it showed that two quick switches saved without waiting for each other, so the later choice could
  land first. Saves now go one after the other.

## design-taste-frontend

Read as: Operate-mode product UI inside Umbraco's design system, for an editor who wants to know whether a form works
and where visitors give up, without learning a tool. Dials 2 / 1 / 5, as for the rest of the backoffice. What it asked
held:

- **One look:** `uui-box` for each box with its switch in the box's header actions, Umbraco's toggle for the
  comparison, the menu of the canvas's "..." for the range, Umbraco's tokens for every colour.
- **Every state composed:** loading as a skeleton; another range loading as the numbers dimmed and marked busy; a read
  that failed with Try again in the numbers' place; insights off for the site or for the form, each in one line naming
  where to turn them on; a form with no visit yet saying when numbers appear; a box with nothing in it saying so.
- **Pre-flight:** no colour carries a change alone (each change is words: "up 9% from 3,876"); no gradient, no new
  motion but the dimming; tabular numbers in the strip and the tables; every string in copy.md's voice.

## impeccable

Operate mode, a refinement inside Umbraco's design system. The critique it guides kept the screen to one toolbar (one
range button, one switch) and one strip of four numbers, each said once: the entries' spam count sits under the entries,
not as a fifth number, and the conversion's change is in points, not a percent of a percent. The range menu holds the
presets, chosen dates and Group by, so grouping is there when the range is chosen and nowhere else. Charts come second to
the numbers and each has its table one press away, which a screen-reader user or a user who wants the exact figures
keeps. The fields that stop visitors read as a sentence ("187, 16.7% of starts, mostly the format"), so the bar is a
picture of a fact already said. The period before is a neutral bar beside each of this period's: quieter by colour, and
still 3:1 against the box, as a bar that carries a number must be (WCAG 1.4.11).

## emil-design-eng

| Before | After | Why |
| --- | --- | --- |
| the range button named "Date range" while it showed "Last 30 days" | named "Date range: Last 30 days" | a voice command says what it sees (WCAG 2.5.3); axe found it |
| the chevron right while the menu is open | down while it is open | the chevron tells the menu's state, as Umbraco's menus do |
| another range read with the old numbers shown as they were | the old numbers dimmed (150 ms, opacity only) and marked busy, "Loading insights" said | the numbers under a new range's name must not pass for its own |
| a read that failed after one that worked left the old numbers | the failure and Try again in their place | numbers of another range would mislead |
| a wrong pair of dates shown in red only | the same line as an alert | a screen-reader user pressing Show hears why nothing changed |
| two quick "Show as table" saves racing | one after the other | the last choice is the one kept |
| a report read after a switch put the box back | the view's switches win after the first report | the switch was saved or is on its way |
| "Mostly" aligned as a number | starts its column | it is words |
| glance links in the browser's blue | the Overview's link style | one look on one screen |

## The words

English and German for every new string, in `lang/insights.*.ts`, which load with the Insights view, the glance and
Data collection rather than with the backoffice's start (ADR-0183); the Settings view's Privacy group in
`lang/settings.*.ts`. copy.md has each string.

## A reader's review

A reader who was given the part's code and ADR-0211, not these notes, found these, each held in the code and each
fixed with a test (`030dd3aa`):

1. **A visit counted twice across a form's posts:** every page the server renders after a post (a summary page, a form
   that posts between its pages, a refusal) sent a view and a start again, and the pages a post reached were never
   counted. Such a page's address now says `continued=true`, so its view and start count nothing, and after a Next by
   post it names the page reached, whose view counts as reaching it.
2. **A browser without beacons** (one may turn them off) threw in the frame and left every later form of the page
   without its logic. The frame calls `sendBeacon` only where it is, and a test holds a page without it working.
3. **The date ranges used the user's own calendar** while the stats keep UTC days: west of UTC an evening's entries
   stayed out of today, east of it the 1st asked for a day not yet begun. The ranges now end on today's UTC day.
4. **A box switched while a report was on its way** went back to its chart when the report came. The view's switches
   win after the first report.
5. **Tests that did not test their names:** a start counted at a focus that a page's own script moves, which the browser
   calls trusted (a focus is no start now, and the test focuses as a script does); the writer's put-back and the
   flusher's write at stop had no test (both have now); a website page named only to a user who may open it in
   Content had none (it has now).
6. **The error under a wrong pair of dates** was not said (it is an alert now).
7. Smaller: the hours were counted in each site's zone and labelled with the shared one (they count in the shared one
   now); the glance answered 500 for a span whose days before start before 1753 (400 now); one visitor stopped first by
   an empty field and then by its format was two stops (one now, with the first kind); and an error the page kept from
   a refused post counted again as the runtime's `server` kind, after the server had counted it (it counts nothing
   now). A form published again during a visit counts nothing more of it; ADR-0211 records that as a limit, as
   counting it would cost the runtime room it does not have.

It also checked and found fine: no personal data or free string reaches a dimension, the sums are bounded and put back
under a lock, an entry's counts roll back with it, every endpoint's permission, the week and month maths with leap days
and short months, and the `:active` deferral, which never loses an error or leaves one waiting.

## The tamper round

40 tampers, each taking back one rule of the part in the working tree after `030dd3aa`, building what it needs and
running the tests that hold the rule; a tamper counts as caught only when a test fails. 32 were caught at once. Three
did not compile, as each left a parameter or a using unused; written so that they compile, all three were caught.
Three were missed, and each has a test now:

- **A beacon past its limit read anyway:** the host's own count answers 413 before the endpoint reads, so no test over
  HTTP reaches the endpoint's own limit. A unit test reads a body without a declared length, as a host that counts
  nothing passes it on.
- **A spam entry counting its page and hour:** no test sent one. One does now, and finds `spam` alone.
- **Forms at a glance ending on the user's own day:** the glance's test asked nothing of its span's last day. It now
  runs at one of two instants either side of UTC midnight, whichever is another day in the machine's zone, as does a
  new test of the Insights view's ranges; two more tampers take the view's UTC day back.

That leaves 40 tampers, all caught.

## What the journeys and the tests found

`insights.spec.ts` in Edge, against the demo built from the working tree: a two-page headless form on a page of its
own origin sends a view, a start, the email's stop and the page reached, never an answer; the host writes them within
its ten seconds; the Insights view shows the numbers, a chart and the fields that stop visitors, axe is clean inside
it, a box switched to its table stays a table after a reload; and once the form's own switch is off and published, its
page carries no address and sends nothing.

- **The press on Next was lost** the first time the journey ran: typing a malformed address and pressing Next left the
  page as it was. The mouse button going down moved the focus out of the field, the field showed its error at once and
  pushed Next down, and the button came up where Next had been. ADR-0113 now has a field left while the browser holds
  something of the form pressed show its error after that press's click; the error-display spec and every website spec
  (346) pass with it, and a unit test holds the order.
- **axe found the range button's name** ("Date range") without the range it shows; it now holds both.
- **The error-display spec counted every POST as a send,** the beacons included; it now leaves the beacons out.

Checks this session: 8,554 unit tests, the insights, rendering, post, fixture, permission and API snapshot integration
tests, 5,599 client tests, the size budgets (the runtime at 59.99 of 60 KB), and the Playwright specs above with the
form-settings, section and library specs.
