// The codes a country field takes (ADR-0058), the same list as Core's BuiltInChecks.CountryCodes, which a unit test
// holds this one to: the ISO 3166-1 alpha-2 codes of the countries and territories, with XK for Kosovo, which browsers
// and .NET name too. The exceptionally reserved codes (such as EU or UK) are not countries a visitor lives in.

/**
 * The codes a country field reads, each in capitals: a first letter followed by the second letters it takes, a group
 * per first letter, which keeps the website runtime small (section 4.12).
 */
export const COUNTRY_CODES: ReadonlySet<string> = new Set(
  (
    'ADEFGILMOQRSTUWXZ BABDEFGHIJLMNOQRSTVWYZ CACDFGHIKLMNORUVWXYZ DEJKMOZ ECEGHRST FIJKMOR ' +
    'GABDEFGHILMNPQRSTUWY HKMNRTU IDELMNOQRST JEMOP KEGHIMNPRWYZ LABCIKRSTUVY ' +
    'MACDEFGHKLMNOPQRSTUVWXYZ NACEFGILOPRUZ OM PAEFGHKLMNRSTWY QA REOSUW SABCDEGHIJKLMNORSTVXYZ ' +
    'TCDFGHJKLMNORTVWZ UAGMSYZ VACEGINU WFS XK YET ZAMW'
  )
    .split(' ')
    .flatMap((group) => [...group.slice(1)].map((second) => group.charAt(0) + second)),
);
