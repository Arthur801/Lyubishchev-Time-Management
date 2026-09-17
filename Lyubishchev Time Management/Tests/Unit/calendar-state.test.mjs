import test from 'node:test';
import assert from 'node:assert/strict';

import { splitEntryIntoDaySegments, layoutOverlappingSegments } from '../../wwwroot/js/calendar-state.mjs';

const days = [
  { dateKey: '2026-09-16', startUtc: '2026-09-15T16:00:00.000Z', endUtc: '2026-09-16T16:00:00.000Z' },
  { dateKey: '2026-09-17', startUtc: '2026-09-16T16:00:00.000Z', endUtc: '2026-09-17T16:00:00.000Z' },
  { dateKey: '2026-09-18', startUtc: '2026-09-17T16:00:00.000Z', endUtc: '2026-09-18T16:00:00.000Z' },
];

test('splitEntryIntoDaySegments keeps a same-day entry as a single segment', () => {
  // 2026-09-17 14:00-15:00 Asia/Taipei (+08:00) = 06:00-07:00 UTC, entirely inside the 09-17 local day.
  const entry = { startTimeUtc: '2026-09-17T06:00:00.000Z', endTimeUtc: '2026-09-17T07:00:00.000Z' };
  const segments = splitEntryIntoDaySegments(entry, days);
  assert.equal(segments.length, 1);
  assert.equal(segments[0].dateKey, '2026-09-17');
  assert.equal(segments[0].startMinutes, 14 * 60);
  assert.equal(segments[0].endMinutes, 15 * 60);
});

test('splitEntryIntoDaySegments splits an entry crossing a local midnight into two day segments', () => {
  // 2026-09-17 23:00 -> 2026-09-18 01:00 Asia/Taipei (+08:00) = 15:00 09-17Z -> 17:00 09-17Z.
  const entry = { startTimeUtc: '2026-09-17T15:00:00.000Z', endTimeUtc: '2026-09-17T17:00:00.000Z' };
  const segments = splitEntryIntoDaySegments(entry, days);
  assert.equal(segments.length, 2);

  const first = segments.find((s) => s.dateKey === '2026-09-17');
  assert.equal(first.startMinutes, 23 * 60);
  assert.equal(first.endMinutes, 24 * 60);

  const second = segments.find((s) => s.dateKey === '2026-09-18');
  assert.equal(second.startMinutes, 0);
  assert.equal(second.endMinutes, 60);
});

test('splitEntryIntoDaySegments drops days the entry does not overlap', () => {
  const entry = { startTimeUtc: '2026-09-17T06:00:00.000Z', endTimeUtc: '2026-09-17T07:00:00.000Z' };
  const segments = splitEntryIntoDaySegments(entry, days);
  assert.deepEqual(segments.map((s) => s.dateKey), ['2026-09-17']);
});

test('layoutOverlappingSegments keeps non-overlapping segments in a single column', () => {
  const segments = [
    { dateKey: '2026-09-17', startMinutes: 0, endMinutes: 60 },
    { dateKey: '2026-09-17', startMinutes: 60, endMinutes: 120 },
  ];
  const laidOut = layoutOverlappingSegments(segments);
  assert.deepEqual(laidOut.map((s) => s.column), [0, 0]);
  assert.deepEqual(laidOut.map((s) => s.columnCount), [1, 1]);
});

test('layoutOverlappingSegments assigns overlapping segments to different columns without hiding any', () => {
  const segments = [
    { id: 'a', startMinutes: 0, endMinutes: 120 },
    { id: 'b', startMinutes: 30, endMinutes: 90 },
    { id: 'c', startMinutes: 150, endMinutes: 180 }, // does not overlap a/b, should reuse a column
  ];
  const laidOut = layoutOverlappingSegments(segments);
  assert.equal(laidOut.length, 3);

  const byId = Object.fromEntries(laidOut.map((s) => [s.id, s]));
  assert.notEqual(byId.a.column, byId.b.column);
  assert.equal(byId.c.column, 0); // column 0 freed up after segment "a" ends at 120
  assert.equal(byId.a.columnCount, 2);
  assert.equal(byId.b.columnCount, 2);
  assert.equal(byId.c.columnCount, 2);
});
