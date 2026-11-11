// The AI add-on's words in German (ADR-0018), with the keys of en.ts, which lang.test.ts checks.
import type { UmbLocalizationDictionary } from '@umbraco-cms/backoffice/localization-api';

const count = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;

export default {
  jfbAI: {
    draftOption: 'Formular mit KI entwerfen…',
    draftOptionDescription: 'Aus einer Beschreibung oder dem eingefügten Text eines Fragebogens.',
    draftHeadline: 'Formular mit KI entwerfen',
    draftLabel: 'Beschreiben Sie das Formular, oder fügen Sie den Text eines Fragebogens ein',
    draftHelp:
      'Sagen Sie, wer es ausfüllt und was Sie wissen müssen. Die KI schreibt einen Entwurf, den Sie prüfen; veröffentlicht wird erst, wenn Sie veröffentlichen.',
    draftSubmit: 'Formular entwerfen',
    drafting: 'Das Formular wird entworfen…',
    draftLeft: (n: number) =>
      `${count(n, 'Frage der KI', 'Fragen der KI')} ${n === 1 ? 'konnte' : 'konnten'} nicht verwendet werden. Prüfen Sie das Formular, bevor Sie es veröffentlichen.`,
    drafted: 'Von KI entworfen, bitte prüfen',

    translateAction: 'Mit KI übersetzen…',
    translateHeadline: 'Mit KI übersetzen',
    translateLanguage: 'Sprache',
    translateReplace: (language: string) => `Auch die Texte übersetzen, die schon auf ${language} sind`,
    translateSubmit: 'Übersetzen',
    translating: 'Wird übersetzt…',
    translateNone: (language: string) => `Jeder Text ist schon auf ${language}.`,
    translateCount: (n: number, language: string) =>
      `${count(n, 'Text', 'Texte')} auf ${language}. Ändern Sie eine Übersetzung, oder nehmen Sie das Häkchen weg, um den Text zu behalten.`,
    translateLeft: (n: number) =>
      `${count(n, 'Text konnte', 'Texte konnten')} nicht übersetzt werden und ${n === 1 ? 'bleibt' : 'bleiben'}, wie ${n === 1 ? 'er ist' : 'sie sind'}.`,
    translateSource: 'Text',
    translateTarget: 'Übersetzung',
    translateRichText: 'Formatierter Text, wird so übernommen',
    translateUse: (text: string) => `Die Übersetzung von „${text}“ übernehmen`,
    translateApply: 'Übernehmen',
    translateApplied: (n: number, language: string) => `${count(n, 'Text', 'Texte')} auf ${language} übersetzt`,
    translateChanged: 'Das Formular hat sich inzwischen geändert. Übersetzen Sie es noch einmal.',
    translateOneLanguage:
      'Die Website hat eine Sprache. Fügen Sie in den Einstellungen von Umbraco eine Sprache hinzu, um in sie zu übersetzen.',

    suggest: 'Hilfetext vorschlagen',
    suggesting: 'Vorschläge werden erfragt…',
    suggestHeadline: 'Vorschläge',
    suggestSetting_label: 'Beschriftung',
    suggestSetting_help: 'Hilfetext',
    suggestSetting_placeholder: 'Platzhalter',
    suggestUse: 'Übernehmen',
    suggestUseText: (what: string, text: string) => `Als ${what} übernehmen: ${text}`,
    suggestNone: 'Keine Vorschläge: Dieses Feld liest sich gut.',
    suggestUsed: (what: string) => `${what} aus dem Vorschlag übernommen`,

    settingsMenu: 'KI',
    settingsBox: 'KI-Profil',
    settingsLead:
      'Das Profil von Umbraco.AI, das die KI-Funktionen verwenden. Ohne eines verwenden sie das Standard-Chat-Profil von Umbraco.AI.',
    settingsProfile: 'Profil',
    settingsReady: (name: string) => `Die KI-Funktionen verwenden ${name}.`,
    settingsSave: 'Speichern',
    settingsSaved: 'Gespeichert',

    problem_ai_no_profile:
      'KI ist noch nicht eingerichtet: Wählen Sie ein Profil unter Einstellungen › KI, oder legen Sie in Umbraco.AI ein Standard-Chat-Profil fest.',
    problem_ai_profile_missing: 'Das unter Einstellungen › KI gewählte Profil gibt es nicht mehr.',
    problem_ai_profile_cannot_chat: 'Das unter Einstellungen › KI gewählte Profil ist kein Chat-Profil.',
    problem_ai_provider_failed: 'Die KI hat nicht geantwortet. Versuchen Sie es gleich noch einmal.',
    problem_ai_reply_unusable: 'Die Antwort der KI war nicht zu verwenden. Versuchen Sie es noch einmal.',
    problem_ai_draft_invalid:
      'Der Entwurf der KI war kein Formular, das Jiron Form Builder verwenden kann. Versuchen Sie es noch einmal, oder beschreiben Sie das Formular anders.',
    problem_ai_language_invalid: 'Die Sprache ist keine der anderen Sprachen der Website.',
    problem_ai_field_missing: 'Speichern Sie das Formular, und fragen Sie dann noch einmal.',
    problem_text_invalid: 'Beschreiben Sie das Formular in höchstens 20.000 Zeichen.',
    problem_profile_invalid: 'Dieses Profil kann nicht chatten, oder es gibt es nicht mehr. Wählen Sie ein anderes.',
    problem_folder_missing: 'Den Ordner gibt es nicht mehr. Beginnen Sie an einem anderen Ort der Bibliothek.',
    problem_folder_not_found: 'Den Ordner gibt es nicht mehr, oder Sie sehen ihn nicht mehr.',
    problem_form_not_found: 'Das Formular gibt es nicht mehr, oder Sie sehen es nicht mehr.',
    problem_cancelled: 'Der eigene Code der Website hat das verhindert.',
    problem_forbidden: 'Sie haben nicht die Berechtigung dazu.',
    problem_failed: 'Etwas ist schiefgegangen. Versuchen Sie es noch einmal.',
  },
} satisfies UmbLocalizationDictionary;
