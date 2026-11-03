// Die Texte des Formular-Arbeitsbereichs auf Deutsch (ROADMAP 9.3a, docs/ux/copy.md): Ansichten, Kopfzeile und Fußzeile.
import type { UmbLocalizationDictionary } from '@umbraco-cms/backoffice/localization-api';

export default {
  jfbDesigner: {
    view_design: 'Gestaltung',
    view_settings: 'Einstellungen',
    view_actions: 'Aktionen',
    view_entries: 'Einträge',
    view_insights: 'Auswertung',
    view_history: 'Verlauf',

    formName: 'Name des Formulars',
    state_draft: 'Entwurf',
    state_published: 'Veröffentlicht',
    state_pending: 'Veröffentlicht (Änderungen bereit)',
    state_unpublished: 'Nicht veröffentlicht',

    saveDraft: 'Entwurf speichern',
    publish: 'Veröffentlichen…',
    publishHeadline: (name: string) => `${name} veröffentlichen?`,
    publishText: 'Besucher erhalten ab jetzt diese Version des Formulars.',
    comment: 'Kommentar zu dieser Version (optional)',
    publishConfirm: 'Veröffentlichen',
    published: (name: string) => `${name} ist veröffentlicht.`,

    pageNumber: (number: number) => `Seite ${number}`,
    untitledField: 'Unbenanntes Feld',
    emptyGroup: 'Diese Gruppe hat noch keine Felder.',
    emptyPage: 'Diese Seite hat noch keine Gruppen.',
  },
} satisfies UmbLocalizationDictionary;
