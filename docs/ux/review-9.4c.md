# Step 9.4c: rich text, merge tags, pickers and patterns

ROADMAP 9.4c gives the inspector its richer editors: rich text for a paragraph's and a consent's wording, "Insert merge
tag" under the texts made for tags, Umbraco's own pickers for an image's picture and for the content and data type
option sources, and "Must match a pattern" with a live "Try it"
([ADR-0173](../decisions/0173-rich-text-merge-tags-pickers-and-patterns-in-the-inspector.md)). Per ADR-0019 and
ADR-0020 the three design skills ran on the result; the design comes from Phase 2
([information-architecture.md](information-architecture.md), section 5.1, and [copy.md](copy.md)). The owner answers no
questions during the run (CLAUDE.md rule 8), so every choice was decided here or in the ADR.

## What was looked at

The e2e host (the demo on `127.0.0.1:5520`, ADR-0031) in Edge through Playwright, as the designer, with a form of a
short text, a heading, a consent, an image, a dropdown and a second short text made through the API, in English and
German, with the demo's pages and picture.

| Shot | What it shows |
| --- | --- |
| [the tag picker](review-9.4c/tag-picker-1440.png) | "Insert merge tag" open under the heading's box, in the inspector's flow: Find a merge tag, then Answers by their labels, then the form's, the page's and the site's paths |
| [found](review-9.4c/tag-found-1440.png) | "your" typed: only "Your name {{ field.name }}" left |
| [none found](review-9.4c/tag-none-1440.png) | "No merge tag matches." |
| [inserted](review-9.4c/tag-inserted-1440.png) | the heading's box holding "Welcome, {{ field.name }}", the caret after the tag |
| [a refused link](review-9.4c/link-refused-1440.png) | the consent's link panel: Address "privacy" marked, why under it, Open in a new tab, Apply and Cancel |
| [a consent's wording](review-9.4c/consent-1440.png) | one line with Bold, Italic and Link, "privacy terms" bold and linked; the canvas's consent the same |
| [an image](review-9.4c/image-1440.png) | Umbraco's media picker's card in the inspector, and the picture on the canvas |
| [a refused pattern](review-9.4c/pattern-unsafe-1440.png) | "Must match a pattern" in the mono font, marked, why under it; Try it waiting |
| [an answer refused](review-9.4c/pattern-miss-1440.png) | "M-[0-9]{4}" and "M-12": "The pattern doesn't take this answer." in the danger colour |
| [an answer taken](review-9.4c/pattern-match-1440.png) | "M-1234": "The pattern takes this answer." in the positive colour |
| [the summary](review-9.4c/pattern-summary-1440.png) | Validation closed: "Must match a pattern: M-[0-9]{4}" |
| [Umbraco's content picker](review-9.4c/content-modal-1440.png) | the designer's content tree with Home |
| [pages as options](review-9.4c/content-1440.png) | Options from "Pages of the site", Start page Home, the canvas's dropdown with the site's pages |
| [of one type](review-9.4c/content-types-1440.png) | Document types as ticks from the site's own, "Demo legal page" ticked |
| [the canvas](review-9.4c/content-types-canvas-1440.png) | the dropdown with Privacy and Imprint only |
| [a data type](review-9.4c/data-type-1440.png) | Options from "A data type": the site's lists in a select, "None chosen" not to be chosen |
| [German](review-9.4c/consent-german-1440.png) | the consent in German: an empty box with the English words, without their markup, as its hint |
| [at 920 px](review-9.4c/tag-picker-920.png) | the inspector over the canvas, the tag picker within it |
| [dark](review-9.4c/consent-dark-1440.png) | Umbraco's dark theme, the consent's wording and its tag picker (a consent's wording takes no answers) |
| [dark pattern](review-9.4c/pattern-dark-1440.png) | the pattern, its message and a refused answer in the dark theme |

**Checks that ran on the last look:**

- **Axe** (WCAG 2.2 A and AA) on the inspector with the tag picker open, with a consent's wording and its link, with an
  image picked, with a pattern, its message and Try it, and with pages as options and their type ticked, in
  `designer-editors.spec.ts` and in the look: none.
- **The bundled detector** (`impeccable detect --json`) on the rich text, the tag picker, the setting editors, the
  option editor, the inspector and its styles: `[]` for each.
- **The journeys** of `designer-editors.spec.ts`, below, with the whole backoffice project.

## design-taste-frontend

Read as: Operate-mode product UI inside Umbraco's own design system, for editors wording a form and setting its checks,
calm and native. The skill's landing-page rules don't apply (ADR-0019); dials 2 / 2 / 5. What it still asks held: one
accent (Umbraco's selection colour for a pressed tool and a focused box), one radius scale (UUI's), Umbraco's icons for
the toolbar, every state composed (no paths yet, none found, a read that failed, a link address refused, a pattern the
matcher refuses, an answer taken or not, nothing picked), and no spinner but the panel's short loading bar while the
paths are read.

## impeccable

Operate mode, a refinement inside the incumbent world. Critique against IA 5.1 and copy.md, and what the look in Edge
found:

| Finding | What was done |
| --- | --- |
| Umbraco's document type picker opened on "No items" for the designer: Umbraco lists document types and data types only to users of its Settings section | the option sources endpoint serves the site's page types and the data types that hold values as the settings' choices, which the inspector shows as ticks and a select; Umbraco's own pickers stay for content and media |
| "Open in a new tab" changed nothing: the editor put a new tab, and a `rel` the server drops, on every link | a link holds its address, and a target only where a new tab is asked; the server adds what such a link needs, as for any rich text |
| The pattern box, like the alias and the formula before it, asked for the mono font, but the boxes' own rule was more specific and won | the rule for code boxes is as specific as the boxes', and the journey checks the pattern's font |
| Try it's answer was the grey of help text | a match is said in the positive colour and a miss in the danger colour, both in words too (`--jfb-bo-color-positive-text`, tokens.md, 5.1:1 or more in every theme) |
| Umbraco's content picker came up empty for the designer persona: its group had the Content section but neither a start node nor Umbraco's Browse permission, which the tree asks | the persona's group may browse the whole content tree, as a site's form designers would; the picker is Umbraco's own, so its rules stay Umbraco's |
| The tag panel, a popover, measured itself while it showed its loading bar and ran past the bottom of the window once its paths came, so the last ones could not be reached | the panel opens under its button in the inspector's own flow: nothing lies over the boxes below it, and the inspector scrolls it as anything else |
| Clicking the consent's link inside its box left the form for Umbraco's "Not found": the backoffice's router followed it | a link in the text is edited, never followed; a unit test and the journey hold it |
| Another language's hint read "privacy terms .": every tag of the default's text became a space, the link's too | only a paragraph, a list item or a line break becomes a space, any other tag goes without one |
| A consent's link on the canvas has no address | as intended: the canvas draws a draft's rich text as bare tags it writes itself (ADR-0168); the draft keeps the address, which the journey reads back |

**Noticed for the phase review (9.6):** on the canvas, the selected element's bar sits over the element above it, and
covers most of a heading's text there (9.3c, ADR-0169); clicking the covered words presses the bar. Step 9.6 now
says so, and decides whether the bar takes room of its own.

## emil-design-eng

| Before | After | Why |
| --- | --- | --- |
| — | the tag panel opens and closes at once, under its button | it is opened many times while writing a form, and often from the keyboard |
| — | a toolbar button shows its state by its fill, without motion | formatting is pressed many times a minute; Ctrl+B and Ctrl+I work as everywhere |
| — | "Try it" answers on every key, with no delay | the answer is the point; a pattern is checked in microseconds by the matcher, so there is nothing to wait for |
| — | Umbraco's pickers keep their own sidebar and its slide | an editor meets the pickers they know from the Content section |

## A reader's review

A reviewer read the whole change with fresh eyes, the server's checks and the decision record beside the client.
Every finding held up; each is now fixed with a test:

| Finding | What was done |
| --- | --- |
| A consent's and a checkbox's text sit in the visitor's `<label>`, but the editor wrapped them in a paragraph, so the site would have drawn a paragraph in a label, and merely visiting the box rewrote the text | such a text names its own editor, `inlineHtml`: one line of Bold, Italic and Link, written without a paragraph, Enter adding none; a visit without typing hands nothing on |
| The tag picker offered every field's answer, even those after the text, which the check of the text then refused | the request names the element whose text it is, and a visitor text is offered only the answers of the fields before it |
| Ctrl+Z in the editor could bring back another field's or another language's text, as setting a text from outside went into the editor's own history | a text set from outside starts a new editor, with nothing to undo |
| Another language's hint showed the default's markup as text | it shows the default's words |
| A "path on the site" could start with `//` or `/\`, which browsers read as another site | a path starts with one slash and never two or a backslash |
| The tag picker stayed open over the boxes below when the focus left it | it closes when the focus leaves it, and now opens in the inspector's own flow, over nothing (above) |
| The document type and data type pickers disagreed with what the server now serves, and the record still named Umbraco's pickers for them | they are the site's own, ticked or chosen (above), and the record says so; a locked one's summary is as ADR-0060 says |
| Emptying the pattern removed its rule at once, so the next key edited the next pattern rule, and the first rule's message was lost | an emptied box keeps its rule, message and all, until it is left empty |
| Try it, on a refused pattern with an answer left in it, said the answer was refused; a slow answer to an earlier opening of the tag picker could replace a newer one; a moved editor came back without its editor | Try it says it waits for a pattern the site takes; an earlier answer is dropped; the editor is made again when it comes back |
| The toolbar was five tab stops without arrow keys; Link said it was pressed though pressing it opens a panel; Umbraco's Choose buttons were said without their setting | the toolbar is one stop whose arrow keys, Home and End move between its buttons; Link says whether its panel is open; Umbraco's inputs sit in a group named by their label |
| Two unit tests would pass with the code wrong: a tag accepted at either end of the text, and a check of the text shown again that never pressed Ctrl+Z | the journey puts the form's tag into the consent's wording at the caret in Edge, and a unit test presses Ctrl+Z after a text set from outside, which fails on the old code |

The reviewer also asked whether a designer could read any dictionary item through a label naming it (`#key`); they
are the site's own words for visitors, and the endpoint needs `design` on the form, as the options' does (ADR-0172).

## What the journeys found

`designer-editors.spec.ts` puts a field's tag into a heading through the picker, which offers only the answers before
it; writes a consent's wording as one line with the form's tag at the caret, bold words and a link, which is never
followed; picks an image's picture with Umbraco's media picker; tries a pattern against answers before it is kept;
and gives a dropdown the pages of the legal type under the home page picked with Umbraco's content picker. Each is
saved and read back.

| Failure | What was done |
| --- | --- |
| The tag's button and a canvas field's both began "Your name", and the media picker's Choose and the inspector's | the journey asks within the tag panel and within Umbraco's picker |
| The canvas's link was looked for by its address and role | the canvas draws it without an address, inert; the journey reads its words |

## The tamper round

The round ran in a throwaway clone of e7e509c (ADR-0117): 79 changes to the merge tag helpers, the tag picker, the
inspector and its model, the setting editors, the rich text, the paths service and its endpoint, and the option kinds'
choices, each run against the tests that should notice it, the server's against `MergeTagPathApiTests` and
`OptionSourceApiTests`. 68 were caught at once. One server change did not compile (an unused regular expression fails
the build here), so it was rewritten and then caught. Ten went unnoticed; nine got tests and were caught on 1369c4f,
and one changes nothing:

| Unnoticed | The test now |
| --- | --- |
| a path found by its words only | a search for a path's own spelling finds it, in any letter case |
| the site's sources each under a heading of their own | two of the site's sources share "The site's own" |
| the owner left out of the request | the request is held: the owner sent, or none for a text after every field |
| the last search kept when the panel opens again | opened again, the search is empty and every path is back |
| a tag in a setting's box sent as typing, merged with the typing before it | a tag is a step of its own, which Undo takes back alone |
| a consent's text dropped from the drawn items | a consent's and a checkbox's text are among them |
| a list's picker taking one item, or writing one key | a list's picker takes as many as its rule allows and writes its keys; another one key |
| rich text without its tag picker | the picker sits under the consent's wording, of its place and its element |
| lists allowed in an inline text | none: the one-paragraph document refuses a list either way, so leaving them out changes nothing but says what an inline text is |
