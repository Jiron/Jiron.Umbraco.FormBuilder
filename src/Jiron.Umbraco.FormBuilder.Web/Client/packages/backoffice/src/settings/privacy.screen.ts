// Privacy > Data collection (ROADMAP 12.4a, IA 8 and 9, ADR-0218): the settings screen of the privacy section, without
// the retention days, which Privacy > Retention shows with its dry run (ADR-0222).
import { settingsScreen } from './settings-screen.element';

export const element = settingsScreen('jfb-settings-privacy', 'privacy', 'dataCollection', ['retention']);
