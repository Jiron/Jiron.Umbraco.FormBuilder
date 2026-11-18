# 0259. The other screens: what the pass decided, and how the walk counts a screen's words

- Status: accepted. Builds the pass over the screens not yet looked at of
  [ADR-0254](0254-a-simpler-backoffice.md) (F), and changes what the walk's count of words holds
  ([ADR-0252](0252-the-last-pass.md), ROADMAP 21.1), which the "Done when" lists of steps 22.2 to 22.4 compare
- Date: 2026-11-18
- Roadmap step: 22.4

## Context

Two things.

**The screens nobody had asked the four questions.** An entry's screen and panel, the entries' filters and exports,
the option list editor, People, Retention, Deliveries, the Audit log and Insights were built before the phase's six
rules ([simpler-backoffice.md](../ux/simpler-backoffice.md), F). Each was asked: is there a second way to do this,
what could wait until something is picked, what is said twice, does the empty state say what to do next.

**The walk's count.** The walk counts the words inside the section's own elements, and steps 22.2 to 22.4 are done
when no screen shows more of them than before. Until step 22.2 the left side's seventeen fixed items were Umbraco's
menu items, so the count never held them. Since 22.2 the five places and a place's tabs are elements of the section,
so the count holds them, on every screen: a screen of Settings counts 14 or 15 words more than in the last walk of
Phase 21, though the left side shows five items where it showed seventeen. The count compares two different things.

## Decision

### 1. The pass

Every finding, fixed or kept with its reason, is in [review-22.md](../ux/review-22.md). The decisions among them:

- **A list's "Actions" column speaks only where something did not go out:** "1 failed", "1 uncertain", "2 waiting".
  An entry whose actions all went out, or that ran none, says nothing. The column keeps its header and its place in
  the column chooser.
- **A dialog and a box name their subject once.** The filter dialog's headline names its boxes; the option list
  editor's "Fixed options" names its rows; the export says how many entries on its button alone, and beside "Entries"
  which ones.
- **A help line stands only where a row's name needs one.** Retention's Rejected and Spam rows drew the key of a word
  that no longer exists. A row without a help word draws none. A key the code builds from a value is now held by the
  words' own test for every value, and the walk fails a screen that draws a key.
- **Insights has one "Show as tables"** for the screen, kept for the user, in place of a button on every chart.
- **A closed section of the inspector is as tall as its header,** and a closed Rules section holds "Add a rule" on
  its header's line.
- **"Export…" is not offered while the list shows no entries.**
- **A table that scrolls inside its box is reached by the keyboard:** Insights' tables and the forms at a glance are
  regions of the table's name that take the focus.
- **An empty list that looks at a span says the span:** "No deliveries in the last 30 days."

Kept, each with its reason in the review: "Re-run actions" on a form without actions (its dialog says that none can
run, and hiding it needs the server to say so for every entry); the option list editor's labelled language select
(it has the room, and the form's button would bring the designer's code into the Library's part of the bundle).

Not built: a page's and a group's description have no box in the inspector. They come with an import, a template or
the API, the website and the canvas draw them, and the designer never offered them. A pass that removes does not add
a setting to two inspectors; the review lists it for later, with what an editor meets until then.

### 2. The walk counts a screen's own words, and the section's navigation by itself

- **`words`** is what a screen's own elements show, without the section's navigation: the left side's places and a
  place's tabs are left out, as the forms tree's rows are. This is what the count held in every walk before step
  22.2, so a screen is compared with itself.
- **`navigation`** is a second number on every screen: the words of the places and of the open place's tabs. The
  review sets it beside the words of the seventeen items the left side had, counted from that build's own words by
  the walk's rule for a word.
- The check of steps 22.2 to 22.4 reads as it did: no screen's `words` grow by more than the noise of a time of day
  or a count. It is not weaker: what it leaves out, the walk before it never counted, and the navigation's own
  numbers say what became of that.

### 3. What the walk counts as a fold

Step 22.3's check, that no screen opens a section inside a section, walked every screen for the first time in this
step, and found controls that open inside the inspector's Essentials: an option's row ("Details of Small", its value
and its description), the row's handle (the option's place in the list), "Paste options…", "Paste rows…" and the rich
text's "Link".

- **A fold is a control that opens and closes a group of the element's settings:** a section, "More options". That is
  what the fifth rule of [ADR-0254](0254-a-simpler-backoffice.md) limits to one level.
- **A row that opens its own item is no section,** and neither is a tool that writes into a box. An option's row
  shows that option's two further boxes where the option is, which is picking first and details after, at the size
  of a row; the others put a value into the list or the text and close.
- The check names the elements it lets through (the option editor, the rich text editor, the merge tag picker, the
  AI add-on's suggestion). Any other control that opens inside a section fails the walk until someone has looked at
  it.

## Consequences

- The entries' lists are quieter by a column of "none"; Insights by two buttons; Retention shows no key.
- The walk's reports, summary and sheets carry `navigation`; `tests/e2e/README.md` says what `words` holds.
- A walk made before this record compares with one made after it for every screen's own words; its navigation is
  counted from the build it walked.
