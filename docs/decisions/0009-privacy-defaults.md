# 0009. Privacy defaults

- Status: accepted; consent texts, sensitive fields in mail, the global privacy section and context values
  amended in 1.8; how long consent texts stay amended by [ADR-0222](0222-retention.md)
- Date: 2026-10-23
- Roadmap step: 1.7b (decided); built in 4.1 (the columns), 6.1 (what a submission stores), 8.x (mail), 13.1 to 13.4
  (retention, data subject tools, sensitive data and IP addresses, the documentation) and 14.3 (members)

## Context

D13 makes privacy a default rather than a setting the site has to find: no cookies, no IP addresses stored,
retention on, sensitive fields masked, consent with its exact wording, no field values in logs, and nothing from third
parties unless an editor chooses a third-party captcha. §3.7 sets the legal frame: the site is the controller;
minimisation, storage limitation, data subject rights, proof of consent (Art. 7, never pre-ticked) and security
(Art. 32); TDDDG § 25 for anything stored on the visitor's device or loaded from third parties; UWG § 7 for
double opt-in. Jiron Form Builder ships defaults, tools and documentation, never a promise of compliance; the site's
adviser makes the legal judgements (§3.7, 13.4).

A form builder holds what visitors typed: names, addresses, messages, files, sometimes health or HR data. Unlike a
chat, most of it is wanted by the site, often for months. The defaults therefore keep entries long enough for normal
work and delete them by themselves after that, and they keep everything around an entry (copies in mail, logs,
search text, events) shorter or out altogether.

## Options

1. **Strict:** store nothing by default (actions only), no IP, no member link. Safe, but most sites need their entries,
   and an empty entries screen after the first submission would surprise every editor.
2. **Balanced:** store entries with retention per state, keep copies short-lived, store no IP, set no cookies, and keep
   personal data out of everything that is not the entry itself. Stricter modes are one setting away.
3. **Minimal:** store everything until someone deletes it. Simple, but it puts storage limitation on every site
   owner's to-do list and contradicts D13.

## Decision

**Option 2 as the default.** Options 1 and 3 stay reachable per form and globally, within the host's locks (ADR-0008):
"never store" is a form setting, and turning retention off needs `JironFormBuilder:Privacy:AllowRetentionOff`.

**What a submission stores, by default:**

| Data | Default | Can change | Lock |
| --- | --- | --- | --- |
| The entry and its values | stored, with retention per state | per form: `never` (kept only until no action trigger can still fire for it and every run is final, then deleted) or a number of days | – |
| IP address | **not stored**; rate limits and blocked ranges see it in memory only | `anonymised` (IPv4 /24, IPv6 /48), as the global default or per form | `full` only with `AllowFullIp` |
| The page | the path only, never the query or the referrer | – | – |
| User agent, screen, location | never | – | – |
| The member | the member's key, when a member is signed in (needed for "one entry per member" and the member's entries view) | – | – |
| Context values | only the names the form lists (`developer.contextValues`), plus the name of the server key a server-to-server submission used, under the reserved name `jfbServerKey` (ADR-0012) | per form | – |
| Duration | seconds from the first interaction to the submission (insights) | – | – |
| Consent | `true` plus the hash of the exact text shown and its culture (`jfbConsentText`). The form posts that same hash for the wording it showed (from the markup, or from the projection for headless and server-key clients), and the server compares it on every submission: a different hash is answered as a changed form (ADR-0002), and a ticked consent without one is `form_changed` for a browser post and `422` for a server key, so the stored wording is always the one shown | – | – |
| Search text | the normalised values of non-sensitive fields only | – | – |

