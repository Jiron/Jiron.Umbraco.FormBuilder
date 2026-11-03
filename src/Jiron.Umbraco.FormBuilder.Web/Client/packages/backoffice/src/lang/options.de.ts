// Die Texte der Optionsquellen auf Deutsch (ADR-0073, ADR-0172): die Einstellungen und Auswahlen der Arten, aus denen
// ein Feld seine Optionen nehmen kann, und die Namen der Standardlisten, die die Management-API als
// `#<Bereich>_<Schlüssel>` liefert (docs/api/option-sources.json enthält sie). options.en.ts hat dieselben Schlüssel.
export default {
  jfbOptionListSettings: {
    contentRoot: 'Ausgangsseite',
    contentRootHelp:
      'Die Seite, deren Unterseiten zu Optionen werden. Ohne Auswahl die Seite, auf der das Formular steht.',
    contentDescendants: 'Alle Seiten darunter, nicht nur die direkt darunter',
    contentDocumentTypes: 'Dokumenttypen',
    contentDocumentTypesHelp: 'Nur Seiten dieser Typen werden zu Optionen. Ohne Auswahl Seiten jedes Typs.',
    contentValueFrom: 'Gespeichert als',
    contentValueFromHelp:
      'Was ein Eintrag speichert: key für den Schlüssel der Seite, name für ihren Namen oder der Alias einer ihrer Eigenschaften.',
    contentLabelFrom: 'Angezeigt als',
    contentLabelFromHelp: 'Was Besucher lesen: name für den Namen der Seite oder der Alias einer ihrer Eigenschaften.',
    contentOrder: 'Reihenfolge',
    dataType: 'Datentyp',
    dataTypeHelp:
      'Ein Datentyp für eine Dropdown-Liste, eine Checkbox-Liste oder eine Radiobutton-Liste, dessen Werte zu Optionen werden.',
    url: 'Adresse',
    urlHelp:
      'Die Webadresse, die mit den Optionen als JSON antwortet. Die Website ruft nur Hosts auf, die ihre Konfiguration erlaubt.',
    urlListPath: 'Ort der Liste',
    urlListPathHelp: 'Ein JSON-Pfad in der Antwort, etwa $.items. $ ist die Antwort selbst.',
    urlValueFrom: 'Gespeichert als',
    urlValueFromHelp: 'Der JSON-Pfad in jedem Element zu dem, was ein Eintrag speichert, etwa $.id.',
    urlLabelFrom: 'Angezeigt als',
    urlLabelFromHelp:
      'Der JSON-Pfad in jedem Element zu dem, was Besucher lesen, etwa $.name. Ohne ihn lesen Besucher, was gespeichert wird.',
    countries: 'Länder',
    countriesHelp: 'Ländercodes aus zwei Buchstaben, einer pro Zeile, etwa CH. Ohne Angabe alle Länder.',
    source: 'Liste',
    sourceHelp: 'Der Name einer Liste, die die Website in ihrem Code registriert.',
  },
  jfbOptionListChoices: {
    contentOrderTree: 'Wie im Inhaltsbaum',
    contentOrderLabel: 'Alphabetisch, nach dem, was Besucher lesen',
  },
  jfbOptionSources: {
    months: 'Monate',
    weekdays: 'Wochentage',
    agreement: 'Zustimmung, von „stimme gar nicht zu“ bis „stimme voll zu“',
    satisfaction: 'Zufriedenheit, von „sehr unzufrieden“ bis „sehr zufrieden“',
    yesNoNotSure: 'Ja, nein, weiß nicht',
  },
};
