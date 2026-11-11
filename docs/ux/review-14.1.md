# Step 14.1: the German screens, and the visitor's texts in seven languages

ROADMAP 14.1 asks for the backoffice in German reviewed from screenshots, and 14.2 for a reader per language who reads
the visitor's texts in context, each one's fixes applied. Both came from the same two walks, so this review holds both:
the backoffice in German ([ADR-0228](../decisions/0228-translating-a-form.md)) and the visitor's side in English,
German, French, Italian, Dutch, Spanish and Danish ([ADR-0230](../decisions/0230-visitor-texts-in-seven-languages.md)).
Per ADR-0019 and ADR-0020 the three design skills ran on what changed; the owner answers no questions during the run
(CLAUDE.md rule 8), so every choice was decided here or in the ADRs.

## What was looked at

- **The backoffice in German:** the UX walk (`specs/ux/walk.spec.ts`, ROADMAP 9.6a) in its six German looks (375, 768
  and 1440 px, light and dark), 124 screens each, in Edge on the e2e host. Two readers who had not seen the work read
  every shot of 1440 light and 375 light against the copy guide's glossary and voice. After the fixes the screens they
  named were walked again at 1440 and 375 px; the shots below are from that run.
- **The visitor's side:** a new look, `specs/ux-languages/visitor-languages.spec.ts`, shows one two-page form on the
  headless page in each of the seven languages at 1024 and 375 px: fresh, after Next on an empty page, with a wrong
  address and a number out of range, its second page (the progress, Back, a counter, a file box and a consent) and sent.
  The public API serves a site's own languages only, so the look adds the five the demo lacks to Umbraco first; its own
  Playwright project runs after the walk, whose screens they would change. A reader per language (French, Italian,
  Dutch, Spanish, Danish and German) read its ten shots and every text of its tables in both runtimes: the messages,
  the rendering words, the yes and no words and the standard lists.

| Shot | What it shows |
| --- | --- |
| [Translating, the canvas](review-14.1/translate-design-1440.png) | German shown: the marks on the fields written in English only, none on the field whose label names a dictionary item |
| [A dictionary item](review-14.1/translate-dictionary-1440.png) | the label `#Demo.Login.Email`, the line under it "Aus dem Wörterbuch: E-Mail-Adresse", the canvas showing the item's text |
| [Translating, the settings](review-14.1/translate-settings-1440.png) | an outcome's row with "Noch kein Text auf Deutsch" |
| [A rule in German](review-14.1/rules-1440.png) | "Pflicht, wenn „…“ kleiner als 7 ist": the verb last, the field's name quoted; „Regel hinzufügen“ under the sentence |
| [Summary by email](review-14.1/digest-1440.png) | the note naming its subject, „Enthalten sind… Die E-Mail geht an Ihre eigene Adresse…“; „Test-E-Mail senden“ follows under it, scrolled out of the shot |
| [Summary by email, on a phone](review-14.1/digest-phone.png) | the footer only Cancel and Save, both whole |
| [History](review-14.1/history-1440.png) | „Veröffentlicht, Entwurf geändert“ beside the name; an empty comment an empty cell |
| [History, two versions compared](review-14.1/history-compare-1440.png) | „Beschriftung · Deutsch (Deutschland)“: a language after its text's name with a dot |
| [French, wrong answers](review-14.1/visitor-fr-1024.png) | "Email : Saisissez une adresse e-mail complète…", a no-break space before the colon |
| [Italian, the second page](review-14.1/visitor-it-375.png) | "Pagina 2 di 2", the counter, the file box, Indietro |
| [Dutch, an empty page](review-14.1/visitor-nl-375.png) | the summary and each field's message |
| [Spanish, sent](review-14.1/visitor-es-1024.png) | the default outcome and the reference |
| [Danish, the second page](review-14.1/visitor-da-1024.png) | "Højst 200 tegn", "Træk en fil hertil, eller" |
| [German, wrong answers](review-14.1/visitor-de-375.png) | the summary and the two messages on a phone |

