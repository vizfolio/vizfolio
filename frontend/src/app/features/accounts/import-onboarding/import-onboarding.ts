import { Component } from '@angular/core';

/**
 * First-run guidance next to the drop zone: export your history from your broker and drop it here. Broker names are
 * fine here — these are import formats, not return labels.
 */
@Component({
  selector: 'app-import-onboarding',
  template: `
    <div class="onboarding">
      <p class="lead">Export your history from your broker, then drop the files here. Accounts are created for you.</p>
      <details>
        <summary>How to export from Vanguard</summary>
        <ol>
          <li>
            <strong>Transaction history (QFX):</strong> on vanguard.com, download your transactions in the Quicken
            (.qfx) format, choosing all your accounts and the longest date range offered.
          </li>
          <li>
            <strong>Full history (transaction report, .xlsx):</strong> for history older than the QFX covers, download
            each account's transaction history report as an Excel file. It doesn't say which account it's for — Vizfolio works that out from
            the transactions, or asks.
          </li>
        </ol>
      </details>
      <p class="more">Other brokers' QFX/OFX files work too; more formats are coming.</p>
    </div>
  `,
  styles: `
    .onboarding {
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
      font-size: 0.9rem;
    }

    .lead {
      margin: 0;
      font-weight: 600;
    }

    summary {
      cursor: pointer;
    }

    ol {
      margin: var(--space-2) 0 0;
      padding-left: var(--space-5);
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
      color: var(--color-text-muted);
    }

    .more {
      margin: 0;
      color: var(--color-text-muted);
    }
  `,
})
export class ImportOnboarding {}