**Retention defaults** (the engine is 13.1; each starts from the entry's last state change):

| What | Default |
| --- | --- |
| Submitted entries | 365 days |
| Approved entries | 365 days |
| Rejected entries | 90 days |
| Spam | 30 days |
| Test entries (from the preview) | 7 days |
| Drafts (save and resume) | when the resume link expires (30 days by default) |
| Entries waiting for email verification | when the verification link expires (7 days by default) |
| Values kept only until the actions have run | once no trigger can still fire for the entry and every run is final (§4.4) |
| Uploaded files without an entry | 24 hours |
| Mail (addresses and bodies) | sent mail 30 days; failed, uncertain and limited mail 30 days after the last attempt or the limit |
| Action-run error messages | with their entry; sanitised and at most 1,000 characters (§4.5) |
| The audit log (user and entity keys, never values) | 365 days |
| Daily statistics (no personal data) | 3 years |
| Consent texts (no personal data: they recall no answers, ADR-0014) | kept: no personal data (ADR-0222) |
| A legal hold | keeps an entry past its retention until the hold's end date, at most 365 days at a time, with a reason and the user who set it |

Deleting an entry deletes its values, files, events, notes, action runs and queued mail with it (§4.4).

**Sensitive fields** (`sensitive: true`):

- masked in lists, the entry panel and previews unless the user holds "view sensitive data" (ADR-0013); every reveal is
  audited;
- left out of the answers in mail and shown as `•••` where a mail names them, unless the action includes them by name
  (ADR-0014); out of exports unless chosen and permitted, out of the entry title, the search text, timeline old and
  new values, and logs;
- masked in the copy of the answers and the outcome message that a visitor sees.

**Members:** when a member is deleted, their entries are anonymised by default (values replaced, files removed,
statistics kept); a site setting can choose deletion instead (14.3).

**On the visitor's device:**

- **No cookies,** ever.
- **No browser storage,** except one key while the visitor uses "save and resume": `sessionStorage` keeps the resume
  token after the visitor opened their link, so a reload does not lose it, and removes it on submission. The facts
  sheet lists the key, and a test compares the runtime's storage use with the sheet (13.4).
- **No third parties:** no web fonts, no analytics and no external scripts. The default theme uses system font
  stacks or the site's own font (CLAUDE.md rule 4). A third-party captcha loads only on forms that use it and only
  after consent (ADR-0012, §4.10).
- **Insights** are counters per form and day with no identifiers (`jfbDailyStat`); the beacon carries the event, the
  page key or the field key and the kind of error, nothing else. The switch is per form and global (the privacy
  section is global, ADR-0008).

**Mail:**

- Mail to an address a visitor typed is capped per recipient and per site, and its visitor text is plain text
  (§4.10, ADR-0012).
- Confirmations quote the visitor's answers only where the editor put them, with sensitive fields masked.
- Links in mail come from the site's base URL only (ADR-0008).

**Logs** never contain field values, email addresses, tokens, file names or IP addresses, and job runs log exception
types and codes, never messages (§4.10). Tests capture the logs and check.

**Data subject tools** (13.2): find a person by an email field's value or by member across every form, export
everything about them as a ZIP, and erase (delete) or anonymise, with a preview first. Every copy is in scope: values
and files, search text, IP, context and consent links, events and notes, queued and sent mail, action-run errors and
drafts. A test searches every Jiron Form Builder table for the person's address after an erase and finds nothing.

**Health checks** warn while full IP addresses are on, while retention is off anywhere, and while a third-party
captcha loads without asking first (the `implicit` consent mode, which only the host can allow).

**Documentation** (13.4): a facts sheet (what is stored, where and for how long; no cookies; the one storage key;
third parties only for a chosen captcha), a checklist for site owners, and privacy-policy templates in English and
German that start with "TEMPLATE, not legal advice; adapt with your data protection adviser". Nothing claims
compliance.

## Consequences

- A fresh installation keeps entries for a year and deletes them by itself; the copies around an entry live shorter
  or not at all, and a site that needs more changes one setting and sees the effect in the dry run first.
- Editors who need the IP address for fraud checks get the anonymised form without asking the host; full addresses
  stay a host decision.
- Erasure has to reach every copy, so every new table or column that can hold personal data must join the erase test
  (13.2) and the retention engine; the 13.4 review checks it.
- A test pins every default in this ADR, so changing one needs a new ADR (13.1, 13.3).
- Legal judgements (the legal basis, retention periods for a given purpose, transfer safeguards for captchas) stay
  with the site's adviser; the documentation names them, and open ones go to OWNER-TODO in 13.4.
- **Revisit** when supervisory authorities publish guidance on form data retention or on captchas, or when sites
  report that a default gets in the way of ordinary work.
