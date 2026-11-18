# Changelog

All notable changes to Jiron Form Builder are recorded in this file. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html) within each Umbraco major line (the package major
follows the Umbraco major).

## [Unreleased]

### Added

- **Save and continue later** on the website: a form can offer visitors a link by email to finish later, on the page
  the form is on. The link brings the answers back on the page they were saved on, and the form asks again for files,
  consents and sensitive answers, which are never saved. Editors turn it on under the form's Settings › Submission ›
  More options, with the days the link works.
- The form check has a new error: a field that takes its options from an option list this site lacks. Such a form
  could be published before and offered its visitors no choices.
- The Access, Settings and Retention screens ask before unsaved changes are left.

### Changed

- **The section's sidebar starts with five places:** Inbox, with its count of unseen entries, Library, Settings, Privacy
  and Activity, then the tree of forms. Each place opens on the first of its tabs you may use, and its screens show the
  place's tabs along their top: Inbox · Spam; Option lists · Themes · Email layouts · Templates; General · Email · Spam
  protection · Files · Access · Integrations; People · Retention · Data collection; Deliveries · Audit log. The
  "Entries" heading is gone. Every screen keeps its address, so links in mails and bookmarks still open it.
- **Each Save says what it saves** ("Save email settings"), and leaving a tab with unsaved changes asks first.
- **Extensions:** a menu item that names the Settings menu is now a tab at the end of Settings' tabs; its screen shows
  the tabs with the `jfb-place-tabs` element (docs/extending.md).
- **The designer gives the form the room.** The list of field types beside the form is gone: every **Add field** and
  **Add a field here** opens a field picker, with its search ready (type, then Enter) and the site's saved groups, and
  what you pick lands where you opened it. The form and the inspector share the screen, under one row: the pages as
  tabs with **+**, **After sending**, the widths, the language and the form check. With nothing selected the inspector
  shows the open page.
- **Pages are handled at their tabs:** the open page's tab has **Rename**, **Duplicate** and **Delete**. Splitting a
  page is **Move what follows to a new page** in a field's **…**, and **Change type…** is offered in the inspector's
  header alone.
- **After sending is edited where it is shown:** the outcomes are listed beside what visitors get on the Design view,
  and the form's Settings no longer have an After sending group. Fix it on an outcome's problem opens it there, and
  History and the publish dialog list an outcome's changes under a part of their own, After sending.
- **A form's views ask you to pick first.** The form's Settings list their six groups, each with a line of what it
  sets, and show the group you pick beside the list, Submission at first and then your last pick; on a narrow screen
  the list stands alone and **All settings** goes back. The language of the texts is chosen with the Design view's
  button.
- **The inspector folds nothing inside a section:** a field's messages have a section of their own, **Messages**,
  after Validation, and Sensitive, what is kept of the answer and whether emails list it are in **Privacy**, before
  Advanced. No section shows more than seven settings.
- **Add action lists the kinds of action first,** each with a line of what it does; the editor that follows holds that
  kind's settings alone, headed by the kind, and **When it runs** is one choice with a line about the moment chosen.
  A package can give its own action type a line ([docs/extending.md](docs/extending.md)).
- **Preview, Save draft and Publish… show on Design, Settings and Actions,** the views that change the form, and the
  other views have no footer. Changes not saved yet stay while you look at the other views, and leaving the form still
  asks first.
- **An earlier version comes back from the History view;** the form's **…** no longer has "Restore an earlier
  version…".
- The form check's badge shows its icon and the number of problems, its words in its tip.
- The website runtime's budget with every feature is 100 KB gzip at most (was 60 KB); what every page with a form
  loads first stays within 16 KB.
- **The screens say less.** About 350 texts of the backoffice are shorter or gone, in English and German: help lines
  that repeated their label, leads that explained the box below them, second sentences that said the first again.
  Nothing a screen does has changed.
- The visitor's texts are shorter in the save panel, the "welcome back" and "not sent" notices and on the page that
  confirms an address, in all seven languages, with corrections to the Danish, Spanish, French, Italian and Dutch
  wording.
- A number field takes a decimal comma on pages whose language writes one, such as 12,5 on a German page.
- A form that stores no entries shows its thank-you message on the post itself when the visitor's browser runs no
  JavaScript.
- Saving a form for later answers with a `resume` value in the public API, which the next save and the entry send
  back.
- The address a form's email verification confirms can't be edited in an entry, and a field marked sensitive is
  masked in the entries sent before it was marked too.

### Fixed

- At a wide window, **Desktop** shows the form at its full 960 px on the canvas, not 958 px.
- The mail with the link to a saved form says "1 day", not "1 days", for a link that works one day.
- Leaving a form for another screen of the backoffice no longer raises an error in the browser's console when the AI
  add-on is installed.
- Save and continue later: a form with a summary page can be sent after its answers came back; "Other" answers and
  dependent options come back; a save the mail limits hold back keeps the earlier one; files attached before a save are
  still taken at Send; Privacy > People finds saved answers for as long as they are kept.
