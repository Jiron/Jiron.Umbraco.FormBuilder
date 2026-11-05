# Step 10.1a: the form's Settings view

ROADMAP 10.1a fills the form workspace's Settings view, which showed the coming screen until now: one page of the groups
of section 4.3.4, Submission open and Availability, Spam protection, Appearance and Developer closed, each with the line
of what is set; each setting a change of the draft, as an inspector box is; the form check's problems under their
setting; and its Fix it, from the check's sidebar and the publish dialog, opening the group with the focus on the
setting ([ADR-0184](../decisions/0184-the-form-settings-view.md), building on ADR-0170, ADR-0173 and ADR-0181). Per
ADR-0019 and ADR-0020 the three design skills ran on the result; the design comes from Phase 2
([information-architecture.md](information-architecture.md), section 5.2, the wireframe `form-settings.html`, and
[copy.md](copy.md)). The owner answers no questions during the run (CLAUDE.md rule 8), so every choice was decided here
or in the ADR.

## What was looked at

The UX walk (`tests/e2e/specs/ux/walk.spec.ts`, ROADMAP 9.6a), which now shoots the Settings view too, run with
`JFB_UX_ONLY=settings` on the e2e host (the demo on `127.0.0.1:5520`, ADR-0031) in Edge: at 375, 768 and 1440 px, in
Umbraco's light and dark themes, in English and German, each shot with what axe and the layout measures found, and a
sheet per screen. The screens: the view as it opens, Submission's More options, each other group opened in turn, and a
form for some member groups that names none, its problem under the setting.

| Shot | What it shows |
| --- | --- |
| [the view](review-10.1a/settings-1440.png) | Submission open, the send button's label with the visitor's word as its placeholder, Store entries, the switches, the entry title with "Insert merge tag" under it |
| [More options](review-10.1a/settings-more-1440-dark.png) | the buttons between pages side by side, the summary page, the progress, the reference with its example; the closed groups with their lines |
| [Availability](review-10.1a/settings-availability-1440.png) | the site's clock named once, opening and closing side by side, the limit, the closed message, who can fill it in |
| [Spam protection](review-10.1a/settings-protection-1440.png) | "Site default (Standard)" and a line for each choice, the providers without keys dimmed, what that means said once under them, the content checks |
| [Appearance](review-10.1a/settings-appearance-768.png) | the themes and marks with the site's own named |
| [Developer](review-10.1a/settings-developer-768.png) | headless access and the names one a line |
| [a problem](review-10.1a/settings-problem-768-dark-de.png) | the member groups under "Who can fill it in", indented, the check's problem under them with its sign |
| [on a phone](review-10.1a/settings-375-de.png) | German at 375 px |

**Checks that ran on the last look:**

- **Axe** (WCAG 2.2 A and AA) on every screen in the twelve ways: nothing in the view's own elements. Umbraco's tree
  reports `label-content-name-mismatch` on its menu items and the sidebar's contrast in the dark theme, as on every
  screen of the section (ADR-0182, OWNER-TODO).
- **The bundled detector** (`impeccable detect --json`) on the view, its styles and the shared box styles: `[]` for
  each.
- **The layout measures:** no box scrolls sideways inside the view and no word is cut by an ellipsis at any width. The
  window scrolls sideways by 545 px at 375 and 152 px at 768, Umbraco's 920 px page, the same on every screen of every
  section (ADR-0182, decision 2).
- **The journeys** of `form-settings.spec.ts` and `designer-form-check.spec.ts`, below.

## design-taste-frontend

Read as: Operate-mode product UI inside Umbraco's own design system, for editors who set up how a form behaves, calm and
native. The skill's landing-page rules don't apply (ADR-0019); dials 2 / 1 / 5. What it still asks held:

- **No colour of its own:** the groups are Umbraco's surface with its shadow, the boxes the inspector's
  (`control.styles.ts`, now shared), a problem's colour Umbraco's danger, warning or muted text, always with a sign and,
  for a warning or a suggestion, its weight in words, so colour is never the only sign.
