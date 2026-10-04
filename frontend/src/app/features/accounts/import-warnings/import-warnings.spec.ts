import { TestBed } from '@angular/core/testing';

import { ImportWarnings } from './import-warnings';

describe('ImportWarnings', () => {
  function render(warnings: unknown[]) {
    const fixture = TestBed.createComponent(ImportWarnings);
    fixture.componentRef.setInput('warnings', warnings);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('renders nothing when the import understood everything', () => {
    expect(render([]).querySelector('details')).toBeNull();
  });

  it('summarises how many rows need a look and lists each warning with examples', () => {
    const el = render([
      { code: 'UnmappedLabel', message: 'Label X was imported as Other.', count: 2, samples: ['row 5', 'row 6'] },
      { code: 'RowFailed', message: 'A row was skipped.', count: 1, samples: [] },
    ]);

    expect(el.querySelector('summary')?.textContent).toContain('3 rows need a look');
    const items = el.querySelectorAll('li');
    expect(items.length).toBe(2);
    expect(items[0].textContent).toContain('(2 rows)');
    expect(items[0].textContent).toContain('e.g. row 5, row 6');
    expect(items[1].textContent).not.toContain('rows)');
  });
});
