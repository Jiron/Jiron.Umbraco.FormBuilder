# 0096. The choice controls

- Status: accepted; extends [ADR-0015](0015-themes-and-the-markup-contract.md) (how a group of choices, one box, a
  grid and a ranking meet the contract, and the copy of the default button) and
  [ADR-0093](0093-the-razor-rendering.md) (the choice types' view models; options from a source rendered on the
  server); amends [ADR-0085](0085-visitor-texts-and-consent-wordings-on-the-form-endpoint.md) (the page carries the
  consents' hashes the answer holds)
- Date: 2026-10-29
- Roadmap step: 6.2d (decided and built); 6.2e renders the other types, 6.2f reads the moves and the switch of a post
  without JavaScript, 6.3 styles the classes, 6.4 enhances the controls, 7.1 shows the places left

## Context

Step 6.2d renders the controls of the choice types on a Razor page: radio buttons and checkboxes in a list, side by
side or as cards, yes or no, a scale, a rating, a dropdown and a list box, a grid, a ranking, a single checkbox, a
consent and a country. The specimen in `docs/ux/website/fields.html` and its README name the classes; the markup
contract (ADR-0015) names the hooks: a `legend` for a group of choices, an id per control, the name repeated for
several answers, `<alias>.<part>` for parts (ADR-0058), and the consent's hash input.

Open were: what wraps a group of choices and where its help and its error go; what labels a single box; how the
"Other" text is posted; how a page without JavaScript offers options that come from a library list or a source, or
that a parent field filters; how a grid names its rows and which input the error summary links to; how a ranking is
answered and reordered without JavaScript, when a move is a submit button before the page's Next; what a yes or no
switch posts while it is off; where the page gets a consent's hash; which marks one box and a grid take; what a
rating's labels say; how countries are ordered; and what shows when there are no options.

## Options

**Options from a source on a page without JavaScript:**

1. **Leave them to the runtime**, which asks the `options` endpoint. A page without JavaScript offers nothing, and a
   cascading field nothing even after its parent's page is posted.
2. **Resolve them on the server** with the same code as the endpoint (`CascadingOptions`, ADR-0081, ADR-0086): the
   options for the parent's answers the page holds, in the page's culture, for the content page. The page and the
   endpoint cannot disagree.
3. **Write every parent value's options into the page** for the runtime to filter. A source's options for answers
   nobody gave would be asked for, and some sources are per page or costly.

**A ranking without JavaScript:**

1. **A number input per option**, the position typed. Easy to get wrong (two options at 1), and the list does not
   show the order the visitor means.
2. **The order shown, each ranked option posting its position in a hidden input, and move buttons that are submit
   buttons**: the post renders the page again with the option moved (6.2f), so the list always shows the answer.
   Section 4.3's "a position number per option, checked for unique positions on the server" is what is posted.
3. **A select of positions per option.** As 1, with more controls on a phone.

**A yes or no switch that is off** posts nothing, as any checkbox does:

1. **A hidden `no` before the box**, so off posts `no` and on posts `no` and `yes`. A type that reads one answer
   finds two (ADR-0058).
2. **The no-JS post reads a switch on the posted page that sent nothing as `no`**, as the runtime sends it; the
   markup stays one box.

## Decision

Options 2 for each.

- **A group of choices** (radio buttons, checkboxes, yes or no as buttons, a scale, a rating, a grid and a ranking):
  `Field.cshtml` writes a `fieldset` in the field's `div`, with the label (and its mark) as the `legend`
  (`jfb-label`), then the help, the control and the error, so the fieldset names the help, the control's parts and
  the error in `aria-describedby` and carries `aria-invalid`. The inputs carry no `aria-describedby` of their own but
  an option's description. `FieldLabelElement.Legend` says so in the model.
- **One box** (a single checkbox, a consent, yes or no as a switch): the box with its own label, then the help, then
  the error, as the README asks; the box names them. The label is the type's text when it has one (a checkbox's
  text, a consent's wording, both sanitised) and otherwise the field's label, so a question is never said twice;
  the field's label still names the field elsewhere. `FieldLabelElement.Choice` says so.
- **Marks:** one box is marked only as required, with the asterisk style, since a box may stay unticked by nature; a
  switch is never marked, since off is an answer. A grid that is not required but has a required row is not
  "(optional)", and with the asterisk style each required row's legend carries the asterisk.
- **Ids:** the first option's input carries the field's id (`<instance>-<alias>`), the others `-2`, `-3` and on; an
  option's description `<input id>-text`; the "Other" text `-other`; a list box's hint `-hint`. A grid's first input
  carries the field's id and the others `-<row>-<column>`, both counted from 1; a ranking's first move button carries
  it. What shows when a field has no options carries it too, so the error summary always links to something.
- **Layouts:** `jfb-choices` with `-horizontal` or `-cards` (a field's `inherit` takes the form's `choiceLayout`),
  `-buttons` for yes or no, `-scale` in `jfb-scale` with `jfb-ends`, and `-rating` with its symbol's class
  (`jfb-choices-star`, `-heart` or `-circle`). A card shows its option's image, which adds nothing to the label
  (`alt=""`), with its width and height when the media item gives them; no other layout shows images.
- **"Other":** the "Other" choice posts no value (`value=""`, which the pipeline leaves out); its text input,
  `<alias>.other` with a visually hidden label, is the answer: in place of an option for radio buttons, after the
  options for checkboxes, as the types read it (ADR-0058). After a post the choice is chosen when the text is the
  answer. The labels are the field's, or the package's words.
- **Scales and ratings:** a whole number per choice; a rating's numbers are for screen readers (its symbols show).
  The words at the ends show below for sight (`aria-hidden`) and are heard with the first and the last number, so
  they are said once to each.
- **A dropdown** is a `select` in `jfb-select` with its prompt (the field's, or the package's "Choose one"); one
  that takes several is a list box (`multiple`, up to six lines) with a hint on how to choose several. A **country**
  is a dropdown of the allowed countries (all by default), the ones shown first in their order, then the others by
  their names in the page's culture, named from .NET's culture data as answers are shown (ADR-0066), with
  `autocomplete="country"`; its default country is chosen on a first render without an answer.
- **A grid:** the columns (the field's options, or the scale's numbers) show once in `jfb-matrix-head`, hidden from
  screen readers; each row is a `fieldset` with the statement as its legend and a choice per column labelled by the
  column, its inputs named `<alias>.<row key>` (ADR-0058). A required row's radios are `required`.
- **A ranking:** `ol.jfb-ranking` with the options in the visitor's order (as positions posted without JavaScript,
  or in order), then the rest in their own; each of the first `top` (all by default) shows its position and posts it
  in `<alias>.<value>`. Each option has two submit buttons, `jfb-nav` = `up:<alias>:<n>` and `down:<alias>:<n>`,
  `n` counting the options shown from 1; one at an end is `aria-disabled` and stays in the tab order. A form with a
  ranking starts with a visually hidden copy of the current page's Next or Send (`tabindex="-1"`,
  `aria-hidden="true"`), so Enter in a field is never a move: the first submit button in the form is the default.
