# 0236. What the security review changed

- Status: accepted
- Date: 2026-11-12
- Roadmap step: 16.1 (the security review; `docs/security-review.md` lists every finding)

## Context

The security review of 16.1 read the code by threat: output encoding, access control, outbound calls, and input, uploads
and limits. Beside the two decisions of [ADR-0235](0235-what-an-action-needs-beyond-manage-actions.md), it found places
where a rule held for most of a feature but not all of it. Each is fixed with a test; this record keeps the decisions
that change what an earlier ADR said.

## Decisions

### The library: a folder's trash and restore need `design` on all of it

A folder goes to the trash with every folder and form below it outside the trash, and a form in the trash is no longer
served. `design` on the top folder alone let a user take a restricted form offline that they hold nothing on, and bring
it back. A trash and a restore now need `design` on every folder and form they take (`FolderTakes`), as a delete for good
needs `design` and `deleteEntries` on everything it removes ([ADR-0047](0047-trash-delete-and-folders.md),
[ADR-0166](0166-the-form-library.md)). The refusal names `design`, and nothing moves.

### Forwarding: sensitive values go only to a user who may see them

A sender who may see sensitive values could forward an entry with them to any user who may view the form's entries, so a
user without `viewSensitive` read them in their mail ([ADR-0208](0208-the-entry-panel.md)). To a user, sensitive values now
go only when that user holds `viewSensitive` on the form; otherwise they stay masked, and the timeline and the audit log say
so. The recipients list says which users may see them, and the sidebar offers the box only for those, with one sentence
for the others. A forward address of the site's settings, which only someone who manages settings names, gets what the
sender chose, as before.

### Deliveries: a visitor's address shows only to who may see sensitive answers

The deliveries log (`manageSettings`) showed every mail's recipients, so the address a visitor typed into a sensitive email
field reached someone who may not see the form's sensitive answers ([ADR-0219](0219-deliveries-and-the-audit-log.md)). The
log cannot tell whether that answer was sensitive, so it holds every visitor's address as if it were: a mail whose
recipient a visitor typed (its stored `recipientKey`) shows "A visitor's address" to a user without `viewSensitive` on the
mail's form, in the list, the details and a limited mail's story, and the search looks at such addresses only for a user who
may see sensitive answers on every form. Addresses the site's own actions name show as before.

### A post without JavaScript: nothing is written down that its checks would refuse

A post that names its form in its address is read before Umbraco's routing, and its files went to temporary files before
the surface action checked the client's share of what may wait, the site's room, or the group's rows
([ADR-0102](0102-files-in-a-post-without-javascript.md), [ADR-0148](0148-what-uploads-may-hold.md)). Now:

- **Only a page's post is read.** A marked post under Umbraco's own path or the public API's goes on untouched, so the
  public API's promise that it never takes a form holds for it too.
- **Share and room first.** At its first file, the client's share and the site's room are checked, as the upload endpoint
  checks them. A spent share leaves its files out and answers as a post over its limit (the page with its answers and
  "choose your files again"); a full site leaves them out as full.
- **What a server holds at once.** Each byte a post writes to a temporary file counts against what this server's posts
  hold (`FormPostBuffers`): a client its share (`Files:MaxWaitingBytesPerAddress`), all of them what may wait
  (`Files:MaxWaitingBytes`). As with what waits, a read goes on while the count is below the limit, so it passes it by
  one read at most; then the file is let go and the post stores nothing, with the full site's notice. The count is this
  server's own, as its temporary files are, and ends with the request. A file is held to its field's limit within the
  site's (Settings › Files), and a post is held to one share past its values at most.
- **Rows past what a group takes.** A group's rows are still read as posted, so the pipeline's check refuses too many
  ([ADR-0110](0110-the-runtimes-frame-its-chunk-and-the-reading-of-the-page.md)), but a file or a typed signature in a
  row past the group's `maxRows` names no field: nothing of it is written down or drawn first.

### The rate limits: a flood leaves new visitors room

