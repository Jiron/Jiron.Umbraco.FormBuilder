// The form workspace's texts in English (ROADMAP 9.3a, docs/ux/copy.md): its views, its header and its footer.
import type { UmbLocalizationDictionary } from '@umbraco-cms/backoffice/localization-api';

export default {
  jfbDesigner: {
    view_design: 'Design',
    view_settings: 'Settings',
    view_actions: 'Actions',
    view_entries: 'Entries',
    view_insights: 'Insights',
    view_history: 'History',

    formName: 'Form name',
    state_draft: 'Draft',
    state_published: 'Published',
    state_pending: 'Published (pending changes)',
    state_unpublished: 'Unpublished',

    saveDraft: 'Save draft',
    publish: 'Publish…',
    publishHeadline: (name: string) => `Publish ${name}?`,
    publishText: 'Visitors get this version of the form from now on.',
    comment: 'Comment for this version (optional)',
    publishConfirm: 'Publish',
    published: (name: string) => `${name} is published.`,

    pageNumber: (number: number) => `Page ${number}`,
    untitledField: 'Untitled field',
    emptyGroup: 'No fields in this group yet.',
    emptyPage: 'No groups on this page yet.',
  },
} satisfies UmbLocalizationDictionary;