## The backoffice in German

The readers found 46 of the 124 screens without a finding at 1440 px; what they found, and what was done:

| Finding | What was done |
| --- | --- |
| A sentence opening with „Sie“ that means "it" or "they" reads as the polite "you": an action's trigger („Sie läuft, sobald…“), the preview's note, the empty Inbox, the digest's lines and its note („Sie enthält…“), the shortcuts' lead, the replace dialog | each names its subject: „Läuft erst, wenn…“, „Für sie laufen keine Aktionen“, „Einträge erscheinen hier…“, „Die Zusammenfassung geht…“, „Enthalten sind… Die E-Mail geht…“, „Die Kürzel wirken…“, „Die Datei wird…“ |
| A rule's words kept the English order inside „wenn“: „Pflicht, wenn How likely…? ist kleiner als 7“, „…wenn die Sprache beginnt mit de“, „…wenn die ausfüllende Person ist angemeldet“ | a clause per language (`jfbRules_clause`): German puts the verb last and quotes a field's name; the start of a culture is quoted as written. English reads as before; tests hold both |
| The field whose label names a dictionary item carried "Noch kein Text auf Deutsch", though visitors read the item's German | a default of exactly `#key` lacks no language (ADR-0228 amended): the dictionary holds its texts, the line under the box says what visitors see. The walk after the fix still showed the mark, which found the cause: the canvas read the marks from the draft as it draws it, the item's text in the key's place; it now reads the draft's own texts, and a canvas test holds it |
| The default theme was "Default" in every German screen: „Wie die Website (Default)“, „Baut auf Default auf“ | its name is a word, said in the backoffice's language wherever a theme is named (`jfbSection_defaultTheme`: Standard); a site's theme keeps its label |
| Words against the glossary: „Sensible Felder“, „Prüfprotokoll“, „freigegeben“, „Platz in der Liste“ (a place is a seat of an option with a limit), „Gruppen“ for user groups, „Eingereicht“ beside the trigger „Gesendet“, two names for one setting („Kennzeichnung“ and „Markierung“, „Standard, mit einer Proof-of-Work-Prüfung“ and „Standard mit Rechenaufgabe“) | the glossary's words: vertraulich, Protokoll, genehmigt, Position in der Liste (in English too: Position in the list), Benutzergruppen, Gesendet, one name each |
| Stiff or literal: „Leer sagt sie Senden“, „Eine Aktion behält, was sie tut“, „signieren jeden Aufruf damit, damit…“, „die größte Größe“, „Das Formular erstellt“, „Die Adresse ohne ihr Ende“, „0,1 Punkte mehr“, „192, 16,8 % der Begonnenen“ | „Leer: „Senden““, „Die Art einer Aktion lässt sich nicht ändern“, „…mit diesem Geheimnis, sodass…“, „Höchstgröße“, „Formular erstellt“, „um ihr Ende gekürzt“, „Prozentpunkte“, „192 (16,8 % der Begonnenen)“ |
| A name read as a word of the sentence: „Typ von Which branch…? ändern“, „An die Antwort von Email address“, „unter Einträge mit dem Filter Aktionen“ | quoted: „Typ von „…“ ändern“, „An die Antwort von „…““, „unter „Einträge“ mit dem Filter „Aktionen““ |
| A space before the ellipsis: „Daten wählen …“, „Theme importieren …“ | „Zeitraum wählen…“, „Theme importieren…“, as the guide's "Move to…" |
| History: „Beschriftung (Deutsch (Deutschland))“, brackets in brackets; „keine Angabe“ in every empty comment cell | the language after a dot, „Beschriftung · Deutsch (Deutschland)“; an empty comment an empty cell |
| The copy dialog's button repeated the form's name the message had just said, and on a phone ran off its footer | „Formular öffnen“ (Open the form) |
| The digest sidebar's footer held three buttons; on a phone „Test-E-Mail senden“ wrapped and Save fell off the screen | Send test email sits under the note that names the address it goes to; the footer keeps Cancel and Save |
| The form's state chip „Veröffentlicht (Änderungen bereit)“ squeezed the name box | „Veröffentlicht, Entwurf geändert“: shorter, and it says which draft |
| „Regel hinzufügen“ beside the closed Rules header left the rule's sentence a column of two or three words, four lines high | where the sentence would keep less than about 14rem, as at the inspector's usual width in either language, Add a rule moves under it, at the same end; a wider inspector keeps it beside |

