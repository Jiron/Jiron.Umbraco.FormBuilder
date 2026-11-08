// Die globalen Einstellungen auf Deutsch (ROADMAP 12.4a, IA 8 und 9, ADR-0218, docs/ux/copy.md): Einstellungen ›
// Allgemein, E-Mail, Spamschutz, Dateien und Integrationen sowie Datenschutz › Datenerfassung.
import type { UmbLocalizationDictionary } from '@umbraco-cms/backoffice/localization-api';

export default {
  jfbGlobalSettings: {
    loading: 'Die Einstellungen werden geladen…',
    failed: 'Die Einstellungen konnten nicht gelesen werden.',
    retry: 'Erneut versuchen',
    save: 'Speichern',
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
    siteShared: 'Einstellungen, die alle Websites teilen, stehen unter Alle Websites.',
    siteOwn: 'Diese Website hat einen eigenen Wert.',
    siteUseAll: 'Den Wert aller Websites verwenden',
    siteGone: 'Diese Website ist nicht mehr im Inhalt. Wählen Sie eine andere.',
    language: 'Texte in',

    // Allgemein
    generalBox_forms: 'Vorgaben für Formulare',
    general_theme: 'Standarddesign',
    general_themeHelp: 'Formulare verwenden es, wenn ihre Gruppe Darstellung kein anderes wählt.',
    general_requiredMarker: 'Felder mit Kennzeichnung',
    general_requiredMarkerHelp:
      'Die meisten Formulare haben mehr Pflichtfelder als optionale; die wenigen optionalen zu kennzeichnen liest sich ruhiger.',
    general_requiredMarker_required: 'Pflichtfelder, mit „*“',
    general_requiredMarker_optional: 'Optionale Felder, mit „(optional)“',
    general_requiredMarker_none: 'Keine Felder',
    general_submitLabel: 'Schaltfläche Senden',
    general_submitLabelHelp:
      'Was die Schaltfläche der letzten Seite sagt, wenn ein Formular sie nicht selbst beschriftet. Leer sagt sie Senden.',
    general_nextLabel: 'Schaltfläche Weiter',
    general_nextLabelHelp:
      'Was die Schaltfläche zur nächsten Seite sagt, wenn ein Formular sie nicht selbst beschriftet. Leer sagt sie Weiter.',
    general_backLabel: 'Schaltfläche Zurück',
    general_backLabelHelp:
      'Was die Schaltfläche zurück sagt, wenn ein Formular sie nicht selbst beschriftet. Leer sagt sie Zurück.',
    generalBox_site: 'Adresse und Zeit',
    general_baseUrl: 'Basis-URL',
    general_baseUrlHelp: 'Links in E-Mails beginnen damit. Leer beginnen sie mit der Adresse, die das Feld zeigt.',
    general_timeZone: 'Zeitzone',
    general_timeZoneHelp:
      'Daten in E-Mails und Exporten verwenden sie, ebenso die Zeiten, zu denen ein Formular öffnet und schließt. Tippen Sie die ersten Buchstaben einer Stadt, um sie zu finden.',
    zonesWithoutArea: 'Universell',
    generalBox_newForms: 'Neue Formulare',
    generalBox_newFormsLead:
      'Womit ein leer erstelltes Formular beginnt. Eine Vorlage bringt ihre eigenen Felder und Aktionen mit.',
    general_newForms: 'Neue Formulare',
    general_newFormsHelp: 'Womit ein leer erstelltes Formular beginnt.',
    general_newForms_consent: 'Ein Einwilligungsfeld hinzufügen',
    general_newForms_consentHelp:
      'Ein Pflichtfeld zum Ankreuzen am Ende der letzten Seite, mit dem eigenen Wortlaut der Website. Formulare, die personenbezogene Daten behalten, brauchen eines, wenn die Einwilligung ihre Grundlage ist.',
    consentLabel: 'Beschriftung',
    consentText: 'Wortlaut',
    consentNeedsLabel: 'Geben Sie dem Einwilligungsfeld eine Beschriftung.',
    general_newForms_notifyCreator: 'Jeden Eintrag an den Ersteller des Formulars senden',
    general_newForms_notifyCreatorHelp:
      'Neue Formulare beginnen mit einer E-Mail-Aktion an die Person, die sie erstellt.',
    generalBox_history: 'Verlauf',
    generalBox_historyLead:
      'Wie lange der Verlauf jedes Formulars seine gespeicherten Entwürfe behält. Veröffentlichte und angeheftete Versionen, der aktuelle Entwurf jedes Formulars und die Versionen, mit denen Einträge gesendet wurden, bleiben immer erhalten.',
    general_history: 'Verlauf',
    general_historyHelp: 'Wie lange der Verlauf jedes Formulars seine gespeicherten Entwürfe behält.',
    general_history_draftDays: 'Gespeicherte Entwürfe behalten für',
    general_history_draftDaysUnit: 'Tage',
    general_history_draftDaysHelp: 'Jeder gespeicherte Entwurf bleibt mindestens so lange erhalten.',
    general_history_draftCount: 'Immer behalten',
    general_history_draftCountUnit: 'neueste gespeicherte Entwürfe',
    general_history_draftCountHelp:
      'Unabhängig von ihrem Alter behält jedes Formular so viele seiner neuesten gespeicherten Entwürfe.',
    formBlock: 'Formulare auf Seiten zeigen',
    formBlockThere: (date: string) => `Seiten zeigen Formulare über den Blocktyp Formular, erstellt am ${date}.`,
    formBlockHow:
      'Erlauben Sie ihn in der Blockliste oder dem Blockraster eines Dokumenttyps, wo Redakteure Formulare hinzufügen dürfen.',
    formBlockMissing:
      'Seiten können noch keine Formulare zeigen: Sie brauchen den Blocktyp Formular, den Redakteure dann einer Blockliste oder einem Blockraster hinzufügen.',
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
    email_senderNameHelp: 'Von wem E-Mails kommen, wie Posteingänge es zeigen.',
    email_senderAddress: 'Adresse',
    email_senderAddressHelp:
      'Verwenden Sie eine Adresse der eigenen Domain der Website, damit Mailserver ihr vertrauen.',
    email_defaultLayout: 'Standardlayout',
    email_defaultLayoutHelp: 'So sehen E-Mails aus, wenn die E-Mail eines Formulars kein anderes Layout wählt.',
    email_defaultLayout_card: 'Karte',
    email_defaultLayout_letter: 'Brief',
    emailBox_team: 'Das Team',
    email_teamRecipients: 'Systemhinweise gehen an',
    email_teamRecipientsHelp: 'Fehlgeschlagene Zustellungen und Warnungen zum Zustand der Website.',
    email_forwardAddresses: 'Adressen, an die Einträge weitergeleitet werden dürfen',
    email_forwardAddressesHelp:
      'Neben Backoffice-Benutzern bietet Weiterleiten nur diese an, damit Einträge die Website nur zu Personen verlassen, denen sie bereits vertraut.',

    // Spamschutz
    protectionBox_level: 'Standardschutz',
    protection_level: 'Schutz',
    protection_levelHelp:
      'Formulare verwenden ihn, wenn ihre Gruppe Spamschutz keinen anderen wählt. reCAPTCHA und hCaptcha laden ein Skript ihres Anbieters, nur in Formularen, die sie verwenden, und nur so, wie es die Einwilligung unten erlaubt.',
    protection_level_standard: 'Standard',
    protection_level_standardLine:
      'Ein verborgenes Feld, eine Mindestzeit zum Ausfüllen und eine signierte Prüfung. Nichts wird von anderen Websites geladen.',
    protection_level_proofOfWork: 'Standard, mit einer Proof-of-Work-Prüfung',
    protection_level_proofOfWorkLine:
      'Der Browser des Besuchers löst zusätzlich ein kurzes Rätsel, das Bots Zeit kostet. Es braucht JavaScript.',
    protection_level_captcha: 'Ein Captcha',
    captchaKeys: 'Schlüssel in der Host-Konfiguration gefunden.',
    captchaNoKeys: (key: string) =>
      `Noch keine Schlüssel. Eine Entwicklerin oder ein Entwickler fügt sie in ${key} hinzu.`,
    captchaGone: 'Nicht mehr auf dieser Website. Wählen Sie einen anderen.',
    protection_captchaProvider: 'Captcha',
    protection_captchaProviderHelp: 'Das Captcha, das Formulare verwenden, wenn ihr Schutz ein Captcha ist.',
    protection_proofBits: 'Stärke des Rätsels',
    protection_proofBitsUnit: 'Bit',
    protection_proofBitsHelp:
      'Jedes weitere Bit verdoppelt die Arbeit für den Browser des Besuchers und für Bots. Der Standard, 16, dauert auf einem Telefon einen Moment.',
    protectionBox_consent: 'Captchas von Drittanbietern',
    protection_captchaConsent: 'Bevor reCAPTCHA oder hCaptcha lädt',
    protection_captchaConsentHelp:
      'Nur Formulare, die reCAPTCHA oder hCaptcha schützt, fragen. Der Standardschutz braucht keine Einwilligung.',
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
    implicitLocked: (key: string) => `Durch die Konfiguration gesperrt: ${key}`,
    protection_cookiebotCategory: 'Cookiebot-Kategorie',
    protection_cookiebotCategoryHelp:
      'Cookiebot ordnet das Cookie eines Captchas als notwendig ein; die Prüfung lädt also sofort, außer Sie wählen Marketing.',
    protection_cookiebotCategory_necessary: 'Notwendig',
    protection_cookiebotCategory_marketing: 'Marketing',
    protectionBox_content: 'Inhaltsprüfungen',
    protectionBox_contentLead:
      'Ein Treffer weist nie einen Eintrag ab: Er verschiebt ihn in den Spam, wo nichts verloren geht. Eine gesperrte E-Mail-Domain genügt allein; gesperrte Wörter und zusätzliche Links zählen zusammen.',
    protection_contentRules: 'Standardstrenge',
    protection_contentRulesHelp: 'Formulare verwenden sie, wenn ihre Gruppe Spamschutz keine andere wählt.',
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
      'Bereiche wie 203.0.113.0/24. Einträge, die von dort gesendet werden, gehen in den Spam. Die Adresse wird beim Eingang eines Eintrags verglichen und nie gespeichert.',

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
      'Webhook-Aktionen signieren jeden Aufruf damit, damit der Empfänger erkennt, dass er von dieser Website kommt. Das Geheimnis selbst bleibt in der Host-Konfiguration.',
    integrations_umbracoWebhookEvents: 'Eintragsereignisse anbieten',
    integrations_umbracoWebhookEventsHelp:
      'Fügt Eintrag gesendet, freigegeben, abgelehnt und gelöscht sowie Aktion fehlgeschlagen den Ereignissen von Umbracos Einstellungen › Webhooks hinzu, wo Entwickler sie abonnieren.',
    hosts: 'Hosts, die Webhooks aufrufen dürfen',
    hostsNone: 'Noch keine, Webhook-Aktionen und JSON-Optionslisten rufen also keine Adresse auf.',
    hostsKey: (key: string) => `Die Host-Konfiguration legt sie fest: ${key}.`,
    hostsHttp: 'Einfache http-Adressen sind ebenfalls erlaubt.',
    integrationsBox_chat: 'Slack und Microsoft Teams',
    integrationsBox_chatLead:
      'Slack- und Microsoft-Teams-Aktionen verwenden diese, wenn sie keine eigenen festlegen. Die Adressen wirken wie Passwörter, darum werden sie als Verweise auf die Host-Konfiguration eingegeben.',
    integrations_slackWebhook: 'Slack-Webhook-Adresse',
    integrations_slackWebhookHelp: 'Wohin Slack-Aktionen posten, wenn sie keine eigene nennen.',
    integrations_teamsWebhook: 'Teams-Webhook-Adresse',
    integrations_teamsWebhookHelp: 'Wohin Teams-Aktionen posten, wenn sie keine eigene nennen.',
    referencePlaceholder: (name: string) => `Ein Verweis, etwa $JironFormBuilder:Secrets:${name}`,
    referenceFound: 'In der Host-Konfiguration gefunden.',
    referenceMissing: (key: string) =>
      `Noch nicht in der Host-Konfiguration: Eine Entwicklerin oder ein Entwickler fügt ${key} hinzu.`,
    referenceInvalid: 'Geben Sie einen Verweis wie $JironFormBuilder:Secrets:Crm ein, nie das Geheimnis selbst.',

    // Datenschutz › Datenerfassung
    privacyBox_insights: 'Einblicke',
    privacyBox_insightsLead:
      'Formulare zählen, was Besucher mit ihnen tun, für die Ansicht Einblicke jedes Formulars und die Übersicht: Aufrufe, Begonnene, die Seiten, die sie erreichen, und die Felder, an denen sie hängen bleiben. Keine Antwort und kein Besucher wird gezählt, und im Browser des Besuchers wird nichts gespeichert.',
    privacy_insights: 'Zählen, was Besucher mit den Formularen tun',
    privacy_insightsHelp: 'Jedes Formular kann es in seiner Ansicht Einstellungen unter Datenschutz ausschalten.',
  },
} satisfies UmbLocalizationDictionary;
