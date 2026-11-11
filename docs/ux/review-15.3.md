# Step 15.3: the AI add-on's screens

ROADMAP 15.3 adds three features to the designer and a settings screen ([ADR-0018](../decisions/0018-the-ai-add-on.md)):
"Draft a form with AI…" in the library's create menu with its sidebar and the "Drafted by AI, please review" mark in
the form's footer, "Translate with AI…" in the canvas's "…" with its preview, "Suggest help text" beside a field's help
text, and Settings › AI. Per ADR-0019 and ADR-0020 the three design skills ran on them: the screens were read as
Operate surfaces inside Umbraco's design system (the skills' brief inference), against §4.13's calm and "say it once",
and the interaction details in the skills' Before/After form. The owner answers no questions during the run (CLAUDE.md
rule 8), so every choice was decided here or in the ADR.

## What was looked at

A local look in Edge on the browser tests' host, whose fake provider answers every request
(`JironFormBuilderTesting:FakeAI=Chosen`), at 1440 and 920 px in the light theme, as a designer and, for Settings › AI,
as an administrator. The shots below are from the run after the fixes.

| Shot | What it shows |
| --- | --- |
| [The create menu](review-15.3/create-menu-1440.png) | "Draft a form with AI…" last, with its icon and description, as the other ways to a new form |
| [The draft sidebar](review-15.3/draft-1440.png) | the one text box, its label and help, Draft form |
| [A drafted form](review-15.3/drafted-1440.png) | the fields the fake drafted, and the mark in the footer beside Preview, Save draft and Publish… |
| [The canvas's "…"](review-15.3/canvas-menu-1440.png) | Translate with AI… after the designer's own items |
| [The translate preview](review-15.3/translate-preview-1440.png) | each text beside its translation, ticked, the language named as the site names it |
| [Suggest](review-15.3/suggest-1440.png) | the list open in the inspector under the label's row, the help box below it in sight, a help text and an improvement with its reason |
| [Suggest at 920 px](review-15.3/suggest-920.png) | the inspector over the canvas, the list in it |
| [Settings › AI](review-15.3/settings.png) | the profile picker with the chosen profile, and nothing that says it again |

## What was found, and what was done

| Before | After | Why |
| --- | --- | --- |
| "Draft a form with AI" in the create menu, beside "Form from a template…", "Import a form…" and "Folder…" | "Draft a form with AI…" | It opens a sidebar that asks for more, as its siblings do; one rule for the whole menu |
| Its description without the full stop the others end with | "From a description, or the text of a questionnaire you paste." | Say it the same way everywhere |
| German strings with a space before the ellipsis ("Mit KI übersetzen …", four in all) | no space ("Mit KI übersetzen…") | The copy rule 14.1 set for German |
| The draft sidebar opened with the focus on the sidebar itself (the browser test caught it) | the text box takes the focus in the frame after the sidebar takes it; Translate's language list likewise once the languages are read | The one thing to do is type; a keyboard user should not have to find the box |
| Suggest's popover opened below its button, over the help box its first **Use** fills; opened toward the canvas instead, it still covered half the box, as the inspector is narrower than any panel beside it | a disclosure: the button (`aria-expanded`) opens the list in the inspector itself, on a line of its own under the label's row and above the box; Escape closes it and gives the focus back to the button | An editor sees what **Use** changed, at the moment it changes; nothing floats over the inspector |
| Two solid primary **Use** buttons in one small panel | the secondary look | Several equal choices in a list; none is the screen's one main action |
| Inline, the list's grey was the secondary look's own, so each **Use** read as bare text | the list on the surface, edged with the border colour | Each button keeps its shape, and the list reads as one block between the label and the box |
| Translate and Suggest saved the draft before learning that the AI was not set up (the phase review's reader) | each asks first whether the AI can answer and says why not, saving nothing; Translate saves when the editor asks for the translation | Nothing changes on the editor's behalf for a request that cannot run |
| The mark's wand touching its first word | its own space | The tag reads as an icon and words, not one shape |
| Settings › AI said "The AI features use Fake chat" under the picker that showed Fake chat | the line speaks only while no profile is chosen (naming the default in use) or of a problem | Say it once |

## Kept as they are

- **Translate with AI… without a separator** after Keyboard shortcuts: one item of the add-on; a line for one item adds
  more than it separates.
- **The language as the site names it** ("Deutsch (Deutschland)"), in the list, the checkbox and the lead, as
  Umbraco's own language list names it; the editor chose that name.
- **The Suggest list's title only for screen readers:** the button that opened it already says "Suggest help text"
  beside the label; a visible title would say it twice.
- **The Design view's outer area overflows** by about a thousand pixels at 1440 px, before any selection and with or
  without the add-on, so a scroll into view can shift the whole view once. It predates Phase 15; Phase 16's hardening
  takes it (the handover note).
- **The drafted form's own fields** come from the fake provider, which writes "Name" and "Email address" first; a real
  provider's drafts are the owner's check (docs/OWNER-TODO.md).
