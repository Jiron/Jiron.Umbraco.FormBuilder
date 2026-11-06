// Settings › General in English (ROADMAP 10.3b, IA 8, ADR-0198, docs/ux/copy.md): so far how long a form's History keeps
// draft saves; 12.4a adds the rest of the section.
import type { UmbLocalizationDictionary } from '@umbraco-cms/backoffice/localization-api';

export default {
  jfbGeneral: {
    loading: 'Loading the settings…',
    failed: "The settings couldn't be read.",
    retry: 'Try again',
    save: 'Save',
    saved: 'The settings are saved.',
    saveFailed: "The settings couldn't be saved. Try again in a moment.",
    conflict:
      'Someone else saved these settings meanwhile. Their values are shown now; make your change again if it is still needed.',
    locked: (key: string) => `The site's configuration sets these settings (${key}), so they can't be changed here.`,
    fieldLocked: (key: string) => `The site's configuration sets this (${key}).`,
    range: (min: number, max: number) => `Use a whole number from ${min} to ${max}.`,

    history: 'History',
    historyLead:
      "How long each form's History keeps its draft saves. Published and pinned versions, each form's current draft and the versions entries were sent with are always kept.",
    draftDays: 'Keep draft saves for',
    draftDaysUnit: 'days',
    draftDaysHelp: 'Every draft save stays at least this long.',
    draftCount: 'Always keep the newest',
    draftCountUnit: 'draft saves',
    draftCountHelp: 'However old they are, each form keeps this many of its newest draft saves.',
  },
} satisfies UmbLocalizationDictionary;
