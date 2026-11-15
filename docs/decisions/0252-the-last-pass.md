# 0252. The last pass: what it changed and what it kept

- Status: accepted; changes [ADR-0163](0163-save-and-resume.md) (how a draft is named and what its mail's row is for),
  [ADR-0007](0007-background-work-outbox-and-email.md) (no `Expires` header), [ADR-0100](0100-the-post-without-javascript.md) (a form that stores no
  entries answers on the post) and [ADR-0218](0218-the-settings-screens-and-access.md) (a field may go without a help line)
- Date: 2026-11-14
- Roadmap step: 21.1

## Context

The owner asked for one last pass over every function, "plain and simple without missing out on features for no
reason". Sixteen readers went through the product in parts, eight over the code and the guides and eight over the
screens' words and looks in English and German (the visitor's side in all seven languages), on the shots of a full walk
of the build that 20.1 verified. `docs/ux/review-21.1.md` lists every finding with what became of it. This record holds
the decisions that change an earlier one, and the rules the pass worked by.

## Decision

### Words

- **A help line says what its label can't.** A line that repeats its label, names the screen it sits on or explains
  the box below it goes. A settings field may therefore go without a help line: `SettingsField(Help = false)`, and the
  screen draws none. Thirteen fields do.
- **Two sentences keep the one that says something,** and a message names the thing and stops ("1 error blocks
  publishing.").
- **A list of choices keeps a line under every choice or under none,** so three lines the readers would have cut stay:
  a list reads evenly.
- **Nothing that tells what happens to data goes:** what a deletion takes with it, what is never stored, what a visitor
  gets, what can't be undone.
- The backoffice's dictionaries lost about 1,330 English and 1,390 German words of 20,700 and 21,100.

### Saving for later (changes ADR-0163)

- **The save's answer names the draft** (`resume`, a random value where the save was limited or taken for spam, so the
  answer tells nothing). The page sends it with the next save, which replaces the draft, and with the entry, which
  takes the draft along; a post without JavaScript carries it in a hidden `jfb-resume` field.
- **A draft keeps its challenge as a mark of its own,** apart from the entries' challenges: the challenge that saved a
  draft still makes its one entry, and files attached before the save are still taken at Send.
- **The draft a save replaces goes only once the new mail is queued,** so a save the mail limits hold back loses
  nothing.
- **A form written from a post, or put in another's place, restores nothing:** it holds what the visitor just typed.
  The server marks it (`data-jfb-posted`), and the page only keeps the draft's name.
- **A tab's draft that is gone is dropped silently;** only a link from a mail says that it expired.
- **Fields the visitor had emptied come back empty,** while the saved version is the served one.
- **The mail that carried a draft's link stays while the draft does,** past the 30 days mails are kept: a draft holds
  no address of its own, and this row is how Privacy > People finds it.

### The page

- **A number field reads a decimal comma where the page's language writes one** (digits with one comma, nothing
  else): in the runtime's page reader and in the post without JavaScript. The JSON API stays strict.
- **A half-typed date or number is refused,** not sent as empty; a 200 without a receipt is not a stored entry; a form
  whose logic or runtime can't load says so at Send and tries again.
- **A form that stores no entries answers its message on the post itself** (changes ADR-0100): the page after a
  redirect found no entry to show the outcome of and showed the empty form again.

### The server

- **Mail goes out without an `Expires` header** (changes ADR-0007): Umbraco's sender writes one from the time-out it
  is handed, and a mail the queue delivers later could be dropped by its receiver.
- **Each outbox queue has its own time in a pass,** and a run that sent nothing gives its attempt back.
- **The mirror never writes over a form's own file that it can't read,** and one refused file no longer ends the pass.
- **A theme or email layout that is one site's default can't be deleted.**
- **A file whose action sends a secret reference is imported only by who manages settings.**
- **A field marked sensitive later is sensitive in older entries too,** in the panel, a forward and an edit, as the
  list, the exports and the downloads already had it ([ADR-0210](0210-entry-exports.md)).
- **The address a form's email verification confirms is not edited in the entry panel,** and an answer kept only until
  the actions ran is not brought back by an edit.
- **A field whose option list the site lacks is an error of the form check** (`option_list_missing`), which
  [ADR-0214](0214-the-option-list-library.md) had left to a check that did not exist.

### The backoffice

- **The Access, Settings and Retention screens ask before unsaved changes are left,** with Umbraco's own dialog, and
  keep pending changes when a reread comes in.
- The entry panel gives a late answer only to its own entry; the entries list reads on when its loaded rows run out;
  publishing refreshes the form's row in the tree; a save takes what is still being typed.

## Kept, with the reason

- **The message tables in seven languages** (two shorter wordings, an Italian date phrase, a Spanish signature
  phrase): the texts are right, and the shared vectors pin them in two engines.
- **The mails' own wording:** each says one thing per sentence already.
- **Every trigger keeps its section in a form's Actions view,** and the third-party captcha box stays in sight: both
  show what a form can do before it does it.
- **A post without JavaScript repeated on a members' form with a file** is answered "not sent" though the first post
  was stored: the repeat stores the file under a new name, so its answers differ. It needs a digest by content, a
  change of its own, and is named in `docs/STATUS.md`.
- **A mail's subject shows in an entry's timeline as it was sent,** also where it holds a sensitive answer the editor
  put there: the subject is what the form's author chose to mail.
- **The search text stored with an older entry** still holds an answer whose field was marked sensitive later, until
  the entry is edited: a search for that answer finds the entry and shows no answer. Rewriting every entry's search text
  at a publish is a job of its own.
- **A form whose first mirror publish was refused stays unpublished until its file changes again:** the log names the
  file and the reason.
- The entry panel, History and the audit log at 375 px, which is below the widths the backoffice supports.

## Consequences

- The public API's draft answer gained `resume`, and the view model `isPosted`; both are in the API snapshots.
- A site that named a missing option list in a published form sees an error at its next publish, not before: the
  published version stays as it is.
