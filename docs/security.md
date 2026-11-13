# Security

How Jiron Form Builder protects a site, its visitors and their data: what is public, how requests are checked, how
files, markup and secrets are handled, and what was reviewed. To report a problem, see [SECURITY.md](../SECURITY.md).

## Contents

- [What is public](#what-is-public)
- [Sending a form](#sending-a-form)
- [Uploaded files](#uploaded-files)
- [Markup and merge tags](#markup-and-merge-tags)
- [Secrets](#secrets)
- [Calls to other servers](#calls-to-other-servers)
- [The backoffice](#the-backoffice)
- [Data Protection](#data-protection)
- [What the visitor's browser loads](#what-the-visitors-browser-loads)
- [Logs](#logs)
- [The security review](#the-security-review)
- [Reporting a problem](#reporting-a-problem)

## What is public

- **The public API** under `/jfb/api/v1`, never under `/umbraco`: published forms, their options, the signed check,
  uploads and entries. Each request has limits on its body, its time and its rate, and a browser may keep only a form's
  definition and a field's options, which it checks again each time with an `ETag` ([headless.md](headless.md)).
- **The website's scripts and stylesheets** under `/jfb/assets/`.
- **Cross-origin requests** only from the origins listed in `JironFormBuilder:PublicApi:AllowedOrigins`, never with
  credentials. Other origins' requests are refused before anything runs. The site's own CORS and rate limiter are left
  alone.
- **Server keys** for servers that send entries: the configuration holds only each key's SHA-256, compared in fixed
  time; an unknown key is refused, never treated as an anonymous request.

## Sending a form

- **No antiforgery token on anonymous forms,** so their pages can be cached. Instead, every entry carries a signed,
  single-use check bound to the form and the time, fetched when the visitor starts, with a hidden field robots fill in
  and a shortest time to fill in. Suspicious entries go to Spam rather than being refused, so nothing is lost
  ([spam-protection.md](spam-protection.md)).
- **Rate limits** per address, per form and for the whole site, with bounded memory; a flood is limited, never let
  through.
- **Forms for members** act for a signed-in member, so their check is bound to that member, a post from another origin
  is refused, and their pages may not be framed by other sites. Jiron Form Builder sets no cookie of its own.
- **The server decides.** Rules and validation run in the browser for speed and again on the server, which is
  authoritative; values the server works out, such as calculations and hidden fields, are never taken from the post.

## Uploaded files

- The check is verified before the file's first byte is read; a declared size over the limit is refused before
  reading; the size is counted as the file streams in.
- The first bytes decide the type: programs and scripts are refused under any name, a blocked extension anywhere in the
  name is refused, and content must match its extension.
- Files are stored under random names, without extensions, outside the web root; the site refuses to start with an
  uploads folder inside it.
- Files are downloaded only through the backoffice, as attachments, by users who may view the form's entries, and
  sensitive fields' files only by users who may see sensitive data, each download recorded.
- An optional virus scanner can quarantine files before anyone downloads them
  ([files.md](files.md)).

## Markup and merge tags

- **Rich text** editors write is cleaned by an established allowlist sanitiser (HtmlSanitizer) before visitors or
  emails see it: formatting, lists and links stay; scripts, styles, frames and event handlers go; links take only
  `http`, `https`, `mailto`, `tel` or site paths.
- **A theme's custom CSS** is rewritten to apply to its own forms only, without imports, scripts or addresses on other
  sites.
- **Merge tags** encode every value for where it goes, HTML, text, a subject line, an address, JSON or XML, and there is
  no raw output, so an answer can't add markup ([merge-tags.md](merge-tags.md#where-merge-tags-work)).
- **Emails to addresses visitors typed** make no links of what they typed; Slack and Teams messages write their own
  formatting characters in answers as text.
- **Exports** guard cells against spreadsheet formulas.

## Secrets

Keys, signing secrets and chat addresses are never typed into the backoffice or stored in the database. A setting names
a secret by reference, `$JironFormBuilder:Secrets:<Name>`, and the value lives in the site's configuration, in user
secrets, environment variables or a secret store. A value typed where a reference belongs is refused when saved. Captcha
keys live only in `JironFormBuilder:Captcha`. Setting up an action that sends a secret's value somewhere, such as a
webhook header, needs the **Manage settings** permission; a reference used as a webhook's address or signing secret
sends no secret elsewhere and needs only **Manage actions**.

## Calls to other servers

Webhooks and JSON option lists call only hosts the configuration allows, over `https` unless allowed otherwise, on the
standard port unless named. Every address a name resolves to must be on the public internet, checked before connecting,
so a name can't point into the site's own network. Webhooks follow no redirects, and every call has a time limit and a
limit on the answer's size ([webhooks.md](webhooks.md)).

## The backoffice

- **Every endpoint declares the permission it needs,** and a test calls each one with users who hold and lack it. A form
  a user may not see answers as if it didn't exist.
- **Actions that reach further** need more than **Manage actions**: a content action is held to the user's own Umbraco
  rights, copying sensitive answers needs **View sensitive data**, and a webhook header that sends a secret needs
  **Manage settings** ([permissions.md](permissions.md#actions-that-reach-further)).
- **Sensitive answers** are masked for users who may not see them, in lists, forwards, exports and the delivery log.
- **The audit log** records reveals of sensitive answers, exports, deletions, changes of state, settings and
  permissions, and privacy-tool searches and erasures, by whom and when. It never records the values themselves, and it
  is kept for a year ([privacy.md](privacy.md#the-audit-log)).
- **The preview** runs in a sandboxed frame with a short-lived ticket for one form.

## Data Protection

The signed checks, upload tokens, preview tickets, address-confirmation links and the page after a send without
JavaScript are protected with ASP.NET Core Data Protection, each with a purpose of its own and a short lifetime. A site's
key ring must last across restarts and be shared between its servers; the **Data Protection keys** health check warns
when it won't ([deployment.md](deployment.md)).

## What the visitor's browser loads

Only the site's own files. No web fonts, no analytics, no other hosts, except a captcha an editor chooses, which loads
from its provider only on forms that use it and only after the visitor agrees. The forms work under a strict Content
Security Policy without inline scripts or styles ([rendering.md](rendering.md#content-security-policy)).

## Logs

Logs never hold answers, email addresses, tokens, file names or IP addresses. Exceptions are logged with their type and
stack but without their message, which might hold a visitor's words, and a test checks this for every log call that
takes an exception.

## The security review

In November 2026 the code was reviewed in four readings by kind of threat: access control; input, uploads and limits;
calls to other servers, mail and secrets; and output encoding. Each finding was confirmed in the code, each fix came
with a test, and each test was checked by undoing the fix. No high or critical finding is open; the two high findings,
both about what an action may do beyond its permission, were fixed. The dependencies' advisories were reviewed one by
one. [security-review.md](security-review.md) has the findings, what was done about each, and the checklist that names
the test behind each requirement.

## Reporting a problem

Report security problems privately, as [SECURITY.md](../SECURITY.md) describes, with the versions, the impact and the
steps to reproduce, and never with real personal data. Please don't open a public issue.
