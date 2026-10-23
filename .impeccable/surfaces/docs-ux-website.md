---
version: 1
slug: "docs-ux-website"
primary_target: "docs/ux/website"
related_targets: []
---

# The website form (default theme)

Scope: the default theme of Jiron Form Builder's rendered forms, the surface a visitor fills in inside a customer's
Umbraco site (ROADMAP 2.3; built in 6.3). Visitor mode: Operate. The visitor completes a task, mostly on a phone.

- **Audience and job:** a member of the public who came to the site for something else and now has to book, apply,
  ask or report. They want a short form that says what it needs, what is wrong and that it worked.
- **Constraints:** the host site's font and page; the markup contract and tokens of ADR-0015; WCAG 2.2 AA; targets of
  at least 44 px; one column on phones; nothing from other hosts; the default theme CSS at most 10 KB gzip.
- **Memorable moment:** an error summary that names each problem and its fix, one tap from the field.
- **Unresolved:** none for 2.3; the motion is specified here and built by the runtime in 6.4.

## Direction contract

THESIS: The form reads like a well-kept ledger set into the host page. Each titled group owns a quiet heading column
beside its fields when the form is wide, and the whole form becomes one calm column on a phone. It refuses the boxed
card inside a card, floating labels and placeholder-as-label.

OWN-WORLD: The site's own font and ground. Cool ink text, one evergreen accent for actions and selection, a separate
blue focus ring, hairline rules between groups, 48 px controls with half-step corners from one radius, and a single
ink-tinted shade kept for card-style groups. No illustration, gradients or decorative icons.

STORY: The visitor sees how long the form is, reads what each group asks and why, fixes every error from one list
that names the field and the fix, checks the answers, and leaves with a reference.

FIRST VIEWPORT: At 1440 px, under the site's own heading: the step list across the form, the page title, then the
first group with its heading and one line of help in the left third and its fields in the right two thirds; the
primary button lines up with the fields. At 375 px: a bar with "Step 1 of 3", the title, then each group's heading
above its fields.

FORM: Question ledger, position 3 of the ordered list of seven structures; seed key 3155e951.

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance
