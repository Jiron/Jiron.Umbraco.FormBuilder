// Die Texte der Vorschau auf Deutsch (ROADMAP 10.3e und 10.3f, ADR-0203, ADR-0204); preview.en.ts sagt, was jeder ist.

export default {
  jfbPreview: {
    headline: (name: string) => `Vorschau von ${name}`,
    width: 'Breite',
    more: 'Weitere Vorschauoptionen',
    scheme: 'Farbschema',
    light: 'Hell',
    dark: 'Dunkel',
    frame: 'Das Formular, wie Besucher es sehen',
    loading: 'Die Vorschau wird geladen…',
    rulesInvalid:
      'Die Regeln dieses Entwurfs haben Fehler, deshalb lässt er sich nicht so zeigen, wie Besucher ihn bekämen. Die Formularprüfung in der Ansicht „Gestaltung“ listet sie auf.',
    sample: 'Mit Beispielantworten füllen',
    startAgain: 'Neu beginnen',
    testEntries: 'Testeinträge',
    runForTests: 'Aktionen für Testeinträge ausführen',
    noteOff: 'Hier gesendete Einträge sind Testeinträge. Sie führen keine Aktionen aus.',
    noteOn:
      'Hier gesendete Einträge sind Testeinträge. Ihre Aktionen laufen, ihre E-Mails und Nachrichten gehen also wirklich hinaus.',
    saved: (number: number) => `Als Testeintrag #${number} gespeichert.`,
    noneRan: 'Es liefen keine Aktionen.',
    running: 'Seine Aktionen laufen.',
    awaiting: 'Seine Aktionen laufen, sobald seine E-Mail-Adresse bestätigt ist.',
    sentOnly: 'Als Testeintrag gesendet.',
    runActions: 'Aktionen ausführen…',
    runActionsFor: (number: number) => `Aktionen ausführen für Testeintrag #${number}…`,
    runHeadline: (number: number) => `Aktionen für Testeintrag #${number} ausführen?`,
    runText: (count: number) =>
      count === 1
        ? 'Diese Aktion läuft wie bei einem echten Eintrag; ihre E-Mail oder Nachricht geht also wirklich hinaus.'
        : `Diese ${count} Aktionen laufen wie bei einem echten Eintrag; ihre E-Mails und Nachrichten gehen also wirklich hinaus.`,
    runConfirm: (count: number) => (count === 1 ? '1 Aktion ausführen' : `${count} Aktionen ausführen`),
    runFailed: 'Die Aktionen konnten nicht ausgeführt werden.',
    expired: 'Diese Vorschau kann nichts mehr senden: Ihr Zugang ist abgelaufen.',
    loadAgain: 'Neu laden',
  },
};
