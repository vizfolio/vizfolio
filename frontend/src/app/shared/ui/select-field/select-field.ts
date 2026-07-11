import { Component, input, output } from '@angular/core';

/** One choice in a {@link SelectField}. */
export interface SelectOption {
  value: string;
  label: string;
}

/**
 * Reusable single-select control: a native `<select>` styled with the app's design tokens so every
 * dropdown looks and behaves the same. Exchanges the selected option's `value` string.
 *
 * This is the single source of truth for select inputs — swap the implementation here (e.g. to a
 * custom CDK listbox) without touching call sites.
 */
@Component({
  selector: 'app-select-field',
  template: `
    <label class="select-field">
      <span class="select-label">{{ label() }}</span>
      <select
        class="select-input"
        [disabled]="disabled()"
        [attr.aria-label]="label()"
        (change)="onChange($event)"
      >
        @for (option of options(); track option.value) {
          <option [value]="option.value" [selected]="option.value === value()">
            {{ option.label }}
          </option>
        }
      </select>
    </label>
  `,
  styleUrl: './select-field.scss',
})
export class SelectField {
  readonly label = input('');
  /** Currently selected option value. */
  readonly value = input('');
  readonly options = input<readonly SelectOption[]>([]);
  readonly disabled = input(false);
  readonly valueChange = output<string>();

  protected onChange(event: Event): void {
    this.valueChange.emit((event.target as HTMLSelectElement).value);
  }
}