Past `Limits:MaxTrackedClients` windows a new client shared one overflow window per partition, and a window lived as long as
its partition (an hour for the hourly limits), so addresses that each asked once (an IPv6 /48 holds 65,536 /64 networks)
filled the bound and held every new visitor to one address's limit for an hour ([ADR-0137](0137-the-rate-limiter.md)). Now a
new client shares one of 64 overflow windows, by a hash of its key, so it shares with a part of the flood only, and while the
bound is nine tenths full the minute's sweep also forgets a client's window that counted one request and has had none for a
minute. Such a client gets that one request back at most; a window that counted more, and a share of uploads, which counts
mebibytes, stay. The site-wide partitions hold a flood as before, and the overflow lets 64 times a client's limit through
before they do.

### Uploads: an IPv6 network's shares hold together

A share of what may wait (`Files:MaxWaitingBytesPerAddress`) was an address's, an IPv6 address's by its /64, so a single /48
held 65,536 shares and filled `Files:MaxWaitingBytes` for a day ([ADR-0148](0148-what-uploads-may-hold.md)). The /48 around
an IPv6 address now holds four shares together, beside each /64's own; a server key keeps its own share alone. IPv4 addresses
are not grouped, as one address is often a whole office.

### Exports: a download's ticket counts only while its user may download

A background export's link carries a ticket that counted for its minute whatever happened in it: a user disabled, or
without `viewSensitive` any more, still downloaded the file, and a person's export shared its tickets' purpose with a form's
([ADR-0210](0210-entry-exports.md), [ADR-0223](0223-data-subject-tools.md)). The download now makes the checks the ticket was
issued after again, for the ticket's user (`ExportDownloadHolders`): enabled, `export` on the form and `viewSensitive` when
sensitive fields went along, or `privacyTools` for a person's export. A person's tickets have a Data Protection purpose of
their own, and a ticket reads only for the form its link names. The file answers `Referrer-Policy: no-referrer` beside
`nosniff` and its sandbox. A ticket still counts more than once in its minute, as a ticket used once would need a store that
every server shares.

### Mail: a held-back link mail keeps no code

A confirmation or resume mail's stored copy lost its code once the mail was sent ([ADR-0226](0226-the-phase-13-privacy-review.md)),
but one the visitor-mail limits held back kept it for as long as it was kept, though it is never sent nor tried again. The
unit of work that stores such a mail as limited now replaces its code with `removed`. A failed or uncertain mail keeps its
code, as a retry from the deliveries log must send a working link; the code expires on its own, and no screen shows a body.

### Redirects: the template is checked when it runs

An outcome's redirect was rendered and the result held to the publish rule, but not the template, which a version stored
without publishing's check (from before the rule, or written another way) could hold with a merge tag in the host
([ADR-0035](0035-structural-validation-and-public-projection.md)). It was not reachable: the renderer writes a tag before the
path as it stands, so `https://{{ field.site }}/thanks` gives an address with braces in its host, which the rule refuses.
The template is now held to the rule when it runs as well, so the guard no longer rests on the renderer alone; a test holds
the behaviour, and a tamper that takes both guards away fails it.

### Logs: no exception reaches a log as it is

Every log call was to pass `LoggedException.Of(…)`, which keeps an exception's type, a database's codes and its stack
and drops its message ([ADR-0226](0226-the-phase-13-privacy-review.md)), but nothing held a call to it: ten places (the
entry lists, recent entries, an entry's title and panel, a member's erase, a site's settings removal, the mail queue's record
of a send, the field types' checks, the health check's probes and a failing change handler) passed the exception as it was, whose message may quote an answer, an address or a file name. Now a log message's
exception parameter is a `LoggedException`, so such a call does not build, and an architecture test finds any log message of
Core and Web that takes another exception. The class moved from the privacy namespace to Core's root, as every part logs
through it. A site's own handler that throws is logged by its type and stack too, as every other exception.

### Logs: only named values, never a record

A record's generated `ToString`, or a structured log that unfolds an object, writes every property, answers and addresses
among them. A log message now takes only scalars (texts, numbers, keys, times, enums and a `LoggedException`), so each value
it writes is one its call names, which an architecture test holds; and the records that carry a submission, a draft or a
mail (`SubmissionRequest`, `NewDraft`, `NewEmail`, `StoredEmail`, `OutgoingEmail`) print their name alone, for a debugger,
a test's message or an exception that quotes one.

