// Die Texte der Formularprüfung auf Deutsch (ROADMAP 9.5f, ADR-0181); check.en.ts sagt, was jeder ist.

/** „ in „Beschriftung““, wo ein Satz den Text nennt, um den es geht. */
const within = (place: string) => (place ? ` in „${place}“` : '');

const count = (number: number, one: string, many: string) => (number === 1 ? `1 ${one}` : `${number} ${many}`);

export default {
  jfbCheck: {
    headline: 'Formularprüfung',
    errors: (number: number) => count(number, 'Fehler', 'Fehler'),
    warnings: (number: number) => count(number, 'Warnung', 'Warnungen'),
    hints: (number: number) => count(number, 'Vorschlag', 'Vorschläge'),
    blocks: 'Verhindert das Veröffentlichen',
    fix: 'Beheben',
    fixNamed: (name: string) => `Beheben: ${name}`,
    none: 'Keine Probleme. Das Formular kann veröffentlicht werden.',
    checking: 'Das Formular wird geprüft…',
    checkingAgain: 'Die letzten Änderungen werden geprüft…',
    failed: 'Die Formularprüfung hat nicht geantwortet. Versuchen Sie es gleich noch einmal.',
    retry: 'Erneut versuchen',

    publishClear: 'Die Formularprüfung hat keine Probleme gefunden.',
    publishErrors: (number: number) =>
      number === 1
        ? 'Dieses Formular kann noch nicht veröffentlicht werden: 1 Fehler muss behoben werden.'
        : `Dieses Formular kann noch nicht veröffentlicht werden: ${number} Fehler müssen behoben werden.`,
    publishWarnings: (number: number) =>
      number === 1
        ? 'Die Formularprüfung hat 1 Warnung gefunden. Sie verhindert das Veröffentlichen nicht.'
        : `Die Formularprüfung hat ${number} Warnungen gefunden. Sie verhindern das Veröffentlichen nicht.`,
    publishAlsoWarnings: (number: number) =>
      number === 1
        ? 'Außerdem gibt es 1 Warnung, die das Veröffentlichen nicht verhindert.'
        : `Außerdem gibt es ${number} Warnungen, die das Veröffentlichen nicht verhindern.`,
    publishFailed: 'Die Formularprüfung hat nicht geantwortet. Beim Veröffentlichen wird das Formular erneut geprüft.',
    publishMore: (number: number) =>
      number === 1
        ? 'Und 1 weiteres, das die Formularprüfung auflistet.'
        : `Und ${number} weitere, die die Formularprüfung auflistet.`,

    name_form: 'Das Formular',
    name_settings: 'Einstellungen des Formulars',
    name_outcomes: 'Nach dem Senden',
    name_outcome: (number: number) => `Nach dem Senden: Abschluss ${number}`,
    name_action: (number: number) => `Aktion ${number}`,
    name_fieldsMissing: 'Mindestens ein Feld hinzufügen',

    place_option: (number: number) => `Option ${number}`,
    place_row: (number: number) => `Zeile ${number}`,
    place_message: 'Meldung',
    place_description: 'Beschreibung',
    place_redirect: 'Adresse der Weiterleitung',
    place_entryTitle: 'Titel des Eintrags',
    place_button: 'Text der Schaltfläche',

    code_fields_missing: 'Das Formular hat noch kein Feld zum Ausfüllen; Besucher hätten also nichts zu senden.',
    code_label_missing: 'Dieses Feld hat keine Beschriftung, mit der Screenreader und die Einträge es benennen.',
    code_lock_replaces_value: (setting: string) =>
      `Die Website legt „${setting}“ fest; das Formular läuft also mit dem Wert der Website statt mit seinem eigenen.`,
    code_unknown_type: (kind: string, alias: string) =>
      kind === 'action'
        ? `Die Website hat keinen Aktionstyp „${alias}“.`
        : kind === 'captcha'
          ? `Die Website hat keinen Captcha-Anbieter „${alias}“.`
          : `Die Website hat keinen Feldtyp „${alias}“.`,
    code_setting_unknown: (name: string) => `Das Element enthält die Einstellung „${name}“, die sein Typ nicht hat.`,
    code_setting_missing: (setting: string) => `„${setting}“ braucht einen Wert.`,

    code_unknown_path: (place: string) => `Ein Platzhalter${within(place)} nennt nichts und zeigt darum nichts.`,
    code_unknown_filter: (place: string) =>
      `Ein Platzhalter${within(place)} verwendet einen Filter, den die Website nicht hat.`,
    code_field_after_text: (place: string) =>
      `Ein Platzhalter${within(place)} ruft die Antwort eines Felds ab, das nach diesem Text kommt; Besucher haben sie also noch nicht gegeben.`,
    code_source_not_for_place: (place: string) =>
      `Ein Platzhalter${within(place)} nimmt seinen Wert aus einer Quelle, die dieser Text nicht verwenden kann.`,
    code_filter_not_for_place: (place: string) =>
      `Ein Platzhalter${within(place)} verwendet einen eigenen Filter der Website, den dieser Text nicht verwenden kann.`,
    code_too_many_tags: (place: string) => `Es gibt${within(place)} mehr Platzhalter, als ein Text enthalten darf.`,
    code_too_many_filters: (place: string) =>
      `Ein Platzhalter${within(place)} hat mehr Filter, als ein Platzhalter haben darf.`,
    code_too_long: (place: string) => `Ein Platzhalter${within(place)} macht den Text länger, als ein Text sein darf.`,
    code_malformed_tag: (place: string) =>
      `Ein Platzhalter${within(place)} ist nicht so geschrieben, wie Platzhalter geschrieben werden.`,
    code_unclosed_tag: (place: string) => `Ein Platzhalter${within(place)} ist nicht mit }} geschlossen.`,
    code_tag_in_markup: (place: string) =>
      `Ein Platzhalter${within(place)} steht im Markup des Texts, wo er nicht angezeigt werden kann.`,
    code_tag_before_path: 'Ein Platzhalter steht vor dem Pfad der Adresse; sie könnte also überallhin führen.',
    code_invalid_argument: (place: string) =>
      `Ein Filter eines Platzhalters${within(place)} erhält einen Wert, den er nicht annehmen kann.`,
    code_filter_not_for_context: (place: string) =>
      `Ein Platzhalter${within(place)} verwendet einen Filter, der in diesem Text nicht funktioniert.`,
    code_filter_after_encoding: (place: string) =>
      `Ein Platzhalter${within(place)} verwendet einen Filter nach dem, der seinen Wert kodiert.`,
    code_answers_in_url:
      'Die Weiterleitung schreibt Antworten in ihre Adresse, und die landet in Protokollen und im Browserverlauf.',

    code_captcha_not_configured: (provider: string) =>
      `Der Captcha-Anbieter „${provider}“ hat auf dieser Website noch keine Schlüssel; die Einträge des Formulars landen also im Spam, bis er sie hat.`,
    code_no_reply_field:
      'Das Formular hat kein E-Mail-Feld; eine Antwort auf diese E-Mail erreicht also niemanden, der das Formular ausgefüllt hat.',
    code_reply_field_unset:
      'Wählen Sie das E-Mail-Feld, an das geantwortet wird, damit eine Antwort auf diese E-Mail die Person erreicht, die das Formular ausgefüllt hat.',
    code_required_can_hide:
      'Diese Regel macht das Feld für Antworten zur Pflicht, für die eine Regel es verbirgt. Ein verborgenes Feld ist nie Pflicht; Besucher können also senden, ohne es auszufüllen.',
    code_page_unreachable: (number: number) =>
      `Keine Antwort, die Besucher vor Seite ${number} geben können, lässt die Seite erscheinen; wer weitergeht, erreicht sie also nie.`,

    code_consent_missing:
      'Das Formular fragt nach personenbezogenen Daten, etwa mit dieser Antwort, hat aber kein Einwilligungsfeld. Fügen Sie eines hinzu, wenn die Einträge auf Grundlage einer Einwilligung aufbewahrt und genutzt werden.',
    code_card_number_asked:
      'Dieses Feld fragt nach einer Kartennummer. Nehmen Sie Zahlungen über einen Zahlungsanbieter entgegen: Kartendaten gehören nie in die Einträge, E-Mails oder Exporte eines Formulars.',
    code_password_asked:
      'Dieses Feld fragt nach einem Passwort, das in den Einträgen, E-Mails und Exporten des Formulars landen würde. Lassen Sie Mitglieder sich stattdessen anmelden.',
    code_file_size_unset:
      'Dieses Feld nimmt Dateien bis zur Grenze der Website je Datei an: Geben Sie ihm die größte Größe, die seine Antworten brauchen.',
    code_options_can_fill:
      'Jede Option dieses Pflichtfelds hat eine begrenzte Zahl von Plätzen; sind alle vergeben, kann niemand das Formular senden: Fügen Sie eine Option ohne Grenze hinzu, oder erlauben Sie, das Feld leer zu lassen.',
    code_limit_without_entries: (form: boolean) =>
      form
        ? 'Das Formular bewahrt keine Einträge auf; diese Grenze vergisst also jeden Eintrag, sobald seine Aktionen gelaufen sind.'
        : 'Die Antworten dieses Felds werden nur aufbewahrt, bis die Aktionen gelaufen sind; diese Grenze gibt sie dann also wieder frei.',
    code_last_outcome_has_rule:
      'Besucher, deren Antworten keine Regel eines Abschlusses trifft, erhalten die eingebaute Dankesmeldung: Lassen Sie den letzten Abschluss ohne Regel, damit er für alle anderen Antworten da ist.',

    code_theme_missing: (theme: string) =>
      `Die Website hat kein Theme „${theme}“; das Formular erscheint also mit dem Standard-Theme.`,
    code_content_missing: (redirect: boolean) =>
      redirect
        ? 'Die Website hat diese Seite nicht mehr; Besucher erhalten also stattdessen die Meldung des Abschlusses oder die eingebaute Dankesmeldung.'
        : 'Die Website hat dieses Inhaltselement nicht mehr.',
    code_redirect_unpublished:
      'Diese Seite ist nicht veröffentlicht; Besucher erhalten also die Meldung des Abschlusses oder die eingebaute Dankesmeldung, bis sie es ist.',
    code_media_missing: 'Die Website hat dieses Medienelement nicht mehr.',
    code_member_group_missing: (group: string) =>
      group
        ? `Die Website hat keine Mitgliedergruppe namens „${group}“; also ist kein Mitglied darin. Namen werden genau verglichen.`
        : 'Die Website hat diese Mitgliedergruppe nicht mehr; also ist kein Mitglied darin.',
    code_dictionary_text_missing: (key: string, place: string) =>
      `Umbracos Wörterbuch hat für „${key}“${within(place)} nicht in allen Sprachen der Website einen Text; Besucher dort sehen ihn also so, wie er geschrieben ist.`,

    code_options_invalid: 'Die Optionen des Elements sind nicht so eingerichtet, wie sein Typ sie braucht.',
    code_property_not_supported: (place: string) =>
      place
        ? `Der Typ des Elements nimmt „${place}“ nicht an.`
        : 'Der Typ des Elements nimmt diese Einstellung nicht an.',
    code_part_missing: (part: string) =>
      ({
        pages: 'Das Formular hat keine Seite.',
        groups: 'Diese Seite hat keine Gruppe.',
        rows: 'Dieses Raster hat keine Zeilen.',
        fields: 'Diese wiederholbare Gruppe hat keine Felder.',
        options: 'Dieses Feld hat keine Optionen.',
        list: 'Dieses Feld nimmt seine Optionen aus einer Optionsliste, nennt aber keine.',
        memberGroups: 'Das Formular ist für einige Mitgliedergruppen, nennt aber keine.',
        captcha: 'Das Formular ist mit einem Captcha geschützt, nennt aber keinen Anbieter.',
        redirect: 'Die Weiterleitung sagt nicht, wohin sie führt.',
      })[part] ?? 'Etwas, das dieses Element braucht, fehlt.',
    code_field_not_allowed: 'Ein Feld dieses Typs kann nicht hier stehen.',
    code_alias_invalid: (alias: string) =>
      alias
        ? `„${alias}“ kann kein Alias sein: Ein Alias beginnt mit einem Kleinbuchstaben und enthält nur Buchstaben und Ziffern.`
        : 'Dieses Feld hat keinen Alias, unter dem die Einträge seine Antwort aufbewahren.',
    code_alias_reserved: (alias: string) => `„${alias}“ kann kein Alias sein: Das Formular verwendet ihn selbst.`,
    code_alias_duplicate: (alias: string) => `Ein anderes Feld hat schon den Alias „${alias}“.`,
    code_name_invalid: 'Dieser Name beginnt so, wie nur die eigenen Namen der Website beginnen dürfen.',
    code_name_duplicate: (name: string) => `„${name}“ steht zweimal in der Liste.`,
    code_option_value_invalid: 'Eine Option hat keinen Wert oder einen, den eine Option nicht haben kann.',
    code_option_value_duplicate: 'Zwei Optionen haben denselben Wert.',
    code_unknown_field: 'Das Element nennt ein Feld, das das Formular nicht hat.',
    code_invalid_reference: 'Das Element verweist auf etwas, das hier nicht passt: ein Feld oder eine Option.',
    code_invalid_condition: 'Eine Bedingung einer Regel des Elements ist nicht vollständig.',
    code_unknown_operator: 'Eine Bedingung vergleicht auf eine Weise, die das Formular nicht kennt.',
    code_operator_not_for_kind:
      'Eine Bedingung vergleicht auf eine Weise, wie sich die Antworten dieses Felds nicht vergleichen lassen.',
    code_invalid_operand: 'Eine Bedingung vergleicht mit einem Wert, der nicht passt.',
    code_unsafe_pattern: 'Die Website kann dieses Muster nicht sicher prüfen.',
    code_too_deep: 'Eine Regel des Elements verschachtelt Bedingungen tiefer, als eine Regel darf.',
    code_constant_formula: 'Die Formel des Elements nennt kein Feld; ihr Ergebnis ändert sich also nie.',
    code_rule_effect_invalid: 'Eine Regel des Elements tut etwas, das es nicht kann.',
    code_rule_duplicate: 'Das Element hat zwei Regeln, die dasselbe tun.',
    code_cycle: 'Die Regeln oder Berechnungen des Elements hängen im Kreis voneinander ab.',
    code_duplicate_key: 'Zwei Elemente des Formulars haben denselben Schlüssel.',
  },
};
