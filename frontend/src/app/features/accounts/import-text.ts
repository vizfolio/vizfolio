import { HttpErrorResponse } from '@angular/common/http';

import {
  AccountImportResult,
  AccountSelection,
  ImportUndoSummary,
  PortfolioImportResult,
  PortfolioImportStatus,
  RoutingCandidate,
} from '../../core/api/models/imports.models';

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

/** Plural-aware "3 transactions" / "1 transaction" (with thousands separators). */
function count(n: number, noun: string): string {
  return `${n.toLocaleString('en-US')} ${noun}${n === 1 ? '' : 's'}`;
}

const DAY = new Intl.DateTimeFormat('en-US', { dateStyle: 'medium', timeZone: 'UTC' });

/** "Jan 2, 2025" for an ISO date (YYYY-MM-DD). */
export function formatDay(iso: string | null): string {
  if (!iso) {
    return '';
  }
  const date = new Date(`${iso}T00:00:00Z`);
  return Number.isNaN(date.getTime()) ? iso : DAY.format(date);
}

/** "Jan 2, 2025 – Jun 30, 2026" (or one date), or null when the file had no rows for the account. */
export function dateRangeText(first: string | null, last: string | null): string | null {
  if (!first || !last) {
    return null;
  }
  return first === last ? formatDay(first) : `${formatDay(first)} – ${formatDay(last)}`;
}

/** How the file found this account, in plain words — or null when it was simply uploaded to it. */
export function routingNote(account: AccountImportResult): string | null {
  switch (account.routing?.method) {
    case 'AccountNumber':
      return 'Matched by account number.';
    case 'Fingerprint':
      return account.routing.matchingRows
        ? `Matched by ${count(account.routing.matchingRows, 'transaction')} already in this account.`
        : 'Matched by transactions already in this account.';
    case 'UserSelected':
      return 'Imported into the account you chose.';
    case 'Created':
      return 'A new account was created for it.';
    default:
      return null;
  }
}

/** The evidence for a candidate account: "2,514 matching transactions · 3 shared funds". */
export function candidateEvidence(candidate: RoutingCandidate): string {
  const parts = [count(candidate.matchingRows, 'matching transaction')];
  if (candidate.sharedTickers > 0) {
    parts.push(count(candidate.sharedTickers, 'shared fund'));
  }
  return parts.join(' · ');
}

/** The question an import asks about one of its statements. */
export function selectionQuestion(selection: AccountSelection): string {
  const span = dateRangeText(selection.firstDate, selection.lastDate);
  const rows = `${count(selection.rows, 'transaction')}${span ? `, ${span}` : ''}`;
  switch (selection.reason) {
    case 'UnknownAccountNumber':
      return `This file's account …${selection.fileAccountNumber.slice(-4)} (${rows}) isn't one of your accounts, ` +
        'but its transactions are already in one. Is it that account with a different number, or a new account?';
    case 'LikelyOtherAccount':
      return `This file (${rows}) looks like it belongs to another account.`;
    default:
      return selection.candidates.length > 0
        ? `Which account is this file for? (${rows})`
        : `Which account is this file for? It doesn't match any account yet (${rows}).`;
  }
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

/** User-facing guidance for a failed portfolio import. */
export function portfolioImportErrorMessage(err: HttpErrorResponse): string {
  switch (err.status) {
    case 413:
      return 'That file is too large. The import limit is 10 MB.';
    case 415:
      return 'Unsupported file type. Upload a QFX/OFX statement or a Vanguard report — or pick the format under Advanced.';
    case 400:
      return err.error?.error ?? "That choice didn't work. Please try again.";
    case 422:
      return 'This file has no transactions or statements to import.';
    case 404:
      return 'Portfolio not found. Try reselecting a portfolio.';
    default:
      return 'The import failed. Please try again.';
  }
}
