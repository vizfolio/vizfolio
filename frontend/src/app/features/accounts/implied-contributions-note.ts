import { AccountImportResult } from '../../core/api/models/imports.models';
import { formatMoney } from '../../shared/util/performance-format';

/**
 * Explains the implied contributions an import recorded for an account, or null when there are none.
 * These are purchases with no recorded deposit (typical of older fund-company history), added to the
 * ledger as "Implied contribution" rows so returns aren't inflated.
 */
export function impliedContributionsNote(account: AccountImportResult): string | null {
  const count = account.impliedContributions ?? 0;
  if (count === 0) {
    return null;
  }
  const total = formatMoney(account.impliedContributionsAmount, 'USD');
  const noun = count === 1 ? 'contribution' : 'contributions';
  return `Includes ${count} implied ${noun} (${total}) for purchases with no recorded deposit — see the Ledger.`;
}
