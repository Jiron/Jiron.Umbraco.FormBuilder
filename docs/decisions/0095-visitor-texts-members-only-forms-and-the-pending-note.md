# 0095. Visitor texts, members-only forms and the note while the migrations are pending

- Status: accepted; amends [ADR-0093](0093-the-razor-rendering.md) (a visitor text with tags renders on the server;
  `inherit` and the labels a form leaves empty take the site's; what renders for a members-only form and while the
  migrations are pending), [ADR-0043](0043-backoffice-settings-sections.md) (`general` gains the required-marker style
  and the three button labels), [ADR-0084](0084-the-public-apis-frame-and-the-form-endpoint.md) and
  [ADR-0085](0085-visitor-texts-and-consent-wordings-on-the-form-endpoint.md) (the visitor's form carries the site's
  style and labels) and [ADR-0033](0033-schema-v1.md) (how a website page knows a backoffice user)
- Date: 2026-10-29
- Roadmap step: 6.2c (decided and built); 6.2h fills the post's answers and rows, 6.2l keeps the renders that are per
  visitor out of the output cache ([ADR-0104](0104-the-output-cache-rules-and-the-render-endpoint.md)), 7.1 adds the member-bound challenge, 12.4a the settings screens, 14.3 the members'
  submissions

## Context

6.2b renders a published form in the page's culture (ADR-0093) and leaves four things to this step:

- A visitor text that holds merge tags (ADR-0068), such as a label that recalls an earlier answer, reached the page as
  its template. The form endpoint gives the browser what it needs to render it (ADR-0085), but a page without
  JavaScript has nothing that does.
- A form for members (`availability.access`) rendered for everyone, though the entries endpoint refuses its
  submissions without a member (ADR-0088).
- While the migrations are pending a form rendered nothing for anyone; ADR-0033 promises a note for a signed-in
  backoffice user.
- A form whose required-marker style is `inherit`, or that leaves a button's label empty, needs the site's choice.
  Section 4.8 puts it in `general`, which held no such field yet.

## Options

**Visitor texts with tags on the server:**

1. **Leave them to the runtime (6.4).** A page without JavaScript, and every page until the runtime starts, shows the
   template or nothing.
2. **Render each through the merge-tag service** in the visitor-text place, with its page, group or field as the
   text's owner and the answers the page holds. The service decides what a tag shows, as it decides the endpoint's
   values, so the server and the browser show one text. Chosen.

**Where the site's style and labels apply:**

1. **In the Razor view models only.** The endpoint would answer `inherit` and leave the labels out, and every headless
   page and the runtime would need the site's settings as well.
2. **In the visitor's form,** which the endpoint's answer and the Razor page share (ADR-0093). Chosen: the model data
   stays equal to the endpoint's answer.

**Whom a members-only form admits on the page:**

1. **Render the form for everyone** and let the submission refuse it. A visitor fills in a form only to learn at the end
   that it is not theirs.
2. **Ask who is signed in when the page renders,** and show a notice in the form's place to a visitor it does not admit.
   The render is then per visitor. Chosen; 6.2l keeps such a render out of the output cache.

**How a website page knows a backoffice user:**

1. **`AuthenticateBackOfficeAsync()`.** Umbraco authenticates its backoffice cookie only on backoffice paths, so on a
   website page it answers "none" even for a signed-in user.
2. **Read the backoffice scheme's ticket from its cookie,** as Umbraco's own preview does on website pages. Chosen.
3. **Show the note to everyone.** It tells every visitor about the site's state, and a visitor can do nothing about it.

## Decision

- **Visitor texts with tags.** The visitor's form lists the texts that hold tags (ADR-0085) with their owner and name:
  a page's and a group's title and description, a field's label, help and placeholder, and the texts among its public
  settings. The page renders each through `IFormBuilderMergeTagService` in `MergeTagPlace.VisitorText`, with the text's
  owner as `TextOwner`, as HTML for a rich text and as text otherwise, and shows the result in its place, the
  progress's step names included. The place takes the field, form, page, site, date and dictionary sources, never the
  member or the query, so a first render stays the same for every visitor.
