# Review 22: the other screens, and the section as Phase 22 leaves it

Step 22.4 is the last of the four steps of [simpler-backoffice.md](simpler-backoffice.md). It asked the four questions
of its section F of the screens the phase had not looked at yet, fixed what they found, and brought the browser tests,
the guides' pictures and every page that describes the section up to the build. This is what was looked at, how, and
what became of each finding. The decisions are in
[ADR-0259](../decisions/0259-the-other-screens-and-the-walks-count.md).

## How it ran

1. **The pass** (run A). The build after step 22.3, shot at 1440 and 920 px in English: 36 screens and states, from an
   entry's panel and screen through the entries' filters and exports, the option list editor, People, Retention,
   Deliveries and the Audit log to Insights. Each screen was asked:
   1. Is there a second way to do this?
   2. What could wait until something is picked?
   3. What is said twice?
   4. Does the empty state say what to do next?

   The main session, which judges the work ([ADR-0255](../decisions/0255-phase-22-in-one-stretch.md)), had read the
   last walk's shots of the same screens before and named ten findings; those and the pass's own are in one table per
   screen below.
2. **Every fix with a unit test that fails without it.** Where a unit test cannot hold the whole fix (a list as the
   server fills it, a layout as the browser draws it), a browser test in its screen's spec holds it too.
3. **Two follow-ups** the shots showed: a closed inspector section kept the room of its empty body, and "Export…"
   opened on a list with no entries.
4. **The walk's count** (run B). The walk counted the section's navigation as a screen's own words from step 22.2 on,
   so no screen compared with itself any more; it counts it by itself now (below).
5. **What describes the section** (run B): the browser specs, the guides' pictures, the information architecture,
   the copy guide, the top tasks and every guide that names a screen's place.

## The pass, a screen at a time

Each finding says what became of it: fixed, with the test that holds it, or kept, with the reason.

### The entries' lists (a form's Entries, the Inbox, Spam)

| Finding | What became of it |
| --- | --- |
| The Actions column said "2/2 delivered", "Not run" or nothing in nearly every row, which hid the rows that needed someone | fixed: it speaks only where something did not go out ("1 failed", "1 uncertain", "2 waiting"), the worst first; `entry-list-model.test.ts` "says its runs where something did not go out, the worst first; nothing where all went or none ran"; in the browser, `entries-list.spec.ts` "the Actions column speaks only where an action did not go out, and a filter names its boxes once" |
| "2 waiting" speaks too | kept: those actions have not gone out yet |
| "Export…" opened on a list that showed no entries, where the sidebar could only say so | fixed: not offered while the list shows none, from the count the list has; `entries.view.test.ts` "offers Export... only while the list shows entries (ROADMAP 22.4)"; in the browser, `entry-export.spec.ts` (a search that finds nothing) |
| The empty states | kept: each says what to do next |

### The filter dialogs

| Finding | What became of it |
| --- | --- |
| "Filter by state" named its boxes a second time ("States"), "Filter by actions" its choices ("Actions") | fixed: the headline names the group, for screen readers too; `entry-dialogs.test.ts` "names its choices by its headline alone" and the State part's test; in the browser, `entries-list.spec.ts` (as above) |
| Received's "Span" and a field's "Options" | kept: other words than the headline, and "Options" shows only once "Is one of" is chosen |

### The export sidebar

| Finding | What became of it |
| --- | --- |
| The number of entries stood beside Entries and on the Export button | fixed: Entries says which ("All, no filters", "Those that match the filters", "Those that match the search", "Those selected"), the button how many; `export-model.test.ts` "names which entries go, without the number the button says: all, filtered, searched, ticked" and `entry-export.modal.test.ts` "says under which entries go how many waiting for email confirmation stay out"; in the browser, `entry-export.spec.ts` |
| The sidebar's "No entries to export" and "No entries match the filters" | kept: entries deleted, or removed by retention, between the list's count and the sidebar's plan, and ticked rows deleted elsewhere, still reach them |

### An entry's panel and screen