### Tests: a user runs sixty an hour

A test mail goes to the user's own address, and an action's test to where the action sends, but nothing limited how many a
user ran. The settings' test mail, a layout's, the summary's and an action's test now share a limit of sixty an hour per user
on each server (`TestLimits`), far past what anyone trying a mail needs; past it they answer `429 tests_limited` with
`Retry-After` and send nothing.

### Entries: another form's entry is none of this form's

Every entry endpoint finds its entry by the route's form and the entry's key together, so a key of another form's entry
answers as an entry that is gone, even for a user who may see both, and changes nothing. A test now holds that for each:
opening, revealing, the timeline, notes, editing answers, the forward's recipients, a forward and the action runs.

### The AI add-on in the permission model

The management API's matrix enumerates Web's endpoints; the AI add-on's were tested one by one. A matrix of its own now
holds that each declares exactly one access and none is anonymous, and that each answers `401` without a token and `403
no_section` to a user without the Forms section.

### The blob sample: a container anyone may read is refused

The sample made its container private where it was missing, but used an existing one as it was, so a site whose container
allowed public reads stored visitors' files where an address alone opened them. The first save or read now refuses such a
container, naming it, and stores nothing.

### Mail limits: a Gmail mailbox is one however it is spelled

The limit of mails to one visitor's address counted a mailbox by its key: case-folded, normalised, without a `+tag`, its
domain as the mail server looks it up ([ADR-0012](0012-anonymous-endpoints-spam-protection-and-challenges.md)). Gmail also
reads a local part without its dots and takes googlemail.com for gmail.com, so `a.d.a@gmail.com`, `ada@googlemail.com` and
sixty other spellings each counted as a mailbox of their own and reached one inbox past the limit. For those two domains the
key now drops the dots and names gmail.com; every other domain keeps its dots, as its mail server may not ignore them. The
privacy tools find a person's mail by the same key.

### Captcha: a stub's address may be plain http only on this machine

`Captcha:<provider>:VerifyAddress` points a provider at a test host's stub, and took any http address, so a host could send
its secret key, which goes with each token, unencrypted to another machine ([ADR-0142](0142-the-captcha-providers.md)). The
site now starts only with an https address, or an http one on a loopback host (`localhost`, `127.0.0.1`, `::1`).

### The public API: a form for members is no shared cache's

The form and options endpoints answered `no-cache` with an `ETag`, which lets a shared cache keep an answer and check it
before it serves it again; for a form for members, which answers only a member it admits or a server key, a cache in front of
the site that left out the check, or keyed it without the member, could hand it to anyone
([ADR-0084](0084-the-public-apis-frame-and-the-form-endpoint.md)). Both now answer `private, no-cache` for such a form, as
every other endpoint answers `no-store`.

### The public API: a refusal of unknown names stays short

A body within its limit could name tens of thousands of fields the form does not have, and the refusal named each. It now
names the first hundred at most, as the body's own reading does.

### Webhooks: a secret as itself is refused where it is saved

Publishing refused a signing secret, a chat address or a credential header typed as itself, and the designer's sidebar
never let one reach the draft, but a call that skipped the sidebar saved it into the draft and its History, where anyone
who reads the form's design could read it ([ADR-0161](0161-webhooks-slack-teams-and-entry-events.md)). A draft save, a
restore, a replace and an import (and their checks) now refuse it with `secretReference` at the setting, in an action they add
or change, so an action stored before keeps it until someone changes that action and other edits still save. Settings
that do not fit the type are left to publishing, as before. The words that mark a credential header grew from auth, cookie,
key, password, secret and token by bearer, credential, passphrase, passwd, session and signature, in the server and the
sidebar alike; a bare "pass" or "private" would have held plain headers such as `X-Bypass-Cache` to a reference.

### Themes: a token's value is read as an allowlist

