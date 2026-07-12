import { Component, ElementRef, input, output, signal, viewChild } from '@angular/core';

/**
 * Accessible file picker with drag-and-drop. Emits the chosen {@link File}; it does not
 * upload anything itself, so import flows can wire it to the API service. Single-file by
 * design (the import endpoints take one file at a time).
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

  /** Emits when the user picks or drops a file. */
  readonly fileSelected = output<File>();

  private readonly fileInput = viewChild.required<ElementRef<HTMLInputElement>>('fileInput');
  protected readonly dragging = signal(false);

  protected openPicker(): void {
    if (!this.disabled()) {
      this.fileInput().nativeElement.click();
    }
  }

  protected onInputChange(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.emitFirst(input.files);
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
    this.emitFirst(event.dataTransfer?.files ?? null);
  }

  private emitFirst(files: FileList | null): void {
    const file = files?.[0];
    if (file) {
      this.fileSelected.emit(file);
    }
  }
}
