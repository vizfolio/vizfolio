import { of } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { pollWhilePending } from './poll';

describe('pollWhilePending', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('refetches while the result is pending and stops once it is not', () => {
    const answers = [true, true, false];
    let calls = 0;
    const seen: boolean[] = [];

    pollWhilePending(() => of(answers[calls++]), (pending) => pending, 1000).subscribe((v) => seen.push(v));

    expect(seen).toEqual([true]);
    vi.advanceTimersByTime(1000);
    expect(seen).toEqual([true, true]);
    vi.advanceTimersByTime(1000);
    expect(seen).toEqual([true, true, false]);
    vi.advanceTimersByTime(5000);
    expect(calls).toBe(3);
  });

  it('gives up after the maximum number of refetches', () => {
    let calls = 0;
    pollWhilePending(() => of(++calls), () => true, 1000, 2).subscribe();

    vi.advanceTimersByTime(10000);
    expect(calls).toBe(3); // the first fetch + 2 refetches
  });
});
