# Feature catalogue

Every feature of Jiron Form Builder, and every candidate idea from the feature study of ROADMAP step 1.1, with where
it is built.

- **How the ideas were found:** public feature pages, documentation, help articles and release notes of about twenty
  form tools in general: form plugins and modules for content management systems, hosted form builders,
  conversational form tools and survey tools. Nothing was installed, run or downloaded, and no screen was used as a
  design reference (CLAUDE.md rule 2). Notes that name products are kept in the owner's private folder only; this
  file names none, and every idea is described in Jiron Form Builder's own words and designed its own way.
- **What the study found:** most ideas that are common elsewhere were in the plan already. The gaps were mostly
  survey-style fields (grid, ranking), seeing what people answered, courtesy features for visitors, and a few privacy
  and workflow refinements. The strong ones were added to the roadmap (reasons in `docs/WORKLOG.md`).
- **Status markers:**
  - **in roadmap (step)**: planned from the start;
  - **added (step)**: added to the roadmap in step 1.1, from the feature study or from the Ligata.Forms port
    (`docs/research/ligata-forms-port.md`);
  - **later (§7)**: out of scope for the first release, listed in ROADMAP §7;
  - **not planned (reason)**: outside what Jiron Form Builder is (ROADMAP §1.4), or against a rule of the plan, named
    in the row.

## 1. Form library

| Feature | Status |
| --- | --- |
| Folders for forms, with move and search | in roadmap (4.5, 9.2) |
| Search for fields across all forms, by label or alias | added (9.2) |
| Starter templates in a gallery (contact, callback, newsletter, event registration, job application, feedback, quote request, support request) | in roadmap (4.5 Contact, 12.3 the rest, 9.2 gallery) |
| A privacy-request template that routes to the people who use the privacy tools | added (12.3) |
| Saved groups of fields (an address block, a standard consent), inserted into any form as a copy | added (12.3) |
| Duplicate a form, with or without its actions | in roadmap (4.5, 9.2) |
| Trash with restore, and delete with a warning naming the pages that use the form | in roadmap (4.5, 6.7, 9.2) |
| Import and export a form as JSON, as a new copy or a replacement | in roadmap (4.5, 9.2, 12.5) |
| Copy actions from one form to another | in roadmap (4.5, 9.2) |
| Save a form as a template of one's own | in roadmap (12.3) |

## 2. The designer

| Feature | Status |
| --- | --- |
| Pages (steps), groups with one to four columns, fields with column spans | in roadmap (4.2, 9.3b) |
| A searchable field palette with groups and recently used types | in roadmap (9.3b) |
| Click-to-insert points between fields | in roadmap (9.3b) |
| Paste a list of questions, one per line, to start a form | added (9.3b) |
| Drag and drop across groups and pages, with "Move to…" for keyboard users | in roadmap (9.3c, ADR-0016) |
| Inline label editing on the canvas | in roadmap (9.3b) |
| Undo and redo for every change, duplicate with a shortcut | in roadmap (9.3a, 9.3c) |
| An inspector sorted from essential to advanced settings | in roadmap (9.4) |
| Per-language texts with "missing translation" marks | in roadmap (9.4, 14.1) |
| Canvas widths for phone, tablet and desktop, and a live preview in the real theme | in roadmap (9.3b, 10.3) |
| Fill the preview with valid sample answers in one click | added (10.3) |
| A form check that flags problems before publishing, with "fix it" links | in roadmap (9.5b) |
| Form-check warnings for redirects that put answers into a URL, for forms that ask for card numbers or passwords, and for a required choice whose options can all become full | added (9.5b) |
| Rule chips on the elements a rule affects, and an overview of every rule | in roadmap (9.5a) |
| Change a field's type to a compatible one, with a preview of what is lost | in roadmap (5.1, 9.4) |
| Locked settings shown with the configuration key that locks them | in roadmap (4.4, 9.4) |
| Draft a form with AI from a description or a pasted questionnaire | in roadmap (15.3), the questionnaire added (15.3) |

