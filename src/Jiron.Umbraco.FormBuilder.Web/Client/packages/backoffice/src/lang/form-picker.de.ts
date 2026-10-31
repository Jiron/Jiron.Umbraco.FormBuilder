// The form picker's screens in German (ADR-0134), in Umbraco's own German terms (docs/ux/copy.md). The type below holds
// them to the keys of form-picker.en.ts, every one and no other, each a text or a function of the same arguments.
import type formPickerEn from './form-picker.en';

export default {
  jfbFormPicker: {
    notAvailable: 'Formular nicht verfügbar',
    notAvailableDetail:
      'Es wurde vielleicht gelöscht oder ist nicht mehr veröffentlicht, oder Sie können es hier nicht wählen. Entfernen Sie es, oder wählen Sie ein anderes Formular.',
    remove: (name: string) => `${name} entfernen`,
    removeNotAvailable: 'Das nicht verfügbare Formular entfernen',
    pickedFailed: 'Die gewählten Formulare konnten nicht geladen werden.',
    tryAgain: 'Erneut versuchen',
    headline: 'Formular wählen',
    headlineMultiple: 'Formulare wählen',
    search: 'Formulare suchen',
    searchPlaceholder: 'Nach Name oder Alias suchen',
    clearSearch: 'Suche zurücksetzen',
    loadFailed: 'Die Formulare konnten nicht geladen werden.',
    searchFailed: 'Die Formulare konnten nicht durchsucht werden.',
    showMore: 'Mehr anzeigen',
    noForms: 'Es gibt noch keine veröffentlichten Formulare zur Auswahl.',
    noAllowedForms: 'Keines der hier erlaubten Formulare ist schon veröffentlicht.',
    emptyFolder: 'In diesem Ordner gibt es noch keine veröffentlichten Formulare.',
    noMatch: (text: string) => `Keine Formulare passen zu „${text}“.`,
    found: (count: number) =>
      count === 1 ? '1 Formular gefunden' : `${new Intl.NumberFormat('de').format(count)} Formulare gefunden`,
    labelNotAvailable: '(nicht verfügbar)',
  },
} satisfies typeof formPickerEn;
