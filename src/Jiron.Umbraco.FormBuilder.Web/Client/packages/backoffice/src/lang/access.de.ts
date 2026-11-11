// Einstellungen › Zugriff und die Seitenleiste Berechtigungen eines Ordners oder Formulars auf Deutsch (ROADMAP 12.4a,
// IA 8, ADR-0013, ADR-0218).
import type { UmbLocalizationDictionary } from '@umbraco-cms/backoffice/localization-api';

const list = (names: readonly string[]) =>
  names.length <= 1 ? (names[0] ?? '') : `${names.slice(0, -1).join(', ')} und ${names[names.length - 1]}`;

export default {
  jfbAccess: {
    loading: 'Der Zugriff wird geladen…',
    failed: 'Der Zugriff konnte nicht gelesen werden.',
    retry: 'Erneut versuchen',
    save: 'Speichern',
    cancel: 'Abbrechen',
    saved: 'Der Zugriff ist gespeichert.',
    saveFailed: 'Der Zugriff konnte nicht gespeichert werden. Versuchen Sie es gleich noch einmal.',
    invalid:
      'Der Zugriff konnte so nicht gespeichert werden. Lesen Sie ihn erneut und nehmen Sie Ihre Änderung noch einmal vor.',
    conflict:
      'Jemand anderes hat den Zugriff inzwischen geändert. Jetzt sehen Sie dessen Berechtigungen; nehmen Sie Ihre Änderung erneut vor, falls sie noch nötig ist.',
    lockedNow:
      'Die Konfiguration der Website hat den Zugriff inzwischen gesperrt. Er wird so gezeigt, wie sie ihn festlegt.',

    everyForm: 'Jedes Formular',
    everyFormLead: 'Formulare und Einträge können je Ordner oder Formular abweichen.',
    administrators: 'Administratoren haben überall jede Berechtigung, weil sie sich jede selbst erteilen könnten.',
    noSection: (count: number) =>
      count === 1
        ? 'Eine Gruppe kann Form Builder noch nicht öffnen. Fügen Sie den Bereich ihrer Benutzergruppe hinzu:'
        : `${count} Gruppen können Form Builder noch nicht öffnen. Fügen Sie den Bereich ihren Benutzergruppen hinzu:`,
    lockedGrants: (key: string) =>
      `Die Konfiguration der Website legt fest, was jede Gruppe mit jedem Formular darf (${key}); hier lässt sich das nicht ändern. Ordner und Formulare können weiterhin eigenen Zugriff haben.`,
    lockedAll: (key: string) =>
      `Die Konfiguration der Website legt den Zugriff fest (${key}); hier lässt er sich nicht ändern.`,
    matrixCaption: 'Was jede Benutzergruppe mit jedem Formular darf',
    permission: 'Berechtigung',
    part_forms: 'Formulare',
    part_entries: 'Einträge',
    part_library: 'Bibliothek, Einstellungen und Datenschutz',
    cell: (permission: string, group: string) => `${permission}, ${group}`,
    alsoOn: (permission: string, besides: readonly string[]) =>
      `${list(besides)} ${besides.length === 1 ? 'ist' : 'sind'} ebenfalls an: ${permission} braucht ${besides.length === 1 ? 'das' : 'diese'}.`,
    alsoOff: (permission: string, besides: readonly string[]) =>
      `${list(besides)} ${besides.length === 1 ? 'ist' : 'sind'} ebenfalls aus: ${besides.length === 1 ? 'es braucht' : 'sie brauchen'} ${permission}.`,

    items: 'Ordner und Formulare mit eigenem Zugriff',
    addItem: 'Ordner oder Formular hinzufügen…',
    itemsNone:
      'Kein Ordner und kein Formular hat eigenen Zugriff; der Zugriff jeder Gruppe oben reicht also bis zu jedem Formular.',
    only: (names: readonly string[]) => `Nur diese Benutzergruppen: ${list(names)}`,
    onlyAdministrators: 'Nur Administratoren',
    also: (names: readonly string[]) => `Zusätzlich: ${list(names)}`,
    alsoNone: 'Keine Gruppe hat hier mehr',
    open: (name: string) => `Berechtigungen für ${name}`,
    folder: 'Ordner',
    form: 'Formular',

    headline: (name: string) => `Berechtigungen für ${name}`,
    restrict: 'Nur diese Benutzergruppen',
    restrictHelpFolder:
      'Nur die Gruppen unten und Administratoren sehen die Formulare dieses Ordners. Zugriff auf jedes Formular oder auf einen übergeordneten Ordner endet hier.',
    restrictHelpForm:
      'Nur die Gruppen unten und Administratoren sehen dieses Formular. Zugriff auf jedes Formular oder auf seinen Ordner endet hier.',
    groupsHere: 'Gruppen mit Zugriff hier',
    addGroup: 'Benutzergruppe hinzufügen',
    addGroupNone: 'Benutzergruppe wählen',
    removeGroup: (group: string) => `${group} entfernen`,
    noneHereFolder: 'Noch keine. Zugriff auf jedes Formular reicht bis in diesen Ordner.',
    noneHereForm: 'Noch keine. Zugriff auf jedes Formular reicht bis zu diesem Formular.',
    noneHereRestricted: 'Noch keine, also sehen nur Administratoren, was hier ist.',
    sidebarCaption: (name: string) => `Was jede Benutzergruppe in ${name} darf`,
    trashedFolder: 'Dieser Ordner ist im Papierkorb. Stellen Sie ihn wieder her, um seinen Zugriff zu ändern.',
    trashedForm: 'Dieses Formular ist im Papierkorb. Stellen Sie es wieder her, um seinen Zugriff zu ändern.',
    gone: 'Dieser Ordner oder dieses Formular ist nicht mehr da.',
    restrictHeadline: (name: string) => `${name} beschränken?`,
    restrictText: 'Noch hat keine Benutzergruppe hier eigenen Zugriff; nur Administratoren werden die Formulare sehen.',
    restrictFolder: 'Ordner beschränken',
    restrictForm: 'Formular beschränken',
    savedTarget: (name: string) => `Die Berechtigungen von ${name} sind gespeichert.`,
  },
} satisfies UmbLocalizationDictionary;