## 3. Field types

| Feature | Status |
| --- | --- |
| Text, long text, email, phone, web address | in roadmap (5.4, 6.3, 9.4) |
| Word limits on long text | added (5.4) |
| Number, date, time, date and time, with absolute or relative limits | in roadmap (5.4) |
| Excluded dates (holidays) on date fields | added (5.4) |
| Rating, scale (0 to 10) and slider | in roadmap (5.4) |
| Calculation (a safe formula over other fields, recomputed by the server) | in roadmap (5.3, 5.4) |
| Days and years between two dates in calculations (an age, a length of stay) | added (5.3) |
| Dropdown, radio buttons and checkboxes, with card layouts, images and an "Other" option | in roadmap (5.4, 6.3) |
| A grid: several statements rated on one shared scale, one row per fieldset, stacked on phones | added (5.4, 6.4, 9.4, 11.4) |
| Ranking: put options in order with move buttons, a position number without JavaScript | added (5.4, 6.4, 11.4) |
| Place limits per option (seats, time slots), with places left shown and full options disabled, never overbooked across servers | added (5.4, 6.1, 7.1, 12.3) |
| Single checkbox, yes/no, and consent that stores the exact wording shown | in roadmap (5.4) |
| Country, name and address, with the right autocomplete tokens | in roadmap (5.4) |
| File upload (with the camera offered on phones) and signature (with a typed alternative) | in roadmap (5.4, 6.4, 7.3), the camera added (5.4) |
| Repeatable group of fields | in roadmap (5.4, 6.4) |
| Hidden value from a merge tag; server-only values that never reach the page | in roadmap (5.4, 6.5), server-only added (5.4) |
| Heading, text block, divider and image | in roadmap (5.4) |
| Captcha placement | in roadmap (5.4, 7.2) |
| Custom field types from code, with a settings editor generated from attributes | in roadmap (5.1, 15.1, 15.2) |
| Collapsible groups, a searchable choice for very long lists, country formats for postcodes | later (§7) |
| Audio or video answers, reading a code with the camera | later (§7) |
| Product, price and coupon fields | later (§7, payments) |
| Registration and sign-in fields | later (§7, member forms) |
| Booking with calendars and availability rules | not planned (§1.4: a booking engine, usually through third parties; place limits cover simple time slots) |
| Address suggestions from a lookup service while typing | not planned (§4.10: no third-party calls with visitor data; possible as an extension, §4.9) |
| A map pin or the visitor's location | not planned (§1.4: location tracking, and map tiles come from third parties) |
| Formatted text typed by visitors | not planned (visitor text is plain text by design, §4.10) |
| Inputs inside a running sentence (fill in the blanks) | not planned (hard to label accessibly, §4.12) |

## 4. Option lists

| Feature | Status |
| --- | --- |
| Options typed into the field, or from a reusable list | in roadmap (5.5, 12.1) |
| Options from Umbraco content or an Umbraco data type | in roadmap (5.5) |
| Options from a JSON address on an allowed host, or from a CSV or text upload | in roadmap (5.5, 12.1) |
| The built-in country list | in roadmap (5.4, 5.5) |
| Presets: months, weekdays, agreement and satisfaction scales, "yes, no, not sure", in every visitor language | added (5.5, 14.2) |
| Options from code | in roadmap (5.5, 15.2) |
| Cascading lists (the second list filters on the first) | in roadmap (5.5, 6.4) |
| Paste many options at once, reorder by drag, images and descriptions for cards | in roadmap (9.4) |
| Options taken from another form's approved entries | later (§7) |

## 5. Rules and validation

