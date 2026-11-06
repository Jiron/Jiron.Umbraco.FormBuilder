// Einstellungen › Allgemein auf Deutsch (ROADMAP 10.3b, IA 8, ADR-0198, docs/ux/copy.md): bisher, wie lange der Verlauf
// eines Formulars gespeicherte Entwürfe behält; 12.4a ergänzt den Rest des Bereichs.
import type { UmbLocalizationDictionary } from '@umbraco-cms/backoffice/localization-api';

export default {
  jfbGeneral: {
    loading: 'Die Einstellungen werden geladen…',
    failed: 'Die Einstellungen konnten nicht gelesen werden.',
    retry: 'Erneut versuchen',
    save: 'Speichern',
    saved: 'Die Einstellungen sind gespeichert.',
    saveFailed: 'Die Einstellungen konnten nicht gespeichert werden. Versuchen Sie es gleich noch einmal.',
    conflict:
      'Jemand anderes hat diese Einstellungen inzwischen gespeichert. Jetzt sehen Sie deren Werte; nehmen Sie Ihre Änderung erneut vor, falls sie noch nötig ist.',
    locked: (key: string) =>
      `Die Konfiguration der Website legt diese Einstellungen fest (${key}); hier lassen sie sich nicht ändern.`,
    fieldLocked: (key: string) => `Die Konfiguration der Website legt das fest (${key}).`,
    range: (min: number, max: number) => `Verwenden Sie eine ganze Zahl von ${min} bis ${max}.`,

    history: 'Verlauf',
    historyLead:
      'Wie lange der Verlauf jedes Formulars seine gespeicherten Entwürfe behält. Veröffentlichte und angeheftete Versionen, der aktuelle Entwurf jedes Formulars und die Versionen, mit denen Einträge gesendet wurden, bleiben immer erhalten.',
    draftDays: 'Gespeicherte Entwürfe behalten für',
    draftDaysUnit: 'Tage',
    draftDaysHelp: 'Jeder gespeicherte Entwurf bleibt mindestens so lange erhalten.',
    draftCount: 'Immer behalten',
    draftCountUnit: 'neueste gespeicherte Entwürfe',
    draftCountHelp: 'Unabhängig von ihrem Alter behält jedes Formular so viele seiner neuesten gespeicherten Entwürfe.',
  },
} satisfies UmbLocalizationDictionary;
