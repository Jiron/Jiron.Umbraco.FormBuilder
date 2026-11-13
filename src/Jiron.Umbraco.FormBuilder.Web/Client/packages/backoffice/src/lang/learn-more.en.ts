// The words of the "Learn more" links in English (ROADMAP 17.2a, docs/ux/copy.md): one phrase per topic of
// docs/learn-more.json, each making sense on its own, and what a screen reader hears after it. In the start dictionary,
// as the links stand on screens in every part of the section. learn-more.de.ts has the same keys.
import type { UmbLocalizationDictionary } from '@umbraco-cms/backoffice/localization-api';

export default {
  jfbLearnMore: {
    newTab: '(opens in a new tab)',
    formBlock: 'Learn more about form blocks',
    migrations: 'Learn more about the migrations',
    sendingEmail: 'Learn more about sending email',
    mailLimits: 'Learn more about email limits',
    fileScanners: 'Learn more about virus scanners',
    signatures: 'Learn more about checking signatures',
    protectionLevels: 'Learn more about spam protection',
    contentChecks: 'Learn more about content checks',
    blockedRanges: 'Learn more about blocked address ranges',
    captchaConsent: 'Learn more about consent for captchas',
    retention: 'Learn more about retention',
    ipAddresses: 'Learn more about IP addresses',
    insights: 'Learn more about what Insights count',
    memberDeletion: "Learn more about members' entries",
    keepAnswer: 'Learn more about sensitive answers',
    patterns: 'Learn more about patterns',
    mergeTags: 'Learn more about merge tags',
    visitorTexts: 'Learn more about visitor texts',
    headless: 'Learn more about headless sites',
  },
} satisfies UmbLocalizationDictionary;
