// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Made by scripts/generate-public-types.mjs from answer.schema.json; do not edit (ADR-0128).

/**
 * What the element endpoint answers with (GET /jfb/api/v1/forms/{key}/element): the view model of a form's first
 * render as the built-in views get it, without what only a post without JavaScript uses, for the headless element
 * of the same install (ADR-0128). A property that may be null is left out instead. The element's TypeScript types
 * in answer.d.ts are made from this schema.
 */
export type JfbElementAnswer = ElementAnswer;

export type AffixModel = {
  text: string;
  id: string;
};

export type CalculationControl = {
  kind: "calculation";
  text: string;
};

export type CaptchaControl = {
  kind: "captcha";
  provider: string;
};

export type ChoiceImageModel = {
  url: string;
  width?: number;
  height?: number;
};

export type ChoiceModel = {
  id: string;
  value: string;
  label: string;
  hiddenText?: string;
  description?: string;
  descriptionId: string;
  image?: ChoiceImageModel;
  isChecked: boolean;
};

export type ChoicesCascadeModel = {
  sample: ChoiceModel;
  none: string;
  unavailable: string;
};

export type ChoicesControl = {
  kind: "choices";
  inputType: string;
  cssClass: string;
  choices: ChoiceModel[];
  other?: OtherChoiceModel;
  cascade?: ChoicesCascadeModel;
  ends?: ScaleEndsModel;
  empty?: string;
};

export type ConfirmInputModel = {
  label: string;
  id: string;
  name: string;
  value: string;
};

export type CounterModel = {
  text: string;
  id: string;
  limit: number;
  left: string;
  leftOne: string;
  over: string;
  overOne: string;
};

export type CustomControl = {
  kind: "custom";
  settings?: unknown;
  parts: Record<string, string[]>;
};

export type DateControl = {
  kind: "date";
  inputType: string;
  min?: string;
  max?: string;
  step?: string;
};

export type ElementAnswer = {
  contract: number;
  form: FormViewModel;
  assets: ElementAssets;
};

export type ElementAssets = {
  styles: string[];
  script: string;
};

export type ErrorLinkModel = {
  message: string;
  target: string;
};

export type ErrorSummaryViewModel = {
  heading: string;
  headingId: string;
  errors: ErrorLinkModel[];
};

export type FieldViewModel = {
  key: string;
  type: string;
  alias?: string;
  id: string;
  name: string;
  cssClass: string;
  labelElement: "none" | "label" | "legend" | "choice";
  label?: string;
  hideLabel: boolean;
  mark: "none" | "optional" | "required";
  markText: string;
  markSpoken?: string;
  isMarkShown: boolean;
  markShownWhile?: string;
  hooks: boolean;
  help?: string;
  helpId: string;
  error?: string;
  errorId: string;
  describedBy?: string;
  choiceErrorId?: string;
  isRequired: boolean;
  isHidden: boolean;
  isDisabled: boolean;
  placeholder?: string;
  recalling: string[];
  autocomplete?: string;
  values: string[];
  fillsDefault: boolean;
  value: string;
  control?: CalculationControl | CaptchaControl | ChoicesControl | CustomControl | DateControl | FileControl | GridControl | HeadingControl | ImageControl | NumberControl | PartsControl | RankingControl | RepeaterControl | SelectControl | SignatureControl | SingleChoiceControl | SliderControl | TextControl | TextAreaControl | TextBlockControl;
};

export type FileCameraModel = {
  id: string;
  text: string;
};

export type FileControl = {
  kind: "file";
  accept: string;
  multiple: boolean;
  dropZone: boolean;
  thumbnails: boolean;
  upload: FileUploadWordsModel;
  camera?: FileCameraModel;
  kept: KeptFileModel[];
};

export type FileUploadWordsModel = {
  drag?: string;
  choose: string;
  list: string;
  uploading: string;
  uploaded: string;
  remove: string;
  stop: string;
  tryAgain: string;
  tryAgainFor: string;
  stopped: string;
  later: string;
  kilobytes: string;
  megabytes: string;
};

export type FormResumeModel = {
  openLabel: string;
  intro: string;
  panelClass: string;
  fieldClass: string;
  emailLabel: string;
  sendLabel: string;
  cancelLabel: string;
  savingText: string;
  invalidText: string;
  failedText: string;
  saved: NoticeViewModel;
  restored: NoticeViewModel;
  expired: NoticeViewModel;
};

export type FormSendingModel = {
  moves?: NavigationViewModel;
  sendingText: string;
  thanksText: string;
  notices: NoticeViewModel[];
  outcome: OutcomeViewModel;
};