**What stays, and why:**

- **The walk sets the backoffice's language in the browser only** (it answers the current user's request with
  `languageIsoCode` in `support/backoffice-look.ts`), so the texts the server writes in the user's language stayed
  English in the shots: the Overview's alert ("6 emails couldn't be sent…", `HealthWords`), the digest's preview
  (`EntryDigests`) and the email layout's sample mail. Their German exists and is tested, and a German user's server
  writes it.
- **Umbraco's own:** at 375 px Umbraco keeps its tree beside the workspace and the page scrolls sideways (545 px,
  ADR-0182), which is what most of the phone reader's "cut off at both edges" findings were; the walk's own check still
  finds no sideways scroll beyond Umbraco's. The header search's tab says "Forms" in every language, as Umbraco shows a
  tab's label as written (its own "Documents" too). A dialog's primary button takes two lines on a phone where its
  German label is long („Vorlage speichern“): UUI's dialog gives its actions no way to wrap, and the label stays whole
  and the target large.
- **Pre-existing at phone width, in English too:** two date boxes side by side in Availability, an action card's
  controls beside its text, a rule builder row beside its select. They passed 9.6's and 13.4's walks, which judged the
  phone by what Umbraco's narrowest workspace allows; they are noted for 16.6, the UX polish pass, which walks every journey at 375 px in both languages.

## The visitor's side in seven languages

Each reader checked about 220 texts. All six found the voice of ADR-0230 kept everywhere ("vous", "usted", "u", "Sie",
the informal Italian and Danish), every placeholder in braces kept, and the French typography right (’, a no-break space
before a colon, Ko and Mo); none found a text cut off or overflowing at 375 px. What they changed, about 135 texts:

- **French (28):** a pronoun pointing at the wrong noun in the captcha notices; "égal ou supérieur" in the usual order;
  words copied from English („Il a reçu toutes les réponses qu'il peut accepter“, „a une nouvelle formulation“) in the
  usual French; a gender agreement („Réponse masquée“); "envoyer" with its object; "Déplacer {0} vers le haut".
- **Italian (12):** „un numero pari“ read as "an even number"; „Le tue risposte non sono andate perse“ for the stiff
  „sono conservate“; „Pagina {0} di {1}“, matching the page names; the article before a size („supera il limite di“).
- **Dutch (20):** "versturen" with its object and one verb for sending; „maximaal“ for „hoogstens“, as the counter says;
  „aannemen“ (to believe) replaced; „Nog {0} tekens over“; the passive "wordt geladen".
- **Spanish (31):** two sentences that read as "you" without a subject („Ha recibido…“); words of Spain only replaced
  by words of both continents („rellenar“, „ratón“, „pulsada“, „hacer una foto“); no comma before "y" and "o"; "por
  ejemplo" with its comma, in brackets beside a decimal comma.
- **Danish (20):** an anglicism („startende ved“); „fuldt booket“ for a full option, as its label says; one word for an
  option („mulighed“); a full sentence after a colon made two sentences; the countdown said of the page, not the visitor.
- **German (21):** "laden" without an object made passive („geladen werden“); literal sentences („Es hat alle Antworten, die es annehmen kann“, „steht Ihrem Konto nicht offen“) in usual German; „Referenznummer“; „rückgängig machen“; „in Schritten von“ for a time's step, as for a number's; a no-break space in „10 MB“ and „60 %“; „Bundesland, Kanton oder Region“, as Swiss visitors read the German too; the one gendered sentence („angemeldete Backoffice-Benutzer“) without a gender.
- **The progress line, in all seven:** two readers saw the same page called „Seite 1“ in the steps and „Schritt 1 von 2“ once they fold on a phone; page is the product's word for it (CLAUDE.md's vocabulary), so the line says "Page 2 of 3" in every language, the rendering fixtures and the canvas's words with it.
- **The standard lists:** the satisfaction scale in French, Italian and Spanish described the visitor in the masculine;
  it now describes what is rated („Satisfaisant“, „Soddisfacente“, „Satisfactorio“), as Danish and German needed no
  change.

