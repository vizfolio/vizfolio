import { TestBed } from '@angular/core/testing';

import { AccountSelection, StatementAssignment } from '../../../core/api/models/imports.models';
import { candidate, selection } from '../testing/import-fixtures';
import { AccountPicker } from './account-picker';

function setup(sel: AccountSelection) {
  const fixture = TestBed.createComponent(AccountPicker);
  fixture.componentRef.setInput('selection', sel);
  fixture.componentRef.setInput('label', 'report.xlsx');
  fixture.detectChanges();
  const answers: StatementAssignment[] = [];
  fixture.componentInstance.chosen.subscribe((a) => answers.push(a));
  const el = fixture.nativeElement as HTMLElement;
  const submit = () => {
    (el.querySelector('button[type="submit"]') as HTMLButtonElement).click();
    fixture.detectChanges();
  };
  const type = (label: string, value: string) => {
    const field = [...el.querySelectorAll('label.field')].find((l) => l.textContent?.includes(label))!;
    const input = field.querySelector('input') as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };
  return { fixture, el, answers, submit, type };
}

describe('AccountPicker', () => {
  const ira = candidate({ accountId: 'a1', name: 'IRA', matchingRows: 2514, sharedTickers: 3 });
  const taxable = candidate({ accountId: 'a2', name: 'Taxable', accountNumberMasked: '…2222', matchingRows: 2 });

  it('lists the candidates with their evidence and pre-selects the best match', () => {
    const { el, answers, submit } = setup(selection({ candidates: [ira, taxable], suggestedAccountId: 'a1' }));

    expect(el.querySelector('legend')?.textContent).toContain('report.xlsx');
    expect(el.textContent).toContain('IRA (…1111)');
    expect(el.textContent).toContain('2,514 matching transactions · 3 shared funds');
    expect(el.textContent).toContain('Best match');

    submit();
    expect(answers).toEqual([{ fileAccountNumber: '', accountId: 'a1' }]);
  });

  it('waits for a choice when nothing is suggested', () => {
    const { el, fixture, answers, submit } = setup(selection({ candidates: [ira, taxable] }));
    expect((el.querySelector('button[type="submit"]') as HTMLButtonElement).disabled).toBe(true);

    (el.querySelectorAll('input[type="radio"]')[1] as HTMLInputElement).dispatchEvent(new Event('change'));
    fixture.detectChanges();
    submit();

    expect(answers).toEqual([{ fileAccountNumber: '', accountId: 'a2' }]);
  });

  it('asks for the institution and number of a new account for a file without one', () => {
    const { el, answers, submit, type } = setup(selection());

    // No candidates: "A new account" is the only option and is pre-selected.
    expect((el.querySelector('button[type="submit"]') as HTMLButtonElement).disabled).toBe(true);
    type('Name', 'Rollover IRA');
    type('Account number', '9876-5432');
    submit();

    expect(answers).toEqual([
      {
        fileAccountNumber: '',
        newAccount: { name: 'Rollover IRA', institutionCode: 'vanguard.com', accountNumber: '9876-5432' },
      },
    ]);
  });

  it("keeps a file's own account number for a new account, asking only for a name", () => {
    const { el, fixture, answers, submit } = setup(
      selection({ reason: 'UnknownAccountNumber', fileAccountNumber: 'AAA111', candidates: [ira], suggestedAccountId: 'a1' }),
    );
    expect(el.textContent).toContain('Its account number becomes …A111.');

    (el.querySelectorAll('input[type="radio"]')[1] as HTMLInputElement).dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect([...el.querySelectorAll('label.field')].map((l) => l.textContent?.trim())).toEqual(['Name (optional)']);
    submit();

    expect(answers).toEqual([{ fileAccountNumber: 'AAA111', newAccount: { name: undefined } }]);
  });

  it('lets the user skip the file', () => {
    const { fixture, el } = setup(selection());
    let skipped = false;
    fixture.componentInstance.skipped.subscribe(() => (skipped = true));

    (el.querySelector('.btn--ghost') as HTMLButtonElement).click();

    expect(skipped).toBe(true);
  });
});