export type FormViewModel = {
  classes: ThemeClassesModel;
  key: string;
  alias: string;
  revision: number;
  id: string;
  culture: string;
  direction: string;
  cssClass: string;
  isLightOnly: boolean;
  display: "form" | "notice" | "outcome";
  apiBase: string;
  contentKey?: string;
  placement?: string;
  asksAtOnce: boolean;
  isPosted: boolean;
  insights: boolean;
  modelData: string;
  pages: PageViewModel[];
  progress?: ProgressViewModel;
  errorSummary?: ErrorSummaryViewModel;
  errorSummaryTemplate?: ErrorSummaryViewModel;
  sending?: FormSendingModel;
  resume?: FormResumeModel;
  defaultButton?: NavigationButtonModel;
  navigation?: NavigationViewModel;
  requiredNote?: string;
  isRequiredNoteHidden: boolean;
  hooks: boolean;
  notice?: NoticeViewModel;
  outcome?: OutcomeViewModel;
  contextValues: HiddenInputModel[];
  honeypot?: HoneypotModel;
};

export type GridControl = {
  kind: "grid";
  inputType: string;
  columns: string[];
  rows: GridRowModel[];
  empty?: string;
};

export type GridRowModel = {
  key: string;
  label: string;
  name: string;
  isRequired: boolean;
  followsRequired: boolean;
  mark: "none" | "optional" | "required";
  markText: string;
  markSpoken?: string;
  errorId?: string;
  isMarkShown: boolean;
  markShownWhile?: string;
  choices: ChoiceModel[];
};

export type GroupViewModel = {
  key: string;
  title?: string;
  description?: string;
  descriptionId: string;
  recalling: string[];
  cssClass: string;
  isHidden: boolean;
  fields: FieldViewModel[];
};

export type HeadingControl = {
  kind: "heading";
  text: string;
  level: number;
};

export type HiddenInputModel = {
  name: string;
  value: string;
};

export type HintModel = {
  text: string;
  id: string;
};

export type HoneypotModel = {
  id: string;
  name: string;
  label: string;
};

export type ImageControl = {
  kind: "image";
  url?: string;
  alt: string;
  size: string;
  width?: number;
  height?: number;
};

export type KeptFileModel = {
  id: string;
  token: string;
  label: string;
};

export type NavigationButtonModel = {
  label: string;
  value: string;
};

export type NavigationViewModel = {
  back?: NavigationButtonModel;
  next?: NavigationButtonModel;
  submit?: NavigationButtonModel;
};

export type NoticeViewModel = {
  kind: "notOpenYet" | "closed" | "full" | "message" | "signIn" | "memberGroups" | "migrationsPending" | "alreadySent";
  heading?: string;
  text: string;
  code?: string;
  headingId?: string;
  cssClass: string;
};

export type NumberControl = {
  kind: "number";
  inputType: string;
  inputMode?: string;
  min?: string;
  max?: string;
  step?: string;
  prefix?: AffixModel;
  suffix?: AffixModel;
};

export type OtherChoiceModel = {
  choice: ChoiceModel;
  textLabel: string;
  textId: string;
  textName: string;
  text: string;
};

export type OutcomeCopyModel = {
  answers?: SummaryViewModel;
  printText: string;
  shownText: string;
  again: OutcomeLinkModel;
};

export type OutcomeCountdownModel = {
  one: string;
  other: string;
  stay: string;
};

export type OutcomeLinkModel = {
  url: string;
  text: string;
};

export type OutcomeViewModel = {
  message: string;
  reference?: string;
  isPending: boolean;
  continue?: OutcomeLinkModel;
  countdown?: OutcomeCountdownModel;
  copy?: OutcomeCopyModel;
};

export type PageViewModel = {
  key: string;
  title?: string;
  description?: string;
  recalling: string[];
  isCurrent: boolean;
  isHidden: boolean;
  summary?: SummaryViewModel;
  groups: GroupViewModel[];
};

export type PartModel = {
  part: string;
  label: string;
  id: string;
  name: string;
  value: string;
  autocomplete: string;
  isWide: boolean;
  isRequired: boolean;
  followsRequired: boolean;
  isInvalid: boolean;
  mark: "none" | "optional" | "required";
  markText: string;
  isMarkShown: boolean;
  markShownWhile?: string;
  countries?: SelectControlModel;
};

export type PartsControl = {
  kind: "parts";
  parts: PartModel[];
};

export type ProgressStepModel = {
  title: string;
  isCurrent: boolean;
  isDone: boolean;
  page: string;
  isHidden: boolean;
  isUntitled: boolean;
  recallsTitle: boolean;
  fallback: string;
};

