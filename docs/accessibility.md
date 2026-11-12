# Accessibility

What was tested of Jiron Form Builder against WCAG 2.2 at levels A and AA, how, and what is known to fall short. This page
states what the tests found. It does not claim conformance: conformance belongs to a whole page, and a page with a form is
the site's, with its own layout, navigation, language and content around the form. Step 16.3 of the roadmap made this
review, on the code of November 2026.

## What it covers

- **The website:** the markup Jiron Form Builder writes for a form and its outcome, in the **default theme** (the other
  built-in themes, `bootstrap5` and `unstyled`, run through most of the same suites), with and without JavaScript, the
  website runtime that enhances it, and the headless element. The page around the form is the site's: its title, its
  language, its skip links and its navigation.
- **The backoffice:** the screens of the **Form Builder section** inside Umbraco's backoffice: the library, the designer,
  a form's settings, actions, history, publishing, preview, entries and insights, the settings with Access, and Activity.
  Umbraco's shell (its header, sections, tree and dialogs) and its UI library are Umbraco HQ's.

## How it was tested

Every browser suite runs in Microsoft Edge through Playwright (ADR-0011). The suites named below live in `tests/e2e/specs`.

- **axe** with the WCAG 2.0, 2.1 and 2.2 A and AA rules, on the demo's *specimen*, a form with a field of every built-in
  type, in every theme, light and dark, left to right and right to left, at a phone's and a desktop's width, empty and
  with the errors of a refused send (`default-theme`, `unstyled-theme`, `keyboard-pass`, `runtime-states`), in each
  state a send goes through (28 website suites run axe), and on every backoffice screen (42 backoffice suites, and the UX
  walk, `ux/walk`, at 375, 768 and 1,440 pixels in Umbraco's light and dark themes, in English and German).
- **The accessibility tree**, read from Edge itself: the role, name, description and state of every kind of control and
  group, as a form first renders and after a refused send, with JavaScript and without (`names-pass`); and
  Playwright's accessibility snapshots where a suite checks what is or is not in the tree (`themes`).
- **The keyboard alone:** the specimen answered and sent with Tab, Space, Enter, the arrows and typing
  (`kitchen-sink-by-keyboard`); every kind of control showing its focus, measured in pixels, also in forced colours,
  and reached in the page's order (`keyboard-pass`); the designer's moves with the keyboard only (`designer-moves`).
- **Forced colours** (`emulateMedia({ forcedColors: 'active' })`): focus and the drawn controls (`keyboard-pass`,
  `focus-and-landing`).
- **Reflow and text spacing:** the specimen and the contact form at 640 and 320 CSS pixels, in every theme and both
  languages, and with WCAG's text spacing applied (`reflow`); every demo form at 375 pixels (`themes`).
- **Reduced motion:** nothing slides or rises (`keyboard-pass`).
- **Contrast:** axe's colour contrast in every theme and scheme; the focus ring measured at 3:1 or more against what it
  stands on (`focus-and-landing`); the backoffice's dark theme (`dark-theme`).
- **By hand:** the theme and screen reviews in `docs/ux` (the 6.x theme reviews, the Phase 9 to 12 UX walks), and the
  visitor texts in seven languages.
- **Not yet:** a session with a real screen reader (NVDA on Windows, VoiceOver on macOS and iOS) is the owner's to
  arrange ([OWNER-TODO](OWNER-TODO.md)). The names pass reads the same tree a screen reader reads, but not what one says.

A test, `scripts/tests/accessibility.test.mjs`, fails when a success criterion of WCAG 2.2 at A or AA is missing from
the table below, or when a row says nothing of how it was tested.

## The success criteria

"Website" is the form in the default theme; "backoffice" is the Form Builder section. A criterion that applies to the
page around a form says what the form does for it.

| Criterion | Level | Applies | How it is tested | Known gaps |
| --- | --- | --- | --- | --- |
| 1.1.1 Non-text Content | A | Website: yes. Backoffice: yes. | axe on the specimen in every theme and on every backoffice screen; `names-pass` reads every control's name; the signature pad has a typed name beside it (`signature-on-the-page`) | An image field's text and an option's image text are the editor's to write. |
| 1.2.1 Audio-only and Video-only (Prerecorded) | A | No: Jiron Form Builder shows no audio or video. | Not applicable | None |
| 1.2.2 Captions (Prerecorded) | A | No: no audio or video. | Not applicable | None |
| 1.2.3 Audio Description or Media Alternative (Prerecorded) | A | No: no audio or video. | Not applicable | None |
| 1.2.4 Captions (Live) | AA | No: no audio or video. | Not applicable | None |
| 1.2.5 Audio Description (Prerecorded) | AA | No: no audio or video. | Not applicable | None |
| 1.3.1 Info and Relationships | A | Website: yes. Backoffice: yes. | `names-pass` (fieldsets and their legends, labels, descriptions, the errors that describe a refused control); axe on every theme and screen | None known |
| 1.3.2 Meaningful Sequence | A | Website: yes. Backoffice: yes. | `keyboard-pass` (Tab follows the page's order), right to left in `journeys`; `designer-moves` | None known |
| 1.3.3 Sensory Characteristics | A | Website: yes. Backoffice: yes. | By hand: the visitor texts and the backoffice words name no shape, place or sound; a required answer is said in words | None known |
| 1.3.4 Orientation | AA | Website: yes. Backoffice: yes. | By hand: nothing reads or locks the orientation; the forms at phone widths (`themes`, `reflow`) | None known |
| 1.3.5 Identify Input Purpose | AA | Website: yes, on personal fields. Backoffice: no fields about the user. | `names-pass` checks `autocomplete` on the name, email, phone and address fields | A text field an editor uses for a personal answer gets no token; the name, email, phone and address types do. |
| 1.4.1 Use of Color | A | Website: yes. Backoffice: yes. | `focus-and-landing` (a refused scale or rating draws the error's colour and says the error); axe | None known |
| 1.4.2 Audio Control | A | No: nothing plays sound. | Not applicable | None |
| 1.4.3 Contrast (Minimum) | AA | Website: yes. Backoffice: yes. | axe in every theme, light and dark; `dark-theme` and the UX walk for the backoffice; the theme editor warns of a colour pair below 4.5:1 | A site's own theme colours are the site's; the editor warns. In Umbraco's dark theme some of Umbraco's own buttons, labels and the open tree row fall below 4.5:1; the section's elements set their own, and the rest is reported to Umbraco HQ ([OWNER-TODO](OWNER-TODO.md)). |
| 1.4.4 Resize Text | AA | Website: yes. Backoffice: yes. | `reflow` at 640 pixels (a 1,280-pixel window at 200%); the UX walk at 768 pixels | Umbraco's shell keeps a width of its own (see 1.4.10). |
| 1.4.5 Images of Text | AA | Website: yes. Backoffice: yes. | By hand: no text is drawn as an image; a signature is the visitor's own drawing, with its typed name beside it | None known |
| 1.4.10 Reflow | AA | Website: yes. Backoffice: yes. | `reflow` at 640 and 320 pixels in every theme, `themes` at 375; the UX walk at 375 and `narrowest` | Umbraco's backoffice is at least 920 pixels wide in every section, so below that it scrolls sideways as one page, its tree beside the workspace; the section adds no width (ADR-0182). Umbraco HQ's to change. |
| 1.4.11 Non-text Contrast | AA | Website: yes. Backoffice: yes. | `focus-and-landing` measures the focus outline at 3:1 or more; `keyboard-pass` in forced colours; the theme reviews in `docs/ux` | Control borders are checked by hand, not by a suite. |
| 1.4.12 Text Spacing | AA | Website: yes. Backoffice: yes. | `reflow` applies WCAG's spacing to the specimen, the contact form and the room hire in every theme, at 640 and 320 pixels | A date-and-time control in a narrow column scrolls its own text, as every text input does; a long word breaks, with a hyphen where the browser knows the language. Not tested in the backoffice. |
| 1.4.13 Content on Hover or Focus | AA | Website: no content on hover or focus. Backoffice: yes. | By hand: menus and popovers open on a press and close on Escape (designer specs press Escape) | Hover titles inside Umbraco's controls are Umbraco's. |
| 2.1.1 Keyboard | A | Website: yes. Backoffice: yes. | `kitchen-sink-by-keyboard`, `keyboard-pass`; `designer-moves` builds a form with the keyboard only; `entry-panel` | None known |
| 2.1.2 No Keyboard Trap | A | Website: yes. Backoffice: yes. | `keyboard-pass` tabs through the whole page; dialogs close on Escape and give the focus back (`designer-moves`) | None known |
| 2.1.4 Character Key Shortcuts | A | Website: no shortcuts. Backoffice: yes. | The entries list's keys of one character act only while the focus is in it (`entries.view.test.ts`); the designer's `?` only in the designer | None known |
| 2.2.1 Timing Adjustable | A | Website: yes. Backoffice: yes. | A redirect's countdown offers Stay here (`sending-on-the-page`); a challenge or a draft link expires without losing what is on the page | Umbraco's own session timeout is Umbraco's. |
| 2.2.2 Pause, Stop, Hide | A | Website: yes. Backoffice: yes. | By hand: nothing moves, blinks or updates on its own for more than five seconds; reduced motion in `keyboard-pass` | None known |
| 2.3.1 Three Flashes or Below Threshold | A | Website: yes. Backoffice: yes. | By hand: nothing flashes | None known |
| 2.4.1 Bypass Blocks | A | Website: the page's, the form adds an error summary whose links reach each field. Backoffice: Umbraco's shell. | `errors-on-the-page` (the summary's links move the focus) | Skip links belong to the site's layout. |
| 2.4.2 Page Titled | A | Website: the page's. Backoffice: Umbraco's shell. | Not tested: neither writes the title | The site's layout gives its pages their titles. |
| 2.4.3 Focus Order | A | Website: yes. Backoffice: yes. | `keyboard-pass`, `pages-on-the-page` (the focus on a new page and on the outcome), `designer-moves` (the focus after every move) | None known |
| 2.4.4 Link Purpose (In Context) | A | Website: yes. Backoffice: yes. | axe; `names-pass`; by hand: "Go now", the summary's links and "Change" name what they open | None known |
| 2.4.5 Multiple Ways | AA | Website: the site's. Backoffice: Umbraco's shell. | Not applicable to a form | None |
| 2.4.6 Headings and Labels | AA | Website: yes. Backoffice: yes. | `names-pass`; axe; by hand in the UX walks | A label is the editor's to word. |
| 2.4.7 Focus Visible | AA | Website: yes. Backoffice: yes. | `keyboard-pass` measures a focus ring on every kind of control in every theme and scheme and in forced colours; `focus-and-landing` | Umbraco's own controls draw Umbraco's focus. |
| 2.4.11 Focus Not Obscured (Minimum) | AA | Website: yes. Backoffice: yes. | `focus-and-landing`: a summary's link lands with its field below a site's sticky header, on a desktop and a phone | Not tested in the backoffice. |
| 2.5.1 Pointer Gestures | A | Website: yes. Backoffice: yes. | A signature is drawn freely or typed (`signature-on-the-page`); a ranking moves with buttons; the designer's drags have buttons and keys (`designer-moves`) | None known |
| 2.5.2 Pointer Cancellation | A | Website: yes. Backoffice: yes. | By hand: every control acts on release, as native buttons do | None known |
| 2.5.3 Label in Name | A | Website: yes. Backoffice: yes. | `names-pass` (each control's name holds its visible label); axe | Umbraco's tree rows (`uui-menu-item`) carry a fixed `aria-label`, so their names do not match their labels, the forms tree's among them; reported to Umbraco HQ ([OWNER-TODO](OWNER-TODO.md)). |
| 2.5.4 Motion Actuation | A | No: nothing acts on motion. | Not applicable | None |
| 2.5.7 Dragging Movements | AA | Website: yes. Backoffice: yes. | The ranking's moves without dragging; the designer's moves by buttons and keys (`designer-moves`); a signature can be typed | None known |
| 2.5.8 Target Size (Minimum) | AA | Website: yes. Backoffice: yes. | axe's target size rule in every theme and on every screen | In the unstyled theme the browser's own controls keep their size, the user agent's exception. |
| 3.1.1 Language of Page | A | Website: the page's. Backoffice: Umbraco's shell. | Not tested: the layout writes `lang` | The site's layout sets the page's language. |
| 3.1.2 Language of Parts | AA | Website: yes. Backoffice: yes. | Every form and outcome carries its own `lang` and `dir`, in the language it shows (`FormRenderingViewTests`, `FormRenderingAccessTests`); the backoffice's German screens in the UX walk | None known |
| 3.2.1 On Focus | A | Website: yes. Backoffice: yes. | By hand and in the keyboard suites: focus changes nothing but where the focus is | None known |
| 3.2.2 On Input | A | Website: yes. Backoffice: yes. | `rules-on-the-page`: an answer shows or hides fields in place and never sends or leaves the page | None known |
| 3.2.3 Consistent Navigation | AA | Website: yes. Backoffice: yes. | By hand: Back, Next and Send keep their place on every page (`journeys`) | None known |
| 3.2.4 Consistent Identification | AA | Website: yes. Backoffice: yes. | By hand: one name for one thing, the vocabulary of the copy guide | None known |
| 3.2.6 Consistent Help | A | Website: a help link is the site's; the form puts none. Backoffice: Umbraco's shell. | Not applicable to a form | None |
| 3.3.1 Error Identification | A | Website: yes. Backoffice: yes. | `errors-on-the-page`, `names-pass` (each refused control invalid and described by its error), with JavaScript and without | None known |
| 3.3.2 Labels or Instructions | A | Website: yes. Backoffice: yes. | `names-pass`; the note that says what marks a required answer; axe | None known |
| 3.3.3 Error Suggestion | AA | Website: yes. Backoffice: yes. | `errors-on-the-page`: each error says what to do, worded as the server words it | None known |
| 3.3.4 Error Prevention (Legal, Financial, Data) | AA | Website: where a form takes such commitments. Backoffice: yes. | A form's summary page shows the answers with a Change for each before the send (`journeys`); the backoffice asks before a deletion and offers Undo (`library`, `entry-panel`) | Whether a form offers a summary page is the editor's choice. |
| 3.3.7 Redundant Entry | A | Website: yes. Backoffice: yes. | Every answer kept across pages and refusals (`journeys`, `pages-on-the-page`, `sending-on-the-page`) | None known |
| 3.3.8 Accessible Authentication (Minimum) | AA | Website: yes. Backoffice: Umbraco's sign-in. | By hand: the protection asks no puzzle (ADR-0012); a third-party captcha is an editor's choice | A third-party captcha, when an editor chooses one, asks what its provider asks. |
| 4.1.2 Name, Role, Value | A | Website: yes. Backoffice: yes. | `names-pass` reads every control's role, name, value and state from Edge; axe | Umbraco's indeterminate checkbox (`uui-checkbox`) writes `aria-checked="false"` on the "select all" of a table that ticks rows, the entries list's among them; reported to Umbraco HQ ([OWNER-TODO](OWNER-TODO.md)). |
| 4.1.3 Status Messages | AA | Website: yes. Backoffice: yes. | The live region says an error, a send on its way and its outcome (`errors-on-the-page`, `sending-on-the-page`); Umbraco's toasts in the backoffice | None known |

4.1.1 Parsing is not in WCAG 2.2.

## For site owners

An [accessibility statement template](accessibility-statement-template.md), marked as a template, helps a site say what its
forms do; the page around them is the site's to describe.
