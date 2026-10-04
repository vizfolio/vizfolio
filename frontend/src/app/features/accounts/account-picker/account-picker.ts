import { Component, computed, input, linkedSignal, output, signal } from '@angular/core';

import { AccountSelection, StatementAssignment } from '../../../core/api/models/imports.models';
import { candidateEvidence, selectionQuestion } from '../import-text';

/** The radio value for "a new account". */
const NEW_ACCOUNT = 'new';

/**
 * Asks where one of a file's statements goes when the import couldn't tell: one of the candidate accounts (with the
 * evidence for each — matching transactions, shared funds; the likeliest pre-selected), or a new account. Every
 * account has a number, so a new account for a file without one asks for the institution and number too; a file
 * that has a number keeps its own. Emits the answer as a {@link StatementAssignment} to send with the file again.
 */
@Component({
  selector: 'app-account-picker',
  templateUrl: './account-picker.html',
  styleUrl: './account-picker.scss',
})
export class AccountPicker {
  readonly selection = input.required<AccountSelection>();
  /** Shown on the heading so several questions on a page stay distinct (e.g. the file name). */
  readonly label = input('');
  readonly disabled = input(false);

  readonly chosen = output<StatementAssignment>();
  readonly skipped = output<void>();

  protected readonly newAccount = NEW_ACCOUNT;
  protected readonly evidence = candidateEvidence;
  protected readonly question = computed(() => selectionQuestion(this.selection()));
  /** A file without an account number needs one typed in for a new account. */
  protected readonly needsNumber = computed(() => this.selection().fileAccountNumber === '');
  protected readonly groupName = `account-picker-${nextId++}`;

  /** The picked option: an account id or {@link NEW_ACCOUNT}; the suggestion (else "new" with no candidates) first. */
  protected readonly picked = linkedSignal(() => {
    const s = this.selection();
    return s.suggestedAccountId ?? (s.candidates.length === 0 ? NEW_ACCOUNT : '');
  });

  protected readonly name = signal('');
  protected readonly institution = linkedSignal(() => this.selection().institutionCode ?? '');
  protected readonly number = signal('');

  protected readonly canSubmit = computed(() => {
    if (this.disabled()) return false;
    const picked = this.picked();
    if (picked !== NEW_ACCOUNT) return picked !== '';
    if (!this.needsNumber()) return true;
    return this.institution().trim() !== '' && /[a-z0-9]/i.test(this.number());
  });

  protected candidateLabel(name: string, masked: string): string {
    return masked ? `${name} (${masked})` : name;
  }

  protected text(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected submit(): void {
    if (!this.canSubmit()) {
      return;
    }
    const fileAccountNumber = this.selection().fileAccountNumber;
    const picked = this.picked();
    if (picked !== NEW_ACCOUNT) {
      this.chosen.emit({ fileAccountNumber, accountId: picked });
      return;
    }
    this.chosen.emit({
      fileAccountNumber,
      newAccount: {
        name: this.name().trim() || undefined,
        ...(this.needsNumber()
          ? { institutionCode: this.institution().trim(), accountNumber: this.number().trim() }
          : {}),
      },
    });
  }
}

let nextId = 0;
