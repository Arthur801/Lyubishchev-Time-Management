import test from 'node:test';
import assert from 'node:assert/strict';

import {
  zonedTimeToUtcIso,
  dateTimeLocalValueToUtcIso,
  isoToDateTimeLocalValue,
  formatTimeOfDayInZone,
  zonedDateKey,
  getZonedRangeForPreset,
  getZonedDateParts,
  addZonedDays,
  weekdayOfDate,
  zonedDayUtcBounds,
} from '../../wwwroot/js/timezone.mjs';

test('zonedTimeToUtcIso converts an Asia/Taipei (UTC+8) wall-clock time to UTC', () => {
  assert.equal(zonedTimeToUtcIso(2026, 9, 17, 14, 0, 'Asia/Taipei'), '2026-09-17T06:00:00.000Z');
});

test('dateTimeLocalValueToUtcIso parses the <input type="datetime-local"> format in the given zone', () => {
  assert.equal(dateTimeLocalValueToUtcIso('2026-09-17T14:00', 'Asia/Taipei'), '2026-09-17T06:00:00.000Z');
  assert.equal(dateTimeLocalValueToUtcIso('2026-09-17T14:00', 'UTC'), '2026-09-17T14:00:00.000Z');
});

test('isoToDateTimeLocalValue renders a UTC instant back as account-timezone wall-clock, not browser-local', () => {
  assert.equal(isoToDateTimeLocalValue('2026-09-17T06:00:00.000Z', 'Asia/Taipei'), '2026-09-17T14:00');
});

test('formatTimeOfDayInZone and zonedDateKey read the account timezone', () => {
  assert.equal(formatTimeOfDayInZone('2026-09-17T06:00:00.000Z', 'Asia/Taipei'), '14:00');
  assert.equal(zonedDateKey('2026-09-17T06:00:00.000Z', 'Asia/Taipei'), '2026-09-17');
  // The same instant is still the previous calendar day in UTC, proving the zone is actually applied.
  assert.equal(zonedDateKey('2026-09-17T06:00:00.000Z', 'UTC'), '2026-09-17');
  assert.equal(zonedDateKey('2026-09-17T01:00:00.000Z', 'Asia/Taipei'), '2026-09-17');
  assert.equal(zonedDateKey('2026-09-16T17:00:00.000Z', 'Asia/Taipei'), '2026-09-17');
});

test('getZonedRangeForPreset computes today/week/month boundaries in the account timezone', () => {
  const reference = new Date('2026-09-17T01:30:00.000Z'); // 2026-09-17 09:30 in Asia/Taipei

  const today = getZonedRangeForPreset('today', 'Asia/Taipei', reference);
  assert.equal(today.startUtc, '2026-09-16T16:00:00.000Z');
  assert.equal(today.endUtc, '2026-09-17T16:00:00.000Z');

  const month = getZonedRangeForPreset('month', 'Asia/Taipei', reference);
  assert.equal(month.startUtc, '2026-08-31T16:00:00.000Z');
  assert.equal(month.endUtc, '2026-09-30T16:00:00.000Z');

  const unknown = getZonedRangeForPreset('all', 'Asia/Taipei', reference);
  assert.deepEqual(unknown, { startUtc: null, endUtc: null });
});

test('getZonedRangeForPreset week boundary starts on Sunday in the account timezone', () => {
  // 2026-09-17 is a Thursday; the week (Sun-start) begins 2026-09-13.
  const reference = new Date('2026-09-17T01:30:00.000Z');
  const week = getZonedRangeForPreset('week', 'Asia/Taipei', reference);
  assert.equal(week.startUtc, '2026-09-12T16:00:00.000Z'); // 2026-09-13 00:00 +08:00
  assert.equal(week.endUtc, '2026-09-19T16:00:00.000Z'); // 2026-09-20 00:00 +08:00
});

test('getZonedDateParts reads the calendar date/time in the account timezone, not browser-local', () => {
  const parts = getZonedDateParts(new Date('2026-09-17T01:30:00.000Z'), 'Asia/Taipei');
  assert.equal(parts.year, 2026);
  assert.equal(parts.month, 9);
  assert.equal(parts.day, 17);
  assert.equal(parts.hour, 9);
  assert.equal(parts.minute, 30);
});

test('addZonedDays adds/subtracts calendar days including month and year rollover', () => {
  assert.deepEqual(addZonedDays(2026, 9, 17, 1), { year: 2026, month: 9, day: 18 });
  assert.deepEqual(addZonedDays(2026, 9, 30, 1), { year: 2026, month: 10, day: 1 });
  assert.deepEqual(addZonedDays(2026, 1, 1, -1), { year: 2025, month: 12, day: 31 });
});

test('weekdayOfDate returns a Sunday-start (0-6) weekday index', () => {
  assert.equal(weekdayOfDate(2026, 9, 13), 0); // Sunday
  assert.equal(weekdayOfDate(2026, 9, 17), 4); // Thursday
});

test('zonedDayUtcBounds returns one local calendar day as [startUtc, endUtc) in the account timezone', () => {
  const bounds = zonedDayUtcBounds(2026, 9, 17, 'Asia/Taipei');
  assert.equal(bounds.startUtc, '2026-09-16T16:00:00.000Z');
  assert.equal(bounds.endUtc, '2026-09-17T16:00:00.000Z');
});
