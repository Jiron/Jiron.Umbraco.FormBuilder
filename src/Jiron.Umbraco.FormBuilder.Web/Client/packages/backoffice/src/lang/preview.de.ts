// Die Texte der Vorschau auf Deutsch (ROADMAP 10.3e, ADR-0203); preview.en.ts sagt, was jeder ist.

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
  },
};
