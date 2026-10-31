// The codes a country field takes (ADR-0058), the same list as Core's BuiltInChecks.CountryCodes, which a unit test
// holds this one to: the ISO 3166-1 alpha-2 codes of the countries and territories, with XK for Kosovo, which browsers
// and .NET name too. The exceptionally reserved codes (such as EU or UK) are not countries a visitor lives in.

/**
 * The codes a country field reads, each in capitals: two letters each, written together a line per first letter, which
 * keeps the website runtime small (section 4.12).
 */
export const COUNTRY_CODES: ReadonlySet<string> = new Set(
  (
    'ADAEAFAGAIALAMAOAQARASATAUAWAXAZ' +
    'BABBBDBEBFBGBHBIBJBLBMBNBOBQBRBSBTBVBWBYBZ' +
    'CACCCDCFCGCHCICKCLCMCNCOCRCUCVCWCXCYCZ' +
    'DEDJDKDMDODZ' +
    'ECEEEGEHERESET' +
    'FIFJFKFMFOFR' +
    'GAGBGDGEGFGGGHGIGLGMGNGPGQGRGSGTGUGWGY' +
    'HKHMHNHRHTHU' +
    'IDIEILIMINIOIQIRISIT' +
    'JEJMJOJP' +
    'KEKGKHKIKMKNKPKRKWKYKZ' +
    'LALBLCLILKLRLSLTLULVLY' +
    'MAMCMDMEMFMGMHMKMLMMMNMOMPMQMRMSMTMUMVMWMXMYMZ' +
    'NANCNENFNGNINLNONPNRNUNZ' +
    'OM' +
    'PAPEPFPGPHPKPLPMPNPRPSPTPWPY' +
    'QA' +
    'RERORSRURW' +
    'SASBSCSDSESGSHSISJSKSLSMSNSOSRSSSTSVSXSYSZ' +
    'TCTDTFTGTHTJTKTLTMTNTOTRTTTVTWTZ' +
    'UAUGUMUSUYUZ' +
    'VAVCVEVGVIVNVU' +
    'WFWS' +
    'XK' +
    'YEYT' +
    'ZAZMZW'
  ).match(/../g),
);
