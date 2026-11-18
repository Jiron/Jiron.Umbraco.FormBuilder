// Die globalen Einstellungen auf Deutsch (ROADMAP 12.4a, IA 8 und 9, ADR-0218, docs/ux/copy.md): Einstellungen ›
// Allgemein, E-Mail, Spamschutz, Dateien und Integrationen sowie Datenschutz › Datenerfassung.
import type { UmbLocalizationDictionary } from '@umbraco-cms/backoffice/localization-api';

const and = (parts: readonly string[]) => new Intl.ListFormat('de', { type: 'conjunction' }).format(parts);

export default {
  jfbGlobalSettings: {
    loading: 'Die Einstellungen werden geladen…',
    failed: 'Die Einstellungen konnten nicht gelesen werden.',
    retry: 'Erneut versuchen',
    save_general: 'Allgemeine Einstellungen speichern',
    save_email: 'E-Mail-Einstellungen speichern',
    save_protection: 'Spamschutz-Einstellungen speichern',
    save_files: 'Dateieinstellungen speichern',
    save_integrations: 'Integrationseinstellungen speichern',
    save_privacy: 'Einstellungen zur Datenerfassung speichern',
    save_retention: 'Aufbewahrungseinstellungen speichern',
    saved: 'Die Einstellungen sind gespeichert.',
    saveFailed: 'Die Einstellungen konnten nicht gespeichert werden. Versuchen Sie es gleich noch einmal.',
    invalid: 'Einige Einstellungen brauchen einen anderen Wert. Jede sagt, warum.',
    conflict:
      'Jemand anderes hat diese Einstellungen inzwischen gespeichert. Jetzt sehen Sie deren Werte; nehmen Sie Ihre Änderung erneut vor, falls sie noch nötig ist.',
    locked: (key: string) =>
      `Die Konfiguration der Website legt diese Einstellungen fest (${key}); hier lassen sie sich nicht ändern.`,
    lockedNow:
      'Die Konfiguration der Website hat diese Einstellungen inzwischen gesperrt. Sie werden so gezeigt, wie sie sie festlegt.',
    moreOptions: (count: number) => `Weitere Optionen (${count})`,
    fewerOptions: 'Weniger Optionen',
    range: (min: string, max: string) => `Verwenden Sie eine ganze Zahl von ${min} bis ${max}.`,
    discardHeadline: 'Ihre Änderungen verwerfen?',
    discardText:
      'Die Änderungen auf diesem Bildschirm sind noch nicht gespeichert. Eine andere Website zeigt ihre eigenen Werte.',
    discardConfirm: 'Verwerfen',

    site: 'Website',
    allSites: 'Alle Websites',
    siteShared: 'Einstellungen, die alle Websites teilen, stehen unter „Alle Websites“.',
    siteOwn: 'Diese Website hat einen eigenen Wert.',
    siteUseAll: 'Den Wert aller Websites verwenden',
    siteGone: 'Diese Website ist nicht mehr im Inhalt. Wählen Sie eine andere.',
    language: 'Texte in',

    // Allgemein
    generalBox_forms: 'Vorgaben für Formulare',
    general_theme: 'Standard-Theme',
    general_requiredMarker: 'Felder mit Markierung',
    general_requiredMarkerHelp:
      'Die meisten Formulare haben wenige optionale Felder; diese zu markieren wirkt ruhiger.',
    general_requiredMarker_required: 'Pflichtfelder, mit „*“',
    general_requiredMarker_optional: 'Optionale Felder, mit „(optional)“',
    general_requiredMarker_none: 'Keine Felder',
    general_submitLabel: 'Schaltfläche Senden',
    general_submitLabelHelp: 'Leer steht dort „Senden“.',
    general_nextLabel: 'Schaltfläche Weiter',
    general_nextLabelHelp: 'Leer steht dort „Weiter“.',
    general_backLabel: 'Schaltfläche Zurück',
    general_backLabelHelp: 'Leer steht dort „Zurück“.',
    general_texts: 'Texte für Besucher',
    general_textsHelp: 'Eigene Worte für die Hinweise, Meldungen und Fehlermeldungen, die jedes Formular mitbringt.',
    generalBox_site: 'Adresse und Zeit',
    general_baseUrl: 'Basis-URL',
    general_baseUrlHelp: 'Links in E-Mails beginnen damit. Leer beginnen sie mit der Adresse, die das Feld zeigt.',
    general_timeZone: 'Zeitzone',
    general_timeZoneHelp:
      'Daten in E-Mails und Exporten verwenden sie. Formulare öffnen und schließen in der Zeitzone aller Websites.',
    zonesWithoutArea: 'Universell',
    generalBox_newForms: 'Neue leere Formulare',
    general_newForms: 'Neue Formulare',
    general_newFormsHelp: 'Womit ein leer erstelltes Formular beginnt.',
    general_newForms_consent: 'Ein Einwilligungsfeld hinzufügen',
    general_newForms_consentHelp:
      'Ein Pflichtfeld zum Ankreuzen am Ende der letzten Seite. Formulare, die personenbezogene Daten behalten, brauchen eines, wenn die Einwilligung ihre Grundlage ist.',
    consentLabel: 'Beschriftung',
    consentText: 'Wortlaut',
    consentNeedsLabel: 'Geben Sie dem Einwilligungsfeld eine Beschriftung.',
    general_newForms_notifyCreator: 'Jeden Eintrag an den Ersteller des Formulars senden',
    generalBox_history: 'Verlauf',
    generalBox_historyLead:
      'Veröffentlichte und angeheftete Versionen, der aktuelle Entwurf jedes Formulars und die Versionen, mit denen Einträge gesendet wurden, bleiben immer erhalten.',
    general_history: 'Verlauf',
    general_historyHelp: 'Wie lange der Verlauf jedes Formulars seine gespeicherten Entwürfe behält.',
    general_history_draftDays: 'Gespeicherte Entwürfe behalten für',
    general_history_draftDaysUnit: 'Tage',
    general_history_draftCount: 'Je Formular immer behalten',
    general_history_draftCountUnit: 'neueste gespeicherte Entwürfe',
    formBlock: 'Formulare auf Seiten zeigen',
    formBlockThere: 'Seiten zeigen Formulare über den Blocktyp Form.',
    formBlockHow:
      'Fügen Sie ihn den verfügbaren Blöcken des Blocklisten- oder Blockraster-Datentyps hinzu, den Ihre Seiten verwenden.',
    formBlockMissing:
      'Seiten können noch keine Formulare zeigen: Sie brauchen den Blocktyp Form, der dann einer Blockliste oder einem Blockraster hinzugefügt wird.',
    formBlockCreate: 'Blocktyp für Formulare erstellen…',
    formBlockAsk: 'Eine Person mit dem Umbraco-Bereich Einstellungen kann ihn erstellen.',

    // E-Mail
    cannotSend:
      'E-Mails können noch nicht gesendet werden: Es ist kein Mailserver eingerichtet. Einträge bleiben erhalten, und ihre E-Mails gehen hinaus, sobald das Senden funktioniert.',
    cannotSendKey: (key: string) => `Eine Entwicklerin oder ein Entwickler richtet das Senden in ${key} ein.`,
    sendTest: 'Test-E-Mail senden',
    testSent: (to: string) => `Test-E-Mail an Sie gesendet (${to}).`,
    testFailed: 'Die Test-E-Mail konnte nicht gesendet werden. Versuchen Sie es gleich noch einmal.',
    emailBox_sender: 'Absender',
    email_senderName: 'Name',
    email_senderAddress: 'Adresse',
    email_senderAddressHelp:
      'Verwenden Sie eine Adresse der eigenen Domain der Website, damit Mailserver ihr vertrauen.',
    email_defaultLayout: 'Standardlayout',
    email_defaultLayout_card: 'Karte',
    email_defaultLayout_letter: 'Brief',
    emailBox_team: 'Das Team',
    email_teamRecipients: 'Systemhinweise gehen an',
    email_teamRecipientsHelp:
      'Diese Adressen erfahren, wenn eine Zustellung endgültig fehlschlägt, höchstens einmal pro Stunde und Formular.',
    email_forwardAddresses: 'Adressen, an die Einträge weitergeleitet werden dürfen',
    email_forwardAddressesHelp: 'Neben Backoffice-Benutzern bietet „Weiterleiten“ nur diese an.',

    // Spamschutz
    protectionBox_level: 'Standardschutz',
    protection_level: 'Schutz',
    protection_levelHelp:
      'reCAPTCHA und hCaptcha laden ein Skript ihres Anbieters, nur in Formularen, die sie verwenden, und nur so, wie es die Einwilligung unten erlaubt.',
    protection_level_standard: 'Standard',
    protection_level_standardLine:
      'Ein verborgenes Feld, eine Mindestzeit zum Ausfüllen und eine signierte Prüfung. Nichts wird von anderen Websites geladen.',
    protection_level_proofOfWork: 'Standard mit Rechenaufgabe',
    protection_level_proofOfWorkLine:
      'Der Browser des Besuchers löst zusätzlich ein kurzes Rätsel, das Bots Zeit kostet. Es braucht JavaScript.',
    protection_level_captcha: 'Ein Captcha',
    captchaKeys: 'Schlüssel in der Host-Konfiguration gefunden.',
    captchaNoKeys: (key: string) => `Noch keine Schlüssel in der Host-Konfiguration: ${key}`,
    captchaGone: 'Nicht mehr auf dieser Website. Wählen Sie einen anderen.',
    protection_captchaProvider: 'Captcha',
    protection_captchaProviderHelp: 'Das Captcha, das Formulare verwenden, wenn ihr Schutz ein Captcha ist.',
    protection_proofBits: 'Stärke des Rätsels',
    protection_proofBitsUnit: 'Bit',
    protection_proofBitsHelp:
      'Jedes weitere Bit verdoppelt die Arbeit für den Browser des Besuchers und für Bots. Der Standard, 16, dauert auf einem Telefon einen Moment.',
    protectionBox_consent: 'Captchas von Drittanbietern',
    protection_captchaConsent: 'Bevor reCAPTCHA oder hCaptcha lädt',
    protection_captchaConsent_explicit: 'Im Formular fragen',
    protection_captchaConsent_explicitLine:
      'Das Formular zeigt einen kurzen Hinweis und eine Schaltfläche, die die Prüfung lädt.',
    protection_captchaConsent_cookiebot: 'Einer Cookiebot-Kategorie folgen',
    protection_captchaConsent_cookiebotLine: 'Es lädt, sobald der Besucher die unten gewählte Kategorie annimmt.',
    protection_captchaConsent_cmp: 'Dem Einwilligungsmanager der Website folgen',
    protection_captchaConsent_cmpLine:
      "Es lädt, sobald der Einwilligungsmanager JironFormBuilder.consent.grant('captcha') aufruft.",
    protection_captchaConsent_implicit: 'Ohne Nachfrage laden',
    protection_captchaConsent_implicitLine:
      'Es lädt mit dem Formular, für eine Website, die die Einwilligung auf anderem Weg hat.',
    choiceLocked: (key: string) => `Durch die Konfiguration gesperrt: ${key}`,
    protection_cookiebotCategory: 'Cookiebot-Kategorie',
    protection_cookiebotCategoryHelp:
      'Cookiebot ordnet das Cookie eines Captchas als notwendig ein; die Prüfung lädt also sofort, außer Sie wählen Marketing.',
    protection_cookiebotCategory_necessary: 'Notwendig',
    protection_cookiebotCategory_marketing: 'Marketing',
    protectionBox_content: 'Inhaltsprüfungen',
    protectionBox_contentLead:
      'Ein Treffer weist nie einen Eintrag ab: Er verschiebt ihn in den Spam, wo nichts verloren geht.',
    protection_contentRules: 'Standardstrenge',
    protection_contentRules_off: 'Aus',
    protection_contentRules_normal: 'Normal: drei gesperrte Wörter oder zwei mit zu vielen Links',
    protection_contentRules_strict: 'Streng: zwei gesperrte Wörter oder eines mit zu vielen Links',
    protection_blockedWords: 'Gesperrte Wörter',
    protection_blockedWordsHelp:
      'Wörter oder Wendungen, gleich in welcher Schreibweise. Jedes gefundene zählt zum Spam.',
    protection_blockedDomains: 'Gesperrte E-Mail-Domains',
    protection_blockedDomainsHelp: 'Ein Eintrag mit einer E-Mail-Adresse bei einer davon geht in den Spam.',
    protection_maxLinks: 'Die meisten Links, die ein Eintrag enthalten darf',
    protection_maxLinksHelp: 'Über alle Antworten gezählt. Mehr Links als diese zählen zum Spam.',
    protectionBox_ranges: 'Gesperrte Adressbereiche',
    protection_blockedRanges: 'Bereiche',
    protection_blockedRangesHelp:
      'Einträge aus ihnen, etwa 203.0.113.0/24, gehen in den Spam. Die Adresse wird beim Eingang eines Eintrags verglichen und nie gespeichert.',

    // Dateien
    filesBox_types: 'Was Besucher hochladen dürfen',
    files_allowedTypes: 'Erlaubte Typen',
    files_allowedTypesHelp: 'Jedes Dateifeld wählt daraus.',
    files_allowedTypes_images: 'Bilder',
    files_allowedTypes_pdf: 'PDF',
    files_allowedTypes_office: 'Office-Dokumente',
    files_allowedTypes_text: 'Text',
    files_allowedTypes_archives: 'Archive',
    blockedExtensions: (key: string) =>
      `Programme, Skripte und Webseiten werden nie angenommen, gleich was ein Feld erlaubt: ${key}.`,
    files_maxFileBytes: 'Größte Datei',
    files_maxFileBytesUnit: 'MB',
    files_maxFileBytesHelp: 'Jedes Dateifeld legt seine eigene Grenze fest, bis zu dieser.',
    hostCap: (megabytes: string, key: string) => `Die Host-Konfiguration erlaubt höchstens ${megabytes} MB: ${key}`,
    sizeInvalid: (megabytes: string) => `Verwenden Sie eine Zahl von Megabyte bis ${megabytes}, etwa 20 oder 2,5.`,
    filesChecks: 'Prüfungen und Ablage',
    typeCheck: 'Typprüfung',
    typeCheckLine: 'Der Inhalt jeder Datei muss zu ihrem Typ passen, gleich was ihr Name sagt.',
    scanner: 'Virenscanner',
    scannerNone: 'Keiner installiert, Dateien werden also nicht auf Schadsoftware geprüft.',
    scannerSome: (count: number) =>
      count === 1
        ? 'Einer installiert: Jede Datei wird geprüft, bevor jemand sie herunterladen kann.'
        : `${count} installiert: Jede Datei wird geprüft, bevor jemand sie herunterladen kann.`,
    storedIn: 'Abgelegt in',
    storedFolder: (key: string) =>
      `Ein privater Ordner außerhalb der Website (${key}). Dateien haben keine öffentliche Adresse: Benutzer laden sie aus ihrem Eintrag herunter.`,
    storedCustom:
      'Ein Dateispeicher, den die Website registriert. Dateien haben keine öffentliche Adresse: Benutzer laden sie aus ihrem Eintrag herunter.',

    // Integrationen
    integrationsBox_webhooks: 'Webhooks',
    integrations_signingSecret: 'Signaturgeheimnis',
    integrations_signingSecretHelp:
      'Webhook-Aktionen signieren jeden Aufruf mit diesem Geheimnis, sodass der Empfänger erkennt, dass er von dieser Website kommt. Das Geheimnis selbst bleibt in der Host-Konfiguration.',
    integrations_umbracoWebhookEvents: 'Eintragsereignisse anbieten',
    integrations_umbracoWebhookEventsHelp:
      'Fügt Eintrag gesendet, genehmigt, abgelehnt und gelöscht sowie Aktion fehlgeschlagen den Ereignissen von Umbracos Einstellungen › Webhooks hinzu.',
    hosts: 'Hosts, die Webhooks aufrufen dürfen',
    hostsNone: 'Noch keine, Webhook-Aktionen und JSON-Optionslisten rufen also keine Adresse auf.',
    hostsKey: (key: string) => `Die Host-Konfiguration legt sie fest: ${key}.`,
    hostsHttp: 'Einfache http-Adressen sind ebenfalls erlaubt.',
    integrationsBox_chat: 'Slack und Microsoft Teams',
    integrationsBox_chatLead:
      'Slack- und Microsoft-Teams-Aktionen verwenden diese, wenn sie keine eigenen festlegen. Die Adressen wirken wie Passwörter, darum werden sie als Verweise auf die Host-Konfiguration eingegeben.',
    integrations_slackWebhook: 'Slack-Webhook-Adresse',
    integrations_teamsWebhook: 'Teams-Webhook-Adresse',
    referencePlaceholder: (name: string) => `Ein Verweis, etwa $JironFormBuilder:Secrets:${name}`,
    referenceFound: 'In der Host-Konfiguration gefunden.',
    referenceMissing: (key: string) => `Noch nicht in der Host-Konfiguration: ${key}`,
    referenceInvalid: 'Geben Sie einen Verweis wie $JironFormBuilder:Secrets:Crm ein, nie das Geheimnis selbst.',

    // Datenschutz › Aufbewahrung
    privacyBox_retention: 'Wie lange Einträge aufbewahrt werden',
    privacy_retention: 'Tage, die Einträge aufbewahrt werden',
    privacy_retentionHelp:
      'Gezählt ab der letzten Statusänderung eines Eintrags. Testeinträge aus der Vorschau gehen nach 7 Tagen.',
    privacy_retention_submitted: 'Gesendet',
    privacy_retention_submittedHelp: 'Einträge, die auf Genehmigung warten, zählen hier mit.',
    privacy_retention_submittedUnit: 'Tage',
    privacy_retention_approved: 'Genehmigt',
    privacy_retention_approvedHelp: 'Auch jeder Eintrag eines Formulars ohne manuelle Genehmigung.',
    privacy_retention_approvedUnit: 'Tage',
    privacy_retention_rejected: 'Abgelehnt',
    privacy_retention_rejectedUnit: 'Tage',
    privacy_retention_spam: 'Spam',
    privacy_retention_spamUnit: 'Tage',
    retentionForGood: 'Dauerhaft aufbewahren',
    retentionForGoodShown: 'Dauerhaft aufbewahrt',
    retentionDaysShown: (days: string) => `${days} Tage`,
    retentionReadOnly:
      'Um die Aufbewahrung zu ändern, brauchen Sie die Berechtigung „Einstellungen verwalten“. Wer Administratorrechte hat, kann sie unter Einstellungen › Zugriff vergeben.',
    retentionForGoodLocked: 'Dauerhaftes Aufbewahren ist durch die Konfiguration gesperrt:',
    retentionOwnBox: 'Formulare mit eigener Aufbewahrung',
    retentionOwnNone:
      'Kein Formular legt eigene Tage fest. Ein Formular kann es in seiner Ansicht Einstellungen unter Datenschutz.',
    retentionOwnState: (state: string, days: string) => `${state}: ${days} Tage`,
    retentionOwnStore: (days: string) => `Jeder Eintrag: ${days} Tage nach dem Senden`,
    retentionTrashed: 'Im Papierkorb',
    retentionWhatBox: 'Was gelöscht wird',
    retentionSpan: 'Gelöscht innerhalb von',
    retentionSpan_7: '7 Tagen',
    retentionSpan_30: '30 Tagen',
    retentionSpan_90: '90 Tagen',
    retentionNothing: 'In dieser Zeit wird nichts gelöscht.',
    retentionTotal: (entries: string, forms: string, oneEntry: boolean, oneForm: boolean) =>
      `${entries} ${oneEntry ? 'Eintrag' : 'Einträge'} in ${forms} ${oneForm ? 'Formular' : 'Formularen'}`,
    retentionHeld: (count: string, one: boolean) =>
      one
        ? `${count} weiterer hat eine Löschsperre und bleibt, bis sie endet.`
        : `${count} weitere haben eine Löschsperre und bleiben, bis sie endet.`,
    retentionHeldStay: 'Einträge mit Löschsperre bleiben.',
    retentionUnsaved: 'Gezählt mit den Tagen oben, bevor Sie speichern.',
    retentionByItself: 'Die Aufbewahrung löscht von selbst, jede Stunde.',
    retentionColumnForm: 'Formular',
    retentionColumnEntries: 'Einträge',
    retentionColumnFiles: 'Dateien',
    retentionNoFiles: 'keine',
    retentionCounting: 'Wird gezählt…',
    retentionCountFailed: 'Die Einträge konnten nicht gezählt werden.',

    // Datenschutz › Personen
    people_findBox: 'Daten einer Person finden',
    people_email: 'E-Mail-Adresse',
    people_emailPlaceholder: 'Zum Beispiel: name@example.com',
    people_find: 'Finden',
    people_findHelp:
      'Alle Formulare werden durchsucht, auch eingeschränkte, und die Suche wird im Protokoll festgehalten.',
    people_byMember: 'Stattdessen ein Mitglied finden…',
    people_byEmail: 'Stattdessen eine E-Mail-Adresse verwenden',
    people_member: 'Mitglied',
    people_chooseMember: 'Mitglied auswählen',
    people_someMember: 'das Mitglied',
    people_emailInvalid: 'Geben Sie eine E-Mail-Adresse ein, zum Beispiel name@example.com.',
    people_entries: (count: string, one: boolean) => `${count} ${one ? 'Eintrag' : 'Einträge'}`,
    people_forms: (count: string, one: boolean) => `${count} ${one ? 'Formular' : 'Formularen'}`,
    people_files: (count: string, one: boolean) => `${count} ${one ? 'Datei' : 'Dateien'}`,
    people_emails: (count: string, one: boolean) => `${count} ${one ? 'E-Mail' : 'E-Mails'}`,
    people_drafts: (count: string, one: boolean) =>
      `${count} ${one ? 'gespeicherter Entwurf' : 'gespeicherte Entwürfe'}`,
    people_found: (entries: string, forms: string) => `${entries} in ${forms}`,
    people_more: 'Das sind die neuesten 500. Löschen Sie sie und suchen Sie dann erneut nach den übrigen.',
    people_none: (who: string) =>
      `Keine Einträge für ${who} gefunden. Einträge, die angemeldet gesendet wurden, finden Sie über das Mitglied.`,
    people_noneFound: (who: string) => `Keine Einträge für ${who} gefunden.`,
    people_failed: 'Die Einträge konnten nicht gefunden werden.',
    people_export: 'Alles exportieren',
    people_hold: 'Löschsperre setzen…',
    people_erase: 'Daten löschen…',
    people_columnEntry: 'Eintrag',
    people_columnForm: 'Formular',
    people_columnReceived: 'Eingegangen',
    people_columnFiles: 'Dateien',
    people_columnEmails: 'E-Mails',
    people_entry: (number: string) => `Eintrag #${number}`,
    people_draft: 'Gespeicherter Entwurf',
    people_test: 'Testeintrag',
    people_trashed: 'Im Papierkorb',
    people_noneCount: 'keine',
    people_heldUntil: (until: string) => `Löschsperre bis ${until}`,
    people_release: 'Aufheben',
    people_releaseLabel: (entry: string) => `Löschsperre für ${entry} aufheben`,
    people_released: 'Die Löschsperre ist aufgehoben.',
    people_exporting: (done: string, total: string) =>
      `Der Export wird vorbereitet: ${done} von ${total} Einträgen. Eine Benachrichtigung verlinkt die Datei.`,
    people_exportLabel: 'Export',
    people_exportFailed: 'Der Export konnte nicht vorbereitet werden. Versuchen Sie es erneut.',
    people_erased: (who: string, parts: readonly string[]) =>
      `Alles über ${who} ist gelöscht: ${and(parts)}. Die Einblicke bleiben.`,
    people_erasedAnonymised: (who: string, entries: string, one: boolean, deleted: readonly string[]) =>
      `Alles über ${who} ist gelöscht: ${entries} anonymisiert, ${one ? 'der in den Zahlen bleibt' : 'die in den Zahlen bleiben'}${deleted.length > 0 ? `, und ${and(deleted)} gelöscht` : ''}. Die Einblicke bleiben.`,
    people_erasedHeld: (entries: string, one: boolean) =>
      one
        ? `${entries} mit Löschsperre bleibt, bis die Sperre endet.`
        : `${entries} mit Löschsperre bleiben, bis die Sperren enden.`,
    people_auditOpen: 'Protokoll öffnen',
    people_auditRecorded: 'Es ist im Protokoll festgehalten.',
    people_eraseHeadline: (who: string) => `Alles über ${who} löschen?`,
    people_eraseText: (parts: readonly string[], one: boolean) =>
      `${and(parts)} ${one ? 'wird' : 'werden'} gelöscht. Die Einblicke bleiben. Das lässt sich nicht rückgängig machen.`,
    people_eraseHeld: (entries: string, one: boolean) =>
      one
        ? `${entries} mit Löschsperre bleibt, bis die Sperre endet.`
        : `${entries} mit Löschsperre bleiben, bis die Sperren enden.`,
    people_eraseMore:
      'Diesmal werden nur die neuesten 500 Einträge gelöscht; suchen Sie danach erneut nach den übrigen.',
    people_eraseFormHeld: (count: string) => `${count} mit Löschsperre`,
    people_eraseHow: 'Was mit den Einträgen geschieht',
    people_eraseDelete: 'Einträge löschen',
    people_eraseDeleteHelp: 'Die Einträge gehen mit ihren Dateien und E-Mails.',
    people_eraseAnonymise: 'Einträge anonymisieren',
    people_eraseAnonymiseHelp: 'Die Einträge bleiben in den Zahlen, ohne eine Antwort, Datei oder E-Mail der Person.',
    people_eraseConfirm: (entries: string) => `${entries} löschen`,
    people_eraseConfirmAnonymise: (entries: string) => `${entries} anonymisieren`,
    people_eraseNothing:
      'Alle Einträge haben eine Löschsperre, daher gibt es nichts zu löschen, bis die Sperren enden.',
    people_eraseFailed: 'Das Löschen konnte nicht ausgeführt werden. Nichts wurde geändert; versuchen Sie es erneut.',
    people_holdHeadline: 'Löschsperre setzen',
    people_holdEntries: 'Einträge',
    people_holdOf: (form: string, received: string) => `${form}, ${received}`,
    people_holdReason: 'Grund',
    people_holdReasonHelp:
      'Wird mit der Sperre gespeichert. Das Protokoll hält fest, wer sie gesetzt hat und bis wann.',
    people_holdUntil: 'Aufbewahren bis',
    people_holdUntilHelp: 'Höchstens ein Jahr ab heute. Sie können die Sperre verlängern, bevor sie endet.',
    people_holdNote:
      'Einträge mit Löschsperre bleiben über ihre Aufbewahrung hinaus, und Löschen… lässt sie aus, bis die Sperre endet.',
    people_holdConfirm: (entries: string) => `Löschsperre für ${entries} setzen`,
    people_holdChoose: 'Wählen Sie mindestens einen Eintrag.',
    people_holdReasonNeeded: 'Geben Sie den Grund in einer Zeile an, höchstens 500 Zeichen.',
    people_holdEndInvalid: 'Wählen Sie einen Tag nach heute und höchstens ein Jahr voraus.',
    people_holdFailed: 'Die Löschsperre konnte nicht gesetzt werden. Versuchen Sie es erneut.',
    people_holdDone: (entries: string) => `${entries} mit Löschsperre.`,

    // Datenschutz › Datenerfassung
    privacyBox_ip: 'IP-Adressen',
    privacyBox_ipLead:
      'Ratenbegrenzungen und gesperrte Adressbereiche lesen die Adresse, von der ein Eintrag gesendet wird, ohne sie zu speichern.',
    privacy_ip: 'Was Einträge speichern',
    privacy_ipHelp: 'Jedes Formular kann in seiner Ansicht Einstellungen unter Datenschutz eine eigene Wahl treffen.',
    privacy_ip_none: 'Nichts',
    privacy_ip_noneLine: 'Einträge speichern die Adresse nicht.',
    privacy_ip_anonymised: 'Die Adresse, um ihr Ende gekürzt',
    privacy_ip_anonymisedLine:
      'Genug, um ein Netz zu unterscheiden: 203.0.113.0 für 203.0.113.42, und die ersten 48 Bit einer IPv6-Adresse.',
    privacy_ip_full: 'Die vollständige Adresse',
    privacy_ip_fullLine:
      'Nur für eine Website, die sie ganz braucht, etwa um Missbrauch zu melden. Nennen Sie das in Ihrer Datenschutzerklärung.',
    privacyBox_insights: 'Einblicke',
    privacyBox_insightsLead:
      'Formulare zählen Aufrufe, Begonnene, die Seiten, die Besucher erreichen, und die Felder, an denen sie hängen bleiben. Keine Antwort und kein Besucher wird gezählt, und im Browser des Besuchers wird nichts gespeichert.',
    privacy_insights: 'Zählen, was Besucher mit den Formularen tun',
    privacy_insightsHelp: 'Jedes Formular kann es in seiner Ansicht Einstellungen unter Datenschutz ausschalten.',
    privacyBox_members: 'Mitglieder',
    privacyBox_membersLead:
      'Die Entwürfe eines gelöschten Mitglieds gehen immer, und eine Löschsperre behält ihre Einträge, wie sie sind.',
    privacy_memberDeleted: 'Wenn ein Mitglied gelöscht wird',
    privacy_memberDeleted_anonymise: 'Seine Einträge anonymisieren',
    privacy_memberDeleted_anonymiseLine:
      'Die Einträge bleiben mit Formular, Nummer und Status in den Zahlen; jede Antwort und alles andere über das Mitglied geht.',
    privacy_memberDeleted_delete: 'Seine Einträge löschen',
    privacy_memberDeleted_deleteLine: 'Die Einträge gehen, mit ihren Dateien und E-Mails.',
  },
} satisfies UmbLocalizationDictionary;