| Finding | What became of it |
| --- | --- |
| Nothing said twice; the bar holds three buttons and "…", Approve only where it applies | kept: within the rules |
| "No notes yet." does not say what to do next | kept: the note's box and Add note stand right above it |
| Re-run actions… in the "…" of an entry whose form has no actions | kept: its dialog says that none can run, and hiding it needs the server to say so for every entry |

### The option list editor

| Finding | What became of it |
| --- | --- |
| "Options" stood under the box's headline "Fixed options" | fixed: the headline names the rows; `option-list.view.test.ts` "edits the list in the inspector's option editor with its source fixed, each change the list's"; in the browser, `option-lists.spec.ts` |
| The labelled language select | kept: the editor has the room, and the form's language button would bring the designer's code into the Library's part of the bundle |

### Privacy › People

| Finding | What became of it |
| --- | --- |
| Search, a person found, Erase… and nobody found | kept: calm; "nobody found" says where else to look (the member) |

### Privacy › Retention

| Finding | What became of it |
| --- | --- |
| The Rejected and Spam rows drew the raw keys of two help words that step 21.1 had cut, as the screen built the key from the state | fixed: a row without a help word draws no help line and no `aria-describedby` to one; `retention-screen.element.test.ts` "draws the days per state, the forms with their own, and what will be deleted with its files"; in the browser, `site-wide/retention.spec.ts` |
| Why the words' test missed it: it held that every word is used, never that every key the code builds exists | fixed: `builtKeyGaps` (`lang/key-use.ts`) holds a key built from a value for every value its siblings are held for, in every area and language; `key-use.test.ts` "is held for each value its siblings are held for, so no screen shows a key" and "is found where the area lacks a sibling's key, as Retention's help lines were, and only there"; it found no other gap |
| A screen that draws a raw key at all | fixed: the walk and the journeys fail it (below) |

### Activity › Deliveries and the Audit log

| Finding | What became of it |
| --- | --- |
| Empty, they said "No deliveries yet" and "Nothing is recorded yet", which was not true: both open on the last 30 days, before which there may be more | fixed: "No deliveries in the last 30 days." and "Nothing recorded in the last 30 days.", in English and German; `activity.screens.test.ts` "says what it shows at first when it is empty: the last 30 days, before which there may be more", for each |
| Deliveries' four filters stay above an empty list | kept: the search and When reach older deliveries |
| A delivery's and an audit entry's details | kept: calm, nothing said twice |

### Insights

| Finding | What became of it |
| --- | --- |
| A "Show as table" button on every chart, each remembered on its own | fixed: one switch "Show as tables" beside the range, for the Overview and the Answers alike, kept for the user, in the list the server kept already; `insights.view.test.ts` "shows every chart as its table by one switch beside Overview and Answers, kept, and none on a box", "keeps the tables switched while a report is on its way, whatever the report says of them" and "show every answer as its table with the screen's one switch, kept for the user"; in the browser, `insights.spec.ts` and `insight-answers.spec.ts` |
| The funnel's first and last bars repeat the cards' Starts and Entries | kept: they are the funnel's scale |
| With no data, the range and the switches stay | kept: another range may hold data |
| A table longer than its box scrolled inside it, and the keyboard could not reach the box to scroll it (axe's `scrollable-region-focusable`, found by the walk's new shot of every chart as its table) | fixed: the box is a region of the table's name that takes the focus, in Insights and in Forms at a glance; `insights.view.test.ts` "shows every chart as its table by one switch beside Overview and Answers, kept, and none on a box" and `insight-glance.test.ts` "lists each form with its numbers and their changes, each form opening its Insights view" |

### The designer's inspector

