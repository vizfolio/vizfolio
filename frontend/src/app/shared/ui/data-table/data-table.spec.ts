import { TestBed } from '@angular/core/testing';

import { DataColumn, DataRow, DataTable } from './data-table';

interface Row extends DataRow {
  name: string;
  units: number;
}

const columns: DataColumn<Row>[] = [
  { key: 'name', header: 'Name' },
  { key: 'units', header: 'Units', align: 'end', format: (v) => `${v} sh` },
];

const rows: Row[] = [
  { name: 'Beta', units: 30 },
  { name: 'Alpha', units: 10 },
  { name: 'Gamma', units: 20 },
];

function render(inputRows: Row[] = rows) {
  const fixture = TestBed.createComponent(DataTable<Row>);
  fixture.componentRef.setInput('columns', columns);
  fixture.componentRef.setInput('rows', inputRows);
  fixture.detectChanges();
  return fixture;
}

function bodyText(fixture: ReturnType<typeof render>, columnIndex: number): string[] {
  const el = fixture.nativeElement as HTMLElement;
  return Array.from(el.querySelectorAll('tbody tr')).map(
    (tr) => tr.querySelectorAll('td')[columnIndex].textContent!.trim(),
  );
}

describe('DataTable', () => {
  it('renders rows in their original order until sorted', () => {
    const fixture = render();
    expect(bodyText(fixture, 0)).toEqual(['Beta', 'Alpha', 'Gamma']);
  });

  it('applies a column formatter to cell values', () => {
    const fixture = render();
    expect(bodyText(fixture, 1)).toEqual(['30 sh', '10 sh', '20 sh']);
  });

  it('sorts ascending on first header click and descending on the second', () => {
    const fixture = render();
    const el = fixture.nativeElement as HTMLElement;
    const nameHeaderBtn = el.querySelectorAll('th .sort-btn')[0] as HTMLButtonElement;

    nameHeaderBtn.click();
    fixture.detectChanges();
    expect(bodyText(fixture, 0)).toEqual(['Alpha', 'Beta', 'Gamma']);

    nameHeaderBtn.click();
    fixture.detectChanges();
    expect(bodyText(fixture, 0)).toEqual(['Gamma', 'Beta', 'Alpha']);
  });

  it('sorts numeric columns numerically', () => {
    const fixture = render();
    const el = fixture.nativeElement as HTMLElement;
    (el.querySelectorAll('th .sort-btn')[1] as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(bodyText(fixture, 1)).toEqual(['10 sh', '20 sh', '30 sh']);
  });

  it('exposes aria-sort on the active column', () => {
    const fixture = render();
    const el = fixture.nativeElement as HTMLElement;
    (el.querySelectorAll('th .sort-btn')[0] as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el.querySelectorAll('th')[0].getAttribute('aria-sort')).toBe('ascending');
    expect(el.querySelectorAll('th')[1].getAttribute('aria-sort')).toBe('none');
  });

  it('shows the empty message when there are no rows', () => {
    const fixture = TestBed.createComponent(DataTable<Row>);
    fixture.componentRef.setInput('columns', columns);
    fixture.componentRef.setInput('rows', []);
    fixture.componentRef.setInput('emptyMessage', 'Nothing here');
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.empty-row')?.textContent?.trim()).toBe('Nothing here');
  });

  it('does not mutate the input rows array when sorting', () => {
    const input = [...rows];
    const fixture = render(input);
    const el = fixture.nativeElement as HTMLElement;
    (el.querySelectorAll('th .sort-btn')[0] as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(input.map((r) => r.name)).toEqual(['Beta', 'Alpha', 'Gamma']);
  });
});
