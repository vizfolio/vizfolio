import { PerformanceReturn } from '../../core/api/models/performance.models';

/**
 * Plain-language text for the backend's return reason and cause codes. The headline is the
 * money-weighted "Your return" (a personal rate of return); the time-weighted figure is the
 * "Investment return".
 */

export const YOUR_RETURN_LABEL = 'Your return';
export const YOUR_RETURN_HINT =
  'How your money did, including when you added or withdrew it — your personal rate of return.';

export const INVESTMENT_RETURN_LABEL = 'Investment return';
export const INVESTMENT_RETURN_HINT =
  "How the investments did, regardless of when you added money — comparable to a fund's published return.";

const CAUSE_TEXT: Record<string, string> = {
  NoPrice: 'no price available',
  StalePrice: 'no recent price',
  PricesPending: 'prices are still downloading',
  NegativePosition: 'your history sells more shares than it bought',
  MaterialMismatch: "your history doesn't match your broker's statement",
  BeforeHistory: 'it was held before your imported history starts',
  ValueWithoutInvestment: 'value appeared without a recorded deposit',
  ValueVanished: 'value disappeared without a recorded withdrawal',
};

const REASON_TEXT: Record<string, string> = {
  NoData: 'No activity yet.',
  IncompleteStartingBalance: "Some holdings couldn't be valued at the start of the period.",
  IncompleteEndingBalance: "Some holdings couldn't be valued at the end of the period.",
  UnvaluedTransfer: 'A transfer of shares in this period has no price.',
  PeriodTooShort: 'Choose a period longer than one day.',
  ZeroDenominator: 'Nothing was invested during this period.',
  NoInvestedBalance: 'Nothing was invested during this period.',
  InsufficientCashFlows: 'Not enough deposits or withdrawals to compute your return.',
  NoSignChange: 'Not enough deposits or withdrawals to compute your return.',
  DidNotConverge:
    "Your return can't be computed for this pattern of deposits; the investment return still applies.",
};

/** Why a valuation was missing, as a lower-case phrase ("no price available"). */
export function causeText(cause: string): string {
  return CAUSE_TEXT[cause] ?? 'some days couldn’t be valued';
}

/** Why a return couldn't be computed; falls back to the raw code for anything unmapped. */
export function returnReasonText(reason: string | null): string | null {
  if (!reason) {
    return null;
  }
  return REASON_TEXT[reason] ?? reason;
}

/** The "approximate" note for a return that fell back to Modified Dietz, or null. */
export function fallbackText(r: PerformanceReturn): string | null {
  return r.fallbackReason
    ? `Approximate: some days couldn't be valued (${causeText(r.fallbackReason)}).`
    : null;
}

/** How a return was computed, for the Returns breakdown ("Money-weighted (XIRR) · annualized"). */
export function returnMethodText(r: PerformanceReturn): string {
  const method =
    r.method === 'XIRR'
      ? 'Money-weighted (XIRR)'
      : r.method === 'DailyValuedTWR'
        ? 'Time-weighted, valued daily'
        : r.method === 'ModifiedDietz'
          ? 'Approximate (Modified Dietz)'
          : r.method;
  return r.basis === 'Annualized' ? `${method} · annualized` : `${method} · over the period`;
}
