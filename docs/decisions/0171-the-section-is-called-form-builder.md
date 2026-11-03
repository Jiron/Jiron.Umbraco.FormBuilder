# 0171. The section is called "Form Builder"

- Status: accepted
- Date: 2026-11-03
- Roadmap step: 9.4a (made during it, at the owner's word); it changes D1 and D12

## Context

D1 and D12 named the backoffice section **Forms**, and CLAUDE.md's conventions repeated it. During 9.4a the owner wrote
that the section in Umbraco's top bar should not be called "Forms" but "Form Builder". CLAUDE.md rule 8 says such a
message beats the plan: it is recorded here, and the plan follows it.

The word "Forms" was doing three jobs: the section in the top bar, the sidebar header above the forms tree, and the tab
of Umbraco's header search that finds forms. Several texts for users and administrators also named the section: the
`403` for a user without it, the reason the authorization logs, the log line about a site's time zone, the description
of the form block's element type and the migration's log lines.

## Options

1. Rename every "Forms": the section, the tree's header and the search tab. The tree and the tab hold forms, though,
   and a header "Form Builder" above a list of forms names the product where Umbraco names the content ("Content" above
   the content tree, "Media" above the media).
2. Rename the section only, where the owner asked, and keep "Forms" where the word names forms.
3. Also change the section's alias and pathname to match. They are not shown to anyone but in the address bar, user
   groups already hold the alias, and links in sent emails and the merge tags of ADR-0067 carry the pathname: renaming
   them would break what exists for no one's benefit.

For German: Umbraco translates its sections' names ("Inhalt", "Medien"), but "Form Builder" is the product's own name,
as in "Jiron Form Builder", which no language translates.

## Decision

- **The section is "Form Builder" in Umbraco's top bar (option 2),** from the term `jfbSection_section`, in English and,
  as a name, in German too. The forms tree's header stays "Forms" ("Formulare"), and so does the header search's tab.
- **The alias `Jiron.FormBuilder.Section` and the pathname `jfb-forms` stay,** with the grants, user groups and links
  that use them. Nothing stored changes.
- **Texts that name the section say "the Form Builder section":** the `no_section` problem, the authorization's reason,
  the time zone's log line, the form block type's description (for sites that create it from now on; a type created
  before keeps its words), the migration's log lines, the management API's description and the copy for the access
  screen.
- **The documents that describe the build as it is now** (CLAUDE.md, ROADMAP's D1 and D12, PRODUCT.md, DESIGN.md,
  architecture.md, database.md and the information architecture) use the new name. Earlier ADRs, the worklog, finished
  steps and the wireframes keep their words: where they say "the Forms section", they mean this section.
- **A check in a real browser:** the smoke journey of every persona reads the top bar's labels and finds "Form Builder"
  wherever the persona has the section; the manifests' test pins the term and the tree's "Forms".

## Consequences

- No roadmap step was added; the change rode on 9.4a, as the owner asked for few new steps.
- A screen, help text or email that later names the section uses "Form Builder" and the term `jfbSection_section`.
- Revisit if the owner names the section differently in another language.