- **A consent** carries `jfb-consent-<alias>` with the wording's hash the form endpoint gives (the visitor's form now
  hands the page its consents' hashes, `VisitorPage.Consents`); one whose wording shows nothing has none, so a tick
  without it cannot be read (ADR-0009).
- **Options:** a field's own options come from the form endpoint's projection in the culture. Options from a list
  or a source, and options a parent filters, come from `CascadingOptions` for the page's answers (a post's, or the
  defaults), in the page's culture, for the content page, so they are the `options` endpoint's for the same answers;
  a parent a rule hides answers nothing, as rules see it. A blank label shows the value, as the endpoint shows it.
  No options show "There is nothing to choose from yet." and a source that failed "The choices could not be loaded."
  (a disabled select with that line for a dropdown).
- **Places left** stay out of the server's markup: they change by the moment, and a page is the same for every
  visitor and may be cached; the runtime shows them (7.1).
- **A switch** that is off posts nothing; the no-JS post of 6.2f reads it as `no` when it was on the posted page and
  shown, and reads the moves (ROADMAP 6.2f, strengthened in this step).
- The built-in views stay at contract version 1: nothing has shipped yet, so version 1 is what the first release
  holds.

## Consequences

- A page without JavaScript offers every option the runtime would, a cascading field's once its parent's page is
  posted, as ADR-0015's rules do; the runtime replaces them as the visitor answers.
- Rendering a form waits for its sources, each held to 5 seconds as for the endpoint (ADR-0073); a source the page
  needs costs one call per render (the option service caches by its own rules), and a cached page shows a source's
  options as they were when it was stored (6.2h).
- An option's description is a hook the specimen did not show: the design's theme (`docs/ux/website`, which 6.3
  ships) now sets it under the option's label with the box beside both, a card's content keeps to its top so the
  labels of a row line up, and the specimen shows one in a list and one on a card.
- The words "Other", "Your own answer", "Choose one", "Choose a country", the hint for several, the two notes for no
  options, "Move {0} up" and "Move {0} down" and "Row {0}" are the package's, in English and German (14.2 adds the
  others); yes and no are the culture's words answers are shown with (ADR-0066).
- **Revisit** if the runtime replaces the list box with checkboxes, or a theme needs the ranking's position as an
  input.