Every message changed in both runtimes, the vectors and the shared words table with it, and the copy guide's tables.
**Declined, with the reason:** Spanish "u" before a word starting with "o" ("PDF u ODT") would need a word of its own in
all seven tables of both runtimes for a rare list; Dutch „Tussenvoegsel“ for a name's middle part fits nl-NL only, and
the name field serves Flemish forms too; French counts zero as singular, but no message names a count of zero (a full
option says „Complet“), which ADR-0230 relies on.

## design-taste-frontend

Read as: Operate-mode product UI inside Umbraco's design system, and a visitor's form that takes the site's own type
and width; the skill's landing-page rules don't apply (ADR-0019). What it asks held: no colour of the package's own was
added, the digest's button is UUI's secondary button in a grid with its note, and no motion was added.

## impeccable

Operate mode, a refinement in the incumbent world, with the copy guide and the IA as the context. The bundled detector
(`impeccable detect --json`) on the visitor texts' editor, the Settings view, the digest sidebar, History, the theme
editor and the form picker's theme: `[]`. The findings it can't see were the readers' above.

## emil-design-eng

| Before | After | Why |
| --- | --- | --- |
| Send test email in the sidebar's footer, beside Cancel and Save | under the note that names its address | it acts on that address, and the footer keeps the two buttons that close the sidebar |
| "none" in every empty History comment cell | an empty cell | a word repeated down a column says nothing and pulls the eye from the comments there are |
| "Label (German (Germany))" | "Label · German (Germany)" | a language's own name may hold brackets; brackets in brackets read as a typo |
| the rule's chip in English order in German | the verb last, the name quoted | a German reader parses a „wenn“ clause by its verb at the end; without it the sentence stalls |
| `.section-line { display: flex }`, the header `flex: 1 1 auto` | `flex-wrap: wrap`, the header `flex: 1 1 18rem`, Add a rule `margin-inline-start: auto` | a sentence a few words wide reads as a list; the action keeps its end of the line whether it wraps or not |

## Checks that ran

- `npx vitest run`: 215 files, 6,097 tests; `npm run lint` (the hard-coded text rule among them) and each workspace's
  typecheck; the dictionaries' key checks, each area held to the keys its code uses.
- `dotnet test` of Tests.Unit: 8,886 passed; the integration groups of rendering (the fixtures the runtime is held to
  written again and reviewed), the public form, settings, the permission matrix, the canvas words and the OpenAPI
  snapshots; `dotnet format --verify-no-changes`.
- Both runtimes on `tests/vectors/messages.json` in all seven languages, and the words table
  `tests/vectors/tables/message-words.json` holding the two runtimes' messages to the letter.
- `npm run size`: the runtime with every feature and the largest language at 59.37 KB of 60 KB, after the savings of the
  page-only template parser, one `own()` and a compact country list (the five languages' loaders had taken it over).
- Playwright: the languages journey (`languages.spec.ts`), the visitor texts journey (`visitor-texts.spec.ts`), the six
  German looks of the walk and the fourteen looks of the visitor's languages.
