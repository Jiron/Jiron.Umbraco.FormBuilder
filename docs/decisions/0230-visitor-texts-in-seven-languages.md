# 0230. Visitor texts in seven languages, and a site's and a form's own wording

- Status: accepted; amends [ADR-0066](0066-answers-as-merge-tags.md) (the words for yes and no),
  [ADR-0073](0073-option-lists-and-sources.md) (the standard lists), [ADR-0093](0093-the-razor-rendering.md) (the
  rendering's words), [ADR-0095](0095-visitor-texts-members-only-forms-and-the-pending-note.md) (a site's labels) and
  [ADR-0099](0099-visitor-error-messages.md) (the messages and their plurals)
- Date: 2026-11-11
- Roadmap step: 14.1 (part 14.2, visitor texts in seven languages)

## Context

A visitor sees words the package brings: the messages for an error (both runtimes, held to one text by
`tests/vectors/messages.json`), the words a rendered form brings (buttons, marks, notices, the file control, the
signature, the copy of the answers), the words for yes and no that answers and merge tags show, the standard lists'
options, and the mails and the page that confirm an address or bring back saved answers. The messages, the rendering's
words, the words for yes and no and the standard lists came in English and German; the two mails and the page already
came in seven languages, each in its own register. The roadmap adds French, Italian, Dutch, Spanish and Danish to all of
them, and asks that every one of them can be reworded per site and per form.

A site could reword only the three buttons; a form could reword its buttons and, per field, the message for each kind
of error. Nothing let a site or a form reword "Choose a file", the summary's heading, "Your answers are kept" or the
words for yes and no.

## Options

**How the two runtimes keep the same words in seven languages:**

1. **One file both read.** Core would embed a file from the client's folders, or the client import one from Core's; and
   the languages Core already writes in its own code would move into it.
2. **A table in each runtime, held to one written list.** `packages/logic` writes every word of its tables to
   `tests/vectors/tables/message-words.json`, as it writes the Unicode tables, and a test of each runtime holds its own
   words to it. Chosen: it keeps each runtime's tables where its code is, and the list is readable in a review.

**The plural forms:** CLDR's French takes its singular for 0 as well as 1.

1. **A rule per language in both runtimes.**
2. **One form for 1 and the other for any other number in all seven.** Every count a message names is at least 1 where
   the message can show (a least of 0 never fails; a most is at least 1), and the one count that reaches 0, the
   character counter, is worded in French as a label and a number ("Caractères restants : 0"). Chosen; ADR-0099's
   "revisit" is answered.

**A site's and a form's own wording:**

1. **A field per text** in the settings and the Settings view: hundreds of fields.
2. **A list of the texts reworded**: the package's texts by a key each, the site's or the form's wording for each one
   it changes, in any of the site's languages. Chosen.

## Decision

1. **The languages and their voice.** English, German, French, Italian, Dutch, Spanish and Danish. French addresses the
   visitor as "vous", Spanish as "usted" (polite in every Spanish-speaking country), Dutch as "u" where a pronoun is
   needed, German as "Sie"; Italian and Danish use the informal address their web forms use, worded so that it names
   nobody's gender ("Se non hai compilato tu il modulo", never "se non sei stato tu"). French writes a typographic
   apostrophe, a no-break space before a colon, and sizes in Ko and Mo; the other languages KB and MB.
2. **The tables.** Each runtime keeps a table per language: `packages/logic/src/lang/messages.<language>.ts` (a page
   loads only its own), and Core's and Web's word records, English and German in their own file and the five added
   languages in `lang/` beside it as partial records, whose letters need no escaping there. The standard lists are
   `OptionLists/lang/<language>.json`. A culture without words of its own takes its more general culture's, then
   English, in every table.
3. **The checks.** A test of each table finds a word a language lacks, leaves empty or writes with other values in
   braces than English's (`message-words.test.ts`, `VisitorWordTablesTests`); the message tables are both runtimes' to
   the letter through `tests/vectors/tables/message-words.json` (`VisitorMessageWordsTableTests`); and the message
   vectors hold a broad sample in each added language, with each culture's formats in `tests/vectors/cultures.mjs`. The
   copy guide lists every message in all seven languages.
4. **The standard lists** take a site's or a form's own options as they always did: a list of the site's library, or
   the field's own options. They are not among the texts below.
5. **A site's and a form's own wording** (`texts`): a map from a text's key to its wording per culture: a culture's
   own (`fr-CH`), its language's (`fr`) or, as `""`, every language's. Unlike a form's texts it needs no default:
   rewording a text in German leaves it the package's in French. Each wording is one line of at most 500 characters,
   and may name a dictionary item (`#key`). The editor writes the culture of the language it edits.
   - **The texts** are the package's visitor texts by key: each message (a counting one as `minLength.one` and
     `minLength.other`), each word the rendered form brings but the three buttons (which keep their own fields) and the
     note only backoffice users see, and `yes` and `no`. Core keeps the list of keys and their groups
     (`VisitorTextKeys`); a test holds it to the tables.
   - **Where:** the form's in its Submission settings beside its button labels (`settings.submission.texts`); the
     site's in Settings › General (`texts`, per site, and for every site at once).
   - **Which wins:** the form's wording of a text, then the site's, then the package's, each in the page's culture,
     then its language, then for every language.
   - **What a wording may say:** a value in braces only where the package's text has it; a key the package does not
     have is refused, as is a wording that is not one line (a control character, or a line or paragraph separator).
   - **Where it shows:** the server renders with the words reworded, the messages a post's errors show and the words for
     yes and no included; the form endpoint gives the browser the reworded messages of the page's culture with the
     form (`texts`), and the runtime takes them over its language file's.
   - **The projection** keeps the form's wording for the server, and its hash counts it, so a text reworded is a new
     public revision, as a label changed is; the visitor's form never holds it (each page gets its culture's wording
     apart), and the visitor form's schema leaves it out (`PublicFormSchema.ServerOnly`).
   - **The editor** lists the texts reworded, each labelled with the package's words in the language the screen edits,
     so its box needs no placeholder (the label says it once); "Reword a text" chooses another from the package's
     texts by group, and a text taken away keeps the package's words. The management API's `visitor-texts` gives the keys, groups and the package's words of a culture.

## Consequences

- A new visitor word is a word in seven languages, or a test fails; a word changed in one runtime only fails until
  the other has it too.
- A site can say its own "Send", "Choose a file" or "Enter an email address" once for all its forms, and a form can say
  its own again; the field's own message for a kind of error still comes before both.
- A language added later needs a table in each runtime, a line in `message-words.json` and its culture's formats in
  the vectors; the texts' keys stay the same.
