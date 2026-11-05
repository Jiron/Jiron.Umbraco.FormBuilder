# 0184. The form's Settings view: groups on one page, the site's choices from the server, and Fix it to a setting

- Status: accepted
- Date: 2026-11-05
- Roadmap step: 10.1a (10.1b adds the outcomes); builds on [ADR-0170](0170-the-inspector.md) (the inspector's editors),
  [ADR-0181](0181-the-form-check-in-the-designer.md) (Fix it) and IA 5.2

## Context

The form's settings (§4.3.4, `FormSettings` in Core) have waited behind the coming screen of the form workspace's
Settings view. IA 5.2 and the Phase 2 wireframe (`docs/ux/wireframes/form-settings.html`) draw one page of groups,
Submission and After sending open, the others closed with a summary line, each setting with its help below it, and
Submission's "More options". What 10.1a's survey found:

1. **Nothing describes the settings to the backoffice.** The schema reader that serves a field type's settings cannot
   read `FormSettings` as it is (a `DateTime`, a `SettingsObject`, two levels of nesting, keys of fields and member
   groups, rules between settings such as "store for days needs the days"), and the limits live in the save checks.
2. **The choices live on the server:** the captcha providers and whether each has its keys (host configuration, never
   sent), the themes (code and library), the member groups (Umbraco lists them only to users of its Members section),
   and the site's defaults that "Site default" stands for (spam protection, content checks, the required marker, the
   theme, all in the site's settings sections), and the site's time zone.
3. **Some settings configure nothing yet** (moved to their steps, WORKLOG): the Privacy group (11.5, 13.1, 13.3), Store
   entries "For a number of days" (13.1), "Save and continue later" (8.6b) and "One entry per member" (14.3).
4. **A captcha provider describes no settings,** though reCAPTCHA v3 reads a threshold from 0 to 1 that §4.3.4 names.
5. **The form check leads to the view** for any problem under `$.settings`, without saying where.

## Options

1. **A schema for the form's settings from the server,** drawn by the inspector's generic editors: one source for every
   label and bound, but a settings page drawn as a list of boxes loses the wireframe's shape (Next and Back side by
   side, verification's field and days under its switch, a reference's example), and the rules between settings need
   code anyway.
2. **A page of the client's own,** with every bound written again in TypeScript: the shape is right, but a bound can
   drift from the save check that enforces it.
3. **The page of the client's own, the bounds and choices from the server:** the view draws each group as designed, with
   the inspector's editors where a setting is one (a text in the language shown, rich text, a number, a switch, a
   choice); `GET form/{key}/setting-choices` serves the bounds the save checks enforce, the site's defaults (its own
   button labels among them), time zone, themes and member groups, and `GET captcha-provider` each provider's name,
   label, whether it has its keys, and its own settings as a field type's are served.

## Decision

Option 3.

1. **One page, IA 5.2's groups in §4.3.4's order:** Submission (open), Availability, Spam protection, Appearance and
   Developer (closed), each closed group with a one-line summary of what is set ("Always open · No limit", "Site
   default: Standard", "Default theme · Optional fields marked", "Headless access off"). The groups' open state is the
   browser's to remember, as the inspector's sections are. After sending comes with 10.1b, the Privacy group with 11.5.
2. **Each setting changes the draft** as an inspector box does: one step to undo per box visited, Save draft keeps it,
   Publish puts it live. A value is checked as it is typed, with the server's bounds, and said once the box is left.
   The form check's problems under `$.settings` show at their setting, in the designer's words (ADR-0181), as they do
   in the check's sidebar, each with its sign and, where it is no error, its weight in words; a closed group counts
   them on its line, and closed More options on its toggle. A problem of a setting the view draws only with another's
   choice (the member groups, verification's field and days, a provider's own setting) shows under that setting while
   it isn't drawn. A provider without its keys is said on its own line among the choices, what that means once under
   them, and not again as the check's warning.
3. **"Site default" names what it stands for:** "Site default (Standard)", from the site's settings, so nobody has to
   look elsewhere to know what a form gets; a button label's box shows the site's own label, else the package's word,
   as its placeholder.
4. **Submission:** the send button's label (in the language shown, the default word as its placeholder), Store entries
   (Always or Never; a number of days, which 13.1 offers, stays chosen until another choice), Manual approval, Email verification (the email field it confirms, from the form's fields that
   can, and the link's days) and the entry title with its merge-tag picker; More options holds the Next and Back
   labels, the summary page, the progress and the entry reference (prefix, year and digits, with an example).
5. **Availability:** opening and closing in the site's time zone, which the group names once, stored as UTC whole
   seconds (a time the clock shows twice is its first, one it skips moves on by the gap, as Temporal reads them), a
   closing before the opening refused at the box typed in; the entry limit; the message a closed form shows (rich text); who may fill it in (anyone, members, or
   members of chosen groups, chosen by name).
6. **Spam protection** is a list of choices with a line each, as the site's own setting is drawn: the site's default,
   Standard, Standard with proof of work, then each captcha provider; a provider without its keys is dimmed and says a
   developer adds them, never which key or value. A chosen provider shows its own settings (reCAPTCHA v3's threshold),
   which `IFormBuilderCaptchaProvider.SettingsType` describes as a field type's settings are, and the form check holds
   them to that description (`setting_invalid`). Then how strict the content checks are.
7. **Appearance:** the theme, the required marker, the validation summary, the choices' layout and a CSS class.
   **Developer:** headless access, the query values and the context values, each a list of names.
8. **Texts in the language shown:** the view keeps the Design view's language, and offers the same switch when the
   site has more than one; the workspace keeps the language either switch chose, so the other view opens in it.
9. **Fix it to a setting:** a problem at `$.settings.<group>.<setting>` opens its group, and More options where the
   setting is there, and puts the focus on the setting's first box, the problem said under it, in the language of the
   text it is in. A path the view does not show (a setting of a later step) leads to the first group's header: the
   view has no heading of its own, since its tab names it (say it once). The workspace hands each Fix it to the view
   that makes it, the Settings view one in the form's settings and the Design view any other, once that view is shown;
   a view still to come (Actions, until 10.2) is handed none, so no Fix it is made long after it was asked.
10. **Who reads the choices:** a form's choices need `design` on it, as the member groups' names are the Members
   section's to show; the captcha providers, which describe the site's code, any user of the section.

## Consequences

- The bounds have one source, the save checks' constants, which `GET form/{key}/setting-choices` serves and an
  integration test holds to them; the choices come from the registries and settings that decide them.
- A site's own captcha provider can describe its settings with the attributes a field type uses.
- Rejected: a schema-drawn page (option 1), which loses the designed shape; bounds written twice (option 2).