export type ProgressViewModel = {
  style: "steps" | "bar";
  isHidden: boolean;
  current: number;
  total: number;
  text: string;
  words?: string;
  untitledWords?: string;
  textId: string;
  label: string;
  steps: ProgressStepModel[];
};

export type RankingControl = {
  kind: "ranking";
  items: RankingItemModel[];
  name: string;
  ranked: number;
  moved: string;
  empty?: string;
};

export type RankingItemModel = {
  id: string;
  label: string;
  position?: number;
  input?: HiddenInputModel;
  order: HiddenInputModel;
  up: RankingMoveModel;
  down: RankingMoveModel;
};

export type RankingMoveModel = {
  label: string;
  value: string;
  isDisabled: boolean;
  id?: string;
};

export type RepeaterControl = {
  kind: "repeater";
  rows: RepeaterRowModel[];
  template: RepeaterRowModel;
  least: number;
  most: number;
  legend: string;
  removed: string;
  add: RowButtonModel;
  rowCount: HiddenInputModel;
  fieldsCssClass: string;
};

export type RepeaterRowModel = {
  index: number;
  id: string;
  legend: string;
  remove: RowButtonModel;
  fields: FieldViewModel[];
};

export type RowButtonModel = {
  value: string;
  text: string;
  isHidden: boolean;
  context?: string;
};

export type ScaleEndsModel = {
  low: string;
  high: string;
};

export type SelectControl = {
  kind: "select";
  multiple: boolean;
  size?: number;
  prompt?: string;
  cascades: boolean;
  none?: string;
  unavailable?: string;
  options: SelectOptionModel[];
  hint?: HintModel;
  empty?: string;
};

export type SelectControlModel = {
  multiple: boolean;
  size?: number;
  prompt?: string;
  cascades: boolean;
  none?: string;
  unavailable?: string;
  options: SelectOptionModel[];
  hint?: HintModel;
  empty?: string;
};

export type SelectOptionModel = {
  value: string;
  label: string;
  isSelected: boolean;
};

export type SignatureControl = {
  kind: "signature";
  pad: SignaturePadModel;
  label: string;
  name: string;
  value: string;
  maxLength: number;
  help: string;
  helpId: string;
};

export type SignaturePadModel = {
  width: number;
  height: number;
  help: string;
  drawn: string;
  clear: string;
  undoClear: string;
  typeInstead: string;
  byHand: string;
};

export type SingleChoiceControl = {
  kind: "singleChoice";
  value: string;
  label: string;
  isChecked: boolean;
  wording?: HiddenInputModel;
};

export type SliderControl = {
  kind: "slider";
  min: string;
  max: string;
  step: string;
  value: string;
  valueText?: string;
  shown?: string;
};

export type SummaryAnswerModel = {
  field: string;
  label: string;
  texts: string[][];
  isRows: boolean;
  isMasked: boolean;
  mask: string;
  emptyText: string;
  hiddenText: string;
  change?: SummaryChangeModel;
  error?: string;
  errorId: string;
  isAnswered: boolean;
  isEmpty: boolean;
};

export type SummaryChangeModel = {
  id: string;
  value: string;
  text: string;
  label: string;
};

export type SummarySectionModel = {
  page: string;
  title: string;
  headingId: string;
  answers: SummaryAnswerModel[];
};

export type SummaryViewModel = {
  sections: SummarySectionModel[];
};

export type TextAreaControl = {
  kind: "textArea";
  rows: number;
  autoGrow: boolean;
  counter?: CounterModel;
};

export type TextBlockControl = {
  kind: "textBlock";
  html: string;
  isEmpty: boolean;
};

export type TextControl = {
  kind: "text";
  inputType: string;
  spellcheck?: boolean;
  prefix?: AffixModel;
  suffix?: AffixModel;
  counter?: CounterModel;
  confirm?: ConfirmInputModel;
};

export type ThemeClassesModel = {
  page: string;
  label: string;
  partLabel?: string;
  fileInput?: string;
  help: string;
  error: string;
  control: string;
  select: string;
  choices: string;
  choice: string;
  choiceInput?: string;
  choiceLabel?: string;
  switchChoice: string;
  switchInput: string;
  range?: string;
  affix: string;
  affixText?: string;
  counter: string;
  navigation: string;
  button: string;
  buttonPrimary: string;
  buttonLink: string;
  buttonIcon: string;
  progressBar: string;
  progressSteps: string;
  errorSummary: string;
  outcome: string;
};