- Mail no longer carries an `Expires` header, which could make a receiver drop a mail that was delivered late.
- A slow webhook receiver no longer delays mail; a form's own file on disk that can't be read is never written over; a
  theme or email layout that is one site's default can't be deleted.
- A file whose action sends a secret is imported only by a user who manages settings.
- A half-typed date or number is refused instead of being sent as empty, and a form whose scripts can't load says so
  at Send.
- The entries list reads on when every loaded row was deleted; the tree shows a form as published right after
  Publish; Ctrl+S saves what is still being typed.

## [17.0.0-alpha.1] - 2026-11-13

The first prerelease of Jiron Form Builder for Umbraco 17.6 or later (within 17), on .NET 10, with SQLite or SQL
Server. It is a preview: try it on a site you can reset, and expect changes before 17.0.0. Upgrades from this version
are tested from now on.

### Added

- **The Form Builder section** in the backoffice: an Overview with a setup checklist, recent entries and the site's
  health; a forms tree with folders, search, starter templates (contact, callback, newsletter, event registration, job
  application, feedback, quote request, support request, privacy request), duplicate, move, trash with restore, and
  import and export as JSON; a warning that names the pages still using a form.
- **The designer:** pages, groups of one to four columns and 33 field types, a searchable palette, drag and drop with a
  full keyboard alternative, undo and redo, an inspector sorted from essential to advanced, texts per language, a live
  preview at phone, tablet and desktop widths, and a form check that names problems before visitors meet them.
- **Option lists** from the field, a reusable list, Umbraco content or a data type, a JSON address on an allowed host, a
  CSV upload, the built-in countries or code; cascading lists, and options with a limited number of places.
- **Rules** that show, hide, skip or require, choose the message or the redirect after sending, and decide whether an
  action runs, with "all" and "any", nested groups, comparisons between answers, and the page's language, the member and
  the date; **validation** with lengths, ranges, safe patterns, formats, file types and sizes, date windows, matching
  fields and unique answers, and messages per language. Rules, validation, calculations and merge tags give the same
  answers in the browser and on the server, which decides.
- **Drafts, publishing and history:** every published version kept, compared in words and restorable; each entry keeps
  the version it was sent with.
- **Merge tags** (`{{ field.email }}`, `{{ member.name }}`, `{{ page.url }}`, `{{ entry.number }}`) inserted from a
  picker, escaped where they land.
- **Actions** after an entry: emails to the team or the visitor with a preview and email layouts, signed webhooks with
  retries, Slack and Microsoft Teams, Umbraco content, and a change of the entry's state; triggers on sending, approval,
  rejection and verification; a rule, an order, a switch, a delivery log and a test run for each; email verification as a
  form setting.
- **Entries:** a list per form and an Inbox across forms, with Spam, unseen markers, filters, search, columns and bulk
  actions; an entry's panel with its answers, files, timeline and notes, approval and rejection, edits with an audit
  trail, and forwarding; exports to CSV, Excel, JSON, a printable view and a ZIP of the files.
- **Insights** without cookies: views, starts, entries and conversion, who reaches each page and how long sending takes,
  the fields that stop visitors most, and summaries of the answers.
- **The library:** reusable option lists, saved groups of fields, themes edited visually with a live preview and contrast
  checks, and email layouts.
- **Privacy:** retention with a dry run, sensitive fields, values kept only until the actions ran, no IP addresses by
  default, finding, exporting and erasing one person's entries across forms, consent records with the wording shown, and
  an audit log.
- **Permissions** per user group, with folders and forms limited further.
- **Forms on the website:** accessible (WCAG 2.2 AA), working without JavaScript and better with it, with inline
  validation, an error summary, uploads with progress, sending without a reload, and a small runtime that loads only the
  parts a form uses; the Default, Bootstrap 5 and Unstyled themes; visitor texts in English, German, French, Italian,
  Dutch, Spanish and Danish. Spam protection with a honeypot, a signed challenge, timing and content rules and an
  optional proof of work, and reCAPTCHA or hCaptcha only when an editor chooses one.
- **Multi-page forms** with a progress indicator, checks per page and going back without losing answers; prefill from
  merge tags.
- **Members, languages and sites:** members-only forms and one entry per member, translations and dictionary items, and
  settings per site in one Umbraco.
- **For developers:** a view component, a tag helper, a form picker and a form block, theme views to override and CSS
  custom properties; a headless API, a web component, TypeScript types, JSON schemas and the form picker in the Content
  Delivery API; extension points for field types, actions, option sources, export formats, captcha providers, spam
  checks, merge-tag sources and file scanners, with notifications, public services and compiled samples; forms mirrored
  to JSON files for deployment; load-balanced hosting; files in a private folder or in blob storage.
- **The AI add-on** `Jiron.Umbraco.FormBuilder.AI`, on Umbraco.AI: draft a form from a description, translate a form's
  texts and suggest help texts, for editors only.
- **The packages:** `Jiron.Umbraco.FormBuilder` (bringing `.Core` and `.Web`) and the AI add-on, each with its readme,
  icon and the licence to accept, and the packages with code with their symbols; the Marketplace listings; the
  documentation for editors, site owners and developers.

### Not yet included

- Saving a form to continue later through an emailed link.