| Feature | Status |
| --- | --- |
| Show or hide fields, groups and pages; skip pages | in roadmap (5.2, 9.5a) |
| Make a field required when a rule matches | added (5.2, 9.5a) |
| Compare two answers ("departure after arrival") | added (5.2, 9.5a) |
| Conditions on the page's language, the member, an allowed query value and today's date, safe on cached pages | added (5.2, 7.1, 9.5a) |
| Switch a rule off without deleting it | added (5.2, 9.5a) |
| Choose the success message or redirect, and decide whether an action runs | in roadmap (5.2, 8.1, 10.1) |
| "All" and "any" with nested groups, and operators for text, numbers, dates, choices and files | in roadmap (5.2, 9.5a) |
| Hidden means gone: never required, validated or stored | in roadmap (5.2, 6.1) |
| Required, length, range, safe patterns, formats, file types and sizes, date windows | in roadmap (5.2, 5.4) |
| "Matches another field" and "unique per form" | in roadmap (5.2, 6.1) |
| Form-level validation rules ("show this message on field X when …") | in roadmap (5.2, 9.5a) |
| An empty submission is refused | added (6.1) |
| Custom messages per language | in roadmap (5.2, 9.4, 14.2) |
| The same answers in the browser and on the server (shared test vectors) | in roadmap (1.5, 5.2, 5.3) |
| Answer recall: earlier answers inside later labels, texts and messages | added (5.3, 6.4, 9.5b) |
| "Read-only when" rules, and an "end the form here" shortcut | later (§7) |
| An opt-in check that an email domain can receive mail | later (§7) |
| Random order, options carried forward from earlier answers, scoring, a time limit | later (§7, quizzes and polls) |
| Lead scoring | not planned (§1.4; weighted totals are possible with calculations) |
| Several submit buttons with different results | not planned (ordered outcomes with rules do this more simply) |
| Lists of throwaway email services | not planned (needs a third-party data list to keep up to date) |
| A code by email or text message before submitting | not planned (email verification after submitting covers it, 8.5) |

## 6. Publishing and history

| Feature | Status |
| --- | --- |
| Drafts that stay private until published | in roadmap (4.5, 10.3) |
| Every published version kept, compared side by side and restored as a new version | in roadmap (4.5, 10.3) |
| Pins and comments on versions | in roadmap (4.5, 10.3) |
| Entries remember the version they were submitted with | in roadmap (D5, 4.3) |
| A publish summary of what changed against the live version | in roadmap (10.3) |
| Conflict handling when someone else saved first | in roadmap (4.5, 10.3) |
| Comments and review on a draft before it goes live | later (§7) |

## 7. Form settings and the visitor's journey

| Feature | Status |
| --- | --- |
| Submit, next and back labels; required or optional marker style | in roadmap (10.1) |
| Ordered outcomes: a message, a redirect, or a message then a redirect, each with a rule | in roadmap (6.2, 10.1) |
| A copy of the answers on the thank-you page, with print and "fill in again" | added (6.5) |
| An entry reference in a format of the form's own (`REQ-2026-00042`) | added (5.3, 10.1) |
| Store entries always, never (actions only) or for N days | in roadmap (6.1, 10.1) |
| Manual approval | in roadmap (8.5, 11.2) |
| Opening and closing dates, an entry limit, and a closed message | in roadmap (6.1, 10.1) |
| Members-only forms and one entry per member | in roadmap (14.3) |
| A progress indicator (steps, bar or none), per-page validation, back without losing answers | in roadmap (6.5) |
| A summary page ("check your answers") with change links | in roadmap (6.5) |
| Save and resume later through an emailed link | in roadmap (8.6) |
| Prefill from members, allowed query values and context values | in roadmap (6.5, 14.3) |
| Inline validation, an error summary with links, and focus management | in roadmap (6.4) |
| Submission without a page reload, and forms that work without JavaScript | in roadmap (6.2, 6.4) |
| Recovery when the form changed while the visitor filled it in, or its entry limit was reached | in roadmap (6.1, 6.4) |
| Several forms on one page | in roadmap (6.2) |
| Shared-password forms, a form that opens in a dialog, a contents list for long forms | later (§7) |
| A self-delete link, editing one's own entry, prefill from one's previous entry | later (§7, front-end editing) |
| Abandoned partial entries, one question at a time | later (§7) |
| Pop-ups on a timer, on scroll or on leaving | not planned (§1.4 and §4.13: interruptive and tracking-like) |
| Text size and colour switches on the form | not planned (browser zoom and contrast settings serve this; the forms reflow, §4.12) |
| The country guessed from the visitor's address | not planned (§1.4: the address is used for protection only) |

