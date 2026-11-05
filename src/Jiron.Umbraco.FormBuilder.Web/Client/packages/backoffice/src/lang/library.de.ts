// Die Formularbibliothek auf Deutsch (docs/ux/copy.md; ADR-0166): die Markierungen der Bäume, die Seiten eines Ordners und des
// Papierkorbs, die Optionen beim Erstellen, das "..." jedes Elements und die Dialoge und Seitenleisten, die es öffnet. Zahlen
// stehen so, wie man sie im Deutschen schreibt.
const number = (value: number) => new Intl.NumberFormat('de').format(value);
const count = (value: number, one: string, many: string) => `${number(value)} ${value === 1 ? one : many}`;

/** Ein Tag aus einer Liste ("Heute", "3. Nov.") in einem Satz: "heute", oder das Datum nach dem Wort, das es braucht. */
const onDay = (day: string, before: string) =>
  day === 'Heute' || day === 'Gestern' ? day.toLowerCase() : `${before} ${day}`;

/** "Das Formular ist auf 3 Seiten eingebunden: Startseite, Kontakt, Veranstaltungen." mit "und 2 weiteren" nach den Namen. */
const pickedOn = (pages: number, names: string, more: number, still = '') => {
  const where = count(pages, 'Seite', 'Seiten');
  const lead = `Das Formular ist ${still}auf ${where} eingebunden`;
  if (!names) return `${lead}.`;
  return more > 0 ? `${lead}: ${names} und ${number(more)} weiteren.` : `${lead}: ${names}.`;
};

/** Einträge und Dateien im Dativ, nach "mit ihren" oder "mit seinen". */
const withHeld = (owner: 'sein' | 'ihr', entries: number, files: number) => {
  const held = entries === 1 ? `${owner}em Eintrag` : `${owner}en ${number(entries)} Einträgen`;
  return files > 0 ? `${held} und ${count(files, 'hochgeladenen Datei', 'hochgeladenen Dateien')}` : held;
};

