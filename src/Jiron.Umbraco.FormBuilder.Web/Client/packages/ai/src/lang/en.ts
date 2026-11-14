// The AI add-on's words in English (ADR-0018). A key is `jfbAI_<key>`: a `#` key in manifests, localize.term in
// elements. de.ts has the same keys, which lang.test.ts checks.
import type { UmbLocalizationDictionary } from '@umbraco-cms/backoffice/localization-api';

const count = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;

export default {
  jfbAI: {
    draftOption: 'Draft a form with AI…',
    draftOptionDescription: 'From a description or pasted questions.',
    draftHeadline: 'Draft a form with AI',
    draftLabel: 'Describe the form, or paste the text of a questionnaire',
    draftHelp:
      'Say who fills it in and what you need to know. The AI writes a draft for you to review; nothing is published until you publish it.',
    draftSubmit: 'Draft form',
    drafting: 'Drafting the form…',
    draftLeft: (n: number) =>
      `${count(n, "of the AI's questions", "of the AI's questions")} could not be used. Check the form before you publish it.`,
    drafted: 'Drafted by AI, please review',

    translateAction: 'Translate with AI…',
    translateHeadline: 'Translate with AI',
    translateLanguage: 'Language',
    translateReplace: (language: string) => `Also translate the texts that are in ${language} already`,
    translateSubmit: 'Translate',
    translating: 'Translating…',
    translateNone: (language: string) => `Every text is in ${language} already.`,
    translateCount: (n: number, language: string) =>
      `${count(n, 'text', 'texts')} in ${language}. Change a translation, or untick it to keep the text as it is.`,
    translateLeft: (n: number) => `${count(n, 'text', 'texts')} could not be translated and stay as they are.`,
    translateSource: 'Text',
    translateTarget: 'Translation',
    translateRichText: 'Rich text, applied as written',
    translateUse: (text: string) => `Use the translation of “${text}”`,
    translateApply: 'Apply',
    translateApplied: (n: number, language: string) => `Translated ${count(n, 'text', 'texts')} into ${language}`,
    translateChanged: 'The form changed meanwhile. Translate it again.',
    translateOneLanguage: "The site has one language. Add a language in Umbraco's Settings to translate into it.",

    suggest: 'Suggest help text',
    suggesting: 'Asking for suggestions…',
    suggestHeadline: 'Suggestions',
    suggestSetting_label: 'Label',
    suggestSetting_help: 'Help text',
    suggestSetting_placeholder: 'Placeholder',
    suggestUse: 'Use',
    suggestUseText: (what: string, text: string) => `Use as the ${what.toLowerCase()}: ${text}`,
    suggestNone: 'No suggestions: this field reads well.',
    suggestUsed: (what: string) => `Used the suggested ${what.toLowerCase()}`,

    settingsMenu: 'AI',
    settingsBox: 'AI profile',
    settingsLead:
      "The Umbraco.AI profile the AI features use. Without one, they use Umbraco.AI's default chat profile.",
    settingsProfile: 'Profile',
    settingsReady: (name: string) => `The AI features use ${name}.`,
    settingsSave: 'Save',
    settingsSaved: 'Saved',

    problem_ai_no_profile:
      "AI isn't set up yet: choose a profile in Settings › AI, or set a default chat profile in Umbraco.AI.",
    problem_ai_profile_missing: 'The AI profile chosen in Settings › AI no longer exists.',
    problem_ai_profile_cannot_chat: 'The AI profile chosen in Settings › AI is not a chat profile.',
    problem_ai_provider_failed: "The AI didn't answer. Try again in a moment.",
    problem_ai_reply_unusable: "The AI's answer could not be used. Try again.",
    problem_ai_draft_invalid:
      "The AI's draft was not a form Jiron Form Builder can use. Try again, or describe the form differently.",
    problem_ai_language_invalid: 'The language is not one of the site’s other languages.',
    problem_ai_field_missing: 'Save the form, then ask again.',
    problem_text_invalid: 'Describe the form in at most 20,000 characters.',
    problem_profile_invalid: 'That profile cannot chat, or it no longer exists. Choose another one.',
    problem_folder_missing: 'The folder is gone. Start again from another place in the library.',
    problem_folder_not_found: "The folder is gone, or you can't see it any more.",
    problem_form_not_found: "The form is gone, or you can't see it any more.",
    problem_cancelled: "The site's own code stopped this.",
    problem_forbidden: "You don't have the permission to do this.",
    problem_failed: 'Something went wrong. Try again.',
  },
} satisfies UmbLocalizationDictionary;
