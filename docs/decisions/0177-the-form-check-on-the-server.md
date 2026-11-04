# 0177. The form check on the server

- Status: accepted; builds on [ADR-0002](0002-form-definition-model-versions-and-public-projection.md) (drafts keep
  problems, publishing refuses them), [ADR-0014](0014-merge-tag-language.md) (merge tags and where they render),
  [ADR-0035](0035-structural-validation-and-public-projection.md) (the structural validation's codes and paths) and
  [ADR-0044](0044-form-service.md) (the publish checks)
- Date: 2026-11-04
- Roadmap step: 9.5b (9.5d adds the checks that reason about the form's logic and the site, 9.5e the designer's side)

## Context

Publishing runs the publish checks of ADR-0044 and refuses every problem they find; nothing in that pipeline can warn
or hint, and nothing checks a draft without publishing it. Section 4 and IA 5.1 want a form check that weighs its
issues (errors block publishing, warnings and hints never do), that the designer can ask of the draft it holds before it
is saved, and whose every issue names the element and the setting a "fix it" link selects. The decision records already
promise some of its checks and their weight: a missing label, a text that recalls a later field and a consent text with
a source other than `form`, `page`, `site` or `dictionary` are errors (ADR-0002); a malformed tag and one past a limit are
errors, a tag that names nothing a warning (ADR-0014). Two disagree: ADR-0002 counts "an email action without an email
field" among the errors, ADR-0160 says the check can warn about it. ADR-0142 lets a form publish with a captcha
provider that has no keys, since keys differ between environments.

## Options

1. **Warnings beside the publish checks:** give every `DefinitionProblem` a weight. Every read, save and import that
   shares the type would carry a weight that means nothing there.
2. **A check of its own over the publish checks:** the publish checks keep their problems, which are all errors; checks
   that weigh their issues join beside them; one service runs both, for publishing and for the designer.

## Decision

Option 2.

- **The form check** (`FormCheck`) runs every publish check, whose problems are errors, then every `IFormCheck`, over
  the definition as the site runs it (the host's designer locks applied, ADR-0060). Its issues (`FormCheckIssue`) carry a
  weight (`error`, `warning`, `hint`), a code, the JSON path of the element and setting as ADR-0035 writes paths, and an
  English sentence the designer replaces by the code; the errors come first, then the warnings, then the hints, at most
  a hundred of each. Publishing refuses its errors as before; its warnings and hints never keep a form from going live.
- **The draft check:** `POST form/{key}/check` takes the definition the designer holds, read as a draft save reads it,
  and answers its issues. It needs `design` on the form, changes nothing, and refuses a definition it cannot read as a
  save does.
- **Its checks in 9.5b,** those the field types, the settings and the merge-tag engine already know:
  - a field visitors see without a label: an error (`label_missing`), since screen readers and the entries name it by
    its label, a hidden label too; content types and a value kept on the server need none. The image's alternative
    text is its type's required setting, which the structural validation already refuses;
  - every text where merge tags render, checked in its place: pages', groups' and fields' texts, the text settings a
    type marks, a consent's wording, default values (on the server for a value kept there), outcomes' messages and
    redirects, the entry title and the actions' templates. The engine's problems keep their codes: a tag that names
    nothing is a warning, every other an error; an action's type already refuses its templates' errors (ADR-0158),
    so only their warnings are added;
  - a redirect whose address recalls answers (`field.`, `entry.`): a warning (`answers_in_url`), since addresses end up
    in logs and browser history;
  - a captcha provider without its keys: a warning (`captcha_not_configured`), which keeps ADR-0142's choice;
  - an email action, switched on, on a form without an email field: a warning (`no_reply_field`), as ADR-0160 says; the
    settings that could never send are the action's own errors, which is what ADR-0002's error covers;
  - duplicate aliases and rules on removed fields stay the structural validation's errors, which the check lists.

## Consequences

- One service answers both publishing and the designer, so the badge can never call a form fine that publishing refuses.
- A site can add a check of its own by registering an `IFormCheck`; the integration tests do so to show that hints pass
  through the API, as 9.5d's first real hint (personal data without a consent field) will.
- A field without a label, which the designer called "Untitled field", now keeps its form from going live; forms made
  before 9.5b may need labels before their next publish.
