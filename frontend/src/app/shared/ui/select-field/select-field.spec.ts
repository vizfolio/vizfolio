import { TestBed } from '@angular/core/testing';

import { SelectField, SelectOption } from './select-field';

const OPTIONS: SelectOption[] = [
  { value: '', label: 'Auto-detect' },
  { value: 'QFX', label: 'OFX / QFX statement' },
  { value: 'VANGUARD', label: 'Vanguard transaction report' },
];

function setup(value = '') {
  TestBed.configureTestingModule({});
  const fixture = TestBed.createComponent(SelectField);
  fixture.componentRef.setInput('label', 'Format');
  fixture.componentRef.setInput('options', OPTIONS);
  fixture.componentRef.setInput('value', value);
  fixture.detectChanges();
  return fixture;
}

describe('SelectField', () => {
  it('renders the label and one option per choice, reflecting the value', () => {
    const fixture = setup('QFX');
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.select-label')?.textContent).toContain('Format');
    expect(el.querySelectorAll('option').length).toBe(3);
    expect((el.querySelector('select') as HTMLSelectElement).value).toBe('QFX');
  });

  it('emits the selected option value on change', () => {
    const fixture = setup('');
    const emitted: string[] = [];
    fixture.componentInstance.valueChange.subscribe((v) => emitted.push(v));

    const select = (fixture.nativeElement as HTMLElement).querySelector('select') as HTMLSelectElement;
    select.value = 'VANGUARD';
    select.dispatchEvent(new Event('change'));

    expect(emitted).toEqual(['VANGUARD']);
  });

  it('reflects the disabled state', () => {
    const fixture = setup('QFX');
    fixture.componentRef.setInput('disabled', true);
    fixture.detectChanges();
    expect((fixture.nativeElement.querySelector('select') as HTMLSelectElement).disabled).toBe(true);
  });
});
