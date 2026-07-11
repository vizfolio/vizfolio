import { TestBed } from '@angular/core/testing';

import { FileUpload } from './file-upload';

function render() {
  const fixture = TestBed.createComponent(FileUpload);
  fixture.detectChanges();
  return fixture;
}

describe('FileUpload', () => {
  it('emits the selected file when the input changes', () => {
    const fixture = render();
    const emitted: File[] = [];
    fixture.componentInstance.fileSelected.subscribe((f) => emitted.push(f));

    const file = new File(['x'], 'positions.qfx');
    const input = (fixture.nativeElement as HTMLElement).querySelector(
      'input[type=file]',
    ) as HTMLInputElement;
    Object.defineProperty(input, 'files', { value: [file], configurable: true });
    input.dispatchEvent(new Event('change'));

    expect(emitted).toEqual([file]);
  });

  it('emits the first dropped file and clears the dragging state', () => {
    const fixture = render();
    const emitted: File[] = [];
    fixture.componentInstance.fileSelected.subscribe((f) => emitted.push(f));

    const file = new File(['x'], 'dropped.csv');
    const dropzone = (fixture.nativeElement as HTMLElement).querySelector('.dropzone')!;
    const event = new Event('drop') as DragEvent;
    Object.defineProperty(event, 'dataTransfer', { value: { files: [file] } });
    Object.defineProperty(event, 'preventDefault', { value: () => {} });
    dropzone.dispatchEvent(event);
    fixture.detectChanges();

    expect(emitted).toEqual([file]);
    expect(dropzone.classList.contains('dragging')).toBe(false);
  });

  it('does not emit when disabled', () => {
    const fixture = render();
    fixture.componentRef.setInput('disabled', true);
    fixture.detectChanges();
    const emitted: File[] = [];
    fixture.componentInstance.fileSelected.subscribe((f) => emitted.push(f));

    const dropzone = (fixture.nativeElement as HTMLElement).querySelector('.dropzone')!;
    const event = new Event('drop') as DragEvent;
    Object.defineProperty(event, 'dataTransfer', {
      value: { files: [new File(['x'], 'x.qfx')] },
    });
    Object.defineProperty(event, 'preventDefault', { value: () => {} });
    dropzone.dispatchEvent(event);

    expect(emitted).toEqual([]);
  });
});