export default {
  jfbLibrary: {
    recycleBin: 'Papierkorb',
    topOfForms: 'der obersten Ebene von „Formulare“',
    sameFolder: 'demselben Ordner',
    untitledForm: 'Unbenanntes Formular',
    notPublished: 'nicht veröffentlicht',
    restricted: 'Eingeschränkt',
    unseenEntries: 'Neue Einträge',
    inPlace: (name: string, place: string) => `${name}, in ${place}`,
    moreActionsFor: (name: string) => `Weitere Aktionen für ${name}`,
    loading: 'Wird geladen',
    tryAgain: 'Erneut versuchen',
    none: 'keine',
    today: 'Heute',
    yesterday: 'Gestern',
    dayBy: (day: string, name: string) => `${day}, von ${name}`,

    actionReload: 'Neu laden',
    actionRenameFolder: 'Umbenennen…',
    actionMove: 'Verschieben nach…',
    actionDeleteFolder: 'Löschen…',
    actionDuplicate: 'Duplizieren…',
    actionCopyActions: 'Aktionen kopieren nach…',
    actionExport: 'Als JSON exportieren',
    actionTrash: 'In den Papierkorb',
    actionEmptyBin: 'Papierkorb leeren…',
    actionRestore: 'Wiederherstellen…',
    actionDeleteForGood: 'Löschen…',
    optionForm: 'Formular',
    optionFormDescription: 'Ein neues, leeres Formular.',
    optionFromTemplate: 'Formular aus einer Vorlage…',
    optionFromTemplateDescription: 'Etwa ein Kontaktformular, zum Anpassen.',
    optionImport: 'Formular importieren…',
    optionImportDescription: 'Ein exportiertes Formular als JSON-Datei.',
    optionFolder: 'Ordner…',
    optionFolderDescription: 'Bündelt Formulare, mit eigenem Zugriff.',
    createForm: 'Formular erstellen',
    createFolder: 'Ordner erstellen',

    columnName: 'Name',
    columnEntries: 'Einträge, letzte 30 Tage',
    columnChanged: 'Zuletzt geändert',
    columnTrashed: 'Verschoben',
    columnActions: 'Aktionen',
    listFailed: 'Die Formulare konnten nicht geladen werden.',
    emptyFolder:
      'Hier gibt es noch keine Formulare. Mit einer Vorlage haben Sie in einer Minute ein fertiges Formular.',
    emptyFolderReadOnly: 'Hier gibt es noch keine Formulare.',
    chooseTemplate: 'Vorlage wählen…',
    loadMore: 'Mehr anzeigen',
    folderName: 'Ordnername',
    binFailed: 'Der Papierkorb konnte nicht geladen werden.',
    binEmpty:
      'Der Papierkorb ist leer. Formulare im Papierkorb warten hier mit ihren Einträgen, bis jemand sie wiederherstellt oder endgültig löscht.',
    emptyBin: 'Papierkorb leeren…',
    itemGone: 'Dieses Element gibt es nicht mehr, oder Sie haben keinen Zugriff darauf.',
    // Ein kurzes Datum endet schon mit einem Punkt ("3. Nov."), der dann auch den Satz beendet.
    inBinSince: (day: string) => {
      const text = `Im Papierkorb seit ${onDay(day, 'dem')}`;
      return text.endsWith('.') ? text : `${text}.`;
    },
    notInBin: 'Dieses Element liegt nicht mehr im Papierkorb.',

    newFolder: 'Neuer Ordner',
    renameFolder: (name: string) => `${name} umbenennen`,
    rename: 'Umbenennen',
    name: 'Name',
    nameNeeded: 'Geben Sie einen Namen ein.',
    deleteFolderHeadline: (name: string) => `${name} löschen?`,
    deleteFolderText: 'Der Ordner ist leer, es wird also nichts anderes mit ihm gelöscht.',
    deleteFolderConfirm: 'Ordner löschen',

    trashHeadline: (name: string) => `${name} in den Papierkorb verschieben?`,
    trashInUse: (pages: number, names: string, more: number) =>
      `${pickedOn(pages, names, more)} Dort erscheint nichts, bis Sie es wiederherstellen.`,
    trashConfirm: 'In den Papierkorb',
    trashed: (name: string) => `${name} in den Papierkorb verschoben.`,
    undo: 'Zurücknehmen',
    mayNotChange: (name: string) =>
      `Sie dürfen ${name} nicht ändern. Wer Administratorrechte hat, kann Ihrer Benutzergruppe unter Einstellungen › Zugriff Zugang geben.`,

    widenHeadline: (name: string) => `Mehr Benutzergruppen Zugriff auf ${name} geben?`,
    widenText: (groups: number) =>
      groups > 0
        ? `Am neuen Ort ${groups === 1 ? 'sieht es 1 weitere Benutzergruppe' : `sehen es ${number(groups)} weitere Benutzergruppen`}, mit allem, was es enthält.`
        : 'Am neuen Ort sehen es weitere Benutzergruppen, mit allem, was es enthält.',
    widenRestoreText: 'Dort, wohin es zurückkehrt, sehen es mehr Benutzergruppen als vor dem Papierkorb.',
    widenConfirm: 'Zugriff geben',

    restore: 'Wiederherstellen',
    restoreToTop: (name: string) => `${name} kehrt in die oberste Ebene von „Formulare“ zurück.`,
    restoreToFolder: (name: string, folder: string) => `${name} kehrt in den Ordner ${folder} zurück.`,
    restoreToWhereItWas: (name: string) => `${name} kehrt an seinen bisherigen Platz zurück.`,
    restoreFolderTrashed: (name: string, folder: string) =>
      `Der Ordner ${folder} liegt auch im Papierkorb, deshalb kehrt ${name} in die oberste Ebene von „Formulare“ zurück.`,
    restoreFormComesBack: (entries: number) =>
      entries === 1 ? 'Sein Eintrag kehrt mit zurück.' : `Seine ${number(entries)} Einträge kehren mit zurück.`,
    restoreFolderComesBack: 'Die Formulare und Ordner, die mit ihm in den Papierkorb kamen, kehren mit zurück.',

    deleteFormHeadline: (name: string) => `${name} endgültig löschen?`,
    deleteFormText: (entries: number, files: number) => {
      if (entries === 0) return 'Dabei wird das Formular gelöscht. Es hat keine Einträge.';
      const held = entries === 1 ? 'sein Eintrag' : `seine ${number(entries)} Einträge`;
      if (!(files > 0)) return `Dabei werden das Formular und ${held} gelöscht.`;
      const uploaded =
        files === 1
          ? 'eine hochgeladene Datei'
          : `${number(files)} ${entries === 1 ? 'hochgeladene' : 'hochgeladenen'} Dateien`;
      return `Dabei werden das Formular, ${held} und ${uploaded} gelöscht.`;
    },
    deleteFormInUse: (pages: number, names: string, more: number) =>
      `${pickedOn(pages, names, more, 'noch ')} Dort erscheint weiterhin nichts.`,
    deleteFormConfirm: (entries: number) =>
      entries === 0
        ? 'Formular löschen'
        : entries === 1
          ? 'Formular und Eintrag löschen'
          : 'Formular und Einträge löschen',
    deleteTrashedFolderHeadline: (name: string) => `${name} endgültig löschen?`,
    deleteTrashedFolderText:
      'Dabei werden der Ordner und die Formulare, Ordner und Einträge, die mit ihm in den Papierkorb kamen, endgültig gelöscht.',
    deleteTrashedFolderConfirm: 'Ordner und Inhalt löschen',
    cannotUndo: 'Das lässt sich nicht rückgängig machen.',

    emptyHeadline: 'Papierkorb leeren?',
    emptyText: (forms: number, entries: number, files: number, folders: number) => {
      if (forms === 0)
        return folders === 1
          ? 'Dabei wird der Ordner darin endgültig gelöscht.'
          : `Dabei werden die ${number(folders)} Ordner darin endgültig gelöscht.`;
      const one = forms === 1;
      const gone = one ? 'Dabei wird 1 Formular' : `Dabei werden ${number(forms)} Formulare`;
      if (entries === 0) return `${gone} endgültig gelöscht. ${one ? 'Es hat' : 'Sie haben'} keine Einträge.`;
      return `${gone} mit ${withHeld(one ? 'sein' : 'ihr', entries, files)} endgültig gelöscht.`;
    },
    emptyLeft: (left: number) =>
      left === 1
        ? '1 Element, das Sie nicht löschen dürfen, bleibt darin.'
        : `${number(left)} Elemente, die Sie nicht löschen dürfen, bleiben darin.`,
    emptyConfirm: 'Papierkorb leeren',
    emptyNothingYours: 'Im Papierkorb ist nichts, das Sie endgültig löschen dürfen.',

    folderNotEmpty: 'Dieser Ordner enthält noch Formulare oder Ordner. Verschieben oder löschen Sie sie zuerst.',
    formGone: 'Dieses Formular gibt es nicht mehr, oder Sie haben keinen Zugriff darauf.',
    folderGone: 'Diesen Ordner gibt es nicht mehr, oder Sie haben keinen Zugriff darauf.',
    templateGone: 'Diese Vorlage gibt es nicht mehr. Wählen Sie eine andere.',
    changedMeanwhile:
      'Jemand hat das geändert, nachdem Sie es geöffnet haben. Laden Sie die Seite neu und versuchen Sie es erneut.',
    missingPermission: (permission: string) =>
      `Dafür brauchen Sie die Berechtigung „${permission}“. Wer Administratorrechte hat, kann sie unter Einstellungen › Zugriff vergeben.`,
    failed: 'Das hat nicht geklappt. Versuchen Sie es erneut, oder laden Sie die Seite neu.',
    permission_design: 'Formulare gestalten',
    permission_manageActions: 'Aktionen verwalten',
    permission_viewEntries: 'Einträge ansehen',
    permission_viewSensitive: 'Vertrauliche Daten ansehen',
    permission_editEntries: 'Einträge bearbeiten',
    permission_deleteEntries: 'Einträge löschen',
    permission_export: 'Einträge exportieren',
    permission_manageLibrary: 'Bibliothek verwalten',
    permission_manageSettings: 'Einstellungen verwalten',
    permission_managePermissions: 'Berechtigungen verwalten',
    permission_privacyTools: 'Datenschutzwerkzeuge nutzen',

    duplicateHeadline: (name: string) => `${name} duplizieren`,
    duplicate: 'Duplizieren',
    copysName: 'Name der Kopie',
    copyName: (name: string) => `${name} (Kopie)`,
    copyActionsToo: (actions: number) =>
      actions === 1 ? 'Auch seine Aktion kopieren' : `Auch seine ${number(actions)} Aktionen kopieren`,
    duplicateRestricted: (where: string) =>
      `Dieses Formular hat eigene Zugriffsrechte, die die Kopie nicht übernimmt: Die Kopie landet in ${where} und erhält den Zugriff, der dort gilt.`,

    copyActionsHeadline: (source: string) => `Aktionen aus ${source} kopieren`,
    copyTo: 'Kopieren nach',
    noTargetYet: 'Noch kein Formular gewählt',
    chooseForm: 'Formular wählen…',
    chooseAnotherForm: 'Anderes Formular wählen…',
    whichActions: 'Zu kopierende Aktionen',
    noActionsToCopy: (source: string) => `${source} hat keine Aktionen, die sich kopieren lassen.`,
    copyCount: (actions: number) => (actions === 1 ? '1 Aktion kopieren' : `${number(actions)} Aktionen kopieren`),
    copied: (actions: number, target: string) => `${count(actions, 'Aktion', 'Aktionen')} nach ${target} kopiert.`,
    copiedClean: (actions: number) =>
      actions === 1
        ? 'Dort findet die Aktion alles, was sie braucht.'
        : 'Dort finden die Aktionen alles, was sie brauchen.',
    copiedNeedsFixing: (actions: number, troubled: number) =>
      `${actions === 1 ? 'Die Aktion nennt' : troubled === actions ? 'Die Aktionen nennen' : 'Einige der Aktionen nennen'} etwas, das dieses Formular nicht hat. Beheben Sie das vor dem Veröffentlichen:`,
    copyLacksField: (alias: string) => `Das Feld „${alias}“`,
    copyLacksElement: 'Eine Seite, Gruppe oder ein Feld',
    copyLacksTag: (tag: string) => `Der Platzhalter ${tag}`,
    fixIn: 'Im Formular beheben',
    openForm: (name: string) => `${name} öffnen`,

    galleryHeadline: 'Vorlage wählen',
    searchTemplates: 'Vorlagen durchsuchen',
    templates: 'Vorlagen',
    builtIn: 'Mitgeliefert',
    savedHere: 'Auf dieser Website gespeichert',
    galleryLoading: 'Die Vorlagen werden geladen',
    galleryFailed: 'Die Vorlagen konnten nicht geladen werden.',
    galleryNoMatch: (query: string) => `Keine Vorlage passt zu „${query}“.`,
    clearSearch: 'Suche zurücksetzen',
    holds: (pages: number, fields: number) => `${count(pages, 'Seite', 'Seiten')}, ${count(fields, 'Feld', 'Felder')}`,
    preview: 'Vorschau',
    allTemplates: 'Alle Vorlagen',
    useTemplate: 'Vorlage verwenden',
    template_contact: 'Kontakt',
    template_contactDescription: 'Name, E-Mail-Adresse und eine Nachricht, danach eine Dankesnachricht.',

    importHeadline: 'Formular importieren',
    importIntro:
      'Wählen Sie eine JSON-Datei, die eine Website mit Jiron Form Builder exportiert hat. Das Formular kommt als neue Kopie an; hier ändert sich sonst nichts.',
    chooseFile: 'Datei wählen…',
    chooseAnotherFile: 'Andere Datei wählen…',
    readingFile: 'Die Datei wird gelesen',
    importTooLarge:
      'Diese Datei ist größer als 5 MB und damit kein Formularexport. Wählen Sie die JSON-Datei, die der Export gespeichert hat.',
    importUnreadable: (reason: string) =>
      reason
        ? `Diese Datei lässt sich nicht importieren: ${reason}`
        : 'Diese Datei lässt sich nicht importieren. Wählen Sie die JSON-Datei, die der Export gespeichert hat.',
    fileExported: (size: string, day: string) => `${size}, exportiert ${onDay(day, 'am')}`,
    whatItHolds: 'Was sie enthält',
    holdsForm: 'Formular',
    holdsPages: 'Seiten',
    holdsFields: 'Felder',
    holdsRules: 'Regeln',
    holdsOutcomes: 'Abschlüsse',
    holdsActions: 'Aktionen',
    exportsNeverHold: 'Ein Export enthält nie Einträge, Dateien oder Zugriffsrechte.',
    whatItCannotImport: 'Was dieser Website fehlt',
    whatComesAlong: 'Was mitkommt',
    noteThemeMissing: (theme: string) =>
      `Diese Website hat kein Theme „${theme}“, deshalb verwendet das Formular das Standard-Theme.`,
    noteListReused: (list: string) =>
      `Die Optionsliste „${list}“ gibt es auf dieser Website schon, das Formular verwendet sie.`,
    noteListCreated: (list: string) => `Die Optionsliste „${list}“ kommt mit und wird dieser Website hinzugefügt.`,
    noteListMissing: (list: string) =>
      `Diese Website hat keine Optionsliste „${list}“. Felder, die sie verwenden, zeigen keine Optionen, bis Sie eine wählen.`,
    noteListUnnamed:
      'Ein Feld verwendet eine Optionsliste, die die Datei nicht nennt. Es zeigt keine Optionen, bis Sie eine wählen.',
    noteActionConfiguration: (key: string) =>
      `Die Einstellung „${key}“ einer Aktion braucht auf dieser Website einen Wert. Bis dahin bleibt die Aktion ausgeschaltet.`,
    noteActionType: (type: string) =>
      `Diese Website hat keinen Aktionstyp „${type}“, deshalb bleibt die Aktion ausgeschaltet.`,
    notePageMissing:
      'Ein Abschluss leitet auf eine Seite weiter, die diese Website nicht hat. Wählen Sie dafür eine andere Seite.',
    noteOther: (code: string) => `Der Import meldet „${code}“. Prüfen Sie das Formular, bevor Sie es veröffentlichen.`,
    arrivesAsDraft: (where: string) =>
      `Es kommt als Entwurf in ${where} an. Besucher sehen nichts davon, bis Sie es veröffentlichen.`,
    importAsNewCopy: 'Als neue Kopie importieren',
  },
};