| Finding | What became of it |
| --- | --- |
| A closed section kept about 50 px of empty room under its header: its body's own display beat its `hidden` | fixed: a closed section is as tall as its header, its body still in the page for the header's `aria-controls`; `inspector.test.ts` "is as tall as its header: its body, which its button still names, takes no room"; in the browser, `designer-rules.spec.ts` measures the closed Rules section. The Settings view's "More options" and the action sheet's hid theirs already |
| A closed Rules section put "Add a rule" under its header at some widths | fixed: at the end of the header's first line, at every width; `inspector.test.ts` "lists an element's rules in words, each opening the builder, and adds one while the element takes another"; in the browser, `designer-rules.spec.ts`, in the docked inspector |
| A page's and a group's description have no box | not built, for later. The definition holds both as text in every language; the website (Razor and the element) and the canvas draw them, merge tags and the list of texts to translate include them. They come only with an import, a template's file or the management API: none of the built-in templates has one, and no inspector edits them. Until then a description without a German text marks its page or group "No German text yet", and the designer offers no way to write one. A pass that removes adds no setting to two inspectors |

### The wireframes

| Finding | What became of it |
| --- | --- |
| The template gallery's cards drew the browser's own round radio | fixed in the wireframe: `appearance: none` on the radios the cards hide; 5 shots, no finding |
| Entries, Inbox, Insights and the option list drawn as the screens were | redrawn as the pass leaves them (856a0206) |

## No raw key on any screen

`rawKeys` (`tests/e2e/support/ux-walk.ts`, with its unit test) finds in a screen's drawn text a word built as a key of
the backoffice's words is: `jfb` and a capital, then parts after underscores, as
`jfbGlobalSettings_privacy_retention_rejectedHelp`. Each shot of the walk and of the journeys reads its screen's
drawn text, Umbraco's included, and the walk fails a screen that draws one, as it fails a fold inside a fold.

**Its first run** (the walk and the journeys of the build after run A, at 1440 px in light, in English and in
German: 143 screens, 286 shots) found no raw key on any screen. Retention's two keys were gone already; a run on the
build before run A would have named them.

## The walk's count: a screen's own words, and the navigation by itself

The walk counts the words a screen's own elements draw, and steps 22.2 to 22.4 are done when no screen shows more
than before. Until step 22.2 the left side was Umbraco's menu, which no walk counted. From 22.2 on, the five places
and a place's tabs are the section's own elements, so every screen counted them, and a Settings screen showed 14 or 15
words more than in the last walk of Phase 21 while its left side showed five items where it had shown seventeen.

Each shot now counts two numbers (`measure` in `tests/e2e/support/ux-shots.ts`):

- **`words`:** what the section's own elements draw, without the forms tree's rows and without the navigation, which is
  what every walk before step 22.2 counted;
- **`navigation`:** what the places (`jfb-places`) and the open place's tabs (`jfb-place-tabs`) draw.

**The old left side,** from the build before step 22.2 (`cce702ae~1`: `section/sidebar.ts`, `section/manifests.ts`
and `lang/section.*.ts`), counted by the walk's rule for a word (a run of characters between spaces that holds a
letter or a digit), for an administrator, who saw every item:

| | Items | Their words | The five headings | Together |
| --- | --- | --- | --- | --- |
| English | 17 | 22 | 5 (Entries, Library, Settings, Privacy, Activity) | 27 |
| German | 17 | 17 | 5 (Einträge, Bibliothek, Einstellungen, Datenschutz, Aktivität) | 22 |

The Inbox's unseen count, a number on its item then as now, is left out of both.

**The navigation now,** measured by the same walk, in words a screen's places and tabs draw (the Inbox's count
included, as a number counts as a word):

| | Most screens (111) | Inbox, Spam | Deliveries, the Audit log | People, Data collection | The Library's tabs | The Settings tabs |
| --- | --- | --- | --- | --- | --- | --- |
| English | 6 | 8 | 9 | 10 | 12 | 14 |
| German | 6 | 8 | 8 | 9 | 10 | 13 |

The old left side showed its 27 words (22 in German) on every screen; the places show 6 on most, and with a place's
tabs at most 14 (13).

**Like for like with Phase 21.** The walk and the journeys above, compared with the last walk of Phase 21
(`.local/ux/21.1-final2`, the same two looks), screen by screen by `words`:

- **143 screens in both, 286 shots:** none shows more than three words more; 165 shots show more than three fewer. In
  all, 59,481 words before and 48,517 after.
