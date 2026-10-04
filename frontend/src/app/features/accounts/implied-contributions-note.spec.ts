import { AccountImportResult } from '../../core/api/models/imports.models';
import { impliedContributionsNote } from './implied-contributions-note';

function result(impliedContributions: number, impliedContributionsAmount: number): AccountImportResult {
  return {
    accountId: 'a1',
    created: false,
    institutionCode: null,
    accountNumber: '1234',
    considered: 2,
    inserted: 2,
    skipped: 0,
    failed: 0,
    failures: [],
    impliedContributions,
    impliedContributionsAmount,
    updated: 0,
    snapshotsInserted: 0,
  };
}

describe('impliedContributionsNote', () => {
  it('says nothing when every purchase had a recorded deposit', () => {
    expect(impliedContributionsNote(result(0, 0))).toBeNull();
  });

  it('explains a single implied contribution with its amount', () => {
    expect(impliedContributionsNote(result(1, 1000))).toBe(
      'Includes 1 implied contribution ($1,000.00) for purchases with no recorded deposit — see the Ledger.',
    );
  });

  it('pluralises and totals several implied contributions', () => {
    expect(impliedContributionsNote(result(3, 1500.5))).toContain('3 implied contributions ($1,500.50)');
  });
});
