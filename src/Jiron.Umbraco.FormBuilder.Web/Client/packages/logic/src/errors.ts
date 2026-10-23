/** The codes the shared logic reports, the same in C# (ADR-0005). */
export type LogicErrorCode =
  | 'unknown_field'
  | 'unknown_operator'
  | 'operator_not_for_kind'
  | 'invalid_operand'
  | 'invalid_condition'
  | 'unsafe_pattern'
  | 'invalid_context'
  | 'unknown_time_zone'
  | 'not_a_number'
  | 'too_many_digits'
  | 'out_of_range'
  | 'division_by_zero';

/**
 * A problem the shared logic reports instead of a result. The code is the contract: both runtimes use the same codes,
 * and the vector files expect them as `{ "error": code }`.
 */
export class LogicError extends Error {
  readonly code: LogicErrorCode;

  constructor(code: LogicErrorCode) {
    super(code);
    this.name = 'LogicError';
    this.code = code;
  }
}

/**
 * The record's own value for the key, never one it inherits: data read from JSON is a plain object, whose prototype
 * answers "constructor" or "toString" (ADR-0030).
 */
export function own<T>(record: Readonly<Record<string, T>>, key: string): T | undefined {
  return Object.hasOwn(record, key) ? record[key] : undefined;
}