- **New since Phase 21:** `picker`, `picker-search`, `tab-menu`, `page-inspector` and `after-sending` (22.1),
  `inspector-messages` and `action-kinds` (22.3), `translate-after`, and `insights-tables` (22.4, the Overview's
  charts as tables).
- **Gone with their screens:** `palette-search` (the field list, 22.1), `settings-after` and `translate-settings`
  (After sending left the Settings view, 22.1), `history-picker` (the form's "Restore an earlier version…", 22.3).
- **The fold check failed** on the designer's inspector, where the walks of steps 22.1 to 22.3, run on their own
  screens, had not looked: in Essentials, the option editor's row "Details of …" (an option's value and
  description), its handle's "Move …" (the place in the list, ADR-0205), "Paste options…" and "Paste rows…", and the
  rich text's "Link" open a region inside the open section (9 screens: `selected`, `element-menu`, `move-to`,
  `options`, `option-place`, `grid`, `rich-text`, `change-type`, `undo-toast`). None of them is a section: a row
  that opens its own option, and a tool that writes into the list or the text. The check lets the option editor and
  the rich text editor through by name, as it does the merge tag picker
  ([ADR-0259](../decisions/0259-the-other-screens-and-the-walks-count.md), section 3), and any other control that
  opens inside a section still fails the walk.

## What describes the section

- **The browser specs** follow run A's words and controls, through their helpers; each fix a unit test cannot hold
  whole has its browser test in its screen's spec (named in the tables above). The walk's Insights shots set the
  screen's one switch; it has a new state, `insights-tables`, the Overview's charts as tables.
- **The guides' pictures** (`docs/screenshots/`) were taken again by `scripts/screenshots.mjs` on the demo, which now
  reaches each screen the new way: a place and its tab, a form's Settings with a group picked, Add action through its
  kind, the designer with its field picker.
- **[information-architecture.md](information-architecture.md)** was read against the build from its first line to its
  last; [copy.md](copy.md) holds the words that came and went; [top-tasks.md](top-tasks.md) was counted again.

### The places the guides name

Every place a guide names (`docs/*.md`, `README.md`, the demo's and the samples' readmes), the words it uses, and
where that is now. "Corrected" means the guide's words led somewhere else, or nowhere, and now lead there.

