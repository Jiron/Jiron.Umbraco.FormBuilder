// The global settings screens in English (ROADMAP 12.4a, IA 8 and 9, ADR-0218, docs/ux/copy.md): Settings › General,
// Email, Spam protection, Files and Integrations, and Privacy › Data collection. Each field's label, help, unit and
// choices are the keys its section serves (`#jfbGlobalSettings_<section>_<field>`), each box's headline is
// `<section>Box_<box>`, and the rest is what the screen says around them. They load with the screens, not with the
// backoffice's start, whose budget counts every word it loads (ADR-0183).
import type { UmbLocalizationDictionary } from '@umbraco-cms/backoffice/localization-api';

const and = (parts: readonly string[]) => new Intl.ListFormat('en-GB', { type: 'conjunction' }).format(parts);

export default {
  jfbGlobalSettings: {
    loading: 'Loading the settings…',
    failed: "The settings couldn't be read.",
    retry: 'Try again',
    save: 'Save',
    saved: 'The settings are saved.',
    saveFailed: "The settings couldn't be saved. Try again in a moment.",
    invalid: 'Some settings need another value. Each one says why.',
    conflict:
      'Someone else saved these settings meanwhile. Their values are shown now; make your change again if it is still needed.',
    locked: (key: string) => `The site's configuration sets these settings (${key}), so they can't be changed here.`,
    lockedNow: "The site's configuration has locked these settings meanwhile. They are shown as it sets them.",
    moreOptions: (count: number) => (count === 1 ? 'More options (1)' : `More options (${count})`),
    fewerOptions: 'Fewer options',
    range: (min: string, max: string) => `Use a whole number from ${min} to ${max}.`,
    discardHeadline: 'Discard your changes?',
    discardText: 'The changes on this screen are not saved yet. Another site shows its own values.',
    discardConfirm: 'Discard',

    site: 'Site',
    allSites: 'All sites',
    siteShared: 'Settings every site shares are under All sites.',
    siteOwn: 'This site has its own value.',
    siteUseAll: 'Use the value for all sites',
    siteGone: 'This site is no longer in Content. Choose another.',
    language: 'Texts in',

    // General
    generalBox_forms: 'Defaults for forms',
    general_theme: 'Default theme',
    general_themeHelp: 'Forms use it unless their Appearance group picks another.',
    general_requiredMarker: 'Fields that get a mark',
    general_requiredMarkerHelp:
      'Most forms have more required fields than optional ones, so marking the few optional ones reads calmer.',
    general_requiredMarker_required: 'Required fields, with "*"',
    general_requiredMarker_optional: 'Optional fields, with "(optional)"',
    general_requiredMarker_none: 'No fields',
    general_submitLabel: 'Send button',
    general_submitLabelHelp: "What the last page's button says, unless a form labels it. Left empty, it says Send.",
    general_nextLabel: 'Next button',
    general_nextLabelHelp:
      "What a page's button to the next one says, unless a form labels it. Left empty, it says Next.",
    general_backLabel: 'Back button',
    general_backLabelHelp: "What a page's button back says, unless a form labels it. Left empty, it says Back.",
    general_texts: 'Visitor texts',
    general_textsHelp:
      "Your own words for the texts every form brings, such as its hints, notices and error messages. A form's own words come first.",
    generalBox_site: 'Address and time',
    general_baseUrl: 'Base URL',
    general_baseUrlHelp: 'Links in emails start with it. Left empty, they start with the address shown in the box.',
    general_timeZone: 'Time zone',
    general_timeZoneHelp:
      "Dates in emails and exports use it, and so do the times a form opens and closes. Type a city's first letters to find it.",
    zonesWithoutArea: 'Universal',
    generalBox_newForms: 'New forms',
    generalBox_newFormsLead:
      'What a form created from blank starts with. A template brings its own fields and actions.',
    general_newForms: 'New forms',
    general_newFormsHelp: 'What a form created from blank starts with.',
    general_newForms_consent: 'Add a consent field',
    general_newForms_consentHelp:
      "A required box at the end of the last page, with the site's own wording. Forms that keep personal data need one where consent is their basis.",
    consentLabel: 'Label',
    consentText: 'Wording',
    consentNeedsLabel: 'Give the consent field a label.',
    general_newForms_notifyCreator: "Email each entry to the form's creator",
    general_newForms_notifyCreatorHelp: 'New forms start with an email action to whoever creates them.',
    generalBox_history: 'History',
    generalBox_historyLead:
      "How long each form's History keeps its draft saves. Published and pinned versions, each form's current draft and the versions entries were sent with are always kept.",
    general_history: 'History',
    general_historyHelp: "How long each form's History keeps its draft saves.",
    general_history_draftDays: 'Keep draft saves for',
    general_history_draftDaysUnit: 'days',
    general_history_draftDaysHelp: 'Every draft save stays at least this long.',
    general_history_draftCount: 'Always keep the newest',
    general_history_draftCountUnit: 'draft saves',
    general_history_draftCountHelp: 'However old they are, each form keeps this many of its newest draft saves.',
    formBlock: 'Putting forms on pages',
    formBlockThere: (date: string) => `Pages show forms through the block type Form, created on ${date}.`,
    formBlockHow: "Allow it on a document type's block list or block grid where editors may add forms.",
    formBlockMissing:
      "Pages can't show forms yet: they need the block type Form, which editors then add to a block list or block grid.",
    formBlockCreate: 'Create a form block type…',
    formBlockAsk: "A user with Umbraco's Settings section can create it.",

    // Email
    cannotSend:
      "Emails can't be sent yet: no mail server is configured. Entries are kept, and their emails go out once sending works.",
    cannotSendKey: (key: string) => `A developer sets up sending in ${key}.`,
    sendTest: 'Send test email',
    testSent: (to: string) => `Test email sent to you (${to}).`,
    testFailed: "The test email couldn't be sent. Try again in a moment.",
    emailBox_sender: 'Sender',
    email_senderName: 'Name',
    email_senderNameHelp: 'Who mail comes from, as inboxes show it.',
    email_senderAddress: 'Address',
    email_senderAddressHelp: "Use an address of the site's own domain, so mail servers trust it.",
    email_defaultLayout: 'Default layout',
    email_defaultLayoutHelp: "Mail looks like this unless a form's email picks another layout.",
    email_defaultLayout_card: 'Card',
    email_defaultLayout_letter: 'Letter',
    emailBox_team: 'The team',
    email_teamRecipients: 'System notices go to',
    email_teamRecipientsHelp: 'Failed deliveries and health warnings.',
    email_forwardAddresses: 'Addresses entries may be forwarded to',
    email_forwardAddressesHelp:
      'Besides backoffice users, Forward offers only these, so entries leave the site only to people it already trusts.',

    // Spam protection
    protectionBox_level: 'Default protection',
    protection_level: 'Protection',
    protection_levelHelp:
      'Forms use it unless their Spam protection group picks another. reCAPTCHA and hCaptcha load a script from their provider, only on forms that use them, and only as the consent below allows.',
    protection_level_standard: 'Standard',
    protection_level_standardLine:
      'A hidden field, a minimum fill time and a signed check. Nothing loads from other sites.',
    protection_level_proofOfWork: 'Standard, with a proof-of-work check',
    protection_level_proofOfWorkLine:
      "The visitor's browser also solves a short puzzle, which costs bots time. It needs JavaScript.",
    protection_level_captcha: 'A captcha',
    captchaKeys: 'Keys found in host configuration.',
    captchaNoKeys: (key: string) => `No keys yet. A developer adds them in ${key}.`,
    captchaGone: 'No longer on this site. Choose another.',
    protection_captchaProvider: 'Captcha',
    protection_captchaProviderHelp: 'The captcha forms use when their protection is a captcha.',
    protection_proofBits: 'Puzzle strength',
    protection_proofBitsUnit: 'bits',
    protection_proofBitsHelp:
      "Each bit more doubles the work for the visitor's browser and for bots. The default, 16, takes a phone a moment.",
    protectionBox_consent: 'Third-party captchas',
    protection_captchaConsent: 'Before reCAPTCHA or hCaptcha loads',
    protection_captchaConsentHelp:
      'Only forms protected by reCAPTCHA or hCaptcha ask. Standard protection needs no consent.',
    protection_captchaConsent_explicit: 'Ask on the form',
    protection_captchaConsent_explicitLine: 'The form shows a short notice and a button that loads the check.',
    protection_captchaConsent_cookiebot: 'Follow a Cookiebot category',
    protection_captchaConsent_cookiebotLine: 'It loads once the visitor accepts the category picked below.',
    protection_captchaConsent_cmp: "Follow the site's consent manager",
    protection_captchaConsent_cmpLine:
      "It loads once the consent manager calls JironFormBuilder.consent.grant('captcha').",
    protection_captchaConsent_implicit: 'Load without asking',
    protection_captchaConsent_implicitLine: 'It loads with the form, for a site that has consent for it another way.',
    choiceLocked: (key: string) => `Locked by configuration: ${key}`,
    protection_cookiebotCategory: 'Cookiebot category',
    protection_cookiebotCategoryHelp:
      "Cookiebot files a captcha's cookie as necessary, so the check loads at once unless you pick Marketing.",
    protection_cookiebotCategory_necessary: 'Necessary',
    protection_cookiebotCategory_marketing: 'Marketing',
    protectionBox_content: 'Content checks',
    protectionBox_contentLead:
      'A match never refuses an entry: it moves it to Spam, where nothing is lost. A blocked email domain is enough on its own; blocked words and extra links add up.',
    protection_contentRules: 'Default strictness',
    protection_contentRulesHelp: 'Forms use it unless their Spam protection group picks another.',
    protection_contentRules_off: 'Off',
    protection_contentRules_normal: 'Normal: three blocked words, or two with too many links',
    protection_contentRules_strict: 'Strict: two blocked words, or one with too many links',
    protection_blockedWords: 'Blocked words',
    protection_blockedWordsHelp: 'Words or phrases, in any case. Each one found counts toward Spam.',
    protection_blockedDomains: 'Blocked email domains',
    protection_blockedDomainsHelp: 'An entry with an email address at one of them goes to Spam.',
    protection_maxLinks: 'The most links an entry may hold',
    protection_maxLinksHelp: 'Counted over all answers. More links than this count toward Spam.',
    protectionBox_ranges: 'Blocked address ranges',
    protection_blockedRanges: 'Ranges',
    protection_blockedRangesHelp:
      'Ranges such as 203.0.113.0/24. Entries sent from them go to Spam. The address is compared when an entry arrives and never stored.',

    // Files
    filesBox_types: 'What visitors may upload',
    files_allowedTypes: 'Allowed types',
    files_allowedTypesHelp: 'Each file field picks from these.',
    files_allowedTypes_images: 'Images',
    files_allowedTypes_pdf: 'PDF',
    files_allowedTypes_office: 'Office documents',
    files_allowedTypes_text: 'Text',
    files_allowedTypes_archives: 'Archives',
    blockedExtensions: (key: string) =>
      `Programs, scripts and web pages are never accepted, whatever a field allows: ${key}.`,
    files_maxFileBytes: 'Largest file',
    files_maxFileBytesUnit: 'MB',
    files_maxFileBytesHelp: 'Each file field sets its own limit, up to this one.',
    hostCap: (megabytes: string, key: string) => `Host configuration allows at most ${megabytes} MB: ${key}`,
    sizeInvalid: (megabytes: string) => `Use a number of megabytes up to ${megabytes}, such as 20 or 2.5.`,
    filesChecks: 'Checks and storage',
    typeCheck: 'Type check',
    typeCheckLine: "Every file's contents must match its type, whatever its name says.",
    scanner: 'Malware scanner',
    scannerNone: "None installed, so files aren't checked for malware.",
    scannerSome: (count: number) =>
      count === 1
        ? 'One installed: each file is checked before anyone can download it.'
        : `${count} installed: each file is checked before anyone can download it.`,
    storedIn: 'Stored in',
    storedFolder: (key: string) =>
      `A private folder outside the website (${key}). Files have no public address: users download them from their entry.`,
    storedCustom:
      'A file store the site registers. Files have no public address: users download them from their entry.',

    // Integrations
    integrationsBox_webhooks: 'Webhooks',
    integrations_signingSecret: 'Signing secret',
    integrations_signingSecretHelp:
      'Webhook actions sign every call with it, so the receiver can tell it came from this site. The secret itself stays in host configuration.',
    integrations_umbracoWebhookEvents: 'Offer entry events',
    integrations_umbracoWebhookEventsHelp:
      "Adds Entry submitted, approved, rejected and deleted, and Action failed, to the events of Umbraco's Settings › Webhooks, where developers subscribe to them.",
    hosts: 'Hosts that webhooks may call',
    hostsNone: 'None yet, so webhook actions and JSON option lists call no address.',
    hostsKey: (key: string) => `Host configuration sets them: ${key}.`,
    hostsHttp: 'Plain http addresses are allowed too.',
    integrationsBox_chat: 'Slack and Microsoft Teams',
    integrationsBox_chatLead:
      'Slack and Microsoft Teams actions use these unless they set their own. The addresses work like passwords, so they are entered as references to host configuration.',
    integrations_slackWebhook: 'Slack webhook address',
    integrations_slackWebhookHelp: 'Where Slack actions post unless they name their own.',
    integrations_teamsWebhook: 'Teams webhook address',
    integrations_teamsWebhookHelp: 'Where Teams actions post unless they name their own.',
    referencePlaceholder: (name: string) => `A reference, such as $JironFormBuilder:Secrets:${name}`,
    referenceFound: 'Found in host configuration.',
    referenceMissing: (key: string) => `Not in host configuration yet: a developer adds ${key}.`,
    referenceInvalid: 'Enter a reference such as $JironFormBuilder:Secrets:Crm, never the secret itself.',

    // Privacy › Retention
    privacyBox_retention: 'How long entries are kept',
    privacy_retention: 'Days entries are kept',
    privacy_retentionHelp:
      "Counted from an entry's last change of state. Test entries from the preview go after 7 days.",
    privacy_retention_submitted: 'Submitted',
    privacy_retention_submittedHelp: 'Entries waiting for approval count here too.',
    privacy_retention_submittedUnit: 'days',
    privacy_retention_approved: 'Approved',
    privacy_retention_approvedHelp: 'Entries a reviewer approved, or that were approved at once.',
    privacy_retention_approvedUnit: 'days',
    privacy_retention_rejected: 'Rejected',
    privacy_retention_rejectedHelp: 'Entries a reviewer rejected.',
    privacy_retention_rejectedUnit: 'days',
    privacy_retention_spam: 'Spam',
    privacy_retention_spamHelp: 'Entries the checks or a reviewer moved to Spam.',
    privacy_retention_spamUnit: 'days',
    retentionForGood: 'Keep for good',
    retentionForGoodShown: 'Kept for good',
    retentionDaysShown: (days: string) => `${days} days`,
    retentionReadOnly:
      'Changing retention needs the "Manage settings" permission. An administrator can grant it in Settings › Access.',
    retentionForGoodLocked: 'Keeping entries for good is locked by configuration:',
    retentionOwnBox: 'Forms with their own retention',
    retentionOwnNone: 'No form sets days of its own. A form can, in its Settings view under Privacy.',
    retentionOwnState: (state: string, days: string) => `${state}: ${days} days`,
    retentionOwnStore: (days: string) => `Every entry: ${days} days after it was sent`,
    retentionTrashed: 'In the Recycle Bin',
    retentionWhatBox: 'What will be deleted',
    retentionSpan: 'Deleted within',
    retentionSpan_7: '7 days',
    retentionSpan_30: '30 days',
    retentionSpan_90: '90 days',
    retentionNothing: 'Nothing is deleted in that time.',
    retentionTotal: (entries: string, forms: string, oneEntry: boolean, oneForm: boolean) =>
      `${entries} ${oneEntry ? 'entry' : 'entries'} in ${forms} ${oneForm ? 'form' : 'forms'}`,
    retentionHeld: (count: string, one: boolean) =>
      one
        ? `${count} more is on legal hold and stays until the hold ends.`
        : `${count} more are on legal hold and stay until their holds end.`,
    retentionHeldStay: 'Entries on legal hold stay.',
    retentionUnsaved: 'Counted with the days above, before you save.',
    retentionByItself: 'Nothing needs to be pressed: retention deletes by itself every hour.',
    retentionColumnForm: 'Form',
    retentionColumnEntries: 'Entries',
    retentionColumnFiles: 'Files',
    retentionNoFiles: 'none',
    retentionCounting: 'Counting…',
    retentionCountFailed: "The entries couldn't be counted.",

    // Privacy › People
    people_findBox: "Find a person's data",
    people_email: 'Email address',
    people_emailPlaceholder: 'For example: name@example.com',
    people_find: 'Find',
    people_findHelp: 'Every form is searched, restricted ones included, and the search is recorded in the audit log.',
    people_byMember: 'Find a member instead…',
    people_byEmail: 'Use an email address instead',
    people_member: 'Member',
    people_chooseMember: 'Choose a member',
    people_someMember: 'the member',
    people_emailInvalid: 'Enter one email address, such as name@example.com.',
    people_entries: (count: string, one: boolean) => `${count} ${one ? 'entry' : 'entries'}`,
    people_forms: (count: string, one: boolean) => `${count} ${one ? 'form' : 'forms'}`,
    people_files: (count: string, one: boolean) => `${count} ${one ? 'file' : 'files'}`,
    people_emails: (count: string, one: boolean) => `${count} ${one ? 'email' : 'emails'}`,
    people_drafts: (count: string, one: boolean) => `${count} saved ${one ? 'draft' : 'drafts'}`,
    people_found: (entries: string, forms: string) => `${entries} in ${forms}`,
    people_more: 'These are the newest 500. Erase them, then find again for the rest.',
    people_none: (who: string) =>
      `No entries found for ${who}. Entries sent while signed in are found through the member.`,
    people_noneFound: (who: string) => `No entries found for ${who}.`,
    people_failed: "The entries couldn't be found.",
    people_export: 'Export everything',
    people_hold: 'Set legal hold…',
    people_erase: 'Erase…',
    people_columnEntry: 'Entry',
    people_columnForm: 'Form',
    people_columnReceived: 'Received',
    people_columnFiles: 'Files',
    people_columnEmails: 'Emails',
    people_entry: (number: string) => `Entry #${number}`,
    people_draft: 'Saved draft',
    people_test: 'Test entry',
    people_trashed: 'In the Recycle Bin',
    people_noneCount: 'none',
    people_heldUntil: (until: string) => `Legal hold until ${until}`,
    people_release: 'Release',
    people_releaseLabel: (entry: string) => `Release the legal hold on ${entry}`,
    people_released: 'The legal hold is released.',
    people_exporting: (done: string, total: string) =>
      `Preparing the export: ${done} of ${total} entries. You can keep working; a notification links to the file.`,
    people_exportLabel: 'Export',
    people_exportFailed: "The export couldn't be prepared. Try again.",
    people_erased: (who: string, parts: readonly string[]) =>
      `Everything about ${who} is erased: ${and(parts)}. Statistics stay.`,
    people_erasedAnonymised: (who: string, entries: string, one: boolean, deleted: readonly string[]) =>
      `Everything about ${who} is erased: ${entries} anonymised, which ${one ? 'stays' : 'stay'} in the counts${deleted.length > 0 ? `, and ${and(deleted)} deleted` : ''}. Statistics stay.`,
    people_erasedHeld: (entries: string, one: boolean) =>
      one
        ? `${entries} on legal hold stays until its hold ends.`
        : `${entries} on legal hold stay until their holds end.`,
    people_auditOpen: 'Open the audit log',
    people_auditRecorded: "It's recorded in the audit log.",
    people_eraseHeadline: (who: string) => `Erase everything about ${who}?`,
    people_eraseText: (parts: readonly string[], one: boolean) =>
      `${and(parts)} ${one ? 'is' : 'are'} erased. Statistics stay. You can't undo this.`,
    people_eraseHeld: (entries: string, one: boolean) =>
      one
        ? `${entries} on legal hold stays until its hold ends.`
        : `${entries} on legal hold stay until their holds end.`,
    people_eraseMore: 'Only the newest 500 entries are erased this time; find again for the rest.',
    people_eraseFormHeld: (count: string) => `${count} on legal hold`,
    people_eraseHow: 'What happens to the entries',
    people_eraseDelete: 'Delete them',
    people_eraseDeleteHelp: 'The entries go, with their files and emails.',
    people_eraseAnonymise: 'Anonymise them',
    people_eraseAnonymiseHelp: 'The entries stay in the counts without any answer, file or email of the person.',
    people_eraseConfirm: (entries: string) => `Erase ${entries}`,
    people_eraseNothing: 'Every entry is on legal hold, so there is nothing to erase until the holds end.',
    people_eraseFailed: "The erase couldn't be done. Nothing changed; try again.",
    people_holdHeadline: 'Set legal hold',
    people_holdEntries: 'Entries',
    people_holdOf: (form: string, received: string) => `${form}, ${received}`,
    people_holdReason: 'Reason',
    people_holdReasonHelp: 'Kept with the hold. The audit log records who set it and until when.',
    people_holdUntil: 'Keep until',
    people_holdUntilHelp: 'At most a year from today. You can extend it before it ends.',
    people_holdNote: 'Held entries are kept past their retention, and Erase… leaves them out until the hold ends.',
    people_holdConfirm: (entries: string) => `Set legal hold on ${entries}`,
    people_holdChoose: 'Choose at least one entry.',
    people_holdReasonNeeded: 'Give the reason on one line, in at most 500 characters.',
    people_holdEndInvalid: 'Choose a day after today and at most a year ahead.',
    people_holdFailed: "The legal hold couldn't be set. Try again.",
    people_holdDone: (entries: string) => `${entries} on legal hold.`,

    // Privacy › Data collection
    privacyBox_ip: 'IP addresses',
    privacyBox_ipLead:
      'Each entry can keep the address it was sent from. Rate limits and blocked address ranges read the address without keeping it.',
    privacy_ip: 'What entries keep',
    privacy_ipHelp: 'Each form can choose its own in its Settings view, under Privacy.',
    privacy_ip_none: 'Nothing',
    privacy_ip_noneLine: "Entries don't keep the address.",
    privacy_ip_anonymised: 'The address with its end removed',
    privacy_ip_anonymisedLine:
      'Enough to tell a network apart: 203.0.113.0 for 203.0.113.42, and the first 48 bits of an IPv6 address.',
    privacy_ip_full: 'The full address',
    privacy_ip_fullLine: 'Only for a site that needs it whole, such as to report abuse. Say so in your privacy policy.',
    privacyBox_insights: 'Insights',
    privacyBox_insightsLead:
      "Forms count what visitors do with them, for each form's Insights view and the Overview: views, starts, the pages they reach and the fields that stop them. No answer and no visitor is counted, and nothing is stored in the visitor's browser.",
    privacy_insights: 'Count what visitors do with the forms',
    privacy_insightsHelp: 'Each form can turn it off in its Settings view, under Privacy.',
    privacyBox_members: 'Members',
    privacyBox_membersLead:
      'When a member is deleted in Umbraco, their entries in every form go with them, or keep nothing that names them. Their drafts always go, and a legal hold keeps its entries as they are.',
    privacy_memberDeleted: 'When a member is deleted',
    privacy_memberDeletedHelp: 'The same for every site, since a member belongs to none.',
    privacy_memberDeleted_anonymise: 'Anonymise their entries',
    privacy_memberDeleted_anonymiseLine:
      'The entries stay with their form, number and state, so counts and statistics keep them; every answer and everything else about the member goes.',
    privacy_memberDeleted_delete: 'Delete their entries',
    privacy_memberDeleted_deleteLine: 'The entries go, with their files and mail.',
  },
} satisfies UmbLocalizationDictionary;
