# Words and messages

How Jiron Form Builder talks: the glossary, the tone, and the patterns for buttons, errors, empty states,
confirmations and the other messages, with English and German examples. It applies to the backoffice, the emails and
the visitor-facing default texts. The screens these words appear on are in
[information-architecture.md](information-architecture.md).

## Who we write for

- **Editors** build forms between other work. They know their content, not our internals.
- **Entry handlers** work through entries quickly and need to trust that nothing was lost.
- **Administrators and privacy officers** need exact answers: what is stored, what is locked, what happens next.
- **Visitors** fill in a form once, often on a phone, and should never wonder what went wrong.

## Voice and tone

The voice stays the same everywhere: **plain, friendly, direct.** The tone follows the moment: brief for routine
success, calm and exact for errors, serious for deletion and privacy, never jokey.

- **Sentence case** for every label, heading, button and menu item ("Save draft", "Email layouts"), never Title Case.
- **Verbs on buttons:** a button says what happens, with its object when that isn't obvious ("Export 124 entries", not
  "OK" or "Submit").
- **"…" after a label** means the action asks for more before it acts ("Move to…", "Delete…"), as in Umbraco.
- **Say what happened, what it means, and what to do next.** Leave out what the user can see already.
- **No internal names** in the UI: no aliases, status codes or class names, except where the user typed them (an
  alias) or must type them (a configuration key).
- **Address the user as "you"** in English. In German, use **"Sie"**, as Umbraco's own German backoffice does, and
  keep it role-neutral ("Wer Administratorrechte hat, …") rather than choosing a gender.
- **Say each thing once.** A heading that explains the state needs no introduction below it, and a confirmation
  doesn't repeat its title in its body.
- **Short beats complete,** until meaning or recovery would be lost.

## Glossary

The CLAUDE.md vocabulary, and the words around it. Use these and only these; never vary a word for style.