| Words a guide uses | Guides | Where it is now | |
| --- | --- | --- | --- |
| the sidebar's five places; the Inbox, "the first place in the sidebar" | editor-guide, entries-and-exports | the places before the forms tree (22.2) | as written |
| the sidebar header's "+", the forms tree, the Recycle Bin | editor-guide, multi-site | as written | as written |
| the Overview, its setup checklist and Forms at a glance | ai, editor-guide, email, entries-and-exports, getting-started, insights, installation, privacy, troubleshooting, upgrading | the section's start page | as written |
| Form Builder › Settings; "the backoffice settings" | configuration, installation | the Settings place | as written |
| Settings › General (› Base URL, › Default theme, › New-form defaults, › Visitor texts, its History box) | architecture, deployment, editor-guide, email, entries-and-exports, getting-started, insights, installation, multi-language, multi-site, privacy, rendering, themes | Settings, its first tab, and the boxes on it | as written |
| Settings › Email (› Send test email, › Sender, › Addresses that entries may be forwarded to) | actions, email, entries-and-exports, getting-started, installation, troubleshooting | Settings › Email | as written |
| Settings › Spam protection | field-types, spam-protection, troubleshooting | Settings › Spam protection | as written |
| Settings › Files | files, troubleshooting | Settings › Files | as written |
| Settings › Access | architecture, getting-started, installation, permissions, troubleshooting | Settings › Access | as written |
| Settings › AI | ai, architecture, extending, README.nuget.ai | the add-on's tab after Settings' own | as written |
| Privacy › People | architecture, permissions, privacy | Privacy, its first tab | as written |
| Privacy › Retention | architecture, installation, privacy | Privacy › Retention | as written |
| Privacy › Data collection (› IP addresses, › Members, › Count what visitors do with the forms) | architecture, insights, members, permissions, privacy | Privacy › Data collection, and its boxes | as written |
| Privacy › Members | architecture | no such tab: Privacy › Data collection › Members | corrected |
| Settings › Privacy › Members | ux/copy | Privacy › Data collection › Members | corrected |
| Activity › Deliveries | actions, architecture, email, permissions, troubleshooting, webhooks | Activity, its first tab | as written |
| Activity › Audit log | privacy | Activity › Audit log | as written |
| Library › Option lists | architecture, field-types | Library, its first tab | as written |
| Library › Themes (› Create theme) | architecture, themes | Library › Themes | as written |
| Library › Email layouts | architecture, email, themes | Library › Email layouts | as written |
| Library › Templates | architecture, editor-guide | Library › Templates | as written |
| a form's views: Design, Settings, Actions, Entries, Insights, History, Embed | actions, editor-guide, entries-and-exports, extending, headless, insights | the form's tabs | as written |
| the form's Settings view › a group (› Appearance › Theme, › Developer, › Privacy › Insights, › Submission › More options, › Store entries › Never) | faq, headless, insights, multi-language, rendering, themes | the group picked from the view's list (22.3) | as written |
| Settings › Developer › Query values, › Context values; Settings › Appearance; "the form's Settings › Developer › Headless access" | field-types, merge-tags, rendering (4), rules-and-validation, troubleshooting | reads as the Settings place, which has no Developer or Appearance tab: the form's Settings view › Developer, › Appearance | corrected (8 places) |
| Add action, then a kind | actions, extending, getting-started | Add action lists the kinds first (22.3) | as written |
| the designer's field picker, "Add field" | editor-guide, extending, README | as written (22.1) | as written |
| "the palette of field types on the left" (the designer's picture) | README | the designer has no palette: its field picker opens from Add field | corrected, with the picture |
| a field's inspector sections (Essentials, Validation, Messages, Privacy › Keep the answer, Advanced) | editor-guide, field-types, privacy, rules-and-validation | the inspector's sections | as written |
| "open **Rules** in the inspector, then **Add a rule**" | rules-and-validation | Add a rule stands on the Rules section's line, open or closed (22.4) | corrected |
| the theme editor's More options › Custom CSS | themes | as written | as written |
| Settings › Templates; Settings › Health checks; Users › User groups | getting-started; ai; permissions, troubleshooting | Umbraco's own sections, named as Umbraco's | as written |
| Actions › …, Settings › Secrets, Settings › Environments | releasing | GitHub's, not the section's | as written |

Beside the places, two guides said what a screen no longer shows, and say it as it is now: the entries' Actions column
and "Export…" ([entries-and-exports.md](../entries-and-exports.md)), and Insights' one "Show as tables"
([insights.md](../insights.md)).

In all: 32 places in 33 guides; 5 led elsewhere or nowhere, in 8 guides (architecture, field-types, merge-tags,
rendering, rules-and-validation, troubleshooting, the README and the copy guide), and are corrected.

## The walk on the final build

The walk in the twelve looks and the five journeys run on the phase's last work commit, with the clean-clone
verification after them. Their result is recorded here once they have run.

## The three design skills

Each skill looked at the screens the phase changed, on the shots taken while each step was built and on the walk
(ADR-0019, ADR-0020). None asked for a new component, a new colour or new motion: the phase takes things off a screen
and puts them where they are looked for.

### design-taste-frontend

- **Design read:** an operating tool for editors inside Umbraco's backoffice, in a calm, plain language, leaning on
  Umbraco's own UI library and tokens. For such a surface the skill's dials are low variance, low motion and a middle
  density; the phase leaves variance and motion alone and lowers what is on a screen at once.
- **What it checked:** each new part is Umbraco's own before one of the section's (the sixth rule). The places are
  Umbraco's menu items, a place's tabs Umbraco's tabs, the field picker Umbraco's small sidebar, the kinds of action a
  list as the Create dialog's. No card, gradient or row of icons came in to fill the room that opened; one accent,
  Umbraco's; one radius and one type scale; every button text on one line, in German too; the empty, loading and
  failed states of the changed screens still say what to do.
- **What it asked for, and got:** the rows of the Settings view's list keep their height, so a pick moves none of
  them; the row picked stands on the backoffice's own current colour, whose text holds 4.5:1 in the dark theme where
  Umbraco's comes to about 4.3:1; a closed section of the inspector takes the room of its header, so six fit where
  three did; the theme editor's picture has a caption that says what it shows.
- **Kept:** the group picked keeps its name as a heading beside the list, as Umbraco names the open item in its tree
  and again in its workspace; the Library's editors keep a labelled language select.

### impeccable

The surface's mode is Operate: the editor completes a task, so scanability and sameness outrank expression. The
skill's distill and clarify lenses, in its bounded two rounds for each step (the shots after the build, the shots
after the fixes).

