import { ImportParser } from '../../core/api/models/imports.models';
import { SelectOption } from '../../shared/ui/select-field/select-field';

/** The default "let the backend figure it out" choice; its empty value means "send no override". */
const AUTO_DETECT: SelectOption = { value: '', label: 'Auto-detect' };

/** Used before the parser list has loaded (or if it comes back empty). */
const FALLBACK_ACCEPT = '.qfx,.ofx,.xlsx,.xls';

/** Dropdown choices for the import "Format" override: Auto-detect first, then one per parser. */
export function parserFormatOptions(parsers: readonly ImportParser[]): SelectOption[] {
  return [
    AUTO_DETECT,
    ...parsers.map((p) => ({ value: p.sourceSystem, label: p.displayName })),
  ];
}

/** The `accept` attribute for the file picker, aggregated from every parser's extensions. */
export function parserAcceptAttr(parsers: readonly ImportParser[]): string {
  const extensions = [...new Set(parsers.flatMap((p) => p.fileExtensions))];
  return extensions.length > 0 ? extensions.join(',') : FALLBACK_ACCEPT;
}