A token's value refused a declaration's end, markup and `url(`, and passed everything else, but tokens feed properties
such as `background`, where `image-set("https://…")`, `-webkit-image-set()` or `u\72l(…)` (an escape that spells
`url`) loaded an image from another host on a visitor's page ([ADR-0215](0215-library-themes-and-email-layouts.md)). A
value now keeps to a grammar (`ThemeTokenValue`): ASCII letters, digits, spaces and `# % . , + - * / ( )` with balanced
parentheses; quoted names of letters, digits, spaces, hyphens and underscores; and calls only to colour, arithmetic,
`var()`, gradient and easing functions. No backslash, comment, colon or other function passes. Every built-in value
keeps to it; a library theme's stored value that does not is left out of its layer, as before, and the theme editor names it.

### Themes: custom CSS stays inside the form and spells no address

Every selector went under the theme's form, but a combinator right after it reached the page beside the form:
`.jfb-form ~ *`, `.jfb-form + p` and `:root ~ p` (the page around the form being the form itself) styled the site's own
elements ([ADR-0215](0215-library-themes-and-email-layouts.md)). Such a selector now goes, said as `outside`, and the rest
of its list stays; a `+` or `~` further in (`.jfb-field ~ .jfb-field`) stays inside the form. CSS reads an escape as the
letter it stands for, so `u\72l(…)` is `url(…)` and `url("https\3a //…")` names another host: a declaration with a
backslash outside quotes goes, said as `escape`, and an address with one is none of the site's. A string's own escapes,
as in `content: "\201C"`, stay. The editor names both kinds in English and German.

### The orphan cleanup moves past what it cannot remove

The cleanup read the oldest two hundred uploads no entry took and stopped when none of them would go, so files a store
kept locked held back everything behind them, and the waiting cap stayed full. A run now reads on after the last file it
tried, and what would not go comes round in the next run ([ADR-0144](0144-cleanup-and-the-scanner.md)).

### Forms as files: an alias Windows keeps for a device

`con`, `nul`, `com1` and the other device names are valid aliases, and `nul.json` is a device on some Windows versions.
The mirror writes such a form's file with an underscore first (`_nul.json`), which no alias can start with; it reads every
file by the form key inside it, so nothing else changes ([ADR-0220](0220-forms-as-files.md)).

### The backoffice: every poll starts the idle gate

A poll waits while nobody uses the backoffice, so it never renews an expired session
([ADR-0209](0209-the-inbox-spam-badges-and-the-summary-email.md)). Only the Inbox's menu item started the gate, so a user
without it (a privacy officer following a person's export) had polls that renewed the session. Every element that polls
now starts the gate from its own auth context first (`startIdleGate`).

### The outcome preview drops SVG's animations

The preview of an outcome puts the server's markup in the backoffice's page, without what could run or load
([ADR-0187](0187-the-outcome-preview.md)). The markup is the sanitised message and the site's own view, so nothing a
visitor writes reaches it; as a second line, SVG's animation elements (which can set a link's address after the
attributes were checked) and `vbscript:` and `data:text/html` addresses go too. The guards that hold the backoffice to Lit's
templates (no `unsafeHTML`, no direct confirm modal, no hard-coded words) now hold the AI add-on's package as well, and the
website code may not parse HTML through `DOMParser`, `createContextualFragment` or `setHTMLUnsafe` either.

## Consequences

- Each decision has a test, and each test was checked by putting the old code back (a "tamper"), which it failed.
- A refused trash, a post over a share and a full server say so as the existing refusals do; no new words for visitors.
- The tamper of the cleanup showed that a test whose loop ends only on a deadline hangs on a regression; the test now
  moves its clock with each step, so a regression fails instead.

## Amendment (2026-11-12, step 16.5)

The phase review found four ways around the refusal of a secret as itself: a duplicate with its actions, copied actions,
saving a form as a template and a new form from a template took a stored draft's actions as they were, so an action saved
before the check carried its secret on into another form and its History, and a template offered it to every designer of
the site. Each now refuses such an action with `secretReference` at its setting in the source, as an import does: a
duplicate and a template look at every action, copied actions at the actions taken, and a duplicate without its actions
takes nothing to refuse. Tested in `ActionGrantApiTests`
(`ASecretAsItselfInAStoredDraft_IsNotCarriedOn_ByADuplicateCopiedActionsATemplateOrAFormFromOne`).
