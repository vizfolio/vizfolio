import { ImportUndoSummary, PortfolioImportResult, PortfolioImportStatus } from '../../core/api/models/imports.models';

const DATE_TIME = new Intl.DateTimeFormat('en-US', { dateStyle: 'medium', timeStyle: 'short' });

/** "Oct 3, 2026, 4:05 PM" for an ISO timestamp from the API. */
export function formatImportedAt(iso: string | null): string {
  if (!iso) {
    return '';
  }
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? iso : DATE_TIME.format(date);
}

/** The notice for a re-upload of a file already imported, or null for any other outcome. */
export function alreadyImportedNote(result: PortfolioImportResult): string | null {
  if (result.status !== PortfolioImportStatus.AlreadyImported) {
    return null;
  }
  const when = formatImportedAt(result.importedAt);
  return `This file was already imported${when ? ` on ${when}` : ''}, so nothing was changed. ` +
    'Its earlier result is shown below; to import it again, undo that import first.';
}

/** Explains a 422 AccountMismatch: which accounts the file is for instead. */
export function accountMismatchMessage(fileAccountNumbers: readonly string[] | undefined): string {
  const numbers = fileAccountNumbers?.filter((n) => n.length > 0) ?? [];
  const which = numbers.length > 0 ? ` (${numbers.join(', ')})` : '';
  return `This file is for a different account${numbers.length > 1 ? 's' : ''}${which}. ` +
    'Import it from the Accounts page to route each statement to its own account.';
}

/** Plural-aware "3 transactions" / "1 transaction". */
function count(n: number, noun: string): string {
  return `${n} ${noun}${n === 1 ? '' : 's'}`;
}

/** The lines an undo confirmation shows: what undoing the import removes, restores and re-runs. */
export function undoPreviewLines(preview: ImportUndoSummary): string[] {
  const lines: string[] = [];
  lines.push(`Removes ${count(preview.transactions, 'transaction')} and ${count(preview.snapshots, 'statement position')} it added.`);
  if (preview.updatesReverted > 0) {
    lines.push(`Restores ${count(preview.updatesReverted, 'earlier row')} it changed.`);
  }
  if (preview.holdingsRemoved > 0 || preview.accountsRemoved > 0) {
    lines.push(`Removes ${count(preview.holdingsRemoved, 'holding')} and ${count(preview.accountsRemoved, 'account')} it created.`);
  }
  if (preview.laterImportsReplayed.length > 0) {
    lines.push(`Re-runs later imports so nothing they share is lost: ${preview.laterImportsReplayed.join(', ')}.`);
  }
  if (preview.laterImportsWithoutFile.length > 0) {
    lines.push(`Re-upload these afterwards to be sure nothing is missing: ${preview.laterImportsWithoutFile.join(', ')}.`);
  }
  return lines;
}
