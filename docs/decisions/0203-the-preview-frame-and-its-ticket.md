# 0203. The preview: its frame, its ticket and its choices

- Status: accepted
- Date: 2026-11-06
- Roadmap step: 10.3e (10.3f adds test entries and sample answers); amends
  [ADR-0012](0012-anonymous-endpoints-spam-protection-and-challenges.md) (the preview ticket) and
  [ADR-0084](0084-the-public-apis-frame-and-the-form-endpoint.md) (the `null` origin); builds on
  [ADR-0168](0168-the-canvas-draws-with-the-elements-views.md) (every theme in Preview); IA 5.7, copy.md

## Context

IA 5.7 asks for Preview in the form workspace's footer: a full-screen modal with the draft rendered by its theme in a
frame, at the designer's widths (Desktop 960 px, Tablet 768 px, Phone 375 px) with "Narrowed to fit", in light or dark
and in another language. §4.10 asks that the frame never reach the backoffice: sandboxed without `allow-same-origin`, so
its requests come from the opaque origin `null`, which the public API accepts only with a short-lived signed ticket
bound to the form (ADR-0012: Data Protection, the form and its draft version, ten minutes), issued to who may design it.
ADR-0084 refuses `null` like any other origin it does not allow.

The canvas draws the form in the default theme inside the backoffice (ADR-0168); a form's own theme, a site's own views
and the website runtime belong on a page of the site, which is what the preview shows. The public API serves published
forms only: its challenge, options and page checks read the version visitors get. A draft may hold rules that both
runtimes' check refuses (ADR-0002, ADR-0061); the server's first render then throws, and the website runtime runs none
of the form's rules. Polls must pause while the user is idle and never renew a session (§4.10).

## Options

**Where the frame's document comes from**

1. The frame's `src` is a public page of its own, the ticket in the fragment, which then fetches the draft's markup:
   a second route outside the management API, a page that injects markup, and two round trips before anything shows.
2. **The management API renders the document**, the draft through the view component a page uses, with the theme's
   stylesheets, the runtime and the ticket, and the backoffice writes it into the frame's `srcdoc`: one authorised
   request, and the document's address is the backoffice's, so its root-relative addresses reach the site.
3. The backoffice draws the form, as the canvas does: the site's theme and runtime would run inside the backoffice.

**Changes not saved yet**

1. A preview of the definition as it is now, kept on the server for the ticket: state that must work across servers,
   and a definition nobody saved reaching the public API.
2. **Preview saves them first**, as Publish does and as Umbraco's own Save and preview does: the ticket names a stored
   version, which every request of the frame reads.

**How the ticket stays valid**

1. The website runtime reads the ticket and asks for a new one: bytes for every visitor, and a change at every place
   that calls the API.
2. The backoffice sends a new ticket every few minutes: a poll that keeps the session alive while the user is away.
3. A longer ticket: a weaker one.
4. **A preview script, loaded by the preview's document alone**, gives the frame's requests to the public API their
   ticket and asks the backoffice for a new one when one is needed, which only a request the user made in the frame
   does.

**A draft whose rules the check refuses**

1. Render it without its rules: a third way to evaluate a form, on the server and in the public API's page checks.
2. **Say so in the preview, with the form check**, which lists each one with Fix it: the preview never evaluates a
   broken rule (ADR-0002).

## Decision

Option 2, 2, 4 and 2.

