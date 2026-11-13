// The words of the "Learn more" links in German (ROADMAP 17.2a), in the backoffice's own German terms (docs/ux/copy.md).
// The type below holds them to the keys of learn-more.en.ts, every one and no other.
import type learnMoreEn from './learn-more.en';

export default {
  jfbLearnMore: {
    newTab: '(öffnet in einem neuen Tab)',
    formBlock: 'Mehr zu Blocktypen für Formulare',
    migrations: 'Mehr zu den Migrationen',
    sendingEmail: 'Mehr zum Versand von E-Mails',
    mailLimits: 'Mehr zu den Grenzen für E-Mails',
    fileScanners: 'Mehr zu Virenscannern',
    signatures: 'Mehr zum Prüfen von Signaturen',
    protectionLevels: 'Mehr zum Spamschutz',
    contentChecks: 'Mehr zu Inhaltsprüfungen',
    blockedRanges: 'Mehr zu gesperrten Adressbereichen',
    captchaConsent: 'Mehr zur Einwilligung für Captchas',
    retention: 'Mehr zur Aufbewahrung',
    ipAddresses: 'Mehr zu IP-Adressen',
    insights: 'Mehr dazu, was Einblicke zählen',
    memberDeletion: 'Mehr zu den Einträgen von Mitgliedern',
    keepAnswer: 'Mehr zu vertraulichen Antworten',
    patterns: 'Mehr zu Mustern',
    mergeTags: 'Mehr zu Platzhaltern',
    visitorTexts: 'Mehr zu Texten für Besucher',
    headless: 'Mehr zu Headless-Websites',
  },
} satisfies { [Area in keyof typeof learnMoreEn]: Record<keyof (typeof learnMoreEn)[Area], string> };