- **The answers a text recalls** are the page's: on a first render the default values without tags (ADR-0093), after a
  post the posted answers (6.2h's state). Each is read by its field's type as both runtimes read it (ADR-0058). An
  answer past what its field's type takes (more texts, or longer ones, than the type accepts) or one its type cannot
  read, a field whose value stays on the server and a field the rules hide recall nothing (ADR-0068). A field's own
  checks, such as its longest answer, are the submission's: an answer that fails them still recalls, as in the
  browser. A calculation's value joins in 6.2e, and a repeatable group's rows with the post state's rows in 6.2f.
- **The site's style and labels** (`general`, per site, ADR-0043):
  - `requiredMarker`: `optional` (the default), `required` or `none`. `inherit` cannot be saved: it is how a form takes
    the site's style.
  - `submitLabel`, `nextLabel` and `backLabel`: a text per culture, checked as a form's own label is (one line, no
    HTML, the culture codes a definition takes). Empty means none.
  - The visitor's form replaces a form's `inherit` with the site's style and gives each button the form does not label
    itself the site's label, so the endpoint's answer, the model data and the markup agree. A form's own values win;
    without a site's label the page shows the package's word, and a stored label that is no localised text counts as
    none. ADR-0008 already lets the button labels and the marker reach the website; its allowlist test fills them and
    finds them in the answer.
- **Members-only forms** (`availability.access`):
  - `anyone`: nobody is asked who is signed in.
  - `members`: a visitor who is not signed in sees the notice "This form is for members", "Sign in to fill it in." in
    the page's culture (`NoticeKind.SignIn`); a member sees the form.
  - `groups`: a member of one of the form's member groups sees the form. Groups are compared by name, in any letter
    case, since Umbraco gives a member's roles by name. A group that no longer exists admits nobody, so a form whose
    groups are all gone admits no member. Another member sees "This form is not open to your account"
    (`NoticeKind.MemberGroups`); a visitor who is not signed in is asked to sign in first.
  - A form that takes no entry now (not open yet, closed, full) shows that notice first, since signing in would not
    help.
  - Until 14.3 builds members' submissions, the entries endpoint still refuses a submission to a members-only form
    (`member_required`). The page only stops showing a form its visitor cannot send.
  - Who is signed in decides what the page shows, so the render is per visitor and 6.2l sends it `no-store`. The first
    render's rules still run without the member (ADR-0093); member values reach the runtime through the challenge
    (7.1).
- **While the migrations are pending** a form renders nothing for a visitor, and the rest of the page renders as usual
  (ADR-0033). A signed-in backoffice user sees in the form's place the default folder's Notice view
  (`NoticeKind.MigrationsPending`): "This form waits for an update", and that visitors see nothing until the database
  update has run and only signed-in backoffice users see the note, in the page's culture. No form is read, so the note
  stands alone: no form element, no model data, no assets and no form's own views.
- **A backoffice user on a website page** (`BackOfficeVisitors`): the cookie of Umbraco's backoffice scheme, its chunks
  joined, unprotected with that scheme's ticket format, unexpired by the site's clock and holding a backoffice
  identity. It signs nobody in and writes nothing. Its limits:
  - it does not check the user's security stamp, so a user whose sessions were ended elsewhere still sees the note
    until the cookie expires; the note tells a backoffice user nothing they may not know;
  - a ticket store that keeps tickets on the server leaves only a reference in the cookie, which holds no backoffice
    identity, so the note does not show: the check fails closed.
- **The package's words** for the notices and the note come in English and German, by the culture and its parent
  (ADR-0093); 14.2 adds five languages.

## Consequences

- A label that recalls an answer reads the same with and without JavaScript, from one service.
- A headless page gets the site's style and labels in the endpoint's answer and needs no settings of its own.
- A page with a members-only form, and any page with a form while the migrations are pending, renders per visitor;
  6.2l's cache rules cover both, and its check shows neither stored (ROADMAP 6.2l, strengthened in this step).
- The settings screens of 12.4a show the four new fields; until then the settings service sets them.
- **Revisit if** sites need a sign-in link in the members' notice (a setting that names the login page); Umbraco offers
  a supported way to know the backoffice user on a website page; or a date in a visitor text proves stale on cached
  pages before the runtime renders it again.
