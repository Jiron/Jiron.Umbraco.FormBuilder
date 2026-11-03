// The library's sentences with numbers and names (docs/ux/copy.md, Confirmations; ADR-0166): copy.md's examples as written,
// and the counts it leaves out (one, none, more names than a confirmation lists).
import { describe, expect, it } from 'vitest';
import de from './library.de';
import en from './library.en';

const EN = en.jfbLibrary;
const DE = de.jfbLibrary;

describe('trashing a form that pages pick', () => {
  it("says copy.md's example", () => {
    expect(EN.trashInUse(3, 'Home, Contact, Events', 0)).toBe(
      "It's picked on 3 pages: Home, Contact, Events. They show nothing in its place until you restore it.",
    );
    expect(DE.trashInUse(3, 'Startseite, Kontakt, Veranstaltungen', 0)).toBe(
      'Das Formular ist auf 3 Seiten eingebunden: Startseite, Kontakt, Veranstaltungen. Dort erscheint nichts, bis Sie es wiederherstellen.',
    );
  });

  it('says one page, the pages past the names, and pages the user may not open', () => {
    expect(EN.trashInUse(1, 'Home', 0)).toBe(
      "It's picked on 1 page: Home. It shows nothing in its place until you restore it.",
    );
    expect(EN.trashInUse(7, 'A, B, C, D, E', 2)).toContain("It's picked on 7 pages: A, B, C, D, E and 2 more.");
    expect(DE.trashInUse(7, 'A, B, C, D, E', 2)).toContain('auf 7 Seiten eingebunden: A, B, C, D, E und 2 weiteren.');
    expect(EN.trashInUse(2, '', 2)).toContain("It's picked on 2 pages. They show");
    expect(DE.trashInUse(1, '', 1)).toContain('Das Formular ist auf 1 Seite eingebunden. Dort');
  });
});

describe('deleting a form for good', () => {
  it("says copy.md's example", () => {
    expect(EN.deleteFormText(1204, 37)).toBe('This deletes the form with its 1,204 entries and 37 uploaded files.');
    expect(DE.deleteFormText(1204, 37)).toBe(
      'Dabei werden das Formular, seine 1.204 Einträge und 37 hochgeladenen Dateien gelöscht.',
    );
    expect(EN.deleteFormConfirm(1204)).toBe('Delete form and entries');
    expect(DE.deleteFormConfirm(1204)).toBe('Formular und Einträge löschen');
  });

  it('says one entry, no files and no entries', () => {
    expect(EN.deleteFormText(1, 0)).toBe('This deletes the form with its 1 entry.');
    expect(DE.deleteFormText(1, 1)).toBe(
      'Dabei werden das Formular, sein Eintrag und eine hochgeladene Datei gelöscht.',
    );
    expect(DE.deleteFormText(5, 0)).toBe('Dabei werden das Formular und seine 5 Einträge gelöscht.');
    expect(EN.deleteFormText(0, 0)).toBe('This deletes the form. It has no entries.');
    expect(EN.deleteFormConfirm(0)).toBe('Delete form');
    expect(DE.deleteFormConfirm(1)).toBe('Formular und Eintrag löschen');
  });

  it('says that the pages still pick it', () => {
    expect(EN.deleteFormInUse(3, 'Home, Contact, Events', 0)).toBe(
      "It's still picked on 3 pages: Home, Contact, Events. They keep showing nothing in its place.",
    );
    expect(DE.deleteFormInUse(1, 'Startseite', 0)).toBe(
      'Das Formular ist noch auf 1 Seite eingebunden: Startseite. Dort erscheint weiterhin nichts.',
    );
  });
});

describe('emptying the Recycle Bin', () => {
  it("says copy.md's example", () => {
    expect(EN.emptyText(3, 412, 12)).toBe(
      'This deletes 3 forms for good, with their 412 entries and 12 uploaded files.',
    );
    expect(DE.emptyText(3, 412, 12)).toBe(
      'Dabei werden 3 Formulare mit ihren 412 Einträgen und 12 hochgeladenen Dateien endgültig gelöscht.',
    );
    expect(EN.emptyConfirm).toBe('Empty recycle bin');
    expect(DE.emptyConfirm).toBe('Papierkorb leeren');
  });

  it('says one form, no entries, only folders, and what stays', () => {
    expect(EN.emptyText(1, 1, 0)).toBe('This deletes 1 form for good, with its 1 entry.');
    expect(DE.emptyText(1, 1, 1)).toBe(
      'Dabei wird 1 Formular mit seinem Eintrag und 1 hochgeladenen Datei endgültig gelöscht.',
    );
    expect(EN.emptyText(2, 0, 0)).toBe('This deletes 2 forms for good. They have no entries.');
    expect(DE.emptyText(0, 0, 0)).toBe('Dabei werden die Ordner darin endgültig gelöscht.');
    expect(EN.emptyLeft(1)).toBe('1 item you may not delete stays in it.');
    expect(DE.emptyLeft(4)).toBe('4 Elemente, die Sie nicht löschen dürfen, bleiben darin.');
  });
});

describe('days inside sentences', () => {
  it('write today and yesterday small, and a date after its word', () => {
    expect(EN.inBinSince('Today')).toBe('In the Recycle Bin since today.');
    expect(EN.inBinSince('3 Nov')).toBe('In the Recycle Bin since 3 Nov.');
    expect(DE.inBinSince('3. Nov.')).toBe('Im Papierkorb seit dem 3. Nov.');
    expect(DE.inBinSince('Gestern')).toBe('Im Papierkorb seit gestern.');
    expect(EN.fileExported('12 KB', 'Yesterday')).toBe('12 KB, exported yesterday');
    expect(EN.fileExported('12 KB', '24 Oct')).toBe('12 KB, exported on 24 Oct');
    expect(DE.fileExported('12 KB', '24. Okt.')).toBe('12 KB, exportiert am 24. Okt.');
    expect(DE.fileExported('12 KB', 'Heute')).toBe('12 KB, exportiert heute');
  });
});

describe('the counts of the other dialogs', () => {
  it('say one and many', () => {
    expect(EN.copyActionsToo(1)).toBe('Copy its action too');
    expect(EN.copyActionsToo(3)).toBe('Copy its 3 actions too');
    expect(DE.copyCount(2)).toBe('2 Aktionen kopieren');
    expect(EN.copied(1, 'Events')).toBe('1 action copied to Events.');
    expect(EN.holds(1, 3)).toBe('1 page, 3 fields');
    expect(DE.pagesWithFields(2, 1)).toBe('2 Seiten mit 1 Feld');
    expect(EN.widenText(1)).toBe('In its new place, 1 more user group sees it and what it holds.');
    expect(DE.widenText(2)).toBe('Am neuen Ort sehen es 2 weitere Benutzergruppen, mit allem, was es enthält.');
    expect(EN.restoreFormComesBack(1)).toBe('Its 1 entry comes back with it.');
    expect(DE.restoreFormComesBack(1204)).toBe('Seine 1.204 Einträge kehren mit zurück.');
  });
});
