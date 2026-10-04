import { Component, ElementRef, input, output, signal, viewChild } from '@angular/core';

/**
 * Accessible file picker with drag-and-drop. Emits the chosen {@link File} (or, with `multiple`, every chosen file
 * through `filesSelected`); it does not upload anything itself, so import flows can wire it to the API service.
 */
@Component({
  selector: 'app-file-upload',
  templateUrl: './file-upload.html',
  styleUrl: './file-upload.scss',
})
export class FileUpload {
  /** `accept` attribute forwarded to the input, e.g. ".qfx,.ofx,.csv". */
  readonly accept = input('');
  /** Instructional label shown in the drop zone. */
  readonly label = input('Drag a file here, or choose one');
  /** Disables interaction (e.g. while an import is in flight). */
  readonly disabled = input(false);
  /** Accept several files at once (emitted together through `filesSelected`). */
  readonly multiple = input(false);

  /** Emits when the user picks or drops a file (the first, when several are dropped). */
  readonly fileSelected = output<File>();
  /** Emits every file the user picked or dropped. */
  readonly filesSelected = output<File[]>();

  private readonly fileInput = viewChild.required<ElementRef<HTMLInputElement>>('fileInput');
  protected readonly dragging = signal(false);

  protected openPicker(): void {
    if (!this.disabled()) {
      this.fileInput().nativeElement.click();
    }
  }

  protected onInputChange(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.emit(input.files);
    // Reset so selecting the same file again still fires a change event.
    input.value = '';
  }

  protected onDragOver(event: DragEvent): void {
    if (this.disabled()) {
      return;
    }
    event.preventDefault();
    this.dragging.set(true);
  }

  protected onDragLeave(): void {
    this.dragging.set(false);
  }

  protected onDrop(event: DragEvent): void {
    if (this.disabled()) {
      return;
    }
    event.preventDefault();
    this.dragging.set(false);
    this.emit(event.dataTransfer?.files ?? null);
  }

  private emit(files: FileList | null): void {
    const all = Array.from(files ?? []);
    if (all.length === 0) {
      return;
    }
    this.fileSelected.emit(all[0]);
    this.filesSelected.emit(this.multiple() ? all : [all[0]]);
  }
}
