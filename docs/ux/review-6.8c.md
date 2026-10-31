# Step 6.8c: the refused groups looked at in Edge

ROADMAP 6.8c moves a refused group's error state from its fieldset to its own choices and writes the word "required"
for screen readers where no control can carry `required` ([ADR-0113](../decisions/0113-errors-on-the-page.md),
[ADR-0093](../decisions/0093-the-razor-rendering.md)). This file records how the result was looked at and what the
look changed.

## What was looked at

The server's refused pages that `ErrorFixtureTests` writes (`tests/fixtures/rendering/errors-en.json`, every required
field left empty), in the default theme with the optional-style marks and in `bootstrap5` with the asterisks, each with
the built stylesheets (`base.css` and the theme's), in Edge through Playwright at 1280 and 375 px. Edge's accessibility
tree was read through DevTools' protocol and, for what a screen reader gets, through Windows UI Automation on a visible
Edge window.

| Shot | What it shows |
| --- | --- |
| [a radio group, default](review-6.8c/default-1280-way.png) | "Way" refused: each radio button drawn with the error's border, the error last in the group |
| [a group of boxes, default](review-6.8c/default-1280-extras.png) | "Extras" refused: each box with the error's border |
| [a grid, default, 375 px](review-6.8c/default-375-rate.png) | "Rate" refused for its required row: every row's radio buttons marked, as the error names no row |
| [a group of boxes, asterisks](review-6.8c/bootstrap5-1280-extras.png) | "Extras \*" in `bootstrap5` without Bootstrap's own stylesheet: the asterisk shows, the word "required" does not |

## What Edge says

| Control | Accessibility tree |
| --- | --- |
| A refused group's radio buttons and boxes | invalid, described by the error ("Choose an answer", "Choose at least one answer", "Choose an answer in each row") |
| Each group's fieldset | a group named by its legend, not invalid, without the error in its description |
| The required group of boxes under the asterisk style | the group named "Extras required"; its boxes not required, as none of them can be |
| The required radio group and the grid's required row | each radio button required (UI Automation's `IsRequiredForForm`) |

DevTools' accessibility pane lists `required` for text boxes only. UI Automation shows that Edge passes it on to
screen readers for text boxes, selects, dates, files, single boxes and radio buttons alike, so the word stays where no
control carries `required`: a group of boxes, a grid's row of boxes and a ranking.

## What the look changed

- **The themes' error borders** keyed on a group's `aria-invalid`, which the fieldset no longer carries: yes or no as
  buttons, a scale, choice cards and a `bootstrap5` rating would have lost their error look, and the `bootstrap5` spec's
  refused card colour would have failed. Their rules now key on the choices' own mark, in `default.css` and
  `bootstrap5.css`. The scale's and the `default` rating's borders remain 6.8d's finding, with its test.
- **A signature's typed name** keeps its own mark on the server; the runtime's mark for it, and the pad's error border
  drawn from it, go with the pad in 6.4n, whose text now says so.

The theme, error, rules, no-JavaScript and unstyled specs ran in Edge after the change: 48 passed, axe clean with the
errors shown.
