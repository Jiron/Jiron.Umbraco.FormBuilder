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
| divider | a line between parts of a form; the theme editor's name for the token `rule` | Trennlinie | rule (for a line) |
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
| an entry on legal hold, in Privacy › People | Legal hold until 30 Apr 2027 | Löschsperre bis 30. Apr. 2027 |

### Status lines

One line that says where something stands, next to the thing it is about.

| Where | English | German |
| --- | --- | --- |
| an action's card, after a failed delivery | Failed at 14:02. Next try at 14:30. | Fehlgeschlagen um 14:02. Nächster Versuch um 14:30. |
| beside the canvas width switch, when the canvas has less room than the chosen width | Narrowed to fit | An den Platz angepasst |
| above the permission matrix, for a user group without the section | Translators can't open Forms yet. Add the section to their user group | Übersetzer können Formulare noch nicht öffnen. Bereich zu ihrer Benutzergruppe hinzufügen |
| above the permission matrix, once, for the fixed Administrators column | Administrators hold every permission everywhere, because they could grant themselves any. | Die Gruppe Administratoren hat überall jede Berechtigung, weil ihre Mitglieder sich jede selbst geben könnten. |
| a form's header, when no page picks it | Not used on any page | Auf keiner Seite verwendet |
| the inspector, while the designer shows another language | Editing the German texts. Where one is empty, visitors see the English text. | Sie bearbeiten die deutschen Texte. Wo einer fehlt, sehen Besucher den englischen. |
| a chip on a field without a text in that language | No German text yet | Noch kein deutscher Text |
| under a secret entered as a reference to host configuration | Found in host configuration | In der Hostkonfiguration gefunden |
| under a revealed sensitive value | Shown to you and recorded in the audit log. | Für Sie sichtbar und im Protokoll vermerkt. |

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
| make an earlier version the draft (in its sidebar, or in a row's "…"; it acts at once and offers Undo) | Restore as a new draft | Als neuen Entwurf wiederherstellen |
| create the block that puts forms on pages | Create a form block type… | Formular-Blocktyp erstellen… |
| start from a ready-made form (the checklist, an empty folder) | Choose a template… | Vorlage wählen… |
| turn a list of questions into fields | Paste questions… | Fragen einfügen… |
| add a merge tag to a text | Insert merge tag | Platzhalter einfügen |
| delete for good (asks first) | Delete… | Löschen… |
| delete a designer element at once, with Undo in the toast | Delete rule · Delete outcome | Regel löschen · Abschluss löschen |
| empty the Recycle Bin | Empty recycle bin… | Papierkorb leeren… |
| reverse the last change | Undo | Zurücknehmen |
| repeat a reversed change | Redo | Erneut anwenden |
| send again after a failure | Retry | Wiederholen |
| approve an entry | Approve | Genehmigen |
| reject an entry | Reject | Ablehnen |
| move an entry to Spam | Mark as spam | Als Spam markieren |
| move an entry out of Spam | Not spam | Kein Spam |
| send an entry to a colleague | Forward… | Weiterleiten… |
| show one masked value, and mask it again | Reveal · Hide | Anzeigen · Verbergen |
| try an action against a sample entry (on its card and in its editor) | Test | Testen |
| run a test entry's actions, from the preview (asks first) | Run actions… | Aktionen ausführen… |
| narrow a list | Filter | Filtern |
| keep the current filters under a name | Save as… | Speichern unter… |
| remove every active filter | Clear filters | Filter zurücksetzen |
| remove the search | Clear search | Suche zurücksetzen |
| choose the summary email | Email me a summary… | Zusammenfassung per E-Mail… |
| send an email to yourself | Send test email | Test-E-Mail senden |
| fetch a JSON address's options again | Fetch now | Jetzt abrufen |
| download entries | Export… | Exportieren… |
| keep a person's entries past their retention | Set legal hold… | Löschsperre setzen… |
| erase one person's data (asks first) | Erase… | Daten löschen… |
| open a problem from the form check | Fix it | Beheben |
| fill the preview with valid answers | Fill with sample answers | Mit Beispielantworten füllen |
| reach the canvas past the header and the palette (the workspace's first stop) | Skip to the form | Zum Formular springen |
| list the keyboard shortcuts | Keyboard shortcuts | Tastenkürzel |

- **Name the object when the button stands alone:** "Create option list", "Add action", "Export 124 entries". Inside a
  dialog whose title names the object, the verb alone is enough ("Duplicate").
- **The confirm button repeats the action and its object** ("Delete 23 entries"), never "Yes" or "OK".
- **A switch names what it turns on, never its state,** and leaves the state to the switch itself: "Use this rule" ·
  „Diese Regel verwenden“; an action's switch is "Use Email the team" · „Email the team verwenden“, not "Email the team
  is on".
- **Icon-only buttons** have an accessible name that matches their tooltip ("More actions for Contact us").
- **The designer's canvas** names its parts for screen readers (ADR-0016): "Add a field here, before Email address" ·
  „Hier ein Feld hinzufügen, vor Email address“; "Email address, email field" · „Email address, Feld vom Typ E-Mail“;
  "Move Email address" · „Email address verschieben“. After a keyboard move it says where the field is now: "Moved.
  Email address: position 3 of 4 in About you, Page 1: Your details." · „Verschoben. Email address: Position 3 von 4
  in About you, Seite 1: Your details.“

### Keyboard shortcuts

The "?" dialogs list them in these words. On a Mac the designer's Ctrl is Cmd, and its dialog says so once: "On a
Mac, use Cmd instead of Ctrl." · „Auf dem Mac Cmd statt Strg verwenden.“

| Where | Keys | English | German |
| --- | --- | --- | --- |
| designer | Ctrl+Z · Ctrl+Y | Undo the last change · Redo it | Letzte Änderung zurücknehmen · Erneut anwenden |
| designer | Ctrl+D · Delete | Duplicate the selected field · Delete the selected field | Ausgewähltes Feld duplizieren · Ausgewähltes Feld löschen |
| designer | Alt+Up or Alt+Down | Move the selected field one place up or down | Ausgewähltes Feld um eine Stelle nach oben oder unten verschieben |
| designer | F2 | Edit the selected field's label in place | Beschriftung des ausgewählten Felds direkt bearbeiten |
| designer | Esc | Clear the selection, close the palette, or cancel a drag | Auswahl aufheben, Palette schließen oder Ziehen abbrechen |
| entries | j · k | Next entry · Previous entry | Nächster Eintrag · Vorheriger Eintrag |
| entries | Enter · Esc | Open the entry · Close the entry | Eintrag öffnen · Eintrag schließen |
| entries | a · r · s | Approve · Reject · Mark as spam | Genehmigen · Ablehnen · Als Spam markieren |
| entries | / | Search entries | Einträge durchsuchen |
| both | ? | Show these shortcuts | Diese Tastenkürzel anzeigen |

## Message patterns

Every message answers, in this order: the one fact the user needs now, the action available next, and only then the
context that changes the decision.

### Errors

An error says **what failed**, **why** when that is known and useful, and **how to go on**. It never shows a status
code as the message, never blames the user, and never claims a cause it cannot know. If work was kept, it says so.

| Case | English | German |
| --- | --- | --- |
| saving failed (network) | Couldn't save the draft. Check your connection, then try again. Your changes are still here. [Try again] | Der Entwurf konnte nicht gespeichert werden. Prüfen Sie die Verbindung und versuchen Sie es erneut. Ihre Änderungen sind noch da. [Erneut versuchen] |
| someone saved first | **Your draft wasn't saved** Ana Novak saved this form at 14:02, after you opened it. Review both versions before you save. Your changes are still here. [Review both versions] | **Ihr Entwurf wurde nicht gespeichert** Ana Novak hat dieses Formular um 14:02 gespeichert, nachdem Sie es geöffnet haben. Prüfen Sie beide Versionen, bevor Sie speichern. Ihre Änderungen sind noch da. [Beide Versionen prüfen] |
| publishing blocked (the publish dialog; each error has its own Fix it) | This form can't be published yet: 1 error needs fixing. | Dieses Formular lässt sich noch nicht veröffentlichen: 1 Fehler muss behoben werden. |
| warnings beside an error, in the publish dialog | It also has 1 warning, which doesn't block publishing. | Außerdem gibt es 1 Warnung, die das Veröffentlichen nicht verhindert. |
| the form check, for a form without fields | **Add at least one field** The form has no fields yet, so visitors would have nothing to fill in. [Fix it] | **Mindestens ein Feld hinzufügen** Das Formular hat noch keine Felder, Besucher hätten also nichts auszufüllen. [Beheben] |
| missing permission | Changing entries needs the "Edit entries" permission. An administrator can grant it in Settings › Access. | Zum Ändern von Einträgen brauchen Sie die Berechtigung „Einträge bearbeiten“. Wer Administratorrechte hat, kann sie unter Einstellungen › Zugriff vergeben. |
| gone or no access | This form doesn't exist any more, or you don't have access to it. [Back to forms] | Dieses Formular gibt es nicht mehr, oder Sie haben keinen Zugriff darauf. [Zurück zu den Formularen] |
| a setting's value | Enter an alias that starts with a letter and uses only letters, digits and hyphens. | Geben Sie einen Alias ein, der mit einem Buchstaben beginnt und nur Buchstaben, Ziffern und Bindestriche enthält. |
| email not configured (Send test email is hidden meanwhile) | Emails can't be sent yet: no mail server is configured. Entries are kept, and their emails go out once sending works. A developer sets up sending in `Umbraco:CMS:Global:Smtp`. | E-Mails können noch nicht versendet werden: Es ist kein Mailserver eingerichtet. Die Einträge bleiben erhalten, und ihre E-Mails gehen raus, sobald der Versand funktioniert. Den Versand richtet jemand aus der Entwicklung in `Umbraco:CMS:Global:Smtp` ein. |
| a delivery failed | The webhook to crm.example.com failed: the server answered "503 Service Unavailable". It is tried again at 14:30. [Retry now] | Der Webhook an crm.example.com ist fehlgeschlagen: Der Server antwortete „503 Service Unavailable“. Er wird um 14:30 erneut versucht. [Jetzt wiederholen] |
| a failed run's details, for a user who can't retry it (the Actions view) | Test sends a sample entry, so you can see whether the server answers again. An administrator can retry this run now, in Activity › Deliveries. [Test] | Testen sendet einen Beispieleintrag, so sehen Sie, ob der Server wieder antwortet. Wer Administratorrechte hat, kann diesen Lauf sofort unter Aktivität › Zustellungen wiederholen. [Testen] |
| a mail held back by a sending limit (no Retry) | Not sent: this site's forms had already sent 3 emails to anna@example.org today, the most one address gets in a day. The limit keeps the forms from being used to send mail to strangers. A limited email is never sent later, so it can't be retried. Host configuration sets the limit: `JironFormBuilder:Limits`. | Nicht gesendet: Die Formulare dieser Website haben heute schon 3 E-Mails an anna@example.org gesendet, die Höchstzahl für eine Adresse an einem Tag. Die Grenze verhindert, dass die Formulare genutzt werden, um Fremden E-Mails zu schicken. Eine begrenzte E-Mail wird nie später gesendet und lässt sich deshalb nicht wiederholen. Die Hostkonfiguration legt die Grenze fest: `JironFormBuilder:Limits`. |
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
| no entries yet, for a user who may design the form | No entries yet. Share the form, or preview it and send a test entry. | Noch keine Einträge. Teilen Sie das Formular, oder öffnen Sie die Vorschau und senden Sie einen Testeintrag. |
| no entries yet, for everyone else (they can neither share nor preview it) | No entries yet. They appear here as soon as visitors send the form. [Email me a summary…] | Noch keine Einträge. Sie erscheinen hier, sobald jemand das Formular sendet. [Zusammenfassung per E-Mail…] |
| filters match nothing (Clear filters sits in the chip row above, so the empty state has no button of its own) | No entries match these filters. Remove one above, or clear them all. | Keine Einträge passen zu diesen Filtern. Entfernen Sie oben einen davon, oder setzen Sie alle zurück. |
| a search finds nothing, and sensitive fields exist | No entries match "Jana". Sensitive fields aren't searched. [Clear search] | Keine Einträge passen zu „Jana“. Vertrauliche Felder werden nicht durchsucht. [Suche zurücksetzen] |
| an empty folder (its footer holds Create form) | No forms here yet. A template gives you a working form in a minute. [Choose a template…] | Hier gibt es noch keine Formulare. Mit einer Vorlage haben Sie in einer Minute ein fertiges Formular. [Vorlage wählen…] |
| an empty Recycle Bin (no footer) | The Recycle Bin is empty. Trashed forms wait here with their entries until someone restores them or deletes them for good. | Der Papierkorb ist leer. Formulare im Papierkorb warten hier mit ihren Einträgen, bis jemand sie wiederherstellt oder endgültig löscht. |
| no entries in any form (the Inbox, without its toolbar) | No entries yet. They arrive here from every form you may view. [Email me a summary…] | Noch keine Einträge. Hier kommen die Einträge aller Formulare an, die Sie sehen dürfen. [Zusammenfassung per E-Mail…] |
| a member without entries (the member's Form entries view) | No entries yet. Entries this member sends while signed in appear here. | Noch keine Einträge. Einträge, die dieses Mitglied angemeldet sendet, erscheinen hier. |
| no access to any form | You can't see any forms yet. An administrator can give your user group access in Settings › Access. | Sie sehen noch keine Formulare. Wer Administratorrechte hat, kann Ihre Benutzergruppe unter Einstellungen › Zugriff freischalten. |
| nothing in Spam | No spam. Entries that look automated land here, and nothing in them is lost. | Kein Spam. Einträge, die automatisiert wirken, landen hier, und nichts davon geht verloren. |
| no actions yet (Add action sits in the toolbar) | No actions yet. Most forms email the team when an entry comes in, and Add action starts with that. | Noch keine Aktionen. Die meisten Formulare benachrichtigen das Team per E-Mail, wenn ein Eintrag eingeht, und „Aktion hinzufügen“ beginnt damit. |
| a new, empty form | Add your first field: pick a type on the left, or paste your questions. | Fügen Sie das erste Feld hinzu: Wählen Sie links einen Typ, oder fügen Sie Ihre Fragen ein. |
| no rules yet (the rules overview, with Add rule in its header) | No rules yet. Most rules show a question only when another answer calls for it, and Add rule starts by asking which question. | Noch keine Regeln. Die meisten Regeln zeigen eine Frage nur, wenn eine andere Antwort sie verlangt, und „Regel hinzufügen“ fragt zuerst, um welche Frage es geht. |
| no insights yet | No data yet. Numbers appear after the first visit to a page with this form. | Noch keine Daten. Zahlen erscheinen nach dem ersten Besuch einer Seite mit diesem Formular. |
| loading failed | Entries couldn't be loaded. [Try again] | Die Einträge konnten nicht geladen werden. [Erneut versuchen] |

An empty value inside a table or panel reads "none" ("keine") or "no data" ("keine Daten"), never a dash (§4.13).

### Confirmations

- **Reversible actions don't ask.** They act at once and offer **Undo** in a toast ("Field deleted. [Undo]" · „Feld
  gelöscht. [Zurücknehmen]“).
- **An entry's change of state is reversible too** (approve, reject, mark as spam, not spam; one entry or many): the
  toast offers Undo for 10 seconds, and the actions the change starts wait until then (ADR-0023). When the window
  ends, Undo leaves the toast, which then says: "It's too late to undo this. Change the state back by hand if
  needed." · „Das lässt sich nicht mehr zurücknehmen. Ändern Sie den Status bei Bedarf von Hand zurück.“
- **Restoring a version is reversible as well:** from a History row's "…" it skips the comparison that the version's
  sidebar shows, so its toast names what changed, and Undo puts the draft before it back (that draft also stays in
  History).
- **Destructive actions ask, and name what will be lost,** with numbers. The confirm button repeats the action; Cancel
  is the default focus.

| Case | English | German |
| --- | --- | --- |
| delete entries | **Delete 23 entries?** This also deletes their 4 uploaded files and their timelines. You can't undo this. [Cancel] [Delete 23 entries] | **23 Einträge löschen?** Dabei werden auch ihre 4 hochgeladenen Dateien und ihre Chroniken gelöscht. Das lässt sich nicht rückgängig machen. [Abbrechen] [23 Einträge löschen] |
| trash a form in use | **Move Contact us to the Recycle Bin?** It's picked on 3 pages: Home, Contact, Events. They show nothing in its place until you restore it. [Cancel] [Move to Recycle Bin] | **Kontakt in den Papierkorb verschieben?** Das Formular ist auf 3 Seiten eingebunden: Startseite, Kontakt, Veranstaltungen. Dort erscheint nichts, bis Sie es wiederherstellen. [Abbrechen] [In den Papierkorb] |
| delete a form for good | **Delete Contact us for good?** This deletes the form with its 1,204 entries and 37 uploaded files. You can't undo this. [Cancel] [Delete form and entries] | **Kontakt endgültig löschen?** Dabei werden das Formular, seine 1.204 Einträge und 37 hochgeladenen Dateien gelöscht. Das lässt sich nicht rückgängig machen. [Abbrechen] [Formular und Einträge löschen] |
| empty the Recycle Bin | **Empty the Recycle Bin?** This deletes 3 forms for good, with their 412 entries and 12 uploaded files. You can't undo this. [Cancel] [Empty recycle bin] | **Papierkorb leeren?** Dabei werden 3 Formulare mit ihren 412 Einträgen und 12 hochgeladenen Dateien endgültig gelöscht. Das lässt sich nicht rückgängig machen. [Abbrechen] [Papierkorb leeren] |
| erase a person | **Erase everything about lena@example.com?** 6 entries in 3 forms, 2 uploaded files and 4 sent emails are erased. Statistics stay. You can't undo this. [Cancel] [Erase 6 entries] | **Alle Daten zu lena@example.com löschen?** 6 Einträge in 3 Formularen, 2 hochgeladene Dateien und 4 gesendete E-Mails werden gelöscht. Die Statistik bleibt. Das lässt sich nicht rückgängig machen. [Abbrechen] [6 Einträge löschen] |
| retry an uncertain delivery | **Retry this delivery?** It may already have arrived, so retrying could deliver it twice. [Cancel] [Retry anyway] | **Diese Zustellung wiederholen?** Sie ist vielleicht schon angekommen, eine Wiederholung könnte sie doppelt zustellen. [Abbrechen] [Trotzdem wiederholen] |
| unpublish | **Unpublish Contact us?** It's picked on 3 pages: Home, Contact, Events. Visitors there see its closed message until you publish it again. Its draft, entries and settings stay. [Cancel] [Unpublish] | **Veröffentlichung von Kontakt zurücknehmen?** Das Formular ist auf 3 Seiten eingebunden: Startseite, Kontakt, Veranstaltungen. Dort erscheint die Meldung für geschlossene Formulare, bis Sie es erneut veröffentlichen. Entwurf, Einträge und Einstellungen bleiben erhalten. [Abbrechen] [Veröffentlichung zurücknehmen] |
| run a test entry's actions, from the preview | **Run the actions for test entry #1044?** These 4 actions run as they would for a real entry, so their emails and messages really go out. [Cancel] [Run 4 actions] | **Aktionen für Testeintrag #1044 ausführen?** Diese 4 Aktionen laufen wie bei einem echten Eintrag; ihre E-Mails und Nachrichten gehen also wirklich hinaus. [Abbrechen] [4 Aktionen ausführen] |
| restrict a folder without grants | **Restrict HR?** No user group has access of its own here yet, so only administrators will see its forms. [Cancel] [Restrict folder] | **HR einschränken?** Noch hat keine Benutzergruppe hier eigenen Zugriff, deshalb sehen nur Personen mit Administratorrechten die Formulare darin. [Abbrechen] [Ordner einschränken] |

### Success

Routine success is brief, and is said by the change itself where possible (the saved state, the row that moved). A
toast adds only what the user cannot see.

| Case | English | German |
| --- | --- | --- |
| draft saved | Draft saved | Entwurf gespeichert |
| published (the chip already says Published) | Visitors get the new version now. | Auf der Website gilt jetzt die neue Version. |
| published for the first time, while no page shows it ("Show how" opens the "Used on" sidebar) | Visitors can fill it in once it's on a page. [Show how] | Besucher können es ausfüllen, sobald es auf einer Seite eingebunden ist. [Anleitung zeigen] |
| a version restored from a History row's "…" | Version 13 is the new draft: 4 fields, After sending, 1 rule and 1 action changed. [Undo] | Version 13 ist der neue Entwurf: 4 Felder, „Nach dem Senden“, 1 Regel und 1 Aktion geändert. [Zurücknehmen] |
| one entry approved, in the entry panel | Entry #214 approved. [Undo] | Eintrag #214 genehmigt. [Zurücknehmen] |
| entries approved | 12 entries approved. [Undo] | 12 Einträge genehmigt. [Zurücknehmen] |
| some selected entries left out | 3 entries approved. 3 weren't waiting for approval. [Undo] | 3 Einträge genehmigt. 3 warteten nicht auf Genehmigung. [Zurücknehmen] |
| an action added to a published form (Publish… stays in the footer) | Post in #leads added. It runs for new entries once you publish. | Post in #leads hinzugefügt. Die Aktion läuft für neue Einträge, sobald Sie veröffentlichen. |
| an outcome added to a published form | Added. Visitors get it once you publish. | Hinzugefügt. Auf der Website gilt es ab der nächsten Veröffentlichung. |
| test email sent | Test email sent to you (ana@example.com). | Test-E-Mail an Sie gesendet (ana@example.com). |
| export ready | Your export of 12,400 entries is ready. [Download] | Ihr Export von 12.400 Einträgen ist fertig. [Herunterladen] |
| one person's export ready (Privacy › People) | Your export for lena@example.com is ready. [Download] | Ihr Export für lena@example.com ist fertig. [Herunterladen] |
| a person's data erased (the link only for users with `manageSettings`; others read "It's recorded in the audit log." · „Das ist im Protokoll vermerkt.“) | Everything about lena@example.com is erased: 6 entries in 3 forms, 2 uploaded files and 4 sent emails. Statistics stay. [Open the audit record] | Alle Daten zu lena@example.com sind gelöscht: 6 Einträge in 3 Formularen, 2 hochgeladene Dateien und 4 gesendete E-Mails. Die Statistik bleibt. [Protokolleintrag öffnen] |

### Loading and long work

- **Short waits** show a skeleton in the shape of what is coming, never a spinner in the middle of content.
- **Long work** names the operation, gives a number when one is known, and says whether the user can carry on:
  "Preparing the export of 12,400 entries. You can keep working; a notification links to the file." · „Der Export von
  12.400 Einträgen wird vorbereitet. Sie können weiterarbeiten; eine Benachrichtigung verlinkt die Datei.“
- **Progress** is determinate when it is known, and never invented: "Preparing the export: 2 of 6 entries. You can
  keep working; a notification links to the file." · „Der Export wird vorbereitet: 2 von 6 Einträgen. Sie können
  weiterarbeiten; eine Benachrichtigung verlinkt die Datei.“

### Help text

Help text sits under its setting and answers the question the label leaves open; it never restates the label.

| Setting | English help | German help |
| --- | --- | --- |
| Keep approved entries for | Approved entries are deleted this many days after approval. Entries on legal hold stay. | Genehmigte Einträge werden so viele Tage nach der Genehmigung gelöscht. Einträge mit Löschsperre bleiben. |
| Email verification | Visitors confirm their address through a link before any action runs. Use it for newsletters. | Wer das Formular sendet, bestätigt die eigene Adresse über einen Link, bevor eine Aktion ausgeführt wird. Sinnvoll für Newsletter. |
| Sensitive | Answers are masked in the backoffice and left out of emails and exports. | Antworten werden im Backoffice verdeckt und in E-Mails und Exporten weggelassen. |
| Fields that get a mark · Felder mit Markierung | Most forms have more required fields than optional ones, so marking the few optional ones reads calmer. | Die meisten Formulare haben mehr Pflichtfelder als optionale, deshalb wirkt es ruhiger, die wenigen optionalen zu markieren. |
| Use this rule · Diese Regel verwenden | Turn it off to keep the rule without using it. The field then always shows. | Schalten Sie sie aus, um die Regel zu behalten, ohne sie zu verwenden. Das Feld wird dann immer angezeigt. |
| Keep fetched options for · Abgerufene Optionen behalten für | Forms use the kept copy and fetch a new one after this time. If the address doesn't answer, the last copy stays in use. | Formulare verwenden die behaltene Kopie und rufen nach dieser Zeit eine neue ab. Antwortet die Adresse nicht, bleibt die letzte Kopie in Gebrauch. |
| How often (Email me a summary) · Wie oft | It goes out at 07:00 Berlin time, the site's time zone, and covers the day or week before. | Sie geht um 07:00 Uhr Berliner Zeit, der Zeitzone der Website, raus und umfasst den Tag oder die Woche davor. |
| the summary email's sidebar, under its preview | It holds counts and links, never answers, and only the forms you may view. It goes to your own address, jonas@example.com. | Sie enthält Zahlen und Links, nie Antworten, und nur die Formulare, die Sie sehen dürfen. Sie geht an Ihre eigene Adresse, jonas@example.com. |
| New note · Neue Notiz | Visitors never see notes. Everyone who may open this entry can read them. | Wer das Formular sendet, sieht Notizen nie. Alle, die diesen Eintrag öffnen dürfen, können sie lesen. |
| Edit answers (one line above the fields) | Your changes are recorded, with the old values. | Ihre Änderungen werden mit den alten Werten protokolliert. |
| Reason (legal hold) · Grund | Shown with the hold, and recorded in the audit log with your name. | Wird mit der Löschsperre angezeigt und mit Ihrem Namen im Protokoll festgehalten. |
| Keep until (legal hold) · Behalten bis | At most a year from today. You can extend it before it ends. | Höchstens ein Jahr ab heute. Sie können sie vor dem Ende verlängern. |
| the legal hold sidebar, under its settings | Held entries are kept past their retention, and Erase… leaves them out until the hold ends. | Gesperrte Einträge bleiben über ihre Aufbewahrung hinaus erhalten, und „Daten löschen…“ lässt sie aus, bis die Sperre endet. |
| Your questions (Paste questions) · Ihre Fragen | One question per line. Each becomes a field, with a type guessed from its words; you can change it afterwards. | Eine Frage pro Zeile. Jede wird ein Feld, mit einem aus ihren Wörtern erratenen Typ; Sie können ihn danach ändern. |

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
  for example name@example.com" · „Geben Sie Ihre E-Mail-Adresse ein, zum Beispiel name@example.com“. The summary's
  heading counts what needs a change: "These answers need a change" · „Diese Antworten müssen noch geändert werden“,
  or for one, "This answer needs a change" · „Diese Antwort muss noch geändert werden“.
- **Never blame or alarm:** "This file is larger than 10 MB. Choose a smaller one." · „Diese Datei ist größer als 10
  MB. Wählen Sie eine kleinere.“
- **A refused send keeps every answer** and says when to try again: "Too many attempts from your network. Your answers
  are kept; send again in 2 minutes." · „Zu viele Versuche aus Ihrem Netzwerk. Ihre Antworten bleiben erhalten; senden
  Sie in 2 Minuten erneut.“
- **The default success message** is brief: "Thank you. We have received your message." · „Vielen Dank. Wir haben Ihre
  Nachricht erhalten.“
- **A notice's heading says what happened, and its text what the visitor can do.** Where a notice sits (instead of the
  form, above it, or just above the buttons) is in ADR-0022.

The mocks in `website/` use these words. Where a text names the example (a booking, a workshop, Mira, the library),
it is the example form's own wording, and the default follows the same pattern with the form's own names.

| Case | English | German |
| --- | --- | --- |
| while sending (the live region) | Sending your answers | Ihre Antworten werden gesendet |
| the connection dropped (above the buttons) | **Your answers weren't sent** Check your connection, then send again. Your answers are kept. | **Ihre Antworten wurden nicht gesendet** Prüfen Sie die Verbindung und senden Sie erneut. Ihre Antworten bleiben erhalten. |
| too many attempts (above the buttons) | **Your answers weren't sent** Too many attempts from your network. Your answers are kept; send again in 2 minutes. | **Ihre Antworten wurden nicht gesendet** Zu viele Versuche aus Ihrem Netzwerk. Ihre Antworten bleiben erhalten; senden Sie in 2 Minuten erneut. |
| the form was published again meanwhile (in the error summary's place) | **This form was updated while you filled it in** Your answers are kept. The agreement at the end has new wording, so it is no longer ticked: read it again before you send. | **Dieses Formular wurde geändert, während Sie es ausgefüllt haben** Ihre Antworten bleiben erhalten. Die Einwilligung am Ende hat einen neuen Wortlaut und ist deshalb nicht mehr angehakt: Lesen Sie sie vor dem Senden noch einmal. |
| the same, the defaults: no agreement changed, one, or several | **This form was updated while you filled it in** Your answers are kept. / ... An agreement has new wording, so it is no longer ticked: read it again before you send. / ... Some agreements have new wording, so they are no longer ticked: read them again before you send. | **Dieses Formular wurde geändert, während Sie es ausgefüllt haben** Ihre Antworten bleiben erhalten. / ... Eine Einwilligung hat einen neuen Wortlaut und ist deshalb nicht mehr angehakt: Lesen Sie sie vor dem Senden noch einmal. / ... Einige Einwilligungen haben einen neuen Wortlaut und sind deshalb nicht mehr angehakt: Lesen Sie sie vor dem Senden noch einmal. |
| a post without JavaScript the form could not take (in the error summary's place) | **Your answers weren't sent** Check your answers, then send again. Your answers are kept. | **Ihre Antworten wurden nicht gesendet** Prüfen Sie Ihre Antworten und senden Sie erneut. Ihre Antworten bleiben erhalten. |
| a post without JavaScript past the form's limit for files (in the error summary's place; nothing could be read) | **Your answers weren't sent** The files were too large to send, so your answers could not be kept. Choose smaller files and fill in the form again. | **Ihre Antworten wurden nicht gesendet** Die Dateien waren zu groß, um sie zu senden, deshalb konnten Ihre Antworten nicht erhalten bleiben. Wählen Sie kleinere Dateien und füllen Sie das Formular erneut aus. |
| a file already taken, without JavaScript (a ticked box above the file input; untick it to drop the file) | Keep cv.pdf (39.1 KB) | cv.pdf (39,1 KB) behalten |
| a redirect after the message, without JavaScript (no countdown) | the message, then [Continue] | die Nachricht, dann [Weiter] |
| a choice filled up meanwhile (an ordinary error) | Bike repair basics is now full. Choose another workshop | Bike repair basics ist jetzt ausgebucht. Wählen Sie einen anderen Workshop |
| the outcome, while the address is unconfirmed | **Check your inbox, Mira** We have sent you a link. Open it within 7 days to confirm your email address; then your place is booked. | **Sehen Sie in Ihrem Postfach nach, Mira** Wir haben Ihnen einen Link geschickt. Öffnen Sie ihn innerhalb von 7 Tagen, um Ihre E-Mail-Adresse zu bestätigen; dann ist Ihr Platz gebucht. |
| a redirect after the message (said once, not every second) | Taking you to the workshop programme in 8 seconds. [Go now] [Stay here] | In 8 Sekunden geht es weiter zum Workshop-Programm. [Jetzt weiter] [Hier bleiben] |
| the verification page | **Confirm your email address** Confirm it to finish your booking for "Book a Saturday workshop". [Confirm my address] | **Bestätigen Sie Ihre E-Mail-Adresse** Bestätigen Sie sie, um Ihre Buchung für „Book a Saturday workshop“ abzuschließen. [Meine Adresse bestätigen] |
| the verification page, confirmed | **Your address is confirmed** Thank you, Mira. Your place in Bike repair basics is booked, and the confirmation is on its way to you. [Back to Lindenhall Library] | **Ihre Adresse ist bestätigt** Vielen Dank, Mira. Ihr Platz in Bike repair basics ist gebucht, und die Bestätigung ist unterwegs zu Ihnen. [Zurück zu Lindenhall Library] |
| the verification page, expired | **This link has expired** Links to confirm an address work for 7 days. We can send a new one to the address you gave. [Send me a new link] | **Dieser Link ist abgelaufen** Links zum Bestätigen einer Adresse gelten 7 Tage. Wir können Ihnen einen neuen an die angegebene Adresse schicken. [Neuen Link senden] |
| save and continue later (the panel in the buttons' place) | **Save and continue later** We email you a link back to this page, valid for 7 days. Files and your signature are not kept, so you add them again when you come back. [Send the link] [Cancel] | **Speichern und später weitermachen** Wir schicken Ihnen per E-Mail einen Link zurück zu dieser Seite, gültig für 7 Tage. Dateien und Ihre Unterschrift werden nicht gespeichert; Sie fügen sie wieder hinzu, wenn Sie zurückkommen. [Link senden] [Abbrechen] |
| saved | **We have emailed you a link** It went to daniel@fernandes.example and works until 30 October. You can close this page now, or carry on. | **Wir haben Ihnen einen Link geschickt** Er ging an daniel@fernandes.example und gilt bis zum 30. Oktober. Sie können diese Seite jetzt schließen oder weitermachen. |
| back through the link (above the form) | **Welcome back** Your answers are back, on the page where you left off. Add your files and your signature again: they are not kept with saved answers. | **Willkommen zurück** Ihre Antworten sind wieder da, auf der Seite, auf der Sie aufgehört haben. Fügen Sie Ihre Dateien und Ihre Unterschrift erneut hinzu: Sie werden mit gespeicherten Antworten nicht aufbewahrt. |
| a saved-answers link too old (instead of the form) | **This link has expired** Saved answers are kept for 7 days, and these were saved on 9 October, so they are gone now. [Start again] | **Dieser Link ist abgelaufen** Gespeicherte Antworten werden 7 Tage aufbewahrt. Diese wurden am 9. Oktober gespeichert und sind deshalb nicht mehr vorhanden. [Neu beginnen] |
| closed: every place taken (instead of the form) | **All places are taken** Every place in the November workshops is booked. Ask at the library desk to join the waiting list. | **Alle Plätze sind vergeben** Jeder Platz in den November-Workshops ist gebucht. Fragen Sie an der Theke der Bibliothek nach der Warteliste. |
| closed: for signed-in members only | **Sign in to book** The workshops are for library members. Sign in with your library card, and the booking form appears here. [Sign in] | **Melden Sie sich an, um zu buchen** Die Workshops sind für Mitglieder der Bibliothek. Melden Sie sich mit Ihrem Bibliotheksausweis an, dann erscheint hier das Buchungsformular. [Anmelden] |
| closed: one entry per member, already sent | **You have already booked** You booked a place on 3 November, and each member can book one workshop a month. To change your booking, write to events@lindenhall.example. | **Sie haben bereits gebucht** Sie haben am 3. November einen Platz gebucht, und jedes Mitglied kann einen Workshop pro Monat buchen. Um Ihre Buchung zu ändern, schreiben Sie an events@lindenhall.example. |
| the signature's Clear, until the next stroke | Undo clear | Löschen zurücknehmen |
| an upload that stopped (in the file's row) | The upload stopped before it finished. [Try again] | Das Hochladen wurde vor dem Ende abgebrochen. [Erneut versuchen] |
| the honeypot's label, out of view and out of the accessibility tree, for a screen reader that reaches it anyway (ADR-0012) | Leave this field empty | Lassen Sie dieses Feld leer |

### The default error messages

What a visitor sees for an error the editor wrote no message for (ADR-0099): a field's own message for the kind, a
check's or a validation rule's comes first. Both runtimes use these words, held to one text by
`tests/vectors/messages.json`; the other five languages come with 14.2. A value in braces prints in the culture's
format; where a count changes the words, the singular comes first, then the plural, after a ·. Under the field a
message stands alone, but for a grid's row, which it names first ("Morning: Choose an answer in this row"); in the
summary a default follows the field's label, and a row's legend or a grid's row after it in brackets; a label that
already ends in a question mark, an exclamation mark, a full stop or a colon takes no colon after it ("Where would you
like to sit? Choose an answer", ADR-0106); a label made only of spaces counts as none, so the message
stands alone, and a legend made only of spaces is left out (ADR-0109).

| Case | English | German |
| --- | --- | --- |
| the word before the last of a list | or | oder |
| a size below a megabyte, 1,024 based, one place at most | {size} KB | {size} KB |
| a size from a megabyte | {size} MB | {size} MB |
| `required`: a text or a text area | Enter an answer | Geben Sie eine Antwort ein |
| `required`: an email address | Enter an email address | Geben Sie eine E-Mail-Adresse ein |
| `required`: a phone number | Enter a phone number | Geben Sie eine Telefonnummer ein |
| `required`: a web address | Enter a web address | Geben Sie eine Webadresse ein |
| `required`: a number | Enter a number | Geben Sie eine Zahl ein |
| `required`: a date | Enter a date | Geben Sie ein Datum ein |
| `required`: a time | Enter a time | Geben Sie eine Uhrzeit ein |
| `required`: a date and a time | Enter a date and a time | Geben Sie ein Datum und eine Uhrzeit ein |
| `required`: a rating | Choose a rating | Wählen Sie eine Bewertung |
| `required`: a scale | Choose a point on the scale | Wählen Sie einen Wert auf der Skala |
| `required`: a slider | Choose a value | Wählen Sie einen Wert |
| `required`: radio buttons, a dropdown of one | Choose an answer | Wählen Sie eine Antwort |
| `required`: checkboxes, a dropdown of several | Choose at least one answer | Wählen Sie mindestens eine Antwort |
| `required`: a grid | Choose an answer in each row | Wählen Sie in jeder Zeile eine Antwort |
| `required`: one of a grid's rows | Choose an answer in this row | Wählen Sie in dieser Zeile eine Antwort |
| `required`: a ranking | Put the options in order | Bringen Sie die Optionen in eine Reihenfolge |
| `required`: a checkbox | Tick the box | Kreuzen Sie das Kästchen an |
| `required`: yes or no, in the field's own words for them | Choose {yes} or {no} | Wählen Sie {yes} oder {no} |
| `required`: a consent | Tick the box to agree | Kreuzen Sie das Kästchen an, um zuzustimmen |
| `required`: a country | Choose a country | Wählen Sie ein Land |
| `required`: a name | Enter the name in full | Geben Sie den vollständigen Namen ein |
| `required`: an address | Enter the address in full | Geben Sie die vollständige Adresse ein |
| `required`: a file upload | Choose a file | Wählen Sie eine Datei |
| `required`: a signature | Add your signature, or type your name instead | Unterschreiben Sie, oder geben Sie stattdessen Ihren Namen ein |
| `required`: a site's own type | Answer this question | Beantworten Sie diese Frage |
| `invalid`: an email address | Enter a complete email address, for example name@example.com | Geben Sie eine vollständige E-Mail-Adresse ein, zum Beispiel name@example.com |
| `invalid`: a well-formed email address at a domain the field does not take | Use an email address at another domain | Verwenden Sie eine E-Mail-Adresse bei einer anderen Domain |
| `invalid`: a phone number | Enter a phone number with digits, and a + for the country code | Geben Sie eine Telefonnummer mit Ziffern ein, mit einem + für die Landesvorwahl |
| `invalid`: a web address | Enter a complete web address, for example https://example.com | Geben Sie eine vollständige Webadresse ein, zum Beispiel https://example.com |
| `invalid`: a number; the example is 12, or 12.5 with decimals, in the culture's format | Enter a number, for example {example} | Geben Sie eine Zahl ein, zum Beispiel {example} |
| `invalid`: a date | Enter a valid date | Geben Sie ein gültiges Datum ein |
| `invalid`: a time | Enter a valid time | Geben Sie eine gültige Uhrzeit ein |
| `invalid`: a date and a time | Enter a valid date and time | Geben Sie ein gültiges Datum und eine gültige Uhrzeit ein |
| `invalid`: a rating | Choose a rating from 1 to {count} | Wählen Sie eine Bewertung von 1 bis {count} |
| `invalid`: a scale | Choose a point from {from} to {to} | Wählen Sie einen Wert von {from} bis {to} |
| `invalid`, `min`, `max`: a slider | Choose a value from {min} to {max} | Wählen Sie einen Wert von {min} bis {max} |
| `invalid`: a choice, a grid or a ranking | Choose one of the options given | Wählen Sie eine der angebotenen Optionen |
| `invalid`: a country | Choose a country from the list | Wählen Sie ein Land aus der Liste |
| `invalid`: a name | Check the name | Prüfen Sie den Namen |
| `invalid`: an address | Check the address | Prüfen Sie die Adresse |
| `invalid`: a signature | Sign again, or type your name instead | Unterschreiben Sie erneut, oder geben Sie stattdessen Ihren Namen ein |
| `invalid` of a site's own type, a validation rule without a message, a kind without words of its own | Check this answer | Prüfen Sie diese Antwort |
| `minLength` | Enter at least {min} character · Enter at least {min} characters | Geben Sie mindestens {min} Zeichen ein |
| `maxLength` | Enter at most {max} character · Enter at most {max} characters | Geben Sie höchstens {max} Zeichen ein |
| `minWords` | Enter at least {min} word · Enter at least {min} words | Geben Sie mindestens {min} Wort ein · Geben Sie mindestens {min} Wörter ein |
| `maxWords` | Enter at most {max} word · Enter at most {max} words | Geben Sie höchstens {max} Wort ein · Geben Sie höchstens {max} Wörter ein |
| `min`, `max`: a number with both limits | Enter a number from {min} to {max} | Geben Sie eine Zahl von {min} bis {max} ein |
| `min`, `max`: a number with a least only | Enter a number of {min} or more | Geben Sie eine Zahl ab {min} ein |
| `min`, `max`: a number with a most only | Enter a number of {max} or less | Geben Sie eine Zahl bis {max} ein |
| `min`, `max`: a time with both limits | Enter a time from {min} to {max} | Geben Sie eine Uhrzeit von {min} bis {max} ein |
| `min`, `max`: a time with an earliest only | Enter a time of {min} or later | Geben Sie eine Uhrzeit ab {min} ein |
| `min`, `max`: a time with a latest only | Enter a time of {max} or earlier | Geben Sie eine Uhrzeit bis {max} ein |
| `step`: a number's step | Enter a number in steps of {step} | Geben Sie eine Zahl in Schritten von {step} ein |
| `step`: a number's step from its least | Enter a number in steps of {step}, starting at {min} | Geben Sie eine Zahl in Schritten von {step} ab {min} ein |
| `step`: a number without decimals | Enter a whole number | Geben Sie eine ganze Zahl ein |
| `step`: a number's decimal places | Enter a number with at most {places} decimal place · Enter a number with at most {places} decimal places | Geben Sie eine Zahl mit höchstens {places} Nachkommastelle ein · Geben Sie eine Zahl mit höchstens {places} Nachkommastellen ein |
| `step`: a slider | Choose a value in steps of {step} | Wählen Sie einen Wert in Schritten von {step} |
| `step`: the minutes of a time or a date and time | Choose a time in steps of {minutes} minute · Choose a time in steps of {minutes} minutes | Wählen Sie eine Uhrzeit im Abstand von {minutes} Minute · Wählen Sie eine Uhrzeit im Abstand von {minutes} Minuten |
| `step`: the minutes of a time from its earliest | Choose a time in steps of {minutes} minute from {min} · Choose a time in steps of {minutes} minutes from {min} | Wählen Sie eine Uhrzeit im Abstand von {minutes} Minute ab {min} · Wählen Sie eine Uhrzeit im Abstand von {minutes} Minuten ab {min} |
| `dateWindow`: a window with both ends | Choose a date from {min} to {max} | Wählen Sie ein Datum vom {min} bis zum {max} |
| `dateWindow`: a window with a start only | Choose a date on or after {min} | Wählen Sie ein Datum ab dem {min} |
| `dateWindow`: a window with an end only | Choose a date on or before {max} | Wählen Sie ein Datum bis zum {max} |
| `dateWindow`: weekdays only, with both ends | Choose a date from {min} to {max}, {monday} to {friday} | Wählen Sie ein Datum vom {min} bis zum {max}, {monday} bis {friday} |
| `dateWindow`: weekdays only, with a start only | Choose a date on or after {min}, {monday} to {friday} | Wählen Sie ein Datum ab dem {min}, {monday} bis {friday} |
| `dateWindow`: weekdays only, with an end only | Choose a date on or before {max}, {monday} to {friday} | Wählen Sie ein Datum bis zum {max}, {monday} bis {friday} |
| `dateWindow`: weekdays only, without ends | Choose a day from {monday} to {friday} | Wählen Sie einen Tag von {monday} bis {friday} |
| `dateWindow`: a date the window takes that the field excludes | This date is not available. Choose another one. | Dieses Datum ist nicht verfügbar. Wählen Sie ein anderes. |
| `selections`: as many as the fewest and the most | Choose {count} answer · Choose {count} answers | Wählen Sie {count} Antwort · Wählen Sie {count} Antworten |
| `selections`: the fewest and the most | Choose from {min} to {max} answers | Wählen Sie {min} bis {max} Antworten |
| `selections`: the fewest only | Choose at least {min} answer · Choose at least {min} answers | Wählen Sie mindestens {min} Antwort · Wählen Sie mindestens {min} Antworten |
| `selections`: the most only | Choose at most {max} answer · Choose at most {max} answers | Wählen Sie höchstens {max} Antwort · Wählen Sie höchstens {max} Antworten |
| `fileType`: the formats, joined with commas and "or" | Choose a file in the format {formats} | Wählen Sie eine Datei im Format {formats} |
| `fileSize`: a field without a limit of its own | This file is too large. Choose a smaller one. | Diese Datei ist zu groß. Wählen Sie eine kleinere. |
| `fileSize`: one file | This file is larger than {size}. Choose a smaller one. | Diese Datei ist größer als {size}. Wählen Sie eine kleinere. |
| `fileSize`: several files | One of the files is larger than {size}. Choose a smaller one. | Eine der Dateien ist größer als {size}. Wählen Sie eine kleinere. |
| `fileCount` | Choose only {max} file · Choose at most {max} files | Wählen Sie nur {max} Datei · Wählen Sie höchstens {max} Dateien |
| `full`: one option, by its label | {option} is now full. Choose another option. | {option} ist jetzt ausgebucht. Wählen Sie eine andere Option. |
| `full`: several options | One of your choices is now full. Choose another option. | Eine Ihrer gewählten Optionen ist jetzt ausgebucht. Wählen Sie eine andere Option. |
| `confirmation` | Enter the same email address twice | Geben Sie zweimal dieselbe E-Mail-Adresse ein |
| `digits` | Enter a number with fewer digits | Geben Sie eine Zahl mit weniger Ziffern ein |
| `rowCount`: too few rows | Add at least {min} row · Add at least {min} rows | Fügen Sie mindestens {min} Zeile hinzu · Fügen Sie mindestens {min} Zeilen hinzu |
| `rowCount`: too many rows | Remove rows to keep at most {max} | Entfernen Sie Zeilen, sodass höchstens {max} bleibt · Entfernen Sie Zeilen, sodass höchstens {max} bleiben |
| `check`: a pattern without a message | Enter the answer in the format asked for | Geben Sie die Antwort im verlangten Format ein |
| `check`: the same as another field, named by its label | Enter the same as for {field} | Geben Sie dasselbe ein wie bei {field} |
| `check`: unique per form | This answer has already been given. Enter another one. | Diese Antwort wurde bereits gegeben. Geben Sie eine andere ein. |
| a submission without an answer, in the summary | Answer at least one question before you send the form | Beantworten Sie mindestens eine Frage, bevor Sie das Formular senden |
| the error summary's heading, by the number of its links | This answer needs a change · These answers need a change | Diese Antwort muss noch geändert werden · Diese Antworten müssen noch geändert werden |
| a summary link to a default | {label}: {message} | {label}: {message} |
| a summary link to a default after a label that ends in a question mark, an exclamation mark, a full stop or a colon | {label} {message} | {label} {message} |
| a summary link to a default in a repeatable group's row | {label} ({row}): {message} | {label} ({row}): {message} |

## Checklist for every new string

- [ ] It uses the glossary's word, in sentence case.
- [ ] A button starts with a verb and names its object when it stands alone.
- [ ] An error says what failed and how to go on, and whether work was kept.
- [ ] An empty state says which kind of empty it is and offers the next action.
- [ ] A destructive confirmation names what is lost, with numbers, and repeats the action on its button.
- [ ] Nothing on the screen already says the same thing.
- [ ] It has an English and a German key, and both read naturally aloud.
- [ ] User-typed names go in as values, never as HTML.