## 8. Spam protection and security

| Feature | Status |
| --- | --- |
| Honeypot, minimum fill time and a signed challenge, with no third party | in roadmap (7.1) |
| Rate limits per address, per form and site-wide, that fail closed | in roadmap (7.1) |
| Blocked address ranges, matched and never stored | added (7.1) |
| Content rules: blocked words, blocked email domains, a maximum number of links | in roadmap (7.1) |
| A spam folder where nothing is lost and no action runs | in roadmap (7.1, 11.3) |
| Optional proof of work | in roadmap (7.2) |
| Optional third-party captchas, loaded only after consent | in roadmap (7.2) |
| Custom spam checks from code | in roadmap (7.1, 15.2) |
| File checks: allowlist, magic numbers, size limits while streaming, protected downloads, scanner hook with quarantine | in roadmap (7.3) |
| Keyword rules combined with and, or and not; a local score for random-letter text; proof of work only for borderline scores | later (§7) |
| Blocking by country, or accepting only from an area | not planned (needs a location database and processes location) |
| Asking an outside reputation service about the visitor's address | not planned (§1.4; possible as a custom spam check, §4.9) |
| AI judging submissions | not planned (D23: visitor data never reaches an AI) |

## 9. Actions and email

| Feature | Status |
| --- | --- |
| Email to the team and confirmation to the visitor, in HTML with layouts, per language, with optional attachments | in roadmap (8.2, 8.3) |
| A recipient map: a choice ("Sales", "Support") picks internal addresses that never reach the page | added (8.3) |
| An answer table that can leave out unanswered fields | added (8.3) |
| Webhooks: signed, retried, SSRF-safe, JSON, form-encoded or XML | in roadmap (8.4) |
| Slack and Microsoft Teams messages | in roadmap (8.4) |
| Create or update Umbraco content | in roadmap (8.5) |
| Change the entry's state under a rule | in roadmap (8.5) |
| Email verification (double opt-in) before actions run | in roadmap (8.5) |
| Triggers: submitted, approved, rejected, verified | in roadmap (8.1, 8.5) |
| Per action: a rule, an order, an on/off switch, "stop the rest if this fails" | in roadmap (8.1, 10.2) |
| Retries with a visible delivery log, and a test run against a sample entry | in roadmap (8.1, 10.2, 12.4b) |
| A confirmation before retrying a run that may have been delivered already | added (8.1, 12.4b) |
| A host switch that pauses all sending without losing anything (for restored copies of a live database, and incidents) | added (8.1, 8.2, 9.1, 12.4b) |
| Limits on mail to addresses that visitors type | in roadmap (8.2, §4.10) |
| Entry events in Umbraco's own webhook settings | in roadmap (8.4) |
| Custom action types from code | in roadmap (8.1, 15.2) |
| Delayed actions and reminders; a calendar file attached to event confirmations | later (§7) |
| A PDF copy attached to emails | later (§7, PDF generation) |
| Ready-made connectors to CRM, newsletter and spreadsheet services | not planned (§1.4; webhooks and extension points instead) |
| Text messages | not planned (third-party providers; possible as a custom action, §4.9) |
| Adding outside data about the person | not planned (§1.4) |
| AI steps on entries (summaries, classification) | not planned (D23) |

## 10. Entries

