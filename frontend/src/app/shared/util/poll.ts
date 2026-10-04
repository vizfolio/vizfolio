import { EMPTY, Observable, expand, switchMap, timer } from 'rxjs';

/**
 * Fetches once, then again every `intervalMs` while `pending(value)` holds (e.g. prices still downloading in the
 * background), up to `maxRefetches` times. Emits every result, so the view updates as data arrives.
 */
export function pollWhilePending<T>(
  fetch: () => Observable<T>,
  pending: (value: T) => boolean,
  intervalMs = 5000,
  maxRefetches = 60,
): Observable<T> {
  let refetches = 0;
  return fetch().pipe(
    expand((value) =>
      pending(value) && refetches++ < maxRefetches ? timer(intervalMs).pipe(switchMap(fetch)) : EMPTY,
    ),
  );
}