- **One radius scale** (UUI's) and one column of 760 px at most, centred in the workspace, so a line of help stays
  readable on a wide screen.
- **Every state composed:** the skeleton while the site's choices are read, a failed read with Try again, a switch that
  can't be turned now (verification without an email field) drawn as such with the reason in its help, a provider
  without keys, a theme or a field the site lost kept as chosen and named, a problem under its setting, and More options
  and a closed group counting the problems they hide.
- **Pre-flight:** no emoji, no gradient, no motion, the copy in plain words from copy.md's voice.

## impeccable

Operate mode, a refinement inside the incumbent world; the code, IA 5.2, the wireframe and copy.md were the context.
Critique against them and what the walk found:

| Finding | What was done |
| --- | --- |
| A setting that brings others under it (Email verification, "Who can fill it in") was indented with them, as a group's member, behind the same rule | the setting stays where it was; only what it brings is indented, under a rule |
| "Insert merge tag" beside the entry title's box broke onto two lines at every width | under the box, as the inspector puts it (ADR-0173) |
| A switch that can't be turned looked the same as one that can | dimmed, with the not-allowed pointer; its help says why |
| "Site default (Optional fields, with "(optional)")" nested a quote in a bracket | "Site default (Optional fields marked)", the closed group's own words |
| The CSS class's box ran the column's whole width for a word | the short width of the other one-line boxes |
| The note under each provider without keys repeated the same long sentence, two times on a site with neither | each says "This site has no keys for it yet."; what that means is said once under the choices |
| The protection's radios took the boxes' 36 px height from the shared rule for every input but a checkbox, pushing each choice's line away from its name | the browser's own radio at 18 px, beside its name, its line right under it |

## emil-design-eng

| Before | After | Why |
| --- | --- | --- |
| — | a group opens and closes at once, without motion | groups open many times a day; a slide would make each one wait |
| — | More options opens at once too, its chevron turning by swapping the icon | the same, and Umbraco's own disclosure does the same |
| — | the closed groups' lines change without motion or announcement | a line changes with every setting changed; the change in the box is what the editor watches |
| — | Undo and Redo say what they did in the view's status region | the change may be in a closed group; the words say it without moving the focus |
| a disabled switch drawn as an enabled one | `opacity: 0.55` and `cursor: not-allowed` | a control that does nothing must look it before it is pressed |
| a problem's colour alone telling an error from a warning | a sign before the words, and "Warning:" or "Suggestion:" for screen readers | colour alone fails WCAG 1.4.1 and a colour-blind editor |

## The words

English and German for every string, in `lang/settings.en.ts` and `settings.de.ts`, from copy.md's voice: help under
each setting that answers what the label leaves open. Two German words follow the glossary since the reader's review:
"Theme" (not "Design") and "Referenz" for the entry reference; the German threshold help says the number is written
with a point, as the box takes it. A limit of one entry is singular in both languages.

## Kept as they are

- **Umbraco's 920 px page** on a phone, as every section (ADR-0182).
- **The radio list's spacing:** each choice keeps its line under its name; the rows are a target as a whole.
- **No visible heading:** the view's tab names it; a Fix it to a setting the view doesn't show yet goes to the first
  group's header (ADR-0184, decision 9).

## A reader's review

A reader who had not seen the work read the change against the step, the ADR and the code. Every finding was fixed
before the tamper round, each with a test:

- **The focus lost** when Email verification or "Members of chosen groups" brought settings under it: the view drew a
  new control in place of the one in use. One template now holds the setting and what it brings.
- **A choice after Undo or Redo** showed what was picked before; its value now follows the draft.
- **A date typed in part** cleared the opening or closing without a word; it is now refused and said.
- **Problems with no box to show under:** under closed More options, at the member groups while anyone may fill the
  form in, at a provider's setting it doesn't describe. Each now shows at the box drawn for its setting, and More
  options counts what it hides.
- **A provider's stored value hidden** by the check's problem with it: the problem now shows under the box without
  holding what the box shows.
- **Verification could pick a field it can't confirm** (one kept only until the actions run); it now offers only those
  it can.
- **The buttons' placeholders** showed the package's word where the site has its own label, which visitors get; the
  site's labels now come with the choices.
- **An opening refused for the closing** stayed refused after the closing moved past it; it is now taken then.
- **The site's clock** was not said with the date boxes to a screen reader; the closed message's problems not with its
  editor. Both are now.
- **Colour alone** told an error from a warning; a sign and words now say it.
- **The words:** the glossary's German, the decimal point, the singular, the provider's line said once.
- **Tests that could not fail:** the provider's settings read once (checked on a provider whose schema is the shared
  empty one) and the time zone's round trip (which allowed any hour off by one); both now name what they hold.
- **Who reads the choices:** the member groups' names went to any user of the section; the choices are now a form's,
  for who may design it (`GET form/{key}/setting-choices`, ADR-0184 decision 10).
- **A stale Fix it:** one for the Actions view, still to come, waited for it; only a view that takes it up is handed one.

## What the journeys found

`form-settings.spec.ts` sets every setting of Submission and More options, says a number of days and of digits out of
bounds, inserts a merge tag into the entry title, follows the reference's example, undoes with Ctrl+Z, and reads the
draft as Save draft kept it; sets Availability on the site's clock with a closing before the opening refused,
the limit, the closed message and a member group, chooses a provider without keys and its threshold with a value out of
bounds, the content checks, the appearance and the developer's names with the reserved prefix refused; and follows the
form check's Fix it from the Design view's sidebar to the member groups and from the publish dialog to verification's
field, each with its problem under it, until fixing both clears them. Axe ran in each.

The first run failed on two locators: each protection choice's name took in its whole line, which the line then said
again as its description. That was the view's fault: each choice is now named by its name alone. A later run read the
draft back before Save draft's request had answered (its button is ready again before); the helper now waits for the
save's own answer, and three runs in a row passed.
