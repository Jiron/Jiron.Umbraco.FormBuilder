# 0136. The form picker on the demo

- Status: accepted
- Date: 2026-10-31
- Roadmap step: 6.7h; builds on [ADR-0028](0028-demo-site.md) (the demo), [ADR-0094](0094-demo-forms.md) (its forms),
  [ADR-0130](0130-the-form-block-type.md) (the form block) and [ADR-0129](0129-the-form-pickers-settings.md) (the page
  after a send)

## Context

The demo shows every feature, and the browser tests run on it (ADR-0031). The form picker arrived in 6.7a to 6.7g; the
demo's pages still name their form by its alias in a text property, which shows a developer `<jfb-render form>`, not
an editor's picker. 6.7h asks for a page type with a block list that allows the form block of 6.7b, a page with the
block, a thanks page for the picker's page after a send, and a browser test of an editor picking a form in a block and
a visitor sending it.

## Options

- **Where the block comes from:** (a) an element type of the demo's own: it would show nothing of 6.7b; (b) 6.7b's
  service, which the seeder calls as a site builder presses "Create a form block for me". (b) is chosen. The demo
  then does what a site builder does next: it sets the picker's page after a send in the data type's settings, once,
  right after the service created the data type, and allows the block in its block list.
- **Which block list:** (a) a second list on a new page type: two lists side by side help no one; (b) "Demo blocks",
  which every page type of the demo has, with the form block beside the text and the highlight, labelled with the
  form's name (`{jfbFormName: form}`, 6.7f). (b) is chosen; the demo's page type is "the page type with a block list
  that allows the form block".
- **Which page and form:** a new page "Volunteer" (German "Ehrenamt") in the menu, with a text block and a form block
  that picks the volunteer sign-up, the demo's form that no page showed so far. Its block names the form by its
  alias in `DemoContent.json`; the seeder writes the form's key, which exists only once the forms are seeded (they
  wait for the package's migrations), so a page whose blocks hold a form is created after the forms, and every other
  page before them as until now.
- **The thanks page:** (a) a page of the existing page type: the menu would list it; (b) a page type of its own,
  "Demo thank-you page", which the menu leaves out as it leaves out the specimen. (b) is chosen: "Thank you" under
  `/en/thank-you/`, "Danke" under `/de/danke/`.
- **The page's own `form` text property** stays: it shows a site's developer naming a form in a view, and the demo's
  browser tests read it. The block shows the editor's way.
- **Who the editor is in the browser test:** (a) the administrator: it proves nothing about content access; (b) a
  persona in Umbraco's own Editors group (Content and Media, publishing, no Forms section), the reader ADR-0013's rule
  serves: it may pick published forms by name without the Forms section. (b) is chosen, as the persona `editor`.
- **The test's page:** the test makes its own page through the Management API, so the seeded pages stay as visitors
  see them, and the editor adds the block, picks the newsletter (the shortest form), saves and publishes in the
  backoffice; the visitor sends it with JavaScript and lands on the thanks page.

## Decision

- The seeder calls `FormBlockTypes` before it makes the block list; when the call created the picker's data type, it
  sets `redirect` to the thanks page's key. "Demo blocks" allows the form block, labelled `{jfbFormName: form}`.
- `Views/Partials/blocklist/Components/jfbFormBlock.cshtml` renders the picked form with `<jfb-render picked>`, in the
  demo's chosen theme when there is one, as any site writes a view for its blocks.
- `DemoContent.json` gains the volunteer page, whose form block names `volunteer`, and the thanks page of the type
  `demoThanks`; a block with `form` is a form block.
- The e2e personas gain `editor` (Umbraco's `editor` group); the smoke checks of sections treat it as they treat the
  outsider, a persona without the Forms section.
- `specs/website/form-picker-on-the-page.spec.ts` walks the editor's and the visitor's way and runs axe on the picker
  (in the block's editor and its sidebar) and on its settings, counting the violations inside the picker's elements.
- **Found on the way:** a post without JavaScript goes to the form's own address with the form's fragment (`#jfb-1`),
  so that a page shown again opens at the form. A redirect without a fragment carries the request's on (RFC 9110,
  10.2.2), so a visitor sent to an outcome's page or to the picker's page after a send landed at whatever bore that id
  there: on the demo, the newsletter form in the thanks page's footer. Such a redirect now carries a fragment of its
  own, an empty one when its address has none, which opens the page at its top; the sent page keeps `#<instance>`.

## Consequences

- A demo started before 6.7h needs `reset` to show the new page, the block and the thanks page, as the README says
  for every change of types and pages.
- Every form block on the demo goes to the thanks page after a send, as the picker's settings say; the forms named by
  the pages' text property keep their own outcome.
- Axe's check of the dark theme stays in the review: Umbraco's positive button there falls short (OWNER-TODO, 6.7f).
