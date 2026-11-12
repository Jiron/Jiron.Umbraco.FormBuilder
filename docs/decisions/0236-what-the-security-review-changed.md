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