| Heuristic | Before | After |
| --- | --- | --- |
| Pick first, details after | a form's Settings showed six groups that opened any number at a time; an action's sheet held every kind and four moments, each explained | the groups to pick from and the one picked; the kinds first, then one kind's sheet with the line of the moment chosen |
| One level of folding | sections that open, with "More options" inside | Messages and Privacy are sections of their own; the walk fails a section inside a section |
| Say it once | the picked group's line beside its own settings; the export's count in its line and on its button; "Filter by state" over "States"; "Fixed options" over "Options"; "Show as table" on three charts; "none" in nearly every row of a list | each once, at the place it belongs to |
| Every text is true | "Add action starts with an email to the team"; "No deliveries yet" over a list of the last 30 days; the keys of two words that no longer exist, under Rejected and Spam | corrected; the words' test and the walk now find a key a screen would draw |
| What is fixed never gives way to what grows | none of the seventeen fixed items on the screen at 900 px height once a site had twenty forms | five places before the tree, always on the screen |
| A button stands where it applies | Publish… under a list of entries, and two primary buttons with an entry open | the form's buttons on the views that change the form; one primary button on a screen |
| A help line is a sentence | "when an entry comes in" under a choice | "It runs when an entry comes in." |
| An offer leads somewhere | "Export…" opened a sidebar that could only say there was nothing to export | not offered while the list shows no entries |

Left as found, on purpose: "Re-run actions" on a form without actions, whose dialog says that none can run; the
funnel's first and last bars, which repeat two of the cards' numbers as the funnel's scale.

### emil-design-eng

| Before | After | Why |
| --- | --- | --- |
| Picking a group changed the height of two rows, so the list moved under the pointer | every row keeps its height | Nothing moves that the user did not move |
| After a kind was picked, the focus sat on the sheet's heading | it goes to the Name box, once the new layout has drawn it | The next keystroke lands where the eye already is |
| In a narrow Settings view the focus stayed behind when the group took the list's place | on a pick it goes to the group's first setting, and "All settings" brings it back to the group's row | A keyboard never loses its place when a screen swaps its parts |
| A closed section kept about 50 px of empty room under its header | it is as tall as its header | A closed thing takes the room of its name |
| "Add a rule" dropped to a line of its own under a closed Rules section | it stands at the end of the header's line | One line for one thing |
| The row picked used Umbraco's current colour, about 4.3:1 against its text in the dark theme | the backoffice's own current colour, about 5.0:1 | A marked row has to be read, not only seen |
| A long table scrolled inside its box by a pointer alone | the box takes the focus and scrolls by the keyboard | Every way in reaches the whole content |
| A dialog opened by an address over Entries would have brought the form's footer back | the view shown is read wherever its name stands in the address | What a screen shows does not change behind a dialog |

No animation was added or changed. The phase's screens are used many times a day, where the skill's own rule is to
animate nothing new; the language menu keeps the short scale from its button that the designer's menus have.

## For later

- **A box for a page's and a group's description** in their inspectors. Until then a description that came with an
  import, a template or the API is drawn but cannot be changed or translated in the designer (the facts are in the
  inspector's table above).
- **"Re-run actions" only where the form has actions:** the entry would have to carry its form's count of actions
  from the server.
- **A footer for a site's own view of a form:** it has none, as the views that show what a form brought in have none.
  A view of a site's own that changes the form would need a way to say so.