| Feature | Status |
| --- | --- |
| A list per form and an all-forms inbox, with "new" markers | in roadmap (11.1, 11.3) |
| An entry title built from answers ("Anna Berg, Acme"), shown in the lists | added (10.1, 11.1, 11.3) |
| Filters, search, sort, a column chooser and paging in the database | in roadmap (4.3, 11.1) |
| Saved filter sets and column sets per user, on every device | added (11.1) |
| Bulk actions: approve, reject, spam, not spam, delete, export, re-run actions | in roadmap (11.1) |
| An entry panel with values, files, consent proof, a timeline and internal notes | in roadmap (11.2) |
| Editing an entry with an audit trail | in roadmap (11.2) |
| Forward an entry to a colleague, audited, never to strangers | added (11.2) |
| Keyboard shortcuts for entry handlers | in roadmap (11.1) |
| Unseen counts on the menu, without keeping idle sessions alive | in roadmap (11.3) |
| A daily or weekly digest email with counts and links only | added (11.3) |
| Exports: CSV, Excel, JSON, printable view, ZIP of files; large exports in the background | in roadmap (11.4) |
| The export dialog remembers the last choices | added (11.4) |
| Entry numbers per form (`#1042`) | in roadmap (4.3, §4.4) |
| Test entries from the preview, kept out of exports, insights and actions | in roadmap (10.3, §4.4) |
| A short trash for deleted entries; the other entries of the same person; a follow-up flag | later (§7) |
| Approved entries shown on the website without code | later (§7) |
| Importing entries, scheduled exports, PDF documents | later (§7) |
| Locking an entry against edits | not planned (legal holds keep entries, 13.1, and every edit is audited, 11.2) |

## 11. Insights

| Feature | Status |
| --- | --- |
| Views, starts, submissions and conversion per form and over time | in roadmap (11.5) |
| Drop-off per page and the median time to complete | in roadmap (11.5) |
| Top origin pages, hour of day, unique members | in roadmap (11.5) |
| The fields that fail validation most, and with which kind of error, counted without values | added (11.5) |
| Answer summaries: counts per option, averages, the 0-to-10 recommendation score, free-text answers listed | added (11.5b) |
| No cookies, no personal data, opt-out per site and per form | in roadmap (11.5, D13) |
| Plain-language hints from the numbers; a phone or desktop split | later (§7) |
| The last field touched before leaving | later (§7, with partial-entry capture) |
| Comparing answers between groups; public results | later (§7, quizzes and polls) |
| Variants of a form compared against each other; per-person journeys | not planned (§1.4: tracking) |
| Formats for statistics software | not planned (CSV and Excel serve them) |

## 12. Library and appearance

| Feature | Status |
| --- | --- |
| Themes edited visually (colours, type, spacing, corners), with contrast checks and a live preview | in roadmap (12.2) |
| Custom CSS scoped to the form and sanitised | in roadmap (12.2) |
| Themes shipped: default, unstyled, Bootstrap 5 classes | in roadmap (6.3) |
| Email layouts with a logo, colours, header and footer per language, and a test send | in roadmap (12.2) |
| Overridable theme views, per theme and per form | in roadmap (6.3) |

## 13. Privacy

| Feature | Status |
| --- | --- |
| Retention per state, with a dry run and legal holds | in roadmap (13.1) |
| Sensitive fields masked in the UI, emails, exports and logs | in roadmap (13.3) |
| Values kept only until the actions have run, then deleted | added (6.1, 8.1, 13.3) |
| IP handling: none (default), anonymised or full | in roadmap (13.3) |
| Find, export and erase one person's entries across all forms | in roadmap (13.2) |
| Consent records with the exact wording shown | in roadmap (5.4, 11.2) |
| An audit log | in roadmap (4.3, 12.4b) |
| Privacy facts sheet, checklist and policy templates for site owners | in roadmap (13.4) |
| Field-level encryption at rest | later (§7) |
| The visitor chooses whether the entry is stored | not planned (a consent field and values kept only until the actions have run cover the need) |
| A hosting region and certifications | not planned (§1.4: not a hosted service; the facts sheet, 13.4, says where data lives) |
| Data-protection labels on forms | not planned (the sensitive flag on fields covers the practical need) |

