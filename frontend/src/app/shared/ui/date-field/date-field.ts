import { Component, input, output } from '@angular/core';

/**
 * Reusable date control: a native `<input type="date">` styled with the app's design tokens so
 * every date field looks and behaves the same. Exchanges plain `YYYY-MM-DD` strings (what the
 * API expects); '' means "no date". The browser provides the calendar popup.
 *
 * This is the single source of truth for date inputs — swap the implementation here (e.g. to a
 * custom CDK calendar) without touching call sites.
 */
@Component({
  selector: 'app-date-field',
  template: `
    <label class="date-field">
      <span class="date-label">{{ label() }}</span>
      <input
        class="date-input"
        type="date"
        [value]="value()"
        [disabled]="disabled()"
        [attr.aria-label]="label()"
        (change)="onChange($event)"
      />
    </label>
  `,
  styleUrl: './date-field.scss',
})
export class DateField {
  readonly label = input('');
  /** Selected date as `YYYY-MM-DD`, or '' for none. */
  readonly value = input('');
  readonly disabled = input(false);
  readonly valueChange = output<string>();

  protected onChange(event: Event): void {
    // A native date input's value is already `YYYY-MM-DD` (or '' when cleared).
    this.valueChange.emit((event.target as HTMLInputElement).value);
  }
}