- **The document:** `GET /umbraco/jfb/management/api/v1/form/{key}/preview?culture=&dark=`, which needs `design`,
  answers the saved draft as a page of the site shows it, in a document of its own titled with the form's name: the
  form's theme (the site's default for a form without one), the culture asked (the site's default for one it lacks), the
  base rules and the theme's stylesheet, the runtime, the preview script and the ticket, with `no-store`; also the
  draft's version, the culture shown, whether the theme offers the dark scheme and how many seconds the ticket counts.
  The form and its buttons have no address to post to, as the headless element's (ADR-0128), so the runtime never falls
  back to a post that would leave the frame. A draft whose rules the check refuses answers `422 rules_invalid`.
- **The frame** is `sandbox="allow-scripts allow-forms allow-popups allow-popups-to-escape-sandbox"`, never
  `allow-same-origin`: an opaque origin with no cookies and no access to the backoffice. A link in a consent text opens
  in a tab of its own, outside the sandbox, as on the site.
- **The ticket** (`Jiron.FormBuilder.Preview.v1`) holds the form, the version and when it expires, ten minutes after it
  was issued. It travels as `Authorization: Jfb-Preview <ticket>`, never in an address. `POST form/{key}/preview/ticket`
  (`design`) issues a new one for a version of the form.
- **The public API** (amends ADR-0084): a preflight from `null` answers `204` with `Content-Type, Authorization`; a
  request from `null` passes only with a ticket that holds, and gets `Access-Control-Allow-Origin: null`; without one it
  is refused as before. A ticket that is malformed, another purpose's, expired, another form's, or sent to an endpoint
  the preview does not use answers `401 preview_ticket_invalid` with `WWW-Authenticate: Jfb-Preview`, as a server key
  that matches none does (ADR-0126). The endpoints that take a ticket say so (`[FormBuilderPreview]`): the challenge,
  the options and the page check now; the uploads, the entries and the receipt with 10.3f's test entries.
- **What a ticket's request reads:** the version it names, read as the website reads a published one (upgraded, the
  host's designer locks applied), published or not, with the revision it would get once published, the form's next.
  Its page check reads that version's aliases whatever revision the page posts, so a publish from another tab leaves
  the frame on the version it shows. A ticket stands in for a test member (ADR-0012): a form for members renders,
  challenges and checks as for a member it admits, with no member.
- **The preview script** (`/jfb/assets/preview/jfb-preview.js`) wraps the document's `fetch` and `XMLHttpRequest`: a
  request to the public API gets the ticket; with less than two minutes left, it first asks the backoffice for a new
  one, and goes with the one it holds when no answer comes within a few seconds. They talk over a port of a channel of
  their own, which the backoffice hands the frame's first loaded document with the one message it posts to the frame's
  window; a page the frame goes on to never gets it, and it closes as the frame loads another and as the preview
  closes. The backoffice answers an ask only once the last ticket is about to run out, and one at a time, so a frame
  that keeps asking cannot keep the backoffice's session alive. Over the same port it tells the document to turn light
  or dark, so the visitor's answers stay.
- **The choices:** the widths are the designer's, and the preview opens at the canvas's; a width chosen in the
  preview is the preview's alone, so the canvas stays as it was left. The frame takes the chosen width, or the room
  there is with "Narrowed to fit", and the height of the modal, scrolling inside as a window does. Light or dark shows
  for a theme with the dark scheme; Language lists the site's languages, and renders again.
  Run actions for test entries joins the "..." with 10.3f.
- **Preview saves first** what is not saved yet, as Publish does; a save refused, a conflict among them, opens no
  preview.

§4.6 gains `preview_ticket_invalid`. The frame is the backoffice's second, which axe leaves alone like the email
preview's (ADR-0189); the browser tests check its document with axe in the frame as it stands, light and dark.

## Consequences

- Positive: the preview is the site's own rendering of the draft, theme, views and runtime included, and none of it
  can reach the backoffice's session or storage.
- Positive: visitors' pages load nothing for the preview; the ticket comes only with requests a user makes in the frame.
- Negative: a site's own fonts load in the frame only when the site sends them with CORS, as the frame's origin is
  opaque; the package's own assets are sent so (ADR-0092).
- Negative: an open preview shows the draft as it was saved when it opened; a save from another tab shows once the
  language or the preview is opened again.
- Negative: a draft with a broken rule cannot be previewed until the rule is fixed; the form check leads to it.

## Amendment (2026-11-07, step 10.4)

A ticket can still run out with the preview open: a backoffice that slept past it, or a session that ended, answers no
ask, and the script goes with the ticket it holds. The public API then answers `401 preview_ticket_invalid`, which no
renewal brings back, and the frame's page would only say it could not send. The script now says so over the port, once
for its document, and the sidebar's note says, as an alert, that the preview can send no more, with Load again, which
renders the saved draft afresh with a new ticket. Any other refusal says nothing over the port.