## 14. Administration

| Feature | Status |
| --- | --- |
| Permissions per user group, globally and per folder or form | in roadmap (4.6, 12.4a) |
| Global settings with host locks, per-site overrides | in roadmap (4.4, 12.4a, 14.4) |
| A guided first run on the dashboard | in roadmap (9.1, 12.6) |
| Health checks on Umbraco's dashboard | in roadmap (9.1) |
| Deliveries and audit log screens | in roadmap (12.4b) |
| A table of every WCAG 2.2 A and AA criterion with how it was tested, for buyers | added (16.3) |
| Test submissions that check a live form end to end | later (§7) |
| Single sign-on and two-factor sign-in | not planned (Umbraco's own sign-in handles them) |

## 15. Languages, members and sites

| Feature | Status |
| --- | --- |
| Texts per culture with fallback, and dictionary keys | in roadmap (14.1) |
| Visitor texts in English, German, French, Italian, Dutch, Spanish and Danish | in roadmap (6.4, 14.2) |
| Right-to-left layouts for languages written that way | added (6.3, 6.5) |
| A backoffice in English and German | in roadmap (9.1, 14.1) |
| A form opened in a chosen language by link | in roadmap (§4.6 `culture`) |
| Members: members-only forms, prefill, entries on the member, deletion handling | in roadmap (14.3) |
| Several sites in one installation: sender, base URL, defaults and theme per site | in roadmap (14.4) |
| More built-in visitor languages | later (§7, community translations) |
| Currency and time zone fixed per form | not planned (the site's time zone, §4.8, and the currency filter, §4.3.3, serve it) |

## 16. Developers and deployment

| Feature | Status |
| --- | --- |
| Tag helper, view component, form picker with value converters, and a ready-made block | in roadmap (6.2, 6.7) |
| Headless: the form as JSON, a submission API, the Content Delivery API, TypeScript types, a JSON schema and a web component | in roadmap (6.6) |
| Extension points: field types, actions, option sources, exports, captchas, spam checks, merge-tag sources and filters, file scanners, file stores | in roadmap (§4.9, 15.1, 15.2) |
| Notifications for every lifecycle event, and public services | in roadmap (4.5, 15.1) |
| Forms mirrored to JSON files for source control | in roadmap (12.5) |
| Load-balanced hosting with no extra infrastructure, SQLite and SQL Server | in roadmap (D7, D8, 8.1, 3.5) |
| An upgrade test for every release | in roadmap (16.5, 19.2) |
| An optional AI add-on for editors (draft, translate, suggest) | in roadmap (15.3) |
| An interface through which outside AI assistants build forms (editors only) | later (§7) |
| A graph-style query API | not planned (Umbraco's Content Delivery API covers it, 6.6) |
| Command-line tools | not planned (forms as files, 12.5, serve deployment) |

## 17. Later and not planned, beyond the study

Ideas that were outside the first release before the study, kept here so the catalogue is complete.

| Idea | Status |
| --- | --- |
| Payments through a provider's hosted checkout | later (§7) |
| Conversational mode (one question at a time) | later (§7) |
| Quizzes, scoring and polls with public results | later (§7) |
| Assignees, custom statuses, tags and a board view for entries | later (§7) |
| Front-end editing of one's own entry, member registration and profile forms | later (§7) |
| More captcha providers and backoffice languages | later (§7) |
| Importers from other systems | later (§7) |
| Card or bank data handling | not planned (§1.4) |
| CRM or marketing-automation features | not planned (§1.4) |
| Tracking visitors (cookies, fingerprinting, third-party scripts) | not planned (§1.4) |
| A hosted service | not planned (§1.4) |
