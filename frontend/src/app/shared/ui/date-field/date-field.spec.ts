import { TestBed } from '@angular/core/testing';

import { DateField } from './date-field';

function setup(value = '') {
  TestBed.configureTestingModule({});
  const fixture = TestBed.createComponent(DateField);
  fixture.componentRef.setInput('label', 'From');
  fixture.componentRef.setInput('value', value);
  fixture.detectChanges();
  return fixture;
}

describe('DateField', () => {
  it('renders the label and reflects the value on the input', () => {
    const el = setup('2025-03-09').nativeElement as HTMLElement;
    expect(el.querySelector('.date-label')?.textContent).toContain('From');
    expect((el.querySelector('input') as HTMLInputElement).value).toBe('2025-03-09');
  });

  it('emits the YYYY-MM-DD value on change', () => {
    const fixture = setup('');
    const emitted: string[] = [];
    fixture.componentInstance.valueChange.subscribe((v) => emitted.push(v));

    const input = (fixture.nativeElement as HTMLElement).querySelector(
      'input',
    ) as HTMLInputElement;
    input.value = '2024-01-15';
    input.dispatchEvent(new Event('change'));

    expect(emitted).toEqual(['2024-01-15']);
  });

  it('reflects the disabled state', () => {
    const fixture = setup('2024-01-15');
    fixture.componentRef.setInput('disabled', true);
    fixture.detectChanges();
    expect(
      (fixture.nativeElement.querySelector('input') as HTMLInputElement).disabled,
    ).toBe(true);
  });
});