| Term | Means | German | Don't say |
| --- | --- | --- | --- |
| form | what an editor builds and a visitor fills in | Formular | questionnaire, survey (unless the editor's own name for it) |
| page | one step of a form | Seite | step (except in "progress: steps"), screen |
| website page | a page of the site's content, where a form is shown ("Sent from /contact") | Seite der Website | page (alone, where it could mean a form's page) |
| group | a section of a page, with one to four columns | Gruppe | fieldset, section, container |
| field | one question or element on a page | Feld | input, control, element (in the UI) |
| option list | a reusable list of choices | Optionsliste | data source, lookup |
| option | one choice in a field or an option list | Option | item, value (in the UI) |
| rule | a condition that shows, hides, requires, skips or decides | Regel | condition (for the whole rule), logic, trigger |
| condition | one line of a rule | Bedingung | – |
| action | what runs after an entry arrives: an email, a webhook, … | Aktion | workflow, integration, handler |
| trigger | when an action runs: Submitted, Approved, Rejected, Verified | Auslöser: Gesendet, Genehmigt, Abgelehnt, Bestätigt | event (in the UI) |
| entry | one submission, as stored | Eintrag | submission, response, record, lead |
| theme | how forms look on the website | Theme | skin, style, design |
| merge tag | a `{{ … }}` value filled in when the text is used | Platzhalter | variable, token, shortcode |
| placeholder | the example text shown inside an empty field | Beispieltext | hint |
| template | a ready-made form or group to start from | Vorlage | blueprint, preset (presets are for actions) |
| draft | the version an editor is changing | Entwurf | working copy |
| publish | make the draft the version visitors get | veröffentlichen | go live, deploy, activate |
| version | one saved state of a form, in History | Version | revision (the public API's word; never in the UI) |
| History | the list of a form's versions | Verlauf | – |
| Timeline | the list of what happened to one entry | Chronik | log, history |
| Inbox | the entries of every form | Posteingang | all entries |
| Spam | where suspicious entries wait, unhandled | Spam | junk, quarantine (quarantine is for files) |
| Recycle Bin | where trashed forms wait | Papierkorb | trash (as a place), bin |
| delivery | one email or webhook run, in Deliveries | Zustellung | dispatch, job |
| audit log | the record of who did what | Protokoll | history, activity |
| form check | the list of problems before publishing | Formularprüfung | validation (that is for answers) |
| content check | a spam check on what an entry says: blocked words, blocked email domains, too many links. The roadmap and the code call these "content rules"; the UI never does, because "rule" means conditional logic. | Inhaltsprüfung | content rule (in the UI) |
| validation | the checks on a visitor's answers | Validierung | – |
| outcome | what the visitor gets after sending: a message or a redirect | Abschluss | thank-you page, confirmation |
| consent | a field where the visitor agrees, stored with its exact text | Einwilligung | opt-in (except "double opt-in" in help text) |
| email verification | the form setting that holds an entry until the visitor confirms their address | E-Mail-Bestätigung | double opt-in (as a label) |
| retention | how long entries are kept | Aufbewahrung | data lifecycle, TTL |
| legal hold | an entry kept past its retention, with a reason and an end date | Löschsperre | freeze, lock |
| sensitive | a field whose answers are masked and kept out of emails and exports | vertraulich | private, secret, PII |
| member | a website member in Umbraco | Mitglied | user (users are backoffice users) |
| user group | a backoffice user group in Umbraco | Benutzergruppe | role (except for the personas in docs) |
| language | the culture of a text or a page | Sprache | culture, locale (in the UI) |
| site | one root of the content tree | Website | root, tenant |
| place | one seat or slot of an option with a limit | Platz | capacity, quota, stock |
| test entry | an entry sent from the preview | Testeintrag | dummy |
| entry number | `#1042`, unique per form | Nummer | ID |
| entry reference | `REQ-2026-00042`, the format shown to visitors | Referenz | ticket |

### State labels

The chips in the lists and panels. The wireframes of step 2.2 use these words.

| State | English | German |
| --- | --- | --- |
| an entry waits for approval (manual approval) | Waiting for approval | Wartet auf Genehmigung |
| an entry waits for the visitor to confirm their address (email verification) | Waiting for confirmation | Wartet auf Bestätigung |
| approved, by someone or automatically | Approved | Genehmigt |
| rejected | Rejected | Abgelehnt |
| an entry in Spam | no chip: the place says it | – |
| a delivery that arrived | Delivered | Zugestellt |
| a delivery that failed and may be tried again | Failed | Fehlgeschlagen |
| a delivery cut off before the other server answered | Uncertain | Unklar |
| a mail held back by a sending limit | Limited | Begrenzt |
| a delivery waiting while sending is paused | Waiting | Wartet |
| the version visitors get, in History | Published | Veröffentlicht |

### Status lines

One line that says where something stands, next to the thing it is about.

| Where | English | German |
| --- | --- | --- |
| an action's card, after a failed delivery | Failed at 14:02. Next try at 14:30. | Fehlgeschlagen um 14:02. Nächster Versuch um 14:30. |
| beside the canvas width switch, when the canvas has less room than the chosen width | Narrowed to fit | An den Platz angepasst |
| above the permission matrix, for a user group without the section | Translators can't open Forms yet. Add the section to their user group | Übersetzer können Formulare noch nicht öffnen. Bereich zu ihrer Benutzergruppe hinzufügen |

## Buttons and actions

Use Umbraco's own words where Umbraco has the same action, so the section feels native.

| Action | English | German |
| --- | --- | --- |
| keep changes without publishing | Save draft | Entwurf speichern |
| make the draft live (asks first) | Publish… | Veröffentlichen… |
| take a form offline | Unpublish… | Veröffentlichung zurücknehmen… |
| see the form as visitors will | Preview | Vorschau |
| save a settings screen | Save | Speichern |
| apply a sidebar's changes to the draft | Update (or Add, when new) | Aktualisieren (oder Hinzufügen) |
| close a sidebar that changes nothing | Close | Schließen |
| stop without changing anything | Cancel | Abbrechen |
| make a copy | Duplicate… | Duplizieren… |
| move to a folder | Move to… | Verschieben nach… |
| move to the Recycle Bin | Trash | In den Papierkorb |
| bring back from the Recycle Bin | Restore… | Wiederherstellen… |
| go back to an earlier version of a form | Restore an earlier version… | Frühere Version wiederherstellen… |
| make an earlier version the draft | Restore as a new draft | Als neuen Entwurf wiederherstellen |
| create the block that puts forms on pages | Create a form block type… | Formular-Blocktyp erstellen… |
| delete for good (asks first) | Delete… | Löschen… |
| empty the Recycle Bin | Empty recycle bin… | Papierkorb leeren… |
| reverse the last change | Undo | Zurücknehmen |
| repeat a reversed change | Redo | Erneut anwenden |
| send again after a failure | Retry | Wiederholen |
| approve an entry | Approve | Genehmigen |
| reject an entry | Reject | Ablehnen |
| move an entry to Spam | Mark as spam | Als Spam markieren |
| move an entry out of Spam | Not spam | Kein Spam |
| send an entry to a colleague | Forward… | Weiterleiten… |
| try an action against a sample entry (on its card and in its editor) | Test | Testen |
| narrow a list | Filter | Filtern |
| keep the current filters under a name | Save as… | Speichern unter… |
| remove every active filter | Clear filters | Filter zurücksetzen |
| send an email to yourself | Send test email | Test-E-Mail senden |
| download entries | Export… | Exportieren… |
| open a problem from the form check | Fix it | Beheben |
| fill the preview with valid answers | Fill with sample answers | Mit Beispielantworten füllen |

- **Name the object when the button stands alone:** "Create option list", "Add action", "Export 124 entries". Inside a
  dialog whose title names the object, the verb alone is enough ("Duplicate").
- **The confirm button repeats the action and its object** ("Delete 23 entries"), never "Yes" or "OK".
- **Icon-only buttons** have an accessible name that matches their tooltip ("More actions for Contact us").

## Message patterns

Every message answers, in this order: the one fact the user needs now, the action available next, and only then the
context that changes the decision.

### Errors

An error says **what failed**, **why** when that is known and useful, and **how to go on**. It never shows a status
code as the message, never blames the user, and never claims a cause it cannot know. If work was kept, it says so.

| Case | English | German |
| --- | --- | --- |
| saving failed (network) | Couldn't save the draft. Check your connection, then try again. Your changes are still here. [Try again] | Der Entwurf konnte nicht gespeichert werden. Prüfen Sie die Verbindung und versuchen Sie es erneut. Ihre Änderungen sind noch da. [Erneut versuchen] |
| someone saved first | Ana Novak saved this form at 14:02, after you opened it. Review both versions before you save. [Review both versions] | Ana Novak hat dieses Formular um 14:02 gespeichert, nachdem Sie es geöffnet haben. Prüfen Sie beide Versionen, bevor Sie speichern. [Beide Versionen prüfen] |
| publishing blocked | This form can't be published yet: 2 problems need fixing. [Show problems] | Dieses Formular kann noch nicht veröffentlicht werden: 2 Probleme müssen behoben werden. [Probleme anzeigen] |
| missing permission | Changing entries needs the "Edit entries" permission. An administrator can grant it in Settings › Access. | Zum Ändern von Einträgen brauchen Sie die Berechtigung „Einträge bearbeiten“. Wer Administratorrechte hat, kann sie unter Einstellungen › Zugriff vergeben. |
| gone or no access | This form doesn't exist any more, or you don't have access to it. [Back to forms] | Dieses Formular gibt es nicht mehr, oder Sie haben keinen Zugriff darauf. [Zurück zu den Formularen] |
| a setting's value | Enter an alias that starts with a letter and uses only letters, digits and hyphens. | Geben Sie einen Alias ein, der mit einem Buchstaben beginnt und nur Buchstaben, Ziffern und Bindestriche enthält. |
| email not configured | Emails can't be sent yet: no mail server is configured. Entries are kept, and their emails go out once sending works. | E-Mails können noch nicht versendet werden: Es ist kein Mailserver eingerichtet. Die Einträge bleiben erhalten, und ihre E-Mails gehen raus, sobald der Versand funktioniert. |
| a delivery failed | The webhook to crm.example.com failed: the server answered "503 Service Unavailable". It is tried again at 14:30. [Retry now] | Der Webhook an crm.example.com ist fehlgeschlagen: Der Server antwortete „503 Service Unavailable“. Er wird um 14:30 erneut versucht. [Jetzt wiederholen] |
| locked by configuration | Locked by configuration: `JironFormBuilder:Privacy:AllowFullIp`. | Durch die Konfiguration gesperrt: `JironFormBuilder:Privacy:AllowFullIp`. |
| folder not empty | This folder still holds 3 forms. Move or delete them first. | Dieser Ordner enthält noch 3 Formulare. Verschieben oder löschen Sie sie zuerst. |

- **Field errors** sit under their field and say how to fix it ("Enter a number between 1 and 20"), never only what is
  wrong ("Invalid").
- **A remote answer** (a webhook's status line) is quoted as it came, marked as the other server's words, and never
  interpreted as HTML.

### Empty states

An empty state tells apart **first use**, **no results**, **no permission** and **failure**, and offers the next
useful action. One sentence, plus a button or link where there is one.

| Case | English | German |
| --- | --- | --- |
| no entries yet | No entries yet. Share the form, or preview it and send a test entry. | Noch keine Einträge. Teilen Sie das Formular, oder öffnen Sie die Vorschau und senden Sie einen Testeintrag. |
| filters match nothing | No entries match these filters. [Clear filters] | Keine Einträge passen zu diesen Filtern. [Filter zurücksetzen] |
| a search finds nothing, and sensitive fields exist | No entries match "Jana". Sensitive fields aren't searched. | Keine Einträge passen zu „Jana“. Vertrauliche Felder werden nicht durchsucht. |
| an empty folder (its footer holds Create form) | No forms here yet. Start from a template; it takes a minute. | Hier gibt es noch keine Formulare. Beginnen Sie mit einer Vorlage, das dauert eine Minute. |
| no entries in any form (the Inbox) | No entries yet. They arrive here from every form you may view. | Noch keine Einträge. Hier kommen die Einträge aller Formulare an, die Sie sehen dürfen. |
| no access to any form | You can't see any forms yet. An administrator can give your user group access in Settings › Access. | Sie sehen noch keine Formulare. Wer Administratorrechte hat, kann Ihre Benutzergruppe unter Einstellungen › Zugriff freischalten. |
| nothing in Spam | No spam. Entries that look automated land here, and nothing in them is lost. | Kein Spam. Einträge, die automatisiert wirken, landen hier, und nichts davon geht verloren. |
| no actions yet (Add action sits in the toolbar) | No actions yet. Most forms email the team when an entry comes in, and Add action starts with that. | Noch keine Aktionen. Die meisten Formulare benachrichtigen das Team per E-Mail, wenn ein Eintrag eingeht, und „Aktion hinzufügen“ beginnt damit. |
| a new, empty form | Add your first field: pick a type on the left, or paste your questions. | Fügen Sie das erste Feld hinzu: Wählen Sie links einen Typ, oder fügen Sie Ihre Fragen ein. |
| no insights yet | No data yet. Numbers appear after the first visit to a page with this form. | Noch keine Daten. Zahlen erscheinen nach dem ersten Besuch einer Seite mit diesem Formular. |
| loading failed | Entries couldn't be loaded. [Try again] | Die Einträge konnten nicht geladen werden. [Erneut versuchen] |

An empty value inside a table or panel reads "none" ("keine") or "no data" ("keine Daten"), never a dash (§4.13).

### Confirmations

- **Reversible actions don't ask.** They act at once and offer **Undo** in a toast ("Field deleted. [Undo]" · „Feld
  gelöscht. [Zurücknehmen]“).
- **Destructive actions ask, and name what will be lost,** with numbers. The confirm button repeats the action; Cancel
  is the default focus.

| Case | English | German |
| --- | --- | --- |
| delete entries | **Delete 23 entries?** This also deletes their 4 uploaded files and their timelines. You can't undo this. [Cancel] [Delete 23 entries] | **23 Einträge löschen?** Dabei werden auch ihre 4 hochgeladenen Dateien und ihre Chroniken gelöscht. Das lässt sich nicht rückgängig machen. [Abbrechen] [23 Einträge löschen] |
| trash a form in use | **Move Contact us to the Recycle Bin?** It's picked on 3 pages: Home, Contact, Events. They show nothing in its place until you restore it. [Cancel] [Move to Recycle Bin] | **Kontakt in den Papierkorb verschieben?** Das Formular ist auf 3 Seiten eingebunden: Startseite, Kontakt, Veranstaltungen. Dort erscheint nichts, bis Sie es wiederherstellen. [Abbrechen] [In den Papierkorb] |
| delete a form for good | **Delete Contact us for good?** This deletes the form with its 1,204 entries and 37 uploaded files. You can't undo this. [Cancel] [Delete form and entries] | **Kontakt endgültig löschen?** Dabei werden das Formular, seine 1.204 Einträge und 37 hochgeladenen Dateien gelöscht. Das lässt sich nicht rückgängig machen. [Abbrechen] [Formular und Einträge löschen] |
| empty the Recycle Bin | **Empty the Recycle Bin?** 3 forms and their 412 entries are deleted for good. [Cancel] [Empty recycle bin] | **Papierkorb leeren?** 3 Formulare und ihre 412 Einträge werden endgültig gelöscht. [Abbrechen] [Papierkorb leeren] |
| erase a person | **Erase everything about lena@example.com?** 6 entries in 3 forms, 2 uploaded files and 4 sent emails are erased. Statistics stay. You can't undo this. [Cancel] [Erase 6 entries] | **Alle Daten zu lena@example.com löschen?** 6 Einträge in 3 Formularen, 2 hochgeladene Dateien und 4 gesendete E-Mails werden gelöscht. Die Statistik bleibt. Das lässt sich nicht rückgängig machen. [Abbrechen] [6 Einträge löschen] |
| retry an uncertain delivery | **Retry this delivery?** It may already have arrived, so retrying could deliver it twice. [Cancel] [Retry anyway] | **Diese Zustellung wiederholen?** Sie ist vielleicht schon angekommen, eine Wiederholung könnte sie doppelt zustellen. [Abbrechen] [Trotzdem wiederholen] |
| unpublish | **Unpublish Contact us?** Visitors see its closed message until you publish it again. Its entries and settings stay. [Cancel] [Unpublish] | **Veröffentlichung von Kontakt zurücknehmen?** Auf der Website erscheint die Meldung für geschlossene Formulare, bis Sie es erneut veröffentlichen. Einträge und Einstellungen bleiben erhalten. [Abbrechen] [Veröffentlichung zurücknehmen] |
| restrict a folder without grants | **Restrict HR?** No user group has access of its own here yet, so only administrators will see its forms. [Cancel] [Restrict folder] | **HR einschränken?** Noch hat keine Benutzergruppe hier eigenen Zugriff, deshalb sehen nur Personen mit Administratorrechten die Formulare darin. [Abbrechen] [Ordner einschränken] |

### Success

Routine success is brief, and is said by the change itself where possible (the saved state, the row that moved). A
toast adds only what the user cannot see.

| Case | English | German |
| --- | --- | --- |
| draft saved | Draft saved | Entwurf gespeichert |
| published | Published. Visitors get the new version now. | Veröffentlicht. Auf der Website gilt jetzt die neue Version. |
| entries approved | 12 entries approved | 12 Einträge genehmigt |
| some selected entries left out | 8 entries approved. 2 weren't waiting for approval. | 8 Einträge genehmigt. 2 warteten nicht auf Genehmigung. |
| an action or outcome added to a published form | Added. Visitors get it once you publish. [Publish…] | Hinzugefügt. Auf der Website gilt es ab der nächsten Veröffentlichung. [Veröffentlichen…] |
| test email sent | Test email sent to you (anna@example.com). | Test-E-Mail an Sie gesendet (anna@example.com). |
| export ready | Your export of 12,400 entries is ready. [Download] | Ihr Export von 12.400 Einträgen ist fertig. [Herunterladen] |

### Loading and long work

- **Short waits** show a skeleton in the shape of what is coming, never a spinner in the middle of content.
- **Long work** names the operation, gives a number when one is known, and says whether the user can carry on:
  "Preparing the export of 12,400 entries. You can keep working; a notification links to the file." · „Der Export von
  12.400 Einträgen wird vorbereitet. Sie können weiterarbeiten; eine Benachrichtigung verlinkt die Datei.“
- **Progress** is determinate when it is known, and never invented.

### Help text

Help text sits under its setting and answers the question the label leaves open; it never restates the label.

| Setting | English help | German help |
| --- | --- | --- |
| Keep approved entries for | Approved entries are deleted this many days after approval. Entries on legal hold stay. | Genehmigte Einträge werden so viele Tage nach der Genehmigung gelöscht. Einträge mit Löschsperre bleiben. |
| Email verification | Visitors confirm their address through a link before any action runs. Use it for newsletters. | Wer das Formular sendet, bestätigt die eigene Adresse über einen Link, bevor eine Aktion ausgeführt wird. Sinnvoll für Newsletter. |
| Sensitive | Answers are masked in the backoffice and left out of emails and exports. | Antworten werden im Backoffice verdeckt und in E-Mails und Exporten weggelassen. |

A "Learn more" link goes to the docs page when a setting needs more than two sentences. Its text makes sense on its
own ("Learn more about retention" · „Mehr zur Aufbewahrung“).

## Numbers, dates, names and translation

- **Numbers and dates** are formatted through the backoffice's localization, never the browser's: "12,400" in English,
  „12.400“ in German. Times use the site's time zone and say so where it matters.
- **Plurals and word order** live inside each language's message function, never in code that joins strings ("1
  entry", "2 entries"; „1 Eintrag“, „2 Einträge“).
- **Whole messages, never fragments:** each message is one translatable string with named values, so each language can
  reorder them.
- **Allow for German:** German labels run about a third longer than English ones. Nothing is abbreviated to fit; the
  layout wraps instead (§4.13, "nothing squashed").
- **Names typed by users** (form names, labels, entry titles) are always passed as values into a Lit template, never
  placed into an HTML string, so a name like `<img src=x onerror=…>` shows as text (9.1).
- **Quotation marks:** English uses "…", German „…“.

## The visitor's side

The default visitor texts ship in seven languages (D20), and editors can change each one. The same rules apply, with
these additions:

- **Address visitors directly and politely.** The German defaults use "Sie".
- **Required and optional:** mark one kind consistently, as the form's setting chooses.
- **An error names the field and how to fix it,** and the error summary links to the field: "Enter your email address,
  for example name@example.com" · „Geben Sie Ihre E-Mail-Adresse ein, zum Beispiel name@example.com“.
- **Never blame or alarm:** "This file is larger than 10 MB. Choose a smaller one." · „Diese Datei ist größer als 10
  MB. Wählen Sie eine kleinere.“
- **A refused send keeps every answer** and says when to try again: "Too many attempts from your network. Your answers
  are kept; send again in 2 minutes." · „Zu viele Versuche aus Ihrem Netzwerk. Ihre Angaben bleiben erhalten; senden
  Sie in 2 Minuten erneut.“
- **The default success message** is brief: "Thank you. We have received your message." · „Vielen Dank. Wir haben Ihre
  Nachricht erhalten.“

## Checklist for every new string

- [ ] It uses the glossary's word, in sentence case.
- [ ] A button starts with a verb and names its object when it stands alone.
- [ ] An error says what failed and how to go on, and whether work was kept.
- [ ] An empty state says which kind of empty it is and offers the next action.
- [ ] A destructive confirmation names what is lost, with numbers, and repeats the action on its button.
- [ ] Nothing on the screen already says the same thing.
- [ ] It has an English and a German key, and both read naturally aloud.
- [ ] User-typed names go in as values, never as HTML.
