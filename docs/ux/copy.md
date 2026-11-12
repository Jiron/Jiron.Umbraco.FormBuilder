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
| insights | what is counted of a form's visits and entries, its view of them and their switch | Einblicke | analytics, statistics, Auswertung |
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
| a delivery whose attempt failed, with its next try ahead | Trying again | Neuer Versuch |
| a run that never ran (an earlier one stopped the rest, or its change was undone) | Skipped | Übersprungen |
| the version visitors get, in History | Published | Veröffentlicht |
| a version the cleanup keeps, in History (a mark with this name) | Pinned | Angeheftet |
| an entry on legal hold, in Privacy › People | Legal hold until 30 Apr 2027 | Löschsperre bis 30. Apr. 2027 |
| the Inbox's sidebar item with unseen entries (its badge shows the number, up to 99 and then "99+"; screen readers hear the item's name with it) | Inbox, 12 unseen · Inbox, 99+ unseen | Posteingang, 12 ungelesen · Posteingang, 99+ ungelesen |
| an unseen entry, for screen readers (a dot shows) | unseen | ungelesen |

### Status lines

One line that says where something stands, next to the thing it is about.

| Where | English | German |
| --- | --- | --- |
| an action's card, after a failed delivery | Failed at 14:02. Next try at 14:30. | Fehlgeschlagen um 14:02. Nächster Versuch um 14:30. |
| an action's card, after it failed for good | Failed at 14:02. | Fehlgeschlagen um 14:02. |
| an action's card, after a webhook (Slack, Teams) went through | Delivered 2 hours ago | Vor 2 Stunden zugestellt |
| an action's card, after an email handed its mail to the outbox | Mail queued 2 hours ago | Vor 2 Stunden zum Versand übergeben |
| an action's card, after another type ran | Ran yesterday | Gestern ausgeführt |
| an action's card, while its next try is due | Failed at 14:02. Next try soon. | Fehlgeschlagen um 14:02. Nächster Versuch in Kürze. |
| an action's card, after an attempt was cut off | Uncertain at 14:02. It may have gone through. | Unklar um 14:02. Möglicherweise wurde der Lauf trotzdem ausgeführt. |
| an action's card, while it runs; after it was skipped | Running now · Skipped 2 hours ago | Läuft gerade · Vor 2 Stunden übersprungen |
| a trigger whose setting is off, without actions (the link opens the setting) | Runs when someone rejects an entry. It needs manual approval: [turn it on in Settings] | Läuft, wenn jemand einen Eintrag ablehnt. Dafür ist die manuelle Genehmigung nötig: [in den Einstellungen einschalten] |
| a trigger whose setting is off, above the actions it holds | These don't run until the form has email verification: [turn it on in Settings] | Diese laufen erst, wenn das Formular die E-Mail-Bestätigung hat: [in den Einstellungen einschalten] |
| the Approved trigger, on a form without manual approval | as each entry comes in, because this form has no manual approval | bei jedem eingehenden Eintrag, da dieses Formular keine manuelle Genehmigung hat |
| the Actions view without Manage actions (one line at the top, no folder named: the grant may be global) | Changing these actions needs the Manage actions permission for this form. An administrator can grant it. | Zum Ändern dieser Aktionen ist die Berechtigung „Aktionen verwalten“ für dieses Formular nötig. Wer Administratorrechte hat, kann sie erteilen. |
| an action's card that is off, read-only | Off | Aus |
| a trigger without actions (its heading says when it runs) | No actions here yet. | Hier noch keine Aktionen. |
| an email's preview: who it goes to, before anyone is chosen | No recipient yet | Noch kein Empfänger |
| an email's preview: who it comes from, on a site without a sender | None yet: the site's email settings name no sender | Noch keiner: Die E-Mail-Einstellungen der Website nennen keinen Absender |
| an email's preview: the sample's answer of the recipient field | alex.berger@example.com, in a mail of its own | alex.berger@example.com, in einer eigenen E-Mail |
| an email's preview: its lines, the same words as the settings | From · To · Also to (a team's mail that a field's answer gets too) · Copy to · Blind copy to · Reply to · Subject | Von · An · Auch an · Kopie an · Blindkopie an · Antwort an · Betreff |
| an email's preview: the frame's title, and the name of its Light · Dark pair | The email · Colour scheme | Die E-Mail · Farbschema |
| an email's preview, for settings its type can't read (the form check says where) | This email's settings have errors. The preview shows once they're fixed. | Die Einstellungen dieser E-Mail haben Fehler. Die Vorschau erscheint, sobald sie behoben sind. |
| an email's To box for who may only read it (one recipient a line) | sales@example.com · The answer of Email address · The person who created the form | sales@example.com · Die Antwort von „Email address“ · Die Person, die das Formular erstellt hat |
| an email's reply-to (a field picker of email fields) | Reply to | Antwort an |
| an email's layout left to the site (the select names the layout it is) | Site default (Card) | Standard der Website (Karte) |
| a webhook's ways of sending (each with its line under it) | Every answer, by its field's alias · The names and values you choose · A body you write · Only that something happened · A message (Slack and Teams) | Jede Antwort, unter dem Alias ihres Felds · Die Namen und Werte, die Sie wählen · Ein Inhalt, den Sie schreiben · Nur, dass etwas geschehen ist · Eine Nachricht |
| a credential or a signing secret typed as itself (under its box; it is not kept; the example is said once a screen, so a chat's address, whose help has one, says none) | Authorization carries a credential: give it as a reference, such as $JironFormBuilder:Secrets:CrmToken. | Authorization trägt Zugangsdaten: Geben Sie sie als Verweis an, etwa $JironFormBuilder:Secrets:CrmToken. |
| where a webhook goes (its target) | An address of its own · Slack · Microsoft Teams | Eine eigene Adresse · Slack · Microsoft Teams |
| a webhook's test, as the server answered | Test sent. The server answered 201. · The server answered 404, so the test wasn't delivered. | Test gesendet. Der Server antwortete 201. · Der Server antwortete 404, daher kam der Test nicht an. |
| a webhook's test of a secret the site lacks (the key, never a value) | The site's configuration has no JironFormBuilder:Secrets:CrmToken yet, so the test can't go out. A developer adds it. | Die Konfiguration der Website hat noch kein JironFormBuilder:Secrets:CrmToken, daher kann der Test nicht hinaus. Ein Entwickler trägt es ein. |
| a webhook's card whose address is a reference | Webhook to the address CrmHook | Webhook an die Adresse CrmHook |
| a secret written as itself where a reference belongs, for who may only read it (never its value) | A secret written as itself, not shown. Replace it with a reference. | Ein Geheimnis im Klartext, nicht angezeigt. Ersetzen Sie es durch einen Verweis. |
| a webhook's test without an address of its own | This webhook has no address yet. Give it one under Where it goes. | Dieser Webhook hat noch keine Adresse. Geben Sie sie unter „Ziel“ an. |
| where a content action's item goes, three choices, each with its line | Under an item you pick (Without one, at the root of the content tree.) · Under the page the form is sent from (Only from pages within the item you pick; elsewhere a run creates nothing.) · Under the root of the visitor's site (Only where that root is within the item you pick; elsewhere a run creates nothing.) | Unter einem Element, das Sie wählen (Ohne eines auf der obersten Ebene des Inhaltsbaums.) · Unter der Seite, von der das Formular gesendet wird (Nur von Seiten innerhalb des gewählten Elements; anderswo legt ein Lauf nichts an.) · Unter dem Stamm der Website der Person, die sendet (Nur wenn dieser Stamm im gewählten Element liegt; anderswo legt ein Lauf nichts an.) |
| the item picked under the chosen choice, its name for who hears it and for who may only read it | Under this item · Only within this item | Unter diesem Element · Nur innerhalb dieses Elements |
| what a content action's property takes (each field it can hold the answer to, one it can't or that is gone, then a value) | The answer to Headline · The answer to Email address (Heading can't hold it) · A field that is gone · A value you write | Die Antwort auf Headline · Die Antwort auf Email address (Heading kann sie nicht aufnehmen) · Ein Feld, das es nicht mehr gibt · Einen Wert, den Sie schreiben |
| a content action's property that nothing on the form fills | This form has no field whose answer Photo can hold, and Photo takes no value written for it. | Dieses Formular hat kein Feld, dessen Antwort Photo aufnehmen kann, und Photo nimmt keinen geschriebenen Wert auf. |
| History's versions that could not be read; a page of Show more that could not be; History's own words that could not be loaded | The versions couldn't be read. · Try again · More versions couldn't be read. Try Show more again. · The History couldn't be shown. Try again in a moment. · Try again | Die Versionen konnten nicht gelesen werden. · Erneut versuchen · Weitere Versionen konnten nicht gelesen werden. Versuchen Sie „Mehr anzeigen“ erneut. · Der Verlauf konnte nicht angezeigt werden. Versuchen Sie es gleich noch einmal. · Erneut versuchen |
| the Entries view's page of Show more that could not be read; its own words that could not be loaded; the columns a user chose, not kept for next time (the list shows them all the same) | More entries couldn't be loaded. Try Show more again. · The entries couldn't be shown. Try again in a moment. · Try again · The columns couldn't be kept for next time. | Weitere Einträge konnten nicht geladen werden. Versuchen Sie „Mehr anzeigen“ erneut. · Die Einträge konnten nicht angezeigt werden. Versuchen Sie es gleich noch einmal. · Erneut versuchen · Die Spalten konnten nicht für das nächste Mal gespeichert werden. |
| a content action's properties that could not be read | The document type's properties couldn't be read. · Try again | Die Eigenschaften des Dokumenttyps konnten nicht gelesen werden. · Erneut versuchen |
| help under a content action's Publish it, Update an item of the same name and Media folder for files | Off, the item is saved for an editor to look at and publish. · Where the name comes from an answer, a visitor's answer chooses which item is overwritten. Turn it on only where that's what you want. · Without one, the files go at the root of the media library. | Aus: Der Inhalt wird gespeichert, damit ihn jemand im Team prüft und veröffentlicht. · Kommt der Name aus einer Antwort, wählt die Antwort einer Person, welcher Inhalt überschrieben wird. Schalten Sie es nur ein, wo das gewollt ist. · Ohne Ordner kommen die Dateien auf die oberste Ebene der Medienbibliothek. |
| Change the entry's state: its choice and help | Move the entry to: Approved · Rejected · Spam (Approving or rejecting starts that trigger's actions for the entry.) | Eintrag verschieben nach: Genehmigt · Abgelehnt · Spam (Genehmigen oder Ablehnen startet die Aktionen dieses Auslösers für den Eintrag.) |
| a content action's card | Saves an item of type Story · Publishes an item of type Story · No document type yet | Speichert einen Inhalt vom Typ Story · Veröffentlicht einen Inhalt vom Typ Story · Noch kein Dokumenttyp |
| a content action's property, for who may only read it | Title: the answer to Headline · Title: a field that is gone | Title: die Antwort auf Headline · Title: ein Feld, das es nicht mehr gibt |
| a picked field or layout the form or site no longer has | A field that is gone · A layout that is gone | Ein Feld, das es nicht mehr gibt · Ein Layout, das es nicht mehr gibt |
| a picked field of another kind, which the picker shows while it is picked | Your name (no email field) · Topic (in a repeatable group) | Your name (kein E-Mail-Feld) · Topic (in einer wiederholbaren Gruppe) |
| beside the canvas's and the preview's width switch, when there is less room than the chosen width | Narrowed to fit | Schmaler als gewählt |
| above the permission matrix, for a user group without the section | Translators can't open Form Builder yet. Add the section to their user group | Übersetzer können Form Builder noch nicht öffnen. Bereich zu ihrer Benutzergruppe hinzufügen |
| above the permission matrix, once, for the fixed Administrators column | Administrators hold every permission everywhere, because they could grant themselves any. | Die Gruppe Administratoren hat überall jede Berechtigung, weil ihre Mitglieder sich jede selbst geben könnten. |
| a form's header, when no page picks it | Not used on any page | Auf keiner Seite verwendet |
| the inspector, while the designer shows another language | Editing the German texts. Where one is empty, visitors see the English text. | Sie bearbeiten die deutschen Texte. Wo einer fehlt, sehen Besucher den englischen. |
| a chip on a field, group or page, a box, an outcome's row or an action's card without a text in that language | No German text yet | Noch kein Text auf Deutsch |
| under a text that names a dictionary item (`#key`) | From the dictionary: Email address · The dictionary has no item Forms.Email, so visitors see "#Forms.Email". · The dictionary item has no text in this language, so visitors see "#Forms.Email". | Aus dem Wörterbuch: E-Mail-Adresse · Das Wörterbuch hat keinen Eintrag Forms.Email, darum sehen Besucher „#Forms.Email“. · Der Wörterbucheintrag hat keinen Text in dieser Sprache, darum sehen Besucher „#Forms.Email“. |
| the button in a text box's row that picks a dictionary item | Use a dictionary item | Wörterbucheintrag verwenden |
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
| restore a version, then open the publish dialog for it (beside Restore as a new draft in the sidebar that compares it with the current draft; not for the live version) | Restore and publish… | Wiederherstellen und veröffentlichen… |
| after a save someone else's save refused (the dialog's primary) | Review both versions | Beide Versionen prüfen |
| the two versions a difference is taken from, in Review both versions (a choice under each) | Ana Novak's version · Your version | Version von Ana Novak · Ihre Version |
| add the draft saves to History's list (a switch) | Show draft saves | Gespeicherte Entwürfe zeigen |
| compare two ticked versions, in History's toolbar | Compare | Vergleichen |
| a History row's "…" (the current draft's own row has no restore) | Restore as a new draft · Pin · Unpin · Add comment… · Change comment… | Als neuen Entwurf wiederherstellen · Anheften · Anheften aufheben · Kommentar hinzufügen… · Kommentar ändern… |
| more rows of a list that reads a page at a time | Show more | Mehr anzeigen |
| create the block that puts forms on pages | Create a form block type… | Formular-Blocktyp erstellen… |
| start from a ready-made form (the checklist, an empty folder) | Choose a template… | Vorlage wählen… |
| make a JSON file a form's draft (a form's "…", for who may design it; the import sidebar for that form) · its primary | Replace from a file… · Replace the draft | Aus einer Datei ersetzen… · Entwurf ersetzen |
| keep a form as a template (its "…" in the tree, for who may manage the library and design the form) · keep the selected group as one (its "…" on the canvas) | Save as template… · Save group as template… | Als Vorlage speichern… · Gruppe als Vorlage speichern… |
| a saved template's "…" (Library › Templates; a built-in one has none) · the primary of the dialog that keeps a form or group as one, whose title names it · the rename dialog's primary | Rename… · Delete… · Save · Rename | Umbenennen… · Löschen… · Speichern · Umbenennen |
| turn a list of questions into fields | Paste questions… | Fragen einfügen… |
| turn lines into a choice field's options, or a grid's rows (one a line) | Paste options… · Paste rows… | Optionen einfügen… · Zeilen einfügen… |
| move an option or a grid's row with single presses: what its handle opens under it | Position in the list · 1 of 3 | Position in der Liste · 1 von 3 |
| add a merge tag to a text | Insert merge tag | Platzhalter einfügen |
| add who gets an email (its To box; it offers An address, The answer of a field, The person who created the form, Addresses chosen by an answer, each kind once) | Add recipient | Empfänger hinzufügen |
| add a row to a webhook's names and values, or to its headers | Add a name · Add header | Namen hinzufügen · Kopfzeile hinzufügen |
| add a row to a content action's properties (it offers the properties not set yet) | Add property | Eigenschaft hinzufügen |
| keep the addresses typed in a box (Enter and leaving the box keep them too; its accessible name says what it adds to, apart from the sidebar's own Add: Add address · Adresse hinzufügen under Add recipient, Add to Copy to · Zu „Kopie an“ hinzufügen for another box) | Add | Hinzufügen |
| take a recipient away (an icon button, named for it) | Remove sales@example.com · Remove the answer of Email address | sales@example.com entfernen · Die Antwort von „Email address“ entfernen |
| show an email's preview in a colour scheme (two pressed buttons) | Light · Dark | Hell · Dunkel |
| the preview's widths, named for screen readers, and its "…" | Width · More preview options | Breite · Weitere Vorschauoptionen |
| in the preview's "…": the page's colour scheme, for a theme with the dark one, and the site's languages, each a group of pressed buttons | Colour scheme: Light · Dark · Language: English · German | Farbschema: Hell · Dunkel · Sprache: Englisch · Deutsch |
| delete for good (asks first) | Delete… | Löschen… |
| delete a designer element at once, with Undo in the toast | Delete rule · Delete outcome | Regel löschen · Abschluss löschen |
| empty the Recycle Bin | Empty recycle bin… | Papierkorb leeren… |
| delete every entry in Spam the user may delete (Spam's toolbar; asks first) | Empty spam… | Spam leeren… |
| reverse the last change | Undo | Zurücknehmen |
| repeat a reversed change | Redo | Erneut anwenden |
| send again after a failure | Retry | Wiederholen |
| approve an entry | Approve | Genehmigen |
| reject an entry | Reject | Ablehnen |
| move an entry to Spam | Mark as spam | Als Spam markieren |
| send an entry to a user or a team address (the entry panel; it opens a sidebar) · the sidebar's primary | Forward… · Forward | Weiterleiten… · Weiterleiten |
| put an approved or rejected entry back, on a form approved by hand (the entry panel's "…") | Back to waiting for approval | Zurück zu „Wartet auf Genehmigung“ |
| show a sensitive answer, one at a time (named for screen readers by its field) · mask it again | Reveal · Hide | Anzeigen · Verbergen |
| turn an entry's answers into fields (the Answers tab; the bar then holds Cancel and Save) | Edit answers | Antworten bearbeiten |
| add a note · keep a changed note of one's own | Add note · Save note | Notiz hinzufügen · Notiz speichern |
| the entry panel's "…", and its close button, named for screen readers | More actions for this entry · Close the entry | Weitere Aktionen für diesen Eintrag · Eintrag schließen |
| move an entry out of Spam | Not spam | Kein Spam |
| send an entry to a colleague | Forward… | Weiterleiten… |
| show one masked value, and mask it again | Reveal · Hide | Anzeigen · Verbergen |
| try an action against a sample entry (on its card, named "Test Email the team" · „Email the team“ testen, and in its sidebar's footer, its title saying what its type's test does: "Sends a test to you alone." · „Sendet einen Test nur an Sie.", "Sends a sample entry where this action sends, after asking you." · „Sendet nach einer Rückfrage einen Beispieleintrag dorthin, wohin diese Aktion sendet.", "Shows what a run would do. Nothing is sent or changed." · „Zeigt, was ein Lauf täte. Nichts wird gesendet oder geändert.") | Test | Testen |
| test an action that reaches what it is set up to reach, after its question "Send a test?" · „Test senden?", which names the place in the card's own line: "Webhook to hooks.example.com. A sample entry goes there as a real one would, marked as a test." · „Webhook an hooks.example.com. Ein Beispieleintrag geht dorthin wie ein echter, als Test gekennzeichnet." (an action whose card says nothing yet: "This sends a sample entry where this action is set up to send it, marked as a test." · „Damit geht ein Beispieleintrag dorthin, wohin diese Aktion sendet, als Test gekennzeichnet.") | Send test | Test senden |
| run a test entry's actions, from the preview (asks first) | Run actions… | Aktionen ausführen… |
| narrow a list | Filter | Filtern |
| keep the current filters under a name | Save as… | Speichern unter… |
| remove every active filter | Clear filters | Filter zurücksetzen |
| remove the search | Clear search | Suche zurücksetzen |
| choose the summary email | Email me a summary… | Zusammenfassung per E-Mail… |
| send an email to yourself | Send test email | Test-E-Mail senden |
| fetch a JSON address's options again | Fetch now | Jetzt abrufen |
| download entries | Export… | Exportieren… |
| download the ticked entries, in the bulk bar's "…" | Export selected… | Ausgewählte exportieren… |
| the export sidebar's primary, with what goes ("Print 124 entries" for the printable view; "more than 1,000" after a search that counts no further) | Export 124 entries | 124 Einträge exportieren |
| print one entry, in the entry panel's "…" | Print | Drucken |
| save a background export's file, in its toast and in the sidebar's list | Download | Herunterladen |
| keep a person's entries past their retention | Set legal hold… | Löschsperre setzen… |
| erase one person's data (asks first) | Erase… | Daten löschen… |
| open a problem from the form check | Fix it | Beheben |
| the "…" of an action's card | Duplicate · Move to another trigger… · Delete | Duplizieren · Zu einem anderen Auslöser verschieben… · Löschen |
| fill the preview with valid answers | Fill with sample answers | Mit Beispielantworten füllen |
| render the preview afresh, once its frame sent the form (in place of Fill with sample answers) | Start again | Neu beginnen |
| the preview's "…", for who may manage the form's actions: whether test entries run them (a switch) | Test entries · Run actions for test entries | Testeinträge · Aktionen für Testeinträge ausführen |
| reach the canvas past the header and the palette (the workspace's first stop) | Skip to the form | Zum Formular springen |
| list the keyboard shortcuts | Keyboard shortcuts | Tastenkürzel |

- **Name the object when the button stands alone:** "Create option list", "Add action", "Export 124 entries". Inside a
  dialog whose title names the object, the verb alone is enough ("Duplicate").
- **The confirm button repeats the action and its object** ("Delete 23 entries"), never "Yes" or "OK".
- **A switch names what it turns on, never its state,** and leaves the state to the switch itself: "Use this rule" ·
  „Diese Regel verwenden“; an action's switch is "Use Email the team" · „„Email the team“ verwenden“ (German quotes a name, which may read as a verb), not "Email the team
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
| a value of a settings screen out of range (under its box, before anything is sent) | Use a whole number from 1 to 3650. | Verwenden Sie eine ganze Zahl von 1 bis 3650. |
| a settings screen's save refused at some fields (one line at the top; each field says why under its box) | Some settings need another value. Each one says why. | Einige Einstellungen brauchen einen anderen Wert. Jede sagt, warum. |
| a secret typed where a reference belongs (Settings › Integrations, under its box) | Enter a reference such as $JironFormBuilder:Secrets:Crm, never the secret itself. | Geben Sie einen Verweis wie $JironFormBuilder:Secrets:Crm ein, nie das Geheimnis selbst. |
| a largest file above the host's cap (Settings › Files, under its box) | Use a number of megabytes up to 50, such as 20 or 2.5. | Verwenden Sie eine Zahl von Megabyte bis 50, etwa 20 oder 2,5. |
| Settings › Access, or a Permissions sidebar, saved by someone else meanwhile (the grants saved are shown) | Someone else changed access meanwhile. Their grants are shown now; make your change again if it is still needed. | Jemand anderes hat den Zugriff inzwischen geändert. Jetzt sehen Sie dessen Berechtigungen; nehmen Sie Ihre Änderung erneut vor, falls sie noch nötig ist. |
| a site chosen on a settings screen that is no longer in Content (back on All sites) | This site is no longer in Content. Choose another. | Diese Website ist nicht mehr im Inhalt. Wählen Sie eine andere. |
| a settings screen saved by someone else meanwhile (the values saved are shown) | Someone else saved these settings meanwhile. Their values are shown now; make your change again if it is still needed. | Jemand anderes hat diese Einstellungen inzwischen gespeichert. Jetzt sehen Sie deren Werte; nehmen Sie Ihre Änderung erneut vor, falls sie noch nötig ist. |
| a settings section the host configuration locks (no Save) | The site's configuration sets these settings (JironFormBuilder:LockedSections:0), so they can't be changed here. | Die Konfiguration der Website legt diese Einstellungen fest (JironFormBuilder:LockedSections:0); hier lassen sie sich nicht ändern. |
| the Answers not read · their next free texts not read (beside Show more, which stays) | The answers couldn't be loaded. [Try again] · More answers couldn't be loaded. | Die Antworten konnten nicht geladen werden. [Erneut versuchen] · Weitere Antworten konnten nicht geladen werden. |
| a settings screen not read, or not saved | The settings couldn't be read. [Try again] · The settings couldn't be saved. Try again in a moment. | Die Einstellungen konnten nicht gelesen werden. [Erneut versuchen] · Die Einstellungen konnten nicht gespeichert werden. Versuchen Sie es gleich noch einmal. |
| Undo after a restore, once the draft has changed since (Undo would lose that change) | The draft has changed since, so the restore can't be undone here. The draft before stays in History. | Der Entwurf hat sich seither geändert, daher lässt sich das Wiederherstellen hier nicht zurücknehmen. Der vorherige Entwurf bleibt im Verlauf. |
| a restored draft the backoffice couldn't read again (no Undo) | The new draft couldn't be read here. Reload the page to see it. | Der neue Entwurf konnte hier nicht gelesen werden. Laden Sie die Seite neu, um ihn zu sehen. |
| a version the cleanup removed while the list showed it | This version doesn't exist any more. Choose another one. | Diese Version gibt es nicht mehr. Wählen Sie eine andere. |
| saving failed (network) | Couldn't save the draft. Check your connection, then try again. Your changes are still here. [Try again] | Der Entwurf konnte nicht gespeichert werden. Prüfen Sie die Verbindung und versuchen Sie es erneut. Ihre Änderungen sind noch da. [Erneut versuchen] |
| someone saved first | **Your draft wasn't saved** Ana Novak saved this form at 14:02, after you opened it. Review both versions before you save. Your changes are still here. [Review both versions] | **Ihr Entwurf wurde nicht gespeichert** Ana Novak hat dieses Formular um 14:02 gespeichert, nachdem Sie es geöffnet haben. Prüfen Sie beide Versionen, bevor Sie speichern. Ihre Änderungen sind noch da. [Beide Versionen prüfen] |
| someone changed the form, where no newer draft was saved (a rename) | **Your draft wasn't saved** Someone changed this form after you opened it. Review both versions before you save. Your changes are still here. [Review both versions] | **Ihr Entwurf wurde nicht gespeichert** Jemand hat dieses Formular geändert, nachdem Sie es geöffnet haben. Prüfen Sie beide Versionen, bevor Sie speichern. Ihre Änderungen sind noch da. [Beide Versionen prüfen] |
| Review both versions, when the versions can't be read | Both versions couldn't be read. Your changes are still here; try again in a moment. | Die beiden Versionen konnten nicht gelesen werden. Ihre Änderungen sind noch da; versuchen Sie es gleich noch einmal. |
| Review both versions, Save with differences still waiting (once, as an alert) · by each that waits | 2 differences still need a choice. · Choose a version | Für 2 Unterschiede fehlt noch die Wahl. · Version wählen |
| publishing blocked (the publish dialog; each error has its own Fix it) | This form can't be published yet: 1 error needs fixing. | Dieses Formular lässt sich noch nicht veröffentlichen: 1 Fehler muss behoben werden. |
| warnings beside an error, in the publish dialog | It also has 1 warning, which doesn't block publishing. | Außerdem gibt es 1 Warnung, die das Veröffentlichen nicht verhindert. |
| the form check, for a form without fields | **Add at least one field** The form has no fields yet, so visitors would have nothing to fill in. [Fix it] | **Mindestens ein Feld hinzufügen** Das Formular hat noch keine Felder, Besucher hätten also nichts auszufüllen. [Beheben] |
| the bulk bar's change of state, delete or re-run that got no answer | The entries couldn't be changed. Try again. · The entries couldn't be deleted. Try again. · The actions couldn't be queued. Try again. | Die Einträge konnten nicht geändert werden. Versuchen Sie es erneut. · Die Einträge konnten nicht gelöscht werden. Versuchen Sie es erneut. · Die Aktionen konnten nicht eingereiht werden. Versuchen Sie es erneut. |
| the summary email's choice not read · not saved · its test not sent | Your choice couldn't be read. [Try again] · Your choice couldn't be saved. Try again. · The test email couldn't be sent. Try again. | Ihre Auswahl konnte nicht gelesen werden. [Erneut versuchen] · Ihre Auswahl konnte nicht gespeichert werden. Versuchen Sie es erneut. · Die Test-E-Mail konnte nicht gesendet werden. Versuchen Sie es erneut. |
| a filter set not saved · its name, which another set of the form has (under the name) | The filter set couldn't be saved. · Another filter set of this form has this name. | Der Filtersatz konnte nicht gespeichert werden. · Ein anderer Filtersatz dieses Formulars hat diesen Namen. |
| the entry panel got no answer · an entry's own link, for an entry that is gone or that the user may not view (Umbraco's own "not found", which names the item; nothing of the entry) | The entry couldn't be shown. · entry not found | Der Eintrag konnte nicht angezeigt werden. · Eintrag nicht gefunden |
| the entry panel's own failures (each where it happened) | The value couldn't be shown. Try again. · The file couldn't be downloaded. Try again. · The answers couldn't be saved. Try again. · The note couldn't be saved. Try again. · The entry couldn't be forwarded. Try again. · The timeline couldn't be read. Try again. | Der Wert konnte nicht angezeigt werden. Versuchen Sie es erneut. · Die Datei konnte nicht heruntergeladen werden. Versuchen Sie es erneut. · Die Antworten konnten nicht gespeichert werden. Versuchen Sie es erneut. · Die Notiz konnte nicht gespeichert werden. Versuchen Sie es erneut. · Der Eintrag konnte nicht weitergeleitet werden. Versuchen Sie es erneut. · Der Verlauf konnte nicht gelesen werden. Versuchen Sie es erneut. |
| an edited answer the field refuses (under its field) | Give an answer. · This answer can't be read. · This answer is longer than the field takes. | Geben Sie eine Antwort an. · Diese Antwort lässt sich nicht lesen. · Diese Antwort ist länger, als das Feld aufnimmt. |
| missing permission | Changing entries needs the "Edit entries" permission. An administrator can grant it in Settings › Access. | Zum Ändern von Einträgen brauchen Sie die Berechtigung „Einträge bearbeiten“. Wer Administratorrechte hat, kann sie unter Einstellungen › Zugriff vergeben. |
| gone or no access | This form doesn't exist any more, or you don't have access to it. [Back to forms] | Dieses Formular gibt es nicht mehr, oder Sie haben keinen Zugriff darauf. [Zurück zu den Formularen] |
| a setting's value | Enter an alias that starts with a letter and uses only letters, digits and hyphens. | Geben Sie einen Alias ein, der mit einem Buchstaben beginnt und nur Buchstaben, Ziffern und Bindestriche enthält. |
| email not configured (Send test email is hidden meanwhile) | Emails can't be sent yet: no mail server is configured. Entries are kept, and their emails go out once sending works. A developer sets up sending in `Umbraco:CMS:Global:Smtp`. | E-Mails können noch nicht versendet werden: Es ist kein Mailserver eingerichtet. Die Einträge bleiben erhalten, und ihre E-Mails gehen raus, sobald der Versand funktioniert. Den Versand richtet jemand aus der Entwicklung in `Umbraco:CMS:Global:Smtp` ein. |
| a delivery failed | The webhook to crm.example.com failed: the server answered "503 Service Unavailable". It is tried again at 14:30. [Retry now] | Der Webhook an crm.example.com ist fehlgeschlagen: Der Server antwortete „503 Service Unavailable“. Er wird um 14:30 erneut versucht. [Jetzt wiederholen] |
| a failed run's Details (the Actions view), under What can be done | Test sends a sample entry, so you can see whether the server answers again. Someone who sees this form's entries and may change its actions finds the entry in Entries with the Actions filter, and can retry the run there. An administrator can also retry it in Activity › Deliveries. [Test] | Testen sendet einen Beispieleintrag, so sehen Sie, ob der Server wieder antwortet. Wer die Einträge dieses Formulars sieht und seine Aktionen ändern darf, findet den Eintrag unter „Einträge“ mit dem Filter „Aktionen“ und kann den Lauf dort wiederholen. Wer Administratorrechte hat, kann ihn auch unter Aktivität › Zustellungen wiederholen. [Testen] |
| a failed run's Details, what happened and the tries | **What happened** The server answered 503. **Tries** Tried 3 times, the first at 13:40. | **Was passiert ist** Der Server antwortete mit 503. **Versuche** 3-mal versucht, zuerst um 13:40. |
| a mail held back by a sending limit (no Retry; the limit is the one the host sets now) | Not sent: this site's forms had already sent 3 emails to anna@example.org that day, the most one address gets in a day. The limit keeps the forms from being used to send mail to strangers. A limited email is never sent later, so it can't be retried. Host configuration sets the limit: `JironFormBuilder:Limits:Mail:PerRecipientPerDay`. | Nicht gesendet: Die Formulare dieser Website hatten an diesem Tag schon 3 E-Mails an anna@example.org gesendet, die Höchstzahl für eine Adresse an einem Tag. Die Grenze verhindert, dass die Formulare genutzt werden, um Fremden E-Mails zu schicken. Eine begrenzte E-Mail wird nie später gesendet und lässt sich deshalb nicht wiederholen. Die Hostkonfiguration legt die Grenze fest: `JironFormBuilder:Limits:Mail:PerRecipientPerDay`. |
| a delivery's details, for a reader who may not retry it | Only someone who may manage the actions of this form can retry it. | Wiederholen kann nur, wer die Aktionen dieses Formulars verwalten darf. |
| Activity › Deliveries while the host pauses sending (once, above the list) | Sending is paused by the host setting `JironFormBuilder:Actions:Paused`. Entries are still stored, and their emails and webhooks go out once it is off. | Der Versand ist durch die Host-Einstellung `JironFormBuilder:Actions:Paused` angehalten. Einträge werden weiter gespeichert; ihre E-Mails und Webhooks gehen raus, sobald sie aus ist. |
| locked by configuration | Locked by configuration: `JironFormBuilder:Privacy:AllowFullIp`. | Durch die Konfiguration gesperrt: `JironFormBuilder:Privacy:AllowFullIp`. |
| folder not empty | This folder still holds 3 forms. Move or delete them first. | Dieser Ordner enthält noch 3 Formulare. Verschieben oder löschen Sie sie zuerst. |
| an address that is none, in an email's address box (what was typed stays) | Enter addresses such as hello@example.com: "sales" isn't one. | Geben Sie Adressen wie hallo@example.com ein: „sales“ ist keine. |
| an email's preview, for a view the site lacks and no body (named as its setting, Razor view, names it) | This site has no Razor view "Receipt", and this email has no body of its own. Write a body, or name a view the site has. | Diese Website hat keine Razor-Ansicht „Receipt“, und diese E-Mail hat keinen eigenen Text. Schreiben Sie einen Text, oder nennen Sie eine Ansicht, die die Website hat. |

- **Field errors** sit under their field and say how to fix it ("Enter a number between 1 and 20"), never only what is
  wrong ("Invalid").
- **A remote answer** (a webhook's status line) is quoted as it came, marked as the other server's words, and never
  interpreted as HTML.

### Empty states

An empty state tells apart **first use**, **no results**, **no permission** and **failure**, and offers the next
useful action. One sentence, plus a button or link where there is one.

| Case | English | German |
| --- | --- | --- |
| Restore an earlier version… on a form without one (the current draft is never listed) | There is no earlier version yet. | Es gibt noch keine frühere Version. |
| Activity › Deliveries, nothing sent yet · nothing matches | No deliveries yet. Emails and webhooks appear here once forms send them. · No deliveries match the search and the filters. | Noch keine Zustellungen. E-Mails und Webhooks erscheinen hier, sobald Formulare sie senden. · Keine Zustellung passt zur Suche und zu den Filtern. |
| Activity › Audit log, nothing recorded yet · nothing matches | Nothing is recorded yet. The log fills as people work with forms, entries and settings. · No actions match the search and the filters. | Noch ist nichts protokolliert. Das Protokoll füllt sich, wenn Personen mit Formularen, Einträgen und Einstellungen arbeiten. · Keine Aktion passt zur Suche und zu den Filtern. |
| an audit log list or Deliveries past its read budget | The list took too long to load. Narrow it down with the search or the filters. [Try again] | Die Liste hat zu lange gebraucht. Grenzen Sie sie mit der Suche oder den Filtern ein. [Erneut versuchen] |
| no entries yet, for a user who may design the form | No entries yet. Share the form, or preview it and send a test entry. | Noch keine Einträge. Teilen Sie das Formular, oder öffnen Sie die Vorschau und senden Sie einen Testeintrag. |
| no entries yet, for everyone else (they can neither share nor preview it) | No entries yet. They appear here as soon as visitors send the form. [Email me a summary…] | Noch keine Einträge. Einträge erscheinen hier, sobald jemand das Formular sendet. [Zusammenfassung per E-Mail…] |
| filters match nothing (Clear filters sits in the chip row above, so the empty state has no button of its own) | No entries match these filters. Remove one above, or clear them all. | Keine Einträge passen zu diesen Filtern. Entfernen Sie oben einen davon, oder setzen Sie alle zurück. |
| a search finds nothing, and sensitive fields exist | No entries match "Jana". Sensitive fields aren't searched. [Clear search] | Keine Einträge passen zu „Jana“. Vertrauliche Felder werden nicht durchsucht. [Suche zurücksetzen] |
| an empty folder (its footer holds Create form) | No forms here yet. A template gives you a working form in a minute. [Choose a template…] | Hier gibt es noch keine Formulare. Mit einer Vorlage haben Sie in einer Minute ein fertiges Formular. [Vorlage wählen…] |
| History before the first publish | Nothing is published yet. Publishing keeps the first version here; until then, Show draft saves lists the saves. | Noch ist nichts veröffentlicht. Beim Veröffentlichen wird hier die erste Version festgehalten; bis dahin listet „Gespeicherte Entwürfe zeigen“ die Speicherungen. |
| two versions that don't differ, in their comparison | No differences. | Keine Unterschiede. |
| an empty Recycle Bin (no footer) | The Recycle Bin is empty. Trashed forms wait here with their entries until someone restores them or deletes them for good. | Der Papierkorb ist leer. Formulare im Papierkorb warten hier mit ihren Einträgen, bis jemand sie wiederherstellt oder endgültig löscht. |
| no entries in any form (the Inbox, without its toolbar) | No entries yet. They arrive here from every form you may view. [Email me a summary…] | Noch keine Einträge. Hier kommen die Einträge aller Formulare an, die Sie sehen dürfen. [Zusammenfassung per E-Mail…] |
| a member without entries (the member's Form entries view) | No entries yet. Entries this member sends while signed in appear here. | Noch keine Einträge. Einträge, die dieses Mitglied angemeldet sendet, erscheinen hier. |
| the member's Form entries view could not load (with Try again) | The member's entries couldn't be loaded. | Die Einträge des Mitglieds konnten nicht geladen werden. |
| no access to any form | You can't see any forms yet. An administrator can give your user group access in Settings › Access. | Sie sehen noch keine Formulare. Wer Administratorrechte hat, kann Ihre Benutzergruppe unter Einstellungen › Zugriff freischalten. |
| nothing in Spam | No spam. Entries that look automated land here, and nothing in them is lost. | Kein Spam. Einträge, die automatisiert wirken, landen hier, und nichts davon geht verloren. |
| the Form filter's search finds no form (Inbox and Spam) | No form you may view has "quiz" in its name. | Kein Formular, das Sie sehen dürfen, hat „quiz“ im Namen. |
| a field picker on a form without such a field (said in its place) | This form has no email field yet. · This form has no file upload yet. · This form has no field marked sensitive. | Dieses Formular hat noch kein E-Mail-Feld. · Dieses Formular hat noch keinen Datei-Upload. · Dieses Formular hat kein als vertraulich markiertes Feld. |
| an entry without notes (under the box for a new one) | No notes yet. | Noch keine Notizen. |
| Forward…, while nobody may receive the entry | Nobody else may view this form's entries yet, and Settings › Email names no team address. | Noch darf niemand sonst die Einträge dieses Formulars sehen, und unter Einstellungen › E-Mail steht keine Team-Adresse. |
| no actions yet (Add action sits in the toolbar) | No actions yet. Most forms email the team when an entry comes in, and Add action starts with that. | Noch keine Aktionen. Die meisten Formulare benachrichtigen das Team per E-Mail, wenn ein Eintrag eingeht, und „Aktion hinzufügen“ beginnt damit. |
| a new, empty form | Add your first field with Add field below, or paste your questions. | Fügen Sie das erste Feld mit „Feld hinzufügen“ unten hinzu, oder fügen Sie Ihre Fragen ein. |
| no rules yet (the rules overview, with Add rule in its header) | No rules yet. Most rules show a question only when another answer calls for it, and Add rule starts by asking which question. | Noch keine Regeln. Die meisten Regeln zeigen eine Frage nur, wenn eine andere Antwort sie verlangt, und „Regel hinzufügen“ fragt zuerst, um welche Frage es geht. |
| no insights yet | No data yet. Numbers appear after the first visit to a page with this form. | Noch keine Daten. Zahlen erscheinen nach dem ersten Besuch einer Seite mit diesem Formular. |
| insights off for the site (the Insights view's Overview, and nothing else; its Answers come from the entries and stay) | Insights are off for this site. An administrator can turn them on in Privacy › Data collection. | Die Einblicke sind für diese Website ausgeschaltet. Ein Administrator kann sie unter Datenschutz › Datenerfassung einschalten. |
| insights off for the form | Insights are off for this form. Turn them on in its Settings view, under Privacy. | Die Einblicke sind für dieses Formular ausgeschaltet. Schalten Sie sie in seiner Ansicht Einstellungen unter Datenschutz ein. |
| a number's change from the period before, in words, never in colour | up 6% from 4,540 · down 25% from 400 · up 0.4 points from 7.7% · up from none | 6 % mehr als 4.540 · 25 % weniger als 400 · 0,4 Prozentpunkte mehr als 7,7 % · davor keine |
| a field that stops visitors | Email: 212, 16% of starts, mostly the format | E-Mail: 212 (16 % der Begonnenen), meist das Format |
| the median time to complete | Half of those who send it finish within 3 minutes. | Die Hälfte derer, die es senden, ist innerhalb von 3 Minuten fertig. |
| no visits yet on Forms at a glance | No visits yet. Numbers appear after the first visit to a page with a form. | Noch keine Aufrufe. Zahlen erscheinen nach dem ersten Besuch einer Seite mit einem Formular. |
| the Answers without an entry in the range · with the filter set too | No entries in this period. · No entries in this period match these filters. | Keine Einträge in diesem Zeitraum. · Kein Eintrag in diesem Zeitraum entspricht diesen Filtern. |
| the fields nobody answered in the range (one line under the Answers' boxes) | Not answered in this period: Phone and Company. | In diesem Zeitraum nicht beantwortet: Telefon und Firma. |
| an option's line in the Answers: its share of the answers and its count · an older version's option · the visitors' own texts · several chosen | 42% (163) · Press (no longer an option) · Other, in their own words · More than one could be chosen, so the shares add up to more than 100%. | 42 % (163) · Presse (keine Option mehr) · Sonstiges, in eigenen Worten · Mehrere konnten gewählt werden, daher ergeben die Anteile zusammen mehr als 100 %. |
| a ranking's line, and the note under its bars | average position 1.4 · The higher an option ranks on average, the longer its bar. | durchschnittlich auf Position 1,4 · Je höher eine Option im Durchschnitt steht, desto länger ist ihr Balken. |
| a 0-to-10 scale's score, and the line under it | Recommendation score: +42 · The share who chose 9 or 10, less the share who chose 0 to 6, from -100 to 100. | Weiterempfehlungswert: +42 · Der Anteil, der 9 oder 10 gewählt hat, abzüglich des Anteils, der 0 bis 6 gewählt hat, von -100 bis 100. |
| a field answered, not summarised · a field of a repeatable group · a checkbox there | Answered in 80% of the entries. · One answer for each row of Guests. · Ticked in 3 rows. | In 80 % der Einträge beantwortet. · Eine Antwort für jede Zeile von „Gäste“. · In 3 Zeilen angehakt. |
| Privacy › Data collection, Insights | Forms count what visitors do with them, for each form's Insights view and the Overview: views, starts, the pages they reach and the fields that stop them. No answer and no visitor is counted, and nothing is stored in the visitor's browser. | Formulare zählen, was Besucher mit ihnen tun, für die Ansicht Einblicke jedes Formulars und die Übersicht: Aufrufe, Begonnene, die Seiten, die sie erreichen, und die Felder, an denen sie hängen bleiben. Keine Antwort und kein Besucher wird gezählt, und im Browser des Besuchers wird nichts gespeichert. |
| loading failed | Entries couldn't be loaded. [Try again] | Die Einträge konnten nicht geladen werden. [Erneut versuchen] |
| an outcome's preview got no answer | The preview couldn't be shown. [Try again] | Die Vorschau konnte nicht angezeigt werden. [Erneut versuchen] |
| the preview's headline, and its frame, named for screen readers | Preview of Contact us · The form as visitors see it | Vorschau von Contact us · Das Formular, wie Besucher es sehen |
| the preview while the draft is rendered | Loading the preview… | Die Vorschau wird geladen… |
| the preview got no answer | The preview couldn't be loaded. [Try again] | Die Vorschau konnte nicht geladen werden. [Erneut versuchen] |
| the preview of a draft whose rules have errors, which it never runs | This draft's rules have errors, so it can't be shown as visitors would get it. The form check on the Design view lists them. | Die Regeln dieses Entwurfs haben Fehler, deshalb lässt er sich nicht so zeigen, wie Besucher ihn bekämen. Die Formularprüfung in der Ansicht „Gestaltung“ listet sie auf. |
| the note above the preview's frame, as the frame's ticket has it, its test entries' actions off · on | Entries sent here are test entries. They run no actions. · Entries sent here are test entries. Their actions run, so their emails and messages really go out. | Hier gesendete Einträge sind Testeinträge. Für sie laufen keine Aktionen. · Hier gesendete Einträge sind Testeinträge. Ihre Aktionen laufen, ihre E-Mails und Nachrichten gehen also wirklich hinaus. |
| the note once the frame sent the form (Run actions… only for who may run them, while its actions wait) · its actions running · waiting for its address to be confirmed · no test entry found | Saved as test entry #1044. No actions ran. [Run actions…] · Saved as test entry #1044. Its actions are running. · Saved as test entry #1044. Its actions run once its email address is confirmed. · Sent as a test entry. | Als Testeintrag #1044 gespeichert. Es liefen keine Aktionen. [Aktionen ausführen…] · Als Testeintrag #1044 gespeichert. Seine Aktionen laufen. · Als Testeintrag #1044 gespeichert. Seine Aktionen laufen, sobald seine E-Mail-Adresse bestätigt ist. · Als Testeintrag gesendet. |
| the note's Run actions… as a screen reader names it, its shown words first (WCAG 2.5.3) | Run actions for test entry #1044… | Aktionen ausführen für Testeintrag #1044… |
| a test entry's actions got no answer (in the note, Run actions… kept) | The actions couldn't be run. | Die Aktionen konnten nicht ausgeführt werden. |
| the entry panel's details line, first, for a test entry opened through its own mail's link | Test entry, sent from the preview | Testeintrag, aus der Vorschau gesendet |
| the public API refused the frame's ticket, which no renewal brings back (in the note, said as an alert) | This preview can't send any more: its access has run out. [Load again] | Diese Vorschau kann nichts mehr senden: Ihr Zugang ist abgelaufen. [Neu laden] |

An empty value inside a table or panel reads "none" ("keine") or "no data" ("keine Daten"), never a dash (§4.13).

### Confirmations

- **Reversible actions don't ask.** They act at once and offer **Undo** in a toast that names what it brings back
  ("Email address deleted. [Undo]" · „Email address gelöscht. [Zurücknehmen]“).
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
| run actions again for the ticked entries (each action listed with its trigger; with none to run, a toast says so in its place) | **Run actions again for 12 entries?** These actions run again, so their emails and messages really go out: Tell the team, when approved. [Cancel] [Run actions for 12 entries] · None of these entries has an action to run again. | **Aktionen für 12 Einträge erneut ausführen?** Diese Aktionen laufen erneut, ihre E-Mails und Nachrichten gehen also wirklich hinaus: Tell the team, bei Genehmigung. [Abbrechen] [Aktionen für 12 Einträge ausführen] · Keiner dieser Einträge hat eine Aktion, die erneut laufen kann. |
| trash a form in use | **Move Contact us to the Recycle Bin?** It's picked on 3 pages: Home, Contact, Events. Those pages show nothing in its place until you restore the form. [Cancel] [Move to Recycle Bin] | **Kontakt in den Papierkorb verschieben?** Das Formular ist auf 3 Seiten eingebunden: Startseite, Kontakt, Veranstaltungen. Dort erscheint nichts, bis Sie es wiederherstellen. [Abbrechen] [In den Papierkorb] |
| delete a form for good | **Delete Contact us for good?** This deletes the form with its 1,204 entries and 37 uploaded files. You can't undo this. [Cancel] [Delete form and entries] | **Kontakt endgültig löschen?** Dabei werden das Formular, seine 1.204 Einträge und 37 hochgeladenen Dateien gelöscht. Das lässt sich nicht rückgängig machen. [Abbrechen] [Formular und Einträge löschen] |
| a folder or form restricted while no group has access of its own there | **Restrict HR?** No user group has access of its own here yet, so only administrators will see its forms. [Cancel] [Restrict folder] | **HR beschränken?** Noch hat keine Benutzergruppe hier eigenen Zugriff; nur Administratoren werden die Formulare sehen. [Abbrechen] [Ordner beschränken] |
| another site picked on a settings screen with changes not saved | **Discard your changes?** The changes on this screen are not saved yet. Another site shows its own values. [Cancel] [Discard] | **Ihre Änderungen verwerfen?** Die Änderungen auf diesem Bildschirm sind noch nicht gespeichert. Eine andere Website zeigt ihre eigenen Werte. [Abbrechen] [Verwerfen] |
| empty Spam (whatever the filters show; with the filters or search on, it says they don't narrow it; the entries of forms where the user may not delete stay, which it says while nothing narrows the list; past 10,000, it says a second Empty spam… deletes the rest) | **Empty Spam?** 12 entries in Spam go for good. 3 entries stay: their forms don't let you delete entries. This also deletes their 2 uploaded files and their timelines. You can't undo this. [Cancel] [Delete 12 entries] | **Spam leeren?** 12 Einträge im Spam werden endgültig gelöscht. 3 Einträge bleiben, weil Sie in ihren Formularen keine Einträge löschen dürfen. Dabei werden auch ihre 2 hochgeladenen Dateien und ihre Chroniken gelöscht. Das lässt sich nicht rückgängig machen. [Abbrechen] [12 Einträge löschen] |
| empty the Recycle Bin | **Empty the Recycle Bin?** This deletes 3 forms for good, with their 412 entries and 12 uploaded files. You can't undo this. [Cancel] [Empty recycle bin] | **Papierkorb leeren?** Dabei werden 3 Formulare mit ihren 412 Einträgen und 12 hochgeladenen Dateien endgültig gelöscht. Das lässt sich nicht rückgängig machen. [Abbrechen] [Papierkorb leeren] |
| erase a person | **Erase everything about lena@example.com?** 6 entries in 3 forms, 2 uploaded files and 4 sent emails are erased. Statistics stay. You can't undo this. [Cancel] [Erase 6 entries] | **Alle Daten zu lena@example.com löschen?** 6 Einträge in 3 Formularen, 2 hochgeladene Dateien und 4 gesendete E-Mails werden gelöscht. Die Statistik bleibt. Das lässt sich nicht rückgängig machen. [Abbrechen] [6 Einträge löschen] |
| delete one entry, from its panel | **Delete entry #214?** This also deletes its uploaded file and its timeline. You can't undo this. [Cancel] [Delete entry] | **Eintrag #214 löschen?** Dabei werden auch seine hochgeladene Datei und seine Chronik gelöscht. Das lässt sich nicht rückgängig machen. [Abbrechen] [Eintrag löschen] |
| move entries back onto a full option or a unique answer another entry holds (nothing moves until confirmed; an edit asks the same, as **Save anyway?** [Save anyway]) | **Move it back anyway?** Front row (Seat) is full by now: all 20 places are taken. Another entry has the same Email, which must be unique. [Cancel] [Move back anyway] | **Trotzdem zurücksetzen?** Front row (Seat) ist inzwischen voll: Alle 20 Plätze sind vergeben. Ein anderer Eintrag hat dieselbe Angabe in „Email“, die eindeutig sein muss. [Abbrechen] [Trotzdem zurücksetzen] |
| retry an uncertain delivery | **Retry this delivery?** It may already have arrived, so retrying could deliver it twice. [Cancel] [Retry anyway] | **Diese Zustellung wiederholen?** Sie ist vielleicht schon angekommen, eine Wiederholung könnte sie doppelt zustellen. [Abbrechen] [Trotzdem wiederholen] |
| unpublish | **Unpublish Contact us?** It's picked on 3 pages: Home, Contact, Events. Visitors there see its closed message until you publish it again. Its draft, entries and settings stay. [Cancel] [Unpublish] | **Veröffentlichung von Kontakt zurücknehmen?** Das Formular ist auf 3 Seiten eingebunden: Startseite, Kontakt, Veranstaltungen. Dort erscheint die Meldung für geschlossene Formulare, bis Sie es erneut veröffentlichen. Entwurf, Einträge und Einstellungen bleiben erhalten. [Abbrechen] [Veröffentlichung zurücknehmen] |
| publish (what changed since the published version, unsaved changes included, each part's first three lines; while an error blocks, only what to fix) | **Publish Contact us?** **Since the published version** Fields: Field added: Phone · Full name: Label and Required · And 2 more. Visitors get this version of the form from now on. [Cancel] [Publish] | **Contact us veröffentlichen?** **Seit der veröffentlichten Version** Felder: Feld hinzugefügt: Phone · Full name: Beschriftung und Pflichtfeld · Und 2 weitere. Besucher erhalten ab jetzt diese Version des Formulars. [Abbrechen] [Veröffentlichen] |
| publish: a form never published · published as it is · the changes can't be read · a form taken offline (over its changes, and published as it is) | The first version holds 1 page, 7 fields, 2 rules, 1 check and 1 action. · Nothing changed since the published version. · What changed couldn't be read. Publishing works all the same. · Since the last published version · Nothing changed since the last published version. | Die erste Version enthält 1 Seite, 7 Felder, 2 Regeln, 1 Prüfung und 1 Aktion. · Seit der veröffentlichten Version hat sich nichts geändert. · Was sich geändert hat, konnte nicht gelesen werden. Veröffentlichen lässt sich trotzdem. · Seit der zuletzt veröffentlichten Version · Seit der zuletzt veröffentlichten Version hat sich nichts geändert. |
| publish, what a screen reader hears of the changes (the status region, over the parts) · a line of many | Changed since the published version: Fields and Settings. · Fields added: Phone, Department, Street, City, Country, 3 more | Seit der veröffentlichten Version geändert: Felder und Einstellungen. · Felder hinzugefügt: Phone, Department, Street, City, Country, 3 weitere |
| publish, while the draft is compared (as "Checking the form…" beside it) · where none is published | Comparing with the published version… · Reading the draft… | Wird mit der veröffentlichten Version verglichen… · Der Entwurf wird gelesen… |
| publish, a rule or a check that changed | Rule changed on Topic · Check of Email changed | Regel an Topic geändert · Prüfung von Email geändert |
| run a test entry's actions, from the preview | **Run the actions for test entry #1044?** These 4 actions run as they would for a real entry, so their emails and messages really go out. [Cancel] [Run 4 actions] | **Aktionen für Testeintrag #1044 ausführen?** Diese 4 Aktionen laufen wie bei einem echten Eintrag; ihre E-Mails und Nachrichten gehen also wirklich hinaus. [Abbrechen] [4 Aktionen ausführen] |
| restrict a folder without grants | **Restrict HR?** No user group has access of its own here yet, so only administrators will see its forms. [Cancel] [Restrict folder] | **HR einschränken?** Noch hat keine Benutzergruppe hier eigenen Zugriff, deshalb sehen nur Personen mit Administratorrechten die Formulare darin. [Abbrechen] [Ordner einschränken] |
| delete a saved template (a group's says "Groups inserted from it stay in their forms." · „Daraus eingefügte Gruppen bleiben in ihren Formularen.“) | **Delete Enquiry?** Forms made from it stay as they are. The template can't be brought back. [Cancel] [Delete] | **Enquiry löschen?** Formulare, die daraus erstellt wurden, bleiben, wie sie sind. Die Vorlage lässt sich nicht wiederherstellen. [Abbrechen] [Löschen] |

### Success

Routine success is brief, and is said by the change itself where possible (the saved state, the row that moved). A
toast adds only what the user cannot see.

| Case | English | German |
| --- | --- | --- |
| draft saved | Draft saved | Entwurf gespeichert |
| published (the chip already says Published) | Visitors get the new version now. | Auf der Website gilt jetzt die neue Version. |
| published for the first time, while no page shows it ("Show how" opens the "Used on" sidebar) | Visitors can fill it in once it's on a page. [Show how] | Besucher können es ausfüllen, sobald es auf einer Seite eingebunden ist. [Anleitung zeigen] |
| a version restored as a new draft (from its sidebar, a History row's "…" or Restore an earlier version…; the list joins as the reader's language does) | Version 13 is the new draft: 4 fields, After sending, 1 rule and 1 action changed. [Undo] | Version 13 ist der neue Entwurf: 4 Felder, „Nach dem Senden“, 1 Regel und 1 Aktion geändert. [Zurücknehmen] |
| a version restored that holds what the draft before held | Version 13 is the new draft. It holds what the draft before held. [Undo] | Version 13 ist der neue Entwurf. Er enthält, was der vorherige Entwurf enthielt. [Zurücknehmen] |
| a version restored whose changes couldn't be read | Version 13 is the new draft. [Undo] | Version 13 ist der neue Entwurf. [Zurücknehmen] |
| a restore undone (the draft before comes back as a new draft) | The draft before is back. | Der vorherige Entwurf ist wiederhergestellt. |
| a settings screen saved | The settings are saved. | Die Einstellungen sind gespeichert. |
| a delivery retried from its details | The delivery is queued again. | Die Zustellung ist wieder eingereiht. |
| a form's draft replaced with a file | The draft of Contact is replaced. Visitors see it once you publish. | Der Entwurf von Contact ist ersetzt. Besucher sehen ihn, sobald Sie veröffentlichen. |
| Settings › Access saved · a folder's or form's Permissions saved | Access is saved. · The permissions of HR are saved. | Der Zugriff ist gespeichert. · Die Berechtigungen von HR sind gespeichert. |
| Settings › Email's test mail sent (saved first when the screen has changes) | Test email sent to you (ana@example.com). | Test-E-Mail an Sie gesendet (ana@example.com). |
| one entry approved, in the entry panel | Entry #214 approved. [Undo] | Eintrag #214 genehmigt. [Zurücknehmen] |
| the panel's other changes of state · put back to waiting | Entry #214 rejected. · Entry #214 marked as spam. · Entry #214 moved out of Spam. · Entry #214 is waiting for approval again. [Undo] | Eintrag #214 abgelehnt. · Eintrag #214 als Spam markiert. · Eintrag #214 aus dem Spam geholt. · Eintrag #214 wartet wieder auf Genehmigung. [Zurücknehmen] |
| one entry deleted · forwarded · its answers saved · a run queued again, from the panel | Entry #214 deleted. · Entry forwarded to Ana Silva. · The answers are saved. · Queued again. | Eintrag #214 gelöscht. · Eintrag an Ana Silva weitergeleitet. · Die Antworten sind gespeichert. · Erneut eingereiht. |
| entries approved | 12 entries approved. [Undo] | 12 Einträge genehmigt. [Zurücknehmen] |
| some selected entries left out | 3 entries approved. 3 weren't waiting for approval. [Undo] | 3 Einträge genehmigt. 3 warteten nicht auf Genehmigung. [Zurücknehmen] |
| entries of several forms, some in a form where the user may not do it (the Inbox's bulk bar; one Undo takes back every form's change) · Spam emptied | 3 entries approved. 1 was left out: their form doesn't let you do this. [Undo] · 12 entries deleted from Spam. | 3 Einträge genehmigt. 1 wurde ausgelassen, weil Sie das in seinem Formular nicht dürfen. [Zurücknehmen] · 12 Einträge aus dem Spam gelöscht. |
| the summary email saved: off · daily · weekly | The summary email is off. · You'll get a summary each morning. · You'll get a summary each Monday morning. | Die Zusammenfassung per E-Mail ist aus. · Sie erhalten jeden Morgen eine Zusammenfassung. · Sie erhalten jeden Montagmorgen eine Zusammenfassung. |
| the summary email's test sent (the sidebar stays open) | Test email sent to jonas@example.com. | Test-E-Mail an jonas@example.com gesendet. |
| entries deleted · actions queued again, and those left out · a filter set saved | 23 entries deleted. · Actions queued for 12 entries. 3 were left out: in Spam, waiting for confirmation, or without actions to run. · Filter set Accountants saved. | 23 Einträge gelöscht. · Aktionen für 12 Einträge eingereiht. 3 wurden ausgelassen: im Spam, wartend auf Bestätigung oder ohne Aktionen. · Filtersatz Accountants gespeichert. |
| an action added to a published form (Publish… stays in the footer) | Post in #leads added. It runs for new entries once you publish. | Post in #leads hinzugefügt. Die Aktion läuft für neue Einträge, sobald Sie veröffentlichen. |
| an action added (the live region; the card takes the focus) | Added. Email the team runs when an entry comes in. | Hinzugefügt. „Email the team“ läuft, wenn ein Eintrag eingeht. |
| an action duplicated (the live region) | Duplicated. Email the team (copy) is right after it. | Dupliziert. „Email the team (Kopie)“ steht direkt danach. |
| an action deleted (the toast) | Mark as approved deleted. [Undo] | „Mark as approved“ gelöscht. [Zurücknehmen] |
| an outcome added to a published form | Added. Visitors get it once you publish. | Hinzugefügt. Auf der Website gilt es ab der nächsten Veröffentlichung. |
| a form saved as a template · a template renamed · deleted (Library › Templates) | Enquiry was saved as a template. · The template is now Enquiries. · Enquiry was deleted. | Enquiry wurde als Vorlage gespeichert. · Die Vorlage heißt jetzt Enquiries. · Enquiry wurde gelöscht. |
| a group saved as a template · without the rules and validation rules that named a field outside it (each count in the words, joined by "and") · refused, as a group the server can't read | Delivery was saved. Any form can insert it from the palette. · Delivery was saved without 1 rule and 1 validation rule that depend on fields outside the group. · The group couldn't be saved: the form check finds a problem in it. Fix it, then save the group again. | Delivery wurde gespeichert. Jedes Formular kann die Gruppe aus der Palette einfügen. · Delivery wurde ohne 1 Regel und 1 Prüfregel gespeichert, die von Feldern außerhalb der Gruppe abhängen. · Die Gruppe konnte nicht gespeichert werden: Die Formularprüfung findet darin ein Problem. Beheben Sie es und speichern Sie die Gruppe dann erneut. |
| test email sent (an action's test says all in its toast's message, which Umbraco reads aloud, led by the action's name; each sentence says on its own what happened) | Email the team: Test email sent to you (ana@example.com). | „Email the team“: Test-E-Mail an Sie gesendet (ana@example.com). |
| an action's test reached what it is set up to reach (the type's own sentence after it) | Test sent. | Test gesendet. |
| an action's test that reached nobody (what a run would do on the lines under it) | Nothing was sent or changed. | Nichts wurde gesendet oder geändert. |
| … a content action's lines (the sample entry's answers in the name; a property it would leave out has a line of its own, and makes the toast a warning that stays) | A run would save an item of type Story named "Ada Lovelace" under Stories. · It would set Title, Email and Age. | Ein Lauf würde einen Inhalt vom Typ Story namens „Ada Lovelace“ unter Stories speichern. · Er würde Title, Email und Age setzen. |
| … Change the entry's state (and the actions the move starts) | A run would approve the entry, which starts 2 Approved actions. · A run would move the entry to Spam. | Ein Lauf würde den Eintrag genehmigen, was 2 Aktionen von „Genehmigt“ startet. · Ein Lauf würde den Eintrag in den Spam verschieben. |
| a content action's test where the visitor's page or site root has no item to stay within | Pick the item it must stay within under Where it goes, then test again. | Wählen Sie unter „Ablageort“ den Inhalt, in dem er bleiben muss, dann testen Sie erneut. |
| the form check of a content action's row (said under the row, so it names no property) | The document type has no such property. Remove the row. · That field is no longer on this form. Choose another. · The property can't hold that field's answer. Choose another. · An earlier row sets this property too; this row's value wins. | Der Dokumenttyp hat keine solche Eigenschaft. Entfernen Sie die Zeile. · Dieses Feld ist nicht mehr in diesem Formular. Wählen Sie ein anderes. · Die Eigenschaft kann die Antwort dieses Felds nicht aufnehmen. Wählen Sie ein anderes. · Eine Zeile davor setzt diese Eigenschaft auch; der Wert dieser Zeile gilt. |
| the form check of two Set state actions that would loop | "Approve again" moves the entry back, so the two would move it back and forth for ever. Change one of them. | „Approve again“ bewegt den Eintrag zurück; die beiden würden ihn also endlos hin und her bewegen. Ändern Sie eine davon. |
| an action's test failed, the tester has no address (the type's own sentence for a reason the backoffice does not know) | Your account has no email address, so a test email has nowhere to go. | Ihr Konto hat keine E-Mail-Adresse, daher kann keine Test-E-Mail ankommen. |
| … the site cannot send mail | This site can't send email yet: it has no mail server or pickup folder. A developer sets up sending in the site's Umbraco:CMS:Global:Smtp settings. | Diese Website kann noch keine E-Mails senden: Es ist weder ein Mailserver noch ein Ablageordner eingerichtet. Den Versand richtet jemand aus der Entwicklung in den Einstellungen Umbraco:CMS:Global:Smtp der Website ein. |
| … actions and email are paused | Actions and email are paused on this site, so the test can't go out. | Aktionen und E-Mails sind auf dieser Website angehalten, daher kann der Test nicht gesendet werden. |
| … the email's view is missing | This site has no Razor view the email names, and the email has no body of its own. | Diese Website hat die Razor-Ansicht nicht, die die E-Mail nennt, und die E-Mail hat keinen eigenen Text. |
| … it threw | The test couldn't finish. A developer finds why in the site's log. | Der Test konnte nicht abgeschlossen werden. Den Grund findet jemand aus der Entwicklung im Protokoll der Website. |
| … settings the type cannot read | This action's settings have errors. Open the action, fix them, then test again. | Die Einstellungen dieser Aktion haben Fehler. Öffnen Sie die Aktion, beheben Sie sie, und testen Sie dann erneut. |
| … a problem the server named (an action or type that is gone) | The test couldn't be run. | Der Test konnte nicht ausgeführt werden. |
| … no answer from the site | The site didn't answer. Try again in a moment. | Die Website hat nicht geantwortet. Versuchen Sie es gleich noch einmal. |
| an action's test running (the list's live region, or the sidebar's; once it runs, after a yes where it asks first) | Testing Email the team… · Testing… | „Email the team“ wird getestet… · Wird getestet… |
| a test mail's first line (in the tester's backoffice language; the rest is the real mail) | This is a test. The real email goes to sales@example.com, and to alex.berger@example.com in a mail of its own. | Dies ist ein Test. Die echte E-Mail geht an sales@example.com und in einer eigenen E-Mail an alex.berger@example.com. |
| … only the visitor gets a mail (the test is the visitor's mail) | This is a test. The real email goes to alex.berger@example.com. | Dies ist ein Test. Die echte E-Mail geht an alex.berger@example.com. |
| export ready | Your export of 12,400 entries is ready. [Download] | Ihr Export von 12.400 Einträgen ist fertig. [Herunterladen] |
| a ready export in the Export sidebar's list (its file goes a day after it was ready) | Your export of 12,400 entries is ready. Kept until Sun 9:41 AM. [Download] | Ihr Export von 12.400 Einträgen ist fertig. Verfügbar bis So., 09:41. [Herunterladen] |
| a background export's progress no longer answers (it goes on) | The export's progress can't be read just now. It goes on, and Export… in the form's Entries view lists it. | Der Fortschritt des Exports lässt sich gerade nicht lesen. Er läuft weiter, und „Exportieren…“ in der Ansicht Einträge des Formulars führt ihn auf. |
| a background export the server no longer knows | The export is no longer there. Start it again from Export… in the Entries view. | Den Export gibt es nicht mehr. Starten Sie ihn erneut über „Exportieren…“ in der Ansicht Einträge. |
| one person's export ready (Privacy › People) | Your export for lena@example.com is ready. [Download] | Ihr Export für lena@example.com ist fertig. [Herunterladen] |
| a person's data erased (the link only for users with `manageSettings`; others read "It's recorded in the audit log." · „Das ist im Protokoll vermerkt.“) | Everything about lena@example.com is erased: 6 entries in 3 forms, 2 uploaded files and 4 sent emails. Statistics stay. [Open the audit record] | Alle Daten zu lena@example.com sind gelöscht: 6 Einträge in 3 Formularen, 2 hochgeladene Dateien und 4 gesendete E-Mails. Die Statistik bleibt. [Protokolleintrag öffnen] |

### Loading and long work

- **Short waits** show a skeleton in the shape of what is coming, never a spinner in the middle of content.
- **Long work** names the operation, gives a number when one is known, and says whether the user can carry on:
  "Preparing the export of 12,400 entries. You can keep working; a notification links to the file." · „Der Export von
  12.400 Einträgen wird vorbereitet. Sie können weiterarbeiten; eine Benachrichtigung verlinkt die Datei.“
- **Progress** is determinate when it is known, and never invented: "Preparing the export: 2 of 6 entries. You can
  keep working; a notification links to the file." · „Der Export wird vorbereitet: 2 von 6 Einträgen. Sie können
  weiterarbeiten; eine Benachrichtigung verlinkt die Datei.“ After a search that counts no further than 1,000, the
  number written so far stands alone: "Preparing the export: 2,500 entries so far." · „Der Export wird vorbereitet:
  bisher 2.500 Einträge.“
- **The export sidebar** names its entries in one line: "All 214 entries, no filters" · „Alle 214 Einträge, keine
  Filter“, "124 entries that match the filters" · „124 Einträge, die den Filtern entsprechen“, "6 entries selected" ·
  „6 Einträge ausgewählt“; an export that will run in the background says so under it: "This export continues in the
  background; a notification links to the file." · „Dieser Export läuft im Hintergrund weiter; eine Benachrichtigung
  verlinkt die Datei.“ Its fields say once what is left out: "Sensitive fields are left out." · „Vertrauliche Felder
  werden weggelassen.“

### Help text

Help text sits under its setting and answers the question the label leaves open; it never restates the label.

| Setting | English help | German help |
| --- | --- | --- |
| One entry per member, under Who can fill it in once the form is for members (Availability) | A member who has sent it sees that in its place, and a second entry is refused. | Wer es gesendet hat, sieht das an seiner Stelle, und ein zweiter Eintrag wird abgelehnt. |
| Anonymous (the form's Privacy group) | Its entries keep no member, even when a member is signed in. | Die Einträge speichern kein Mitglied, auch wenn ein Mitglied angemeldet ist. |
| When a member is deleted (Settings › Privacy › Members, under the box's lead) | The same for every site, since a member belongs to none. · Anonymise their entries: The entries stay with their form, number and state, so counts and statistics keep them; every answer and everything else about the member goes. · Delete their entries: The entries go, with their files and mail. | Für alle Websites gleich, da ein Mitglied zu keiner gehört. · Seine Einträge anonymisieren: Die Einträge bleiben mit Formular, Nummer und Status erhalten, sodass Zählungen und Statistiken sie behalten; jede Antwort und alles andere über das Mitglied geht. · Seine Einträge löschen: Die Einträge gehen, mit ihren Dateien und E-Mails. |
| a version's comment (History's dialog) | At most 500 characters. Saving it empty removes the comment. | Höchstens 500 Zeichen. Leer gespeichert, wird der Kommentar entfernt. |
| Settings › Email while Umbraco can't send mail (one line at the top; no Send test email) | Emails can't be sent yet: no mail server is configured. Entries are kept, and their emails go out once sending works. A developer sets up sending in Umbraco:CMS:Global:Smtp. | E-Mails können noch nicht gesendet werden: Es ist kein Mailserver eingerichtet. Einträge bleiben erhalten, und ihre E-Mails gehen hinaus, sobald das Senden funktioniert. Eine Entwicklerin oder ein Entwickler richtet das Senden in Umbraco:CMS:Global:Smtp ein. |
| a box of the Access matrix ticked that needs another, or unticked that others need (said to screen readers) | View entries is on too: Export entries needs it. · Edit entries and Export entries are off too: they need View entries. | View entries ist ebenfalls an: Export entries braucht das. · Edit entries und Export entries sind ebenfalls aus: sie brauchen View entries. |
| Settings › Access, a group that can't open the section yet (with a link to each such group) | A group can't open Form Builder yet. Add the section to its user group: Translators | Eine Gruppe kann Form Builder noch nicht öffnen. Fügen Sie den Bereich ihrer Benutzergruppe hinzu: Translators |
| a folder or form on Settings › Access's list | Only these groups: HR team · Also: Sales · Only administrators | Nur diese Benutzergruppen: HR team · Zusätzlich: Sales · Nur Administratoren |
| a field a site sets of its own (under it, with Use the value for all sites) | This site has its own value. | Diese Website hat einen eigenen Wert. |
| a site chosen on a settings screen (one line at the top) | Settings every site shares are under All sites. | Einstellungen, die alle Websites teilen, stehen unter „Alle Websites“. |
| a captcha without its keys (its line; it can't be chosen) | No keys yet. A developer adds them in JironFormBuilder:Captcha:RecaptchaV2. | Noch keine Schlüssel. Eine Entwicklerin oder ein Entwickler fügt sie in JironFormBuilder:Captcha:RecaptchaV2 hinzu. |
| a choice the host refuses (its line; it can't be chosen) | Locked by configuration: JironFormBuilder:Privacy:AllowImplicitCaptchaConsent | Durch die Konfiguration gesperrt: JironFormBuilder:Privacy:AllowImplicitCaptchaConsent |
| a saved reference to a secret (under its box) | Found in host configuration. · Not in host configuration yet: a developer adds JironFormBuilder:Secrets:Slack. | In der Host-Konfiguration gefunden. · Noch nicht in der Host-Konfiguration: Eine Entwicklerin oder ein Entwickler fügt JironFormBuilder:Secrets:Slack hinzu. |
| Settings › General, History (the group's sentence) | How long each form's History keeps its draft saves. Published and pinned versions, each form's current draft and the versions entries were sent with are always kept. | Wie lange der Verlauf jedes Formulars seine gespeicherten Entwürfe behält. Veröffentlichte und angeheftete Versionen, der aktuelle Entwurf jedes Formulars und die Versionen, mit denen Einträge gesendet wurden, bleiben immer erhalten. |
| Keep draft saves for … days · Gespeicherte Entwürfe behalten für … Tage | Every draft save stays at least this long. | Jeder gespeicherte Entwurf bleibt mindestens so lange erhalten. |
| Always keep the newest … draft saves · Immer behalten … neueste gespeicherte Entwürfe | However old they are, each form keeps this many of its newest draft saves. | Unabhängig von ihrem Alter behält jedes Formular so viele seiner neuesten gespeicherten Entwürfe. |
| a version compared with a draft that has changes not saved yet (its sidebar, under when and by whom) | Changes not saved yet aren't in this comparison. A restore saves them first, so they stay in History. | Noch nicht gespeicherte Änderungen sind in diesem Vergleich nicht enthalten. Beim Wiederherstellen werden sie zuerst gespeichert und bleiben so im Verlauf. |
| Review both versions (its sentence; the first alone where no choice is made yet) | For each difference, choose the version to keep, then save. Where only one of you changed something, that version is chosen already. | Wählen Sie für jeden Unterschied die Version, die bleiben soll, und speichern Sie dann. Wo nur einer von Ihnen etwas geändert hat, ist diese Version schon gewählt. |
| Review both versions, a difference both changed (a chip by its name) · without differences | Changed in both · The two versions don't differ. Saving keeps your draft as it is. | Von beiden geändert · Die beiden Versionen unterscheiden sich nicht. Beim Speichern bleibt Ihr Entwurf, wie er ist. |
| Review both versions, what a difference is about, and its rows | Field "Email" · Field "Email": place · Settings: Appearance · The form; In the form · Holds · Place: After "Name", First in "About you" | Feld „Email“ · Feld „Email“: Platz · Einstellungen: Darstellung · Das Formular; Im Formular · Enthält · Platz: Nach „Name“, An erster Stelle in „Über Sie“ |
| what Undo takes back after the review saved (said as "Undone: ...") | Merged both versions | Beide Versionen zusammengeführt |
| Restore an earlier version (its sidebar's sentence) | Choose a version to compare it with the current draft, then restore it from there. | Wählen Sie eine Version, um sie mit dem aktuellen Entwurf zu vergleichen, und stellen Sie sie dort wieder her. |
| Keep approved entries for | Approved entries are deleted this many days after approval. Entries on legal hold stay. | Genehmigte Einträge werden so viele Tage nach der Genehmigung gelöscht. Einträge mit Löschsperre bleiben. |
| Email verification | Visitors confirm their address through a link before any action runs. Use it for newsletters. | Wer das Formular sendet, bestätigt die eigene Adresse über einen Link, bevor eine Aktion ausgeführt wird. Sinnvoll für Newsletter. |
| Sensitive | Answers are masked in the backoffice and left out of emails and exports. | Antworten werden im Backoffice verdeckt und in E-Mails und Exporten weggelassen. |
| Fields that get a mark · Felder mit Markierung | Most forms have more required fields than optional ones, so marking the few optional ones reads calmer. | Die meisten Formulare haben mehr Pflichtfelder als optionale, deshalb wirkt es ruhiger, die wenigen optionalen zu markieren. |
| Use this rule · Diese Regel verwenden | Turn it off to keep the rule without using it. The field then always shows. | Schalten Sie sie aus, um die Regel zu behalten, ohne sie zu verwenden. Das Feld wird dann immer angezeigt. |
| Keep fetched options for · Abgerufene Optionen behalten für | Forms use the kept copy and fetch a new one after this time. If the address doesn't answer, the last copy stays in use. | Formulare verwenden die behaltene Kopie und rufen nach dieser Zeit eine neue ab. Antwortet die Adresse nicht, bleibt die letzte Kopie in Gebrauch. |
| How often (Email me a summary; the place is the last part of the time zone's name, and a site on UTC says "07:00 UTC") · Wie oft | It goes out at 07:00 Berlin time, the site's time zone, and covers the day or week before. | Die Zusammenfassung geht um 07:00 Uhr Ortszeit Berlin, der Zeitzone der Website, raus und umfasst den Tag oder die Woche davor. |
| the summary email's sidebar, under its preview (whose head names the sender and the subject; the address is said here once) | It holds counts and links, never answers, and only the forms you may view. It goes to your own address, jonas@example.com. | Enthalten sind Zahlen und Links, nie Antworten, und nur die Formulare, die Sie sehen dürfen. Die E-Mail geht an Ihre eigene Adresse, jonas@example.com. |
| the same place, for a user without an address (Send test email is left out) | Your user has no email address yet, so no summary can go out. | Ihr Benutzer hat noch keine E-Mail-Adresse, deshalb kann keine Zusammenfassung rausgehen. |
| New note · Neue Notiz | Visitors never see notes. Everyone who may open this entry can read them. | Wer das Formular sendet, sieht Notizen nie. Alle, die diesen Eintrag öffnen dürfen, können sie lesen. |
| Edit answers (one line above the fields) | Your changes are recorded, with the old values. | Ihre Änderungen werden mit den alten Werten protokolliert. |
| a revealed sensitive answer (under it, with Hide) | Shown to you and recorded in the audit log. | Nur Ihnen angezeigt und im Protokoll vermerkt. |
| To (Forward…) · An | A backoffice user, or one of the team addresses in Settings › Email. | Eine Person im Backoffice oder eine der Team-Adressen unter Einstellungen › E-Mail. |
| Reason (legal hold) · Grund | Shown with the hold, and recorded in the audit log with your name. | Wird mit der Löschsperre angezeigt und mit Ihrem Namen im Protokoll festgehalten. |
| Keep until (legal hold) · Behalten bis | At most a year from today. You can extend it before it ends. | Höchstens ein Jahr ab heute. Sie können sie vor dem Ende verlängern. |
| the legal hold sidebar, under its settings | Held entries are kept past their retention, and Erase… leaves them out until the hold ends. | Gesperrte Einträge bleiben über ihre Aufbewahrung hinaus erhalten, und „Daten löschen…“ lässt sie aus, bis die Sperre endet. |
| Preview with a sample entry (an outcome's sidebar) · Vorschau mit einem Beispieleintrag | As visitors see it after sending, with sample answers made from the form's fields. | So sehen Besucher es nach dem Absenden, mit Beispielantworten aus den Feldern des Formulars. |
| Preview with a sample entry (an email's sidebar) · Vorschau mit einem Beispieleintrag | As the team gets it, with sample answers made from the form's fields. | So bekommt das Team sie, mit Beispielantworten aus den Feldern des Formulars. |
| Preview with a sample entry (an email only a field's answer gets) · Vorschau mit einem Beispieleintrag | As the visitor gets it, with sample answers made from the form's fields. | So bekommt die Person, die das Formular sendet, die E-Mail, mit Beispielantworten aus den Feldern des Formulars. |
| An address (under Add recipient; said once on the sidebar, though Copy to's boxes take several the same way) · Eine Adresse | Several at once: separate them with commas. | Mehrere auf einmal: durch Kommas getrennt. |
| Addresses chosen by (each option's addresses) · Adressen je nach | The addresses each answer sends to, separated by commas. An answer without addresses adds no one. | Die Adressen, an die jede Antwort geht, durch Kommas getrennt. Eine Antwort ohne Adressen fügt niemanden hinzu. |
| What happens, for an action of the form · Was passiert | An action keeps what it does. To do something else, add another action. | Die Art einer Aktion lässt sich nicht ändern. Für etwas anderes fügen Sie eine weitere Aktion hinzu. |
| Name (an action) · Name | Shown on its card and in the deliveries log. | Steht auf ihrer Karte und im Zustellprotokoll. |
| Columns, on the Inbox or Spam not filtered to one form (in the fields' place) · Spalten | Filter the list by one form to choose that form's fields as columns. | Filtern Sie die Liste nach einem Formular, um dessen Felder als Spalten zu wählen. |
| Position in the list (an action) · Position in der Liste | Actions of the same trigger run in this order. | Aktionen desselben Auslösers laufen in dieser Reihenfolge. |
| Stop the rest if this fails · Die übrigen anhalten, wenn diese fehlschlägt | If it fails for good, the actions after it for the same entry don't run. Retrying it from Deliveries brings them back. | Schlägt sie endgültig fehl, laufen die Aktionen danach für denselben Eintrag nicht. Ein neuer Versuch unter Zustellungen bringt sie zurück. |
| Rule, without one (an action) · Regel | Runs for every entry its trigger brings. | Läuft für jeden Eintrag, den ihr Auslöser bringt. |
| Your questions (Paste questions) · Ihre Fragen | One question per line. Each becomes a field, with a type guessed from its words; you can change it afterwards. | Eine Frage pro Zeile. Jede wird ein Feld, mit einem aus ihren Wörtern erratenen Typ; Sie können ihn danach ändern. |
| Save as template, under its name and description, in the form's workspace while it has changes not saved yet · Save group as template | The template holds the form as last saved: changes not saved yet stay out. · The template holds the group as you see it, with the validation rules of its fields. Rules and validation rules that depend on fields outside the group are left out. | Die Vorlage enthält das Formular, wie es zuletzt gespeichert wurde: Ungespeicherte Änderungen bleiben außen vor. · Die Vorlage enthält die Gruppe, wie Sie sie sehen, mit den Prüfregeln ihrer Felder. Regeln und Prüfregeln, die von Feldern außerhalb der Gruppe abhängen, bleiben außen vor. |
| a saved group's preview (Library › Templates, in Use template's place) | A group: the designer's palette inserts it into any form, under Saved groups. | Eine Gruppe: Die Palette des Designers fügt sie unter „Gespeicherte Gruppen“ in jedes Formular ein. |
| Visitor texts (the Settings view, under More options) · Texte für Besucher | Your own words for the texts this form brings, such as its hints, notices and error messages. Texts you don't reword keep the site's words. | Eigene Worte für die Texte, die dieses Formular mitbringt, etwa seine Hinweise, Meldungen und Fehlermeldungen. Texte, die Sie nicht umformulieren, behalten die Worte der Website. |
| Visitor texts (Settings › General) · Texte für Besucher | Your own words for the texts every form brings, such as its hints, notices and error messages. A form's own words come first. | Eigene Worte für die Texte, die jedes Formular mitbringt, etwa seine Hinweise, Meldungen und Fehlermeldungen. Die eigenen Worte eines Formulars gehen vor. |

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
| the connection dropped (above the buttons) | **Your answers weren't sent** Check your connection, then send again. Your answers are kept. | **Ihre Antworten wurden nicht gesendet** Prüfen Sie Ihre Internetverbindung und senden Sie erneut. Ihre Antworten bleiben erhalten. |
| the server failed on a send (above the buttons) | **Your answers weren't sent** Something went wrong on our side. Send again in a moment. Your answers are kept. | **Ihre Antworten wurden nicht gesendet** Bei uns ist etwas schiefgegangen. Senden Sie gleich noch einmal. Ihre Antworten bleiben erhalten. |
| too many attempts (above the buttons) | **Your answers weren't sent** Too many attempts from your network. Your answers are kept; send again in 2 minutes. | **Ihre Antworten wurden nicht gesendet** Zu viele Versuche aus Ihrem Netzwerk. Ihre Antworten bleiben erhalten; senden Sie in 2 Minuten erneut. |
| the form was published again meanwhile (in the error summary's place) | **This form was updated while you filled it in** Your answers are kept. The agreement at the end has new wording, so it is no longer ticked: read it again before you send. | **Dieses Formular wurde geändert, während Sie es ausgefüllt haben** Ihre Antworten bleiben erhalten. Die Einwilligung am Ende hat einen neuen Wortlaut und ist deshalb nicht mehr angehakt: Lesen Sie sie vor dem Senden noch einmal. |
| the same, the defaults: no agreement changed, one, or several | **This form was updated while you filled it in** Your answers are kept. / ... An agreement has new wording, so it is no longer ticked: read it again before you send. / ... Some agreements have new wording, so they are no longer ticked: read them again before you send. | **Dieses Formular wurde geändert, während Sie es ausgefüllt haben** Ihre Antworten bleiben erhalten. / ... Eine Einwilligung hat einen neuen Wortlaut und ist deshalb nicht mehr angehakt: Lesen Sie sie vor dem Senden noch einmal. / ... Einige Einwilligungen haben einen neuen Wortlaut und sind deshalb nicht mehr angehakt: Lesen Sie sie vor dem Senden noch einmal. |
| a post without JavaScript the form could not take (in the error summary's place) | **Your answers weren't sent** Check your answers, then send again. Your answers are kept. | **Ihre Antworten wurden nicht gesendet** Prüfen Sie Ihre Antworten und senden Sie erneut. Ihre Antworten bleiben erhalten. |
| a post without JavaScript past the form's limit for files (in the error summary's place; nothing could be read) | **Your answers weren't sent** The files were too large to send, so your answers could not be kept. Choose smaller files and fill in the form again. | **Ihre Antworten wurden nicht gesendet** Die Dateien waren zu groß, um sie zu senden, deshalb konnten Ihre Antworten nicht erhalten bleiben. Wählen Sie kleinere Dateien und füllen Sie das Formular erneut aus. |
| a file already taken, without JavaScript (a ticked box above the file input; untick it to drop the file) | Keep cv.pdf (39.1 KB) | cv.pdf (39,1 KB) behalten |
| a redirect after the message, without JavaScript (no countdown) | the message, then [Continue] | die Nachricht, dann [Weiter] |
| a choice filled up meanwhile (an ordinary error) | Bike repair basics is now full. Choose another workshop | Bike repair basics ist jetzt ausgebucht. Wählen Sie einen anderen Workshop |
| an answer's button on the summary page (the label follows it for screen readers) | Change | Ändern |
| an unanswered question on the summary page | Not answered | Nicht beantwortet |
| a sensitive answer on the summary page, for screen readers (three bullets show) | Hidden for your privacy | Zum Schutz Ihrer Daten ausgeblendet |
| the copy of the answers after sending, the heading of a form of one untitled page | Your answers | Ihre Antworten |
| the copy of the answers, its print button | Print your answers | Ihre Antworten drucken |
| the copy of the answers, its link back to the form | Fill in the form again | Formular erneut ausfüllen |
| the page after sending seen again, in the copy's place | Your answers show only once, right after sending. | Ihre Antworten werden nur einmal angezeigt, direkt nach dem Senden. |
| the outcome, while the address is unconfirmed | **Check your inbox, Mira** We have sent you a link. Open it within 7 days to confirm your email address; then your place is booked. | **Sehen Sie in Ihrem Postfach nach, Mira** Wir haben Ihnen einen Link geschickt. Öffnen Sie ihn innerhalb von 7 Tagen, um Ihre E-Mail-Adresse zu bestätigen; dann ist Ihr Platz gebucht. |
| a redirect after the message (said once, not every second) | Taking you to the workshop programme in 8 seconds. [Go now] [Stay here] | In 8 Sekunden geht es weiter zum Workshop-Programm. [Jetzt weiter] [Hier bleiben] |
| the same, the default, which names no page | Taking you to the next page in 8 seconds. [Go now] [Stay here] | In 8 Sekunden geht es weiter zur nächsten Seite. [Jetzt weiter] [Hier bleiben] |
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
| the same, in the package's words, which a site or form may reword (`notices` › alreadySent) | **You've already sent this form** Each member can send it once. | **Sie haben dieses Formular bereits gesendet** Jedes Mitglied kann es einmal senden. |
| the signature's Clear, until the next stroke | Undo clear | Löschen rückgängig machen |
| a counter as the text changes | 453 characters left · 1 character left · 16 characters too many · 1 character too many | Noch 453 Zeichen · 16 Zeichen zu viel |
| said when a ranking's option moves, or a row goes | Bread moved to place 1. · Guest 2 removed. | Bread auf Platz 1 verschoben. · Gast 2 entfernt. |
| the signature's pad (the line above it, what a screen reader calls it, its button) | Sign with your finger or the mouse, or type your name instead. · Your signature, drawn · Clear | Unterschreiben Sie mit dem Finger oder der Maus, oder geben Sie stattdessen Ihren Namen ein. · Ihre Unterschrift, gezeichnet · Löschen |
| the way between the pad and the typed name | Type your name instead · Sign by hand instead | Stattdessen Namen eingeben · Stattdessen von Hand unterschreiben |
| an upload that stopped (in the file's row) | The upload stopped before it finished. [Try again] | Das Hochladen wurde unterbrochen. [Erneut versuchen] |
| a file field, with the runtime (one file or several) | Drag a file here, or [Choose a file] · Drag files here, or [Choose files] · [Take a photo] | Datei hierher ziehen oder [Datei auswählen] · Dateien hierher ziehen oder [Dateien auswählen] · [Foto aufnehmen] |
| a file on its way (in its row, and said once it is there) | 60% uploaded · cv.pdf is uploaded. | 60 % hochgeladen · cv.pdf ist hochgeladen. |
| a file's button, for screen readers | Stop uploading cv.pdf · Remove cv.pdf | Hochladen von cv.pdf abbrechen · cv.pdf entfernen |
| the site cannot take a file now (in its row) | This form can't take files right now. Try again later. Your answers are kept. | Dieses Formular kann gerade keine Dateien annehmen. Versuchen Sie es später noch einmal. Ihre Antworten bleiben erhalten. |
| the honeypot's label, out of view and out of the accessibility tree, for a screen reader that reaches it anyway (ADR-0012) | Leave this field empty | Lassen Sie dieses Feld leer |

### The default error messages

What a visitor sees for an error the editor wrote no message for (ADR-0099): a field's own message for the kind, a
check's or a validation rule's comes first. Both runtimes use these words, held to one text by
`tests/vectors/messages.json`; the other five languages follow this table. A value in braces prints in the culture's
format; where a count changes the words, the singular comes first, then the plural, after a ·. Under the field a
message stands alone, but for a grid's row, which it names first ("Morning: Choose an answer in this row"); in the
summary a default follows the field's label, and a row's legend or a grid's row after it in brackets; a label that
already ends in a question mark, an exclamation mark, a full stop or a colon takes no colon after it ("Where would you
like to sit? Choose an answer", ADR-0106); a label made only of spaces counts as none, so the message
stands alone, and a legend made only of spaces is left out (ADR-0109).

| Case | English | German |
| --- | --- | --- |
| the word before the last of a list | or | oder |
| a size below a megabyte, 1,024 based, one place at most | {size} KB | {size} KB |
| a size from a megabyte | {size} MB | {size} MB |
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
| `required`: a ranking | Put the options in order | Bringen Sie die Optionen in die gewünschte Reihenfolge |
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
| `invalid`: a well-formed email address at a domain the field does not take | Use an email address at another domain | Verwenden Sie eine E-Mail-Adresse mit einer anderen Domain |
| `invalid`: a phone number | Enter a phone number with digits, and a + for the country code | Geben Sie eine Telefonnummer in Ziffern ein, mit + vor der Landesvorwahl |
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
| `step`: the minutes of a time or a date and time | Choose a time in steps of {minutes} minute · Choose a time in steps of {minutes} minutes | Wählen Sie eine Uhrzeit in Schritten von {minutes} Minute · Wählen Sie eine Uhrzeit in Schritten von {minutes} Minuten |
| `step`: the minutes of a time from its earliest | Choose a time in steps of {minutes} minute from {min} · Choose a time in steps of {minutes} minutes from {min} | Wählen Sie eine Uhrzeit in Schritten von {minutes} Minute ab {min} · Wählen Sie eine Uhrzeit in Schritten von {minutes} Minuten ab {min} |
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
| `full`: several options | One of your choices is now full. Choose another option. | Eine der von Ihnen gewählten Optionen ist jetzt ausgebucht. Wählen Sie eine andere Option. |
| an option's places left, in its label (ADR-0156) | {count} place left · {count} places left | {count} Platz frei · {count} Plätze frei |
| an option without a place left, in its label | Full | Ausgebucht |
| a third party's check before it may load (ADR-0157) | This form uses a spam check from an outside service, which loads only if you agree. | Dieses Formular nutzt eine Spam-Prüfung eines externen Dienstes, die nur geladen wird, wenn Sie zustimmen. |
| the button that loads the check | Load the check | Prüfung laden |
| the check, when a consent manager decides | This form uses a spam check from an outside service, which loads once you agree in the cookie settings. | Dieses Formular nutzt eine Spam-Prüfung eines externen Dienstes, die geladen wird, sobald Sie in den Cookie-Einstellungen zustimmen. |
| the check, when a cookie category decides, by its name | This form uses a spam check from an outside service, which loads once you agree to {category} cookies. | Dieses Formular nutzt eine Spam-Prüfung eines externen Dienstes, die geladen wird, sobald Sie {category}-Cookies zustimmen. |
| the link to the cookie settings | Change the cookie settings | Cookie-Einstellungen ändern |
| a send before the check is done | Complete the check before you send the form. | Schließen Sie die Prüfung ab, bevor Sie das Formular senden. |
| a check that could not load | The spam check could not load. You can still send the form. | Die Spam-Prüfung konnte nicht geladen werden. Sie können das Formular trotzdem senden. |
| a slow device while it solves the puzzle | Checking your browser, which can take a moment. | Ihr Browser wird geprüft, das kann einen Moment dauern. |
| `confirmation` | Enter the same email address twice | Geben Sie zweimal dieselbe E-Mail-Adresse ein |
| `digits` | Enter a number with fewer digits | Geben Sie eine Zahl mit weniger Ziffern ein |
| `rowCount`: too few rows | Add at least {min} row · Add at least {min} rows | Fügen Sie mindestens {min} Zeile hinzu · Fügen Sie mindestens {min} Zeilen hinzu |
| `rowCount`: too many rows | Remove rows to keep at most {max} | Entfernen Sie Zeilen, sodass höchstens {max} bleibt · Entfernen Sie Zeilen, sodass höchstens {max} bleiben |
| `check`: a pattern without a message | Enter the answer in the format asked for | Geben Sie die Antwort im vorgegebenen Format ein |
| `check`: the same as another field, named by its label | Enter the same as for {field} | Geben Sie dasselbe ein wie bei {field} |
| `check`: unique per form | This answer has already been given. Enter another one. | Diese Antwort wurde bereits gegeben. Geben Sie eine andere ein. |
| a submission without an answer, in the summary | Answer at least one question before you send the form | Beantworten Sie mindestens eine Frage, bevor Sie das Formular senden |
| the error summary's heading, by the number of its links | This answer needs a change · These answers need a change | Diese Antwort muss noch geändert werden · Diese Antworten müssen noch geändert werden |
| a summary link to a default | {label}: {message} | {label}: {message} |
| a summary link to a default after a label that ends in a question mark, an exclamation mark, a full stop or a colon | {label} {message} | {label} {message} |
| a summary link to a default in a repeatable group's row | {label} ({row}): {message} | {label} ({row}): {message} |

### The default error messages in the other five languages

The same messages in French, Italian, Dutch, Spanish and Danish (ADR-0230), row for row as above. French and Spanish
address the visitor as "vous" and "usted", Dutch as "u" where a pronoun is needed, Italian and Danish in the informal
imperative their forms use; French puts a no-break space before a colon and writes sizes in Ko and Mo. A count
takes its singular for 1 and its plural otherwise in every language: a message names a count only where it is at
least 1. `tests/vectors/tables/message-words.json` holds both runtimes to every word.

#### French

| Case | French |
| --- | --- |
| the word before the last of a list | ou |
| a size below a megabyte, 1,024 based, one place at most | {size} Ko |
| a size from a megabyte | {size} Mo |
| `required`: a text or a text area | Saisissez une réponse |
| `required`: an email address | Saisissez une adresse e-mail |
| `required`: a phone number | Saisissez un numéro de téléphone |
| `required`: a web address | Saisissez une adresse web |
| `required`: a number | Saisissez un nombre |
| `required`: a date | Saisissez une date |
| `required`: a time | Saisissez une heure |
| `required`: a date and a time | Saisissez une date et une heure |
| `required`: a rating | Choisissez une note |
| `required`: a scale | Choisissez un point sur l’échelle |
| `required`: a slider | Choisissez une valeur |
| `required`: radio buttons, a dropdown of one | Choisissez une réponse |
| `required`: checkboxes, a dropdown of several | Choisissez au moins une réponse |
| `required`: a grid | Choisissez une réponse dans chaque ligne |
| `required`: one of a grid's rows | Choisissez une réponse dans cette ligne |
| `required`: a ranking | Classez les options dans l’ordre |
| `required`: a checkbox | Cochez la case |
| `required`: yes or no, in the field's own words for them | Choisissez {yes} ou {no} |
| `required`: a consent | Cochez la case pour donner votre accord |
| `required`: a country | Choisissez un pays |
| `required`: a name | Saisissez le nom complet |
| `required`: an address | Saisissez l’adresse complète |
| `required`: a file upload | Choisissez un fichier |
| `required`: a signature | Signez, ou saisissez plutôt votre nom |
| `required`: a site's own type | Répondez à cette question |
| `invalid`: an email address | Saisissez une adresse e-mail complète, par exemple nom@example.com |
| `invalid`: a well-formed email address at a domain the field does not take | Utilisez une adresse e-mail d’un autre domaine |
| `invalid`: a phone number | Saisissez un numéro de téléphone en chiffres, avec un + pour l’indicatif du pays |
| `invalid`: a web address | Saisissez une adresse web complète, par exemple https://example.com |
| `invalid`: a number; the example is 12, or 12.5 with decimals, in the culture's format | Saisissez un nombre, par exemple {example} |
| `invalid`: a date | Saisissez une date valide |
| `invalid`: a time | Saisissez une heure valide |
| `invalid`: a date and a time | Saisissez une date et une heure valides |
| `invalid`: a rating | Choisissez une note de 1 à {count} |
| `invalid`: a scale | Choisissez un point de {from} à {to} |
| `invalid`, `min`, `max`: a slider | Choisissez une valeur de {min} à {max} |
| `invalid`: a choice, a grid or a ranking | Choisissez l’une des options proposées |
| `invalid`: a country | Choisissez un pays dans la liste |
| `invalid`: a name | Vérifiez le nom |
| `invalid`: an address | Vérifiez l’adresse |
| `invalid`: a signature | Signez à nouveau, ou saisissez plutôt votre nom |
| `invalid` of a site's own type, a validation rule without a message, a kind without words of its own | Vérifiez cette réponse |
| `minLength` | Saisissez au moins {min} caractère · Saisissez au moins {min} caractères |
| `maxLength` | Saisissez au plus {max} caractère · Saisissez au plus {max} caractères |
| `minWords` | Saisissez au moins {min} mot · Saisissez au moins {min} mots |
| `maxWords` | Saisissez au plus {max} mot · Saisissez au plus {max} mots |
| `min`, `max`: a number with both limits | Saisissez un nombre de {min} à {max} |
| `min`, `max`: a number with a least only | Saisissez un nombre supérieur ou égal à {min} |
| `min`, `max`: a number with a most only | Saisissez un nombre inférieur ou égal à {max} |
| `min`, `max`: a time with both limits | Saisissez une heure entre {min} et {max} |
| `min`, `max`: a time with an earliest only | Saisissez une heure à partir de {min} |
| `min`, `max`: a time with a latest only | Saisissez une heure jusqu’à {max} |
| `step`: a number's step | Saisissez un nombre par pas de {step} |
| `step`: a number's step from its least | Saisissez un nombre par pas de {step}, à partir de {min} |
| `step`: a number without decimals | Saisissez un nombre entier |
| `step`: a number's decimal places | Saisissez un nombre avec au plus {places} décimale · Saisissez un nombre avec au plus {places} décimales |
| `step`: a slider | Choisissez une valeur par pas de {step} |
| `step`: the minutes of a time or a date and time | Choisissez une heure par intervalles de {minutes} minute · Choisissez une heure par intervalles de {minutes} minutes |
| `step`: the minutes of a time from its earliest | Choisissez une heure par intervalles de {minutes} minute à partir de {min} · Choisissez une heure par intervalles de {minutes} minutes à partir de {min} |
| `dateWindow`: a window with both ends | Choisissez une date du {min} au {max} |
| `dateWindow`: a window with a start only | Choisissez une date à partir du {min} |
| `dateWindow`: a window with an end only | Choisissez une date jusqu’au {max} |
| `dateWindow`: weekdays only, with both ends | Choisissez une date du {min} au {max}, du {monday} au {friday} |
| `dateWindow`: weekdays only, with a start only | Choisissez une date à partir du {min}, du {monday} au {friday} |
| `dateWindow`: weekdays only, with an end only | Choisissez une date jusqu’au {max}, du {monday} au {friday} |
| `dateWindow`: weekdays only, without ends | Choisissez un jour du {monday} au {friday} |
| `dateWindow`: a date the window takes that the field excludes | Cette date n’est pas disponible. Choisissez-en une autre. |
| `selections`: as many as the fewest and the most | Choisissez {count} réponse · Choisissez {count} réponses |
| `selections`: the fewest and the most | Choisissez de {min} à {max} réponses |
| `selections`: the fewest only | Choisissez au moins {min} réponse · Choisissez au moins {min} réponses |
| `selections`: the most only | Choisissez au plus {max} réponse · Choisissez au plus {max} réponses |
| `fileType`: the formats, joined with commas and "or" | Choisissez un fichier au format {formats} |
| `fileSize`: a field without a limit of its own | Ce fichier est trop volumineux. Choisissez-en un plus petit. |
| `fileSize`: one file | Ce fichier dépasse {size}. Choisissez-en un plus petit. |
| `fileSize`: several files | L’un des fichiers dépasse {size}. Choisissez-en un plus petit. |
| `fileCount` | Choisissez au plus {max} fichier · Choisissez au plus {max} fichiers |
| `full`: one option, by its label | Il n’y a plus de place pour {option}. Choisissez une autre option. |
| `full`: several options | Il n’y a plus de place pour l’une de vos options. Choisissez-en une autre. |
| an option's places left, in its label (ADR-0156) | {count} place restante · {count} places restantes |
| an option without a place left, in its label | Complet |
| a third party's check before it may load (ADR-0157) | Ce formulaire utilise une vérification anti-spam fournie par un service externe. Elle ne se charge qu’avec votre accord. |
| the button that loads the check | Charger la vérification |
| the check, when a consent manager decides | Ce formulaire utilise une vérification anti-spam fournie par un service externe. Elle se charge dès que vous donnez votre accord dans les paramètres des cookies. |
| the check, when a cookie category decides, by its name | Ce formulaire utilise une vérification anti-spam fournie par un service externe. Elle se charge dès que vous acceptez les cookies {category}. |
| the link to the cookie settings | Modifier les paramètres des cookies |
| a send before the check is done | Effectuez la vérification avant d’envoyer le formulaire. |
| a check that could not load | La vérification anti-spam n’a pas pu se charger. Vous pouvez tout de même envoyer le formulaire. |
| a slow device while it solves the puzzle | Vérification de votre navigateur en cours. Cela peut prendre un instant. |
| `confirmation` | Saisissez deux fois la même adresse e-mail |
| `digits` | Saisissez un nombre comportant moins de chiffres |
| `rowCount`: too few rows | Ajoutez au moins {min} ligne · Ajoutez au moins {min} lignes |
| `rowCount`: too many rows | Supprimez des lignes pour en garder au plus {max} |
| `check`: a pattern without a message | Saisissez la réponse au format demandé |
| `check`: the same as another field, named by its label | Saisissez la même réponse que pour {field} |
| `check`: unique per form | Cette réponse a déjà été donnée. Saisissez-en une autre. |
| a submission without an answer, in the summary | Répondez à au moins une question avant d’envoyer le formulaire |
| the error summary's heading, by the number of its links | Cette réponse doit être modifiée · Ces réponses doivent être modifiées |
| a summary link to a default | {label} : {message} |
| a summary link to a default after a label that ends in a question mark, an exclamation mark, a full stop or a colon | {label} {message} |
| a summary link to a default in a repeatable group's row | {label} ({row}) : {message} |

#### Italian

| Case | Italian |
| --- | --- |
| the word before the last of a list | o |
| a size below a megabyte, 1,024 based, one place at most | {size} KB |
| a size from a megabyte | {size} MB |
| `required`: a text or a text area | Inserisci una risposta |
| `required`: an email address | Inserisci un indirizzo email |
| `required`: a phone number | Inserisci un numero di telefono |
| `required`: a web address | Inserisci un indirizzo web |
| `required`: a number | Inserisci un numero |
| `required`: a date | Inserisci una data |
| `required`: a time | Inserisci un orario |
| `required`: a date and a time | Inserisci una data e un orario |
| `required`: a rating | Scegli una valutazione |
| `required`: a scale | Scegli un punto della scala |
| `required`: a slider | Scegli un valore |
| `required`: radio buttons, a dropdown of one | Scegli una risposta |
| `required`: checkboxes, a dropdown of several | Scegli almeno una risposta |
| `required`: a grid | Scegli una risposta in ogni riga |
| `required`: one of a grid's rows | Scegli una risposta in questa riga |
| `required`: a ranking | Metti le opzioni in ordine |
| `required`: a checkbox | Seleziona la casella |
| `required`: yes or no, in the field's own words for them | Scegli {yes} o {no} |
| `required`: a consent | Seleziona la casella per dare il tuo consenso |
| `required`: a country | Scegli un paese |
| `required`: a name | Inserisci il nome completo |
| `required`: an address | Inserisci l’indirizzo completo |
| `required`: a file upload | Scegli un file |
| `required`: a signature | Aggiungi la tua firma, oppure scrivi il tuo nome al suo posto |
| `required`: a site's own type | Rispondi a questa domanda |
| `invalid`: an email address | Inserisci un indirizzo email completo, ad esempio nome@example.com |
| `invalid`: a well-formed email address at a domain the field does not take | Usa un indirizzo email di un altro dominio |
| `invalid`: a phone number | Inserisci un numero di telefono in cifre, con un + per il prefisso internazionale |
| `invalid`: a web address | Inserisci un indirizzo web completo, ad esempio https://example.com |
| `invalid`: a number; the example is 12, or 12.5 with decimals, in the culture's format | Inserisci un numero, ad esempio {example} |
| `invalid`: a date | Inserisci una data valida |
| `invalid`: a time | Inserisci un orario valido |
| `invalid`: a date and a time | Inserisci una data e un orario validi |
| `invalid`: a rating | Scegli una valutazione da 1 a {count} |
| `invalid`: a scale | Scegli un punto da {from} a {to} |
| `invalid`, `min`, `max`: a slider | Scegli un valore da {min} a {max} |
| `invalid`: a choice, a grid or a ranking | Scegli una delle opzioni proposte |
| `invalid`: a country | Scegli un paese dall’elenco |
| `invalid`: a name | Controlla il nome |
| `invalid`: an address | Controlla l’indirizzo |
| `invalid`: a signature | Firma di nuovo, oppure scrivi il tuo nome al suo posto |
| `invalid` of a site's own type, a validation rule without a message, a kind without words of its own | Controlla questa risposta |
| `minLength` | Inserisci almeno {min} carattere · Inserisci almeno {min} caratteri |
| `maxLength` | Inserisci al massimo {max} carattere · Inserisci al massimo {max} caratteri |
| `minWords` | Inserisci almeno {min} parola · Inserisci almeno {min} parole |
| `maxWords` | Inserisci al massimo {max} parola · Inserisci al massimo {max} parole |
| `min`, `max`: a number with both limits | Inserisci un numero da {min} a {max} |
| `min`, `max`: a number with a least only | Inserisci un numero uguale o superiore a {min} |
| `min`, `max`: a number with a most only | Inserisci un numero uguale o inferiore a {max} |
| `min`, `max`: a time with both limits | Inserisci un orario tra le {min} e le {max} |
| `min`, `max`: a time with an earliest only | Inserisci un orario dalle {min} in poi |
| `min`, `max`: a time with a latest only | Inserisci un orario fino alle {max} |
| `step`: a number's step | Inserisci un numero a intervalli di {step} |
| `step`: a number's step from its least | Inserisci un numero a intervalli di {step}, a partire da {min} |
| `step`: a number without decimals | Inserisci un numero intero |
| `step`: a number's decimal places | Inserisci un numero con al massimo {places} cifra decimale · Inserisci un numero con al massimo {places} cifre decimali |
| `step`: a slider | Scegli un valore a intervalli di {step} |
| `step`: the minutes of a time or a date and time | Scegli un orario a intervalli di {minutes} minuto · Scegli un orario a intervalli di {minutes} minuti |
| `step`: the minutes of a time from its earliest | Scegli un orario a intervalli di {minutes} minuto dalle {min} · Scegli un orario a intervalli di {minutes} minuti dalle {min} |
| `dateWindow`: a window with both ends | Scegli una data dal {min} al {max} |
| `dateWindow`: a window with a start only | Scegli una data dal {min} in poi |
| `dateWindow`: a window with an end only | Scegli una data fino al {max} |
| `dateWindow`: weekdays only, with both ends | Scegli una data dal {min} al {max}, da {monday} a {friday} |
| `dateWindow`: weekdays only, with a start only | Scegli una data dal {min} in poi, da {monday} a {friday} |
| `dateWindow`: weekdays only, with an end only | Scegli una data fino al {max}, da {monday} a {friday} |
| `dateWindow`: weekdays only, without ends | Scegli un giorno da {monday} a {friday} |
| `dateWindow`: a date the window takes that the field excludes | Questa data non è disponibile. Scegline un’altra. |
| `selections`: as many as the fewest and the most | Scegli {count} risposta · Scegli {count} risposte |
| `selections`: the fewest and the most | Scegli da {min} a {max} risposte |
| `selections`: the fewest only | Scegli almeno {min} risposta · Scegli almeno {min} risposte |
| `selections`: the most only | Scegli al massimo {max} risposta · Scegli al massimo {max} risposte |
| `fileType`: the formats, joined with commas and "or" | Scegli un file in formato {formats} |
| `fileSize`: a field without a limit of its own | Questo file è troppo grande. Scegline uno più piccolo. |
| `fileSize`: one file | Questo file supera il limite di {size}. Scegline uno più piccolo. |
| `fileSize`: several files | Uno dei file supera il limite di {size}. Scegline uno più piccolo. |
| `fileCount` | Scegli al massimo {max} file |
| `full`: one option, by its label | Non ci sono più posti per {option}. Scegli un’altra opzione. |
| `full`: several options | Non ci sono più posti per una delle opzioni scelte. Scegli un’altra opzione. |
| an option's places left, in its label (ADR-0156) | {count} posto disponibile · {count} posti disponibili |
| an option without a place left, in its label | Posti esauriti |
| a third party's check before it may load (ADR-0157) | Questo modulo usa un controllo antispam di un servizio esterno, che si carica solo se acconsenti. |
| the button that loads the check | Carica il controllo |
| the check, when a consent manager decides | Questo modulo usa un controllo antispam di un servizio esterno, che si carica quando acconsenti nelle impostazioni dei cookie. |
| the check, when a cookie category decides, by its name | Questo modulo usa un controllo antispam di un servizio esterno, che si carica quando accetti i cookie di {category}. |
| the link to the cookie settings | Modifica le impostazioni dei cookie |
| a send before the check is done | Completa il controllo prima di inviare il modulo. |
| a check that could not load | Il controllo antispam non si è caricato. Puoi comunque inviare il modulo. |
| a slow device while it solves the puzzle | Stiamo verificando il tuo browser, potrebbe volerci un momento. |
| `confirmation` | Inserisci due volte lo stesso indirizzo email |
| `digits` | Inserisci un numero con meno cifre |
| `rowCount`: too few rows | Aggiungi almeno {min} riga · Aggiungi almeno {min} righe |
| `rowCount`: too many rows | Rimuovi delle righe per tenerne al massimo {max} |
| `check`: a pattern without a message | Inserisci la risposta nel formato richiesto |
| `check`: the same as another field, named by its label | Inserisci lo stesso valore di {field} |
| `check`: unique per form | Questa risposta è già stata data. Inseriscine un’altra. |
| a submission without an answer, in the summary | Rispondi ad almeno una domanda prima di inviare il modulo |
| the error summary's heading, by the number of its links | Questa risposta va corretta · Queste risposte vanno corrette |
| a summary link to a default | {label}: {message} |
| a summary link to a default after a label that ends in a question mark, an exclamation mark, a full stop or a colon | {label} {message} |
| a summary link to a default in a repeatable group's row | {label} ({row}): {message} |

#### Dutch

| Case | Dutch |
| --- | --- |
| the word before the last of a list | of |
| a size below a megabyte, 1,024 based, one place at most | {size} KB |
| a size from a megabyte | {size} MB |
| `required`: a text or a text area | Vul een antwoord in |
| `required`: an email address | Vul een e-mailadres in |
| `required`: a phone number | Vul een telefoonnummer in |
| `required`: a web address | Vul een webadres in |
| `required`: a number | Vul een getal in |
| `required`: a date | Vul een datum in |
| `required`: a time | Vul een tijd in |
| `required`: a date and a time | Vul een datum en een tijd in |
| `required`: a rating | Kies een beoordeling |
| `required`: a scale | Kies een punt op de schaal |
| `required`: a slider | Kies een waarde |
| `required`: radio buttons, a dropdown of one | Kies een antwoord |
| `required`: checkboxes, a dropdown of several | Kies minstens één antwoord |
| `required`: a grid | Kies in elke rij een antwoord |
| `required`: one of a grid's rows | Kies in deze rij een antwoord |
| `required`: a ranking | Zet de opties in volgorde |
| `required`: a checkbox | Vink het vakje aan |
| `required`: yes or no, in the field's own words for them | Kies {yes} of {no} |
| `required`: a consent | Vink het vakje aan om akkoord te gaan |
| `required`: a country | Kies een land |
| `required`: a name | Vul de volledige naam in |
| `required`: an address | Vul het volledige adres in |
| `required`: a file upload | Kies een bestand |
| `required`: a signature | Zet uw handtekening, of typ in plaats daarvan uw naam |
| `required`: a site's own type | Beantwoord deze vraag |
| `invalid`: an email address | Vul een volledig e-mailadres in, bijvoorbeeld naam@example.com |
| `invalid`: a well-formed email address at a domain the field does not take | Gebruik een e-mailadres met een ander domein |
| `invalid`: a phone number | Vul een telefoonnummer in met cijfers en een + voor de landcode |
| `invalid`: a web address | Vul een volledig webadres in, bijvoorbeeld https://example.com |
| `invalid`: a number; the example is 12, or 12.5 with decimals, in the culture's format | Vul een getal in, bijvoorbeeld {example} |
| `invalid`: a date | Vul een geldige datum in |
| `invalid`: a time | Vul een geldige tijd in |
| `invalid`: a date and a time | Vul een geldige datum en tijd in |
| `invalid`: a rating | Kies een beoordeling van 1 tot en met {count} |
| `invalid`: a scale | Kies een punt van {from} tot en met {to} |
| `invalid`, `min`, `max`: a slider | Kies een waarde van {min} tot en met {max} |
| `invalid`: a choice, a grid or a ranking | Kies een van de aangeboden opties |
| `invalid`: a country | Kies een land uit de lijst |
| `invalid`: a name | Controleer de naam |
| `invalid`: an address | Controleer het adres |
| `invalid`: a signature | Zet uw handtekening opnieuw, of typ in plaats daarvan uw naam |
| `invalid` of a site's own type, a validation rule without a message, a kind without words of its own | Controleer dit antwoord |
| `minLength` | Vul minstens {min} teken in · Vul minstens {min} tekens in |
| `maxLength` | Vul maximaal {max} teken in · Vul maximaal {max} tekens in |
| `minWords` | Vul minstens {min} woord in · Vul minstens {min} woorden in |
| `maxWords` | Vul maximaal {max} woord in · Vul maximaal {max} woorden in |
| `min`, `max`: a number with both limits | Vul een getal van {min} tot en met {max} in |
| `min`, `max`: a number with a least only | Vul een getal van {min} of hoger in |
| `min`, `max`: a number with a most only | Vul een getal van {max} of lager in |
| `min`, `max`: a time with both limits | Vul een tijd van {min} tot en met {max} in |
| `min`, `max`: a time with an earliest only | Vul een tijd van {min} of later in |
| `min`, `max`: a time with a latest only | Vul een tijd van {max} of eerder in |
| `step`: a number's step | Vul een getal in stappen van {step} in |
| `step`: a number's step from its least | Vul een getal in stappen van {step} in, vanaf {min} |
| `step`: a number without decimals | Vul een geheel getal in |
| `step`: a number's decimal places | Vul een getal in met maximaal {places} decimaal · Vul een getal in met maximaal {places} decimalen |
| `step`: a slider | Kies een waarde in stappen van {step} |
| `step`: the minutes of a time or a date and time | Kies een tijd in stappen van {minutes} minuut · Kies een tijd in stappen van {minutes} minuten |
| `step`: the minutes of a time from its earliest | Kies een tijd in stappen van {minutes} minuut vanaf {min} · Kies een tijd in stappen van {minutes} minuten vanaf {min} |
| `dateWindow`: a window with both ends | Kies een datum van {min} tot en met {max} |
| `dateWindow`: a window with a start only | Kies een datum op of na {min} |
| `dateWindow`: a window with an end only | Kies een datum op of vóór {max} |
| `dateWindow`: weekdays only, with both ends | Kies een datum van {min} tot en met {max}, {monday} tot en met {friday} |
| `dateWindow`: weekdays only, with a start only | Kies een datum op of na {min}, {monday} tot en met {friday} |
| `dateWindow`: weekdays only, with an end only | Kies een datum op of vóór {max}, {monday} tot en met {friday} |
| `dateWindow`: weekdays only, without ends | Kies een dag van {monday} tot en met {friday} |
| `dateWindow`: a date the window takes that the field excludes | Deze datum is niet beschikbaar. Kies een andere. |
| `selections`: as many as the fewest and the most | Kies {count} antwoord · Kies {count} antwoorden |
| `selections`: the fewest and the most | Kies {min} tot en met {max} antwoorden |
| `selections`: the fewest only | Kies minstens {min} antwoord · Kies minstens {min} antwoorden |
| `selections`: the most only | Kies maximaal {max} antwoord · Kies maximaal {max} antwoorden |
| `fileType`: the formats, joined with commas and "or" | Kies een bestand van het type {formats} |
| `fileSize`: a field without a limit of its own | Dit bestand is te groot. Kies een kleiner bestand. |
| `fileSize`: one file | Dit bestand is groter dan {size}. Kies een kleiner bestand. |
| `fileSize`: several files | Een van de bestanden is groter dan {size}. Kies een kleiner bestand. |
| `fileCount` | Kies maximaal {max} bestand · Kies maximaal {max} bestanden |
| `full`: one option, by its label | {option} is nu vol. Kies een andere optie. |
| `full`: several options | Een van uw gekozen opties is nu vol. Kies een andere optie. |
| an option's places left, in its label (ADR-0156) | Nog {count} plek vrij · Nog {count} plekken vrij |
| an option without a place left, in its label | Vol |
| a third party's check before it may load (ADR-0157) | Dit formulier gebruikt een spamcontrole van een externe dienst, die alleen wordt geladen als u daarmee akkoord gaat. |
| the button that loads the check | Controle laden |
| the check, when a consent manager decides | Dit formulier gebruikt een spamcontrole van een externe dienst, die wordt geladen zodra u akkoord gaat in de cookie-instellingen. |
| the check, when a cookie category decides, by its name | Dit formulier gebruikt een spamcontrole van een externe dienst, die wordt geladen zodra u {category}cookies accepteert. |
| the link to the cookie settings | Cookie-instellingen wijzigen |
| a send before the check is done | Voer de controle uit voordat u het formulier verstuurt. |
| a check that could not load | De spamcontrole kon niet worden geladen. U kunt het formulier toch versturen. |
| a slow device while it solves the puzzle | Uw browser wordt gecontroleerd. Dit kan even duren. |
| `confirmation` | Vul twee keer hetzelfde e-mailadres in |
| `digits` | Vul een getal met minder cijfers in |
| `rowCount`: too few rows | Voeg minstens {min} rij toe · Voeg minstens {min} rijen toe |
| `rowCount`: too many rows | Verwijder rijen zodat er maximaal {max} overblijft · Verwijder rijen zodat er maximaal {max} overblijven |
| `check`: a pattern without a message | Vul het antwoord in de gevraagde vorm in |
| `check`: the same as another field, named by its label | Vul hetzelfde in als bij {field} |
| `check`: unique per form | Dit antwoord is al gegeven. Vul een ander antwoord in. |
| a submission without an answer, in the summary | Beantwoord minstens één vraag voordat u het formulier verstuurt |
| the error summary's heading, by the number of its links | Dit antwoord moet nog worden aangepast · Deze antwoorden moeten nog worden aangepast |
| a summary link to a default | {label}: {message} |
| a summary link to a default after a label that ends in a question mark, an exclamation mark, a full stop or a colon | {label} {message} |
| a summary link to a default in a repeatable group's row | {label} ({row}): {message} |

#### Spanish

| Case | Spanish |
| --- | --- |
| the word before the last of a list | o |
| a size below a megabyte, 1,024 based, one place at most | {size} KB |
| a size from a megabyte | {size} MB |
| `required`: a text or a text area | Escriba una respuesta |
| `required`: an email address | Escriba una dirección de correo electrónico |
| `required`: a phone number | Escriba un número de teléfono |
| `required`: a web address | Escriba una dirección web |
| `required`: a number | Escriba un número |
| `required`: a date | Escriba una fecha |
| `required`: a time | Escriba una hora |
| `required`: a date and a time | Escriba una fecha y una hora |
| `required`: a rating | Elija una valoración |
| `required`: a scale | Elija un punto de la escala |
| `required`: a slider | Elija un valor |
| `required`: radio buttons, a dropdown of one | Elija una respuesta |
| `required`: checkboxes, a dropdown of several | Elija al menos una respuesta |
| `required`: a grid | Elija una respuesta en cada fila |
| `required`: one of a grid's rows | Elija una respuesta en esta fila |
| `required`: a ranking | Ordene las opciones |
| `required`: a checkbox | Marque la casilla |
| `required`: yes or no, in the field's own words for them | Elija {yes} o {no} |
| `required`: a consent | Marque la casilla para aceptar |
| `required`: a country | Elija un país |
| `required`: a name | Escriba el nombre completo |
| `required`: an address | Escriba la dirección completa |
| `required`: a file upload | Elija un archivo |
| `required`: a signature | Añada su firma o escriba su nombre en su lugar |
| `required`: a site's own type | Responda a esta pregunta |
| `invalid`: an email address | Escriba una dirección de correo electrónico completa (por ejemplo, nombre@example.com) |
| `invalid`: a well-formed email address at a domain the field does not take | Use una dirección de correo electrónico de otro dominio |
| `invalid`: a phone number | Escriba un número de teléfono con cifras y un + delante del prefijo del país |
| `invalid`: a web address | Escriba una dirección web completa (por ejemplo, https://example.com) |
| `invalid`: a number; the example is 12, or 12.5 with decimals, in the culture's format | Escriba un número (por ejemplo, {example}) |
| `invalid`: a date | Escriba una fecha válida |
| `invalid`: a time | Escriba una hora válida |
| `invalid`: a date and a time | Escriba una fecha y una hora válidas |
| `invalid`: a rating | Elija una valoración de 1 a {count} |
| `invalid`: a scale | Elija un punto de {from} a {to} |
| `invalid`, `min`, `max`: a slider | Elija un valor de {min} a {max} |
| `invalid`: a choice, a grid or a ranking | Elija una de las opciones disponibles |
| `invalid`: a country | Elija un país de la lista |
| `invalid`: a name | Revise el nombre |
| `invalid`: an address | Revise la dirección |
| `invalid`: a signature | Vuelva a firmar o escriba su nombre en su lugar |
| `invalid` of a site's own type, a validation rule without a message, a kind without words of its own | Revise esta respuesta |
| `minLength` | Escriba al menos {min} carácter · Escriba al menos {min} caracteres |
| `maxLength` | Escriba como máximo {max} carácter · Escriba como máximo {max} caracteres |
| `minWords` | Escriba al menos {min} palabra · Escriba al menos {min} palabras |
| `maxWords` | Escriba como máximo {max} palabra · Escriba como máximo {max} palabras |
| `min`, `max`: a number with both limits | Escriba un número de {min} a {max} |
| `min`, `max`: a number with a least only | Escriba un número igual o mayor que {min} |
| `min`, `max`: a number with a most only | Escriba un número igual o menor que {max} |
| `min`, `max`: a time with both limits | Escriba una hora de {min} a {max} |
| `min`, `max`: a time with an earliest only | Escriba una hora a partir de las {min} |
| `min`, `max`: a time with a latest only | Escriba una hora hasta las {max} |
| `step`: a number's step | Escriba un número en intervalos de {step} |
| `step`: a number's step from its least | Escriba un número en intervalos de {step}, empezando por {min} |
| `step`: a number without decimals | Escriba un número entero |
| `step`: a number's decimal places | Escriba un número con {places} decimal como máximo · Escriba un número con {places} decimales como máximo |
| `step`: a slider | Elija un valor en intervalos de {step} |
| `step`: the minutes of a time or a date and time | Elija una hora en intervalos de {minutes} minuto · Elija una hora en intervalos de {minutes} minutos |
| `step`: the minutes of a time from its earliest | Elija una hora en intervalos de {minutes} minuto a partir de las {min} · Elija una hora en intervalos de {minutes} minutos a partir de las {min} |
| `dateWindow`: a window with both ends | Elija una fecha del {min} al {max} |
| `dateWindow`: a window with a start only | Elija una fecha a partir del {min} |
| `dateWindow`: a window with an end only | Elija una fecha hasta el {max} |
| `dateWindow`: weekdays only, with both ends | Elija una fecha del {min} al {max}, de {monday} a {friday} |
| `dateWindow`: weekdays only, with a start only | Elija una fecha a partir del {min}, de {monday} a {friday} |
| `dateWindow`: weekdays only, with an end only | Elija una fecha hasta el {max}, de {monday} a {friday} |
| `dateWindow`: weekdays only, without ends | Elija un día de {monday} a {friday} |
| `dateWindow`: a date the window takes that the field excludes | Esta fecha no está disponible. Elija otra. |
| `selections`: as many as the fewest and the most | Elija {count} respuesta · Elija {count} respuestas |
| `selections`: the fewest and the most | Elija de {min} a {max} respuestas |
| `selections`: the fewest only | Elija al menos {min} respuesta · Elija al menos {min} respuestas |
| `selections`: the most only | Elija como máximo {max} respuesta · Elija como máximo {max} respuestas |
| `fileType`: the formats, joined with commas and "or" | Elija un archivo en formato {formats} |
| `fileSize`: a field without a limit of its own | Este archivo es demasiado grande. Elija uno más pequeño. |
| `fileSize`: one file | Este archivo ocupa más de {size}. Elija uno más pequeño. |
| `fileSize`: several files | Uno de los archivos ocupa más de {size}. Elija uno más pequeño. |
| `fileCount` | Elija solo {max} archivo · Elija como máximo {max} archivos |
| `full`: one option, by its label | Ya no quedan plazas para {option}. Elija otra opción. |
| `full`: several options | Ya no quedan plazas para una de las opciones elegidas. Elija otra opción. |
| an option's places left, in its label (ADR-0156) | Queda {count} plaza · Quedan {count} plazas |
| an option without a place left, in its label | Completo |
| a third party's check before it may load (ADR-0157) | Este formulario usa una comprobación antispam de un servicio externo, que solo se carga si usted lo acepta. |
| the button that loads the check | Cargar la comprobación |
| the check, when a consent manager decides | Este formulario usa una comprobación antispam de un servicio externo, que se carga en cuanto lo acepte en la configuración de cookies. |
| the check, when a cookie category decides, by its name | Este formulario usa una comprobación antispam de un servicio externo, que se carga en cuanto acepte las cookies de {category}. |
| the link to the cookie settings | Cambiar la configuración de cookies |
| a send before the check is done | Complete la comprobación antes de enviar el formulario. |
| a check that could not load | La comprobación antispam no se ha podido cargar. Aun así, puede enviar el formulario. |
| a slow device while it solves the puzzle | Estamos comprobando su navegador. Puede tardar un momento. |
| `confirmation` | Escriba dos veces la misma dirección de correo electrónico |
| `digits` | Escriba un número con menos cifras |
| `rowCount`: too few rows | Añada al menos {min} fila · Añada al menos {min} filas |
| `rowCount`: too many rows | Quite filas para que quede como máximo {max} · Quite filas para que queden como máximo {max} |
| `check`: a pattern without a message | Escriba la respuesta en el formato indicado |
| `check`: the same as another field, named by its label | Escriba lo mismo que en {field} |
| `check`: unique per form | Esta respuesta ya está en uso. Escriba otra. |
| a submission without an answer, in the summary | Responda al menos a una pregunta antes de enviar el formulario |
| the error summary's heading, by the number of its links | Hay que corregir esta respuesta · Hay que corregir estas respuestas |
| a summary link to a default | {label}: {message} |
| a summary link to a default after a label that ends in a question mark, an exclamation mark, a full stop or a colon | {label} {message} |
| a summary link to a default in a repeatable group's row | {label} ({row}): {message} |

#### Danish

| Case | Danish |
| --- | --- |
| the word before the last of a list | eller |
| a size below a megabyte, 1,024 based, one place at most | {size} KB |
| a size from a megabyte | {size} MB |
| `required`: a text or a text area | Skriv et svar |
| `required`: an email address | Skriv en e-mailadresse |
| `required`: a phone number | Skriv et telefonnummer |
| `required`: a web address | Skriv en webadresse |
| `required`: a number | Skriv et tal |
| `required`: a date | Skriv en dato |
| `required`: a time | Skriv et klokkeslæt |
| `required`: a date and a time | Skriv en dato og et klokkeslæt |
| `required`: a rating | Vælg en bedømmelse |
| `required`: a scale | Vælg et punkt på skalaen |
| `required`: a slider | Vælg en værdi |
| `required`: radio buttons, a dropdown of one | Vælg et svar |
| `required`: checkboxes, a dropdown of several | Vælg mindst ét svar |
| `required`: a grid | Vælg et svar i hver række |
| `required`: one of a grid's rows | Vælg et svar i denne række |
| `required`: a ranking | Sæt mulighederne i rækkefølge |
| `required`: a checkbox | Sæt kryds i feltet |
| `required`: yes or no, in the field's own words for them | Vælg {yes} eller {no} |
| `required`: a consent | Sæt kryds i feltet for at give dit samtykke |
| `required`: a country | Vælg et land |
| `required`: a name | Skriv hele navnet |
| `required`: an address | Skriv hele adressen |
| `required`: a file upload | Vælg en fil |
| `required`: a signature | Skriv under, eller skriv dit navn i stedet |
| `required`: a site's own type | Besvar dette spørgsmål |
| `invalid`: an email address | Skriv en fuldstændig e-mailadresse, f.eks. navn@example.com |
| `invalid`: a well-formed email address at a domain the field does not take | Brug en e-mailadresse på et andet domæne |
| `invalid`: a phone number | Skriv et telefonnummer med cifre og et + foran landekoden |
| `invalid`: a web address | Skriv en fuldstændig webadresse, f.eks. https://example.com |
| `invalid`: a number; the example is 12, or 12.5 with decimals, in the culture's format | Skriv et tal, f.eks. {example} |
| `invalid`: a date | Skriv en gyldig dato |
| `invalid`: a time | Skriv et gyldigt klokkeslæt |
| `invalid`: a date and a time | Skriv en gyldig dato og et gyldigt klokkeslæt |
| `invalid`: a rating | Vælg en bedømmelse fra 1 til {count} |
| `invalid`: a scale | Vælg et punkt fra {from} til {to} |
| `invalid`, `min`, `max`: a slider | Vælg en værdi fra {min} til {max} |
| `invalid`: a choice, a grid or a ranking | Vælg en af de viste muligheder |
| `invalid`: a country | Vælg et land på listen |
| `invalid`: a name | Tjek navnet |
| `invalid`: an address | Tjek adressen |
| `invalid`: a signature | Skriv under igen, eller skriv dit navn i stedet |
| `invalid` of a site's own type, a validation rule without a message, a kind without words of its own | Tjek dette svar |
| `minLength` | Skriv mindst {min} tegn |
| `maxLength` | Skriv højst {max} tegn |
| `minWords` | Skriv mindst {min} ord |
| `maxWords` | Skriv højst {max} ord |
| `min`, `max`: a number with both limits | Skriv et tal fra {min} til {max} |
| `min`, `max`: a number with a least only | Skriv et tal på {min} eller derover |
| `min`, `max`: a number with a most only | Skriv et tal på {max} eller derunder |
| `min`, `max`: a time with both limits | Skriv et klokkeslæt fra {min} til {max} |
| `min`, `max`: a time with an earliest only | Skriv et klokkeslæt fra og med {min} |
| `min`, `max`: a time with a latest only | Skriv et klokkeslæt til og med {max} |
| `step`: a number's step | Skriv et tal i trin på {step} |
| `step`: a number's step from its least | Skriv et tal i trin på {step} fra {min} |
| `step`: a number without decimals | Skriv et helt tal |
| `step`: a number's decimal places | Skriv et tal med højst {places} decimal · Skriv et tal med højst {places} decimaler |
| `step`: a slider | Vælg en værdi i trin på {step} |
| `step`: the minutes of a time or a date and time | Vælg et klokkeslæt i trin på {minutes} minut · Vælg et klokkeslæt i trin på {minutes} minutter |
| `step`: the minutes of a time from its earliest | Vælg et klokkeslæt i trin på {minutes} minut fra {min} · Vælg et klokkeslæt i trin på {minutes} minutter fra {min} |
| `dateWindow`: a window with both ends | Vælg en dato fra {min} til {max} |
| `dateWindow`: a window with a start only | Vælg en dato fra og med {min} |
| `dateWindow`: a window with an end only | Vælg en dato til og med {max} |
| `dateWindow`: weekdays only, with both ends | Vælg en dato fra {min} til {max}, {monday} til {friday} |
| `dateWindow`: weekdays only, with a start only | Vælg en dato fra og med {min}, {monday} til {friday} |
| `dateWindow`: weekdays only, with an end only | Vælg en dato til og med {max}, {monday} til {friday} |
| `dateWindow`: weekdays only, without ends | Vælg en dag fra {monday} til {friday} |
| `dateWindow`: a date the window takes that the field excludes | Denne dato kan ikke vælges. Vælg en anden. |
| `selections`: as many as the fewest and the most | Vælg {count} svar |
| `selections`: the fewest and the most | Vælg fra {min} til {max} svar |
| `selections`: the fewest only | Vælg mindst {min} svar |
| `selections`: the most only | Vælg højst {max} svar |
| `fileType`: the formats, joined with commas and "or" | Vælg en fil i formatet {formats} |
| `fileSize`: a field without a limit of its own | Filen er for stor. Vælg en mindre. |
| `fileSize`: one file | Filen er større end {size}. Vælg en mindre. |
| `fileSize`: several files | En af filerne er større end {size}. Vælg en mindre. |
| `fileCount` | Vælg kun {max} fil · Vælg højst {max} filer |
| `full`: one option, by its label | {option} er fuldt booket. Vælg en anden mulighed. |
| `full`: several options | Et af dine valg er fuldt booket. Vælg en anden mulighed. |
| an option's places left, in its label (ADR-0156) | {count} ledig plads · {count} ledige pladser |
| an option without a place left, in its label | Fuldt booket |
| a third party's check before it may load (ADR-0157) | Formularen bruger et spamtjek fra en ekstern tjeneste, som kun indlæses, hvis du giver samtykke. |
| the button that loads the check | Indlæs tjekket |
| the check, when a consent manager decides | Formularen bruger et spamtjek fra en ekstern tjeneste, som indlæses, når du giver samtykke i cookieindstillingerne. |
| the check, when a cookie category decides, by its name | Formularen bruger et spamtjek fra en ekstern tjeneste, som indlæses, når du accepterer {category}cookies. |
| the link to the cookie settings | Skift cookieindstillinger |
| a send before the check is done | Gennemfør tjekket, før du sender formularen. |
| a check that could not load | Spamtjekket kunne ikke indlæses. Du kan stadig sende formularen. |
| a slow device while it solves the puzzle | Vi tjekker din browser. Det kan tage et øjeblik. |
| `confirmation` | Skriv den samme e-mailadresse to gange |
| `digits` | Skriv et tal med færre cifre |
| `rowCount`: too few rows | Tilføj mindst {min} række · Tilføj mindst {min} rækker |
| `rowCount`: too many rows | Fjern rækker, så der højst er {max} tilbage |
| `check`: a pattern without a message | Skriv svaret i det ønskede format |
| `check`: the same as another field, named by its label | Skriv det samme som ved {field} |
| `check`: unique per form | Dette svar er allerede brugt. Skriv et andet. |
| a submission without an answer, in the summary | Besvar mindst ét spørgsmål, før du sender formularen |
| the error summary's heading, by the number of its links | Dette svar skal rettes · Disse svar skal rettes |
| a summary link to a default | {label}: {message} |
| a summary link to a default after a label that ends in a question mark, an exclamation mark, a full stop or a colon | {label} {message} |
| a summary link to a default in a repeatable group's row | {label} ({row}): {message} |

## Checklist for every new string

- [ ] It uses the glossary's word, in sentence case.
- [ ] A button starts with a verb and names its object when it stands alone.
- [ ] An error says what failed and how to go on, and whether work was kept.
- [ ] An empty state says which kind of empty it is and offers the next action.
- [ ] A destructive confirmation names what is lost, with numbers, and repeats the action on its button.
- [ ] Nothing on the screen already says the same thing.
- [ ] It has an English and a German key, and both read naturally aloud.
- [ ] User-typed names go in as values, never as HTML.
