import test from 'node:test';
import assert from 'node:assert/strict';

import { formatClock } from '../../wwwroot/js/timer-state.mjs';

test('formatClock formats sub-day elapsed seconds as HH:mm:ss', () => {
  assert.equal(formatClock(0), '00:00:00');
  assert.equal(formatClock(5), '00:00:05');
  assert.equal(formatClock(3661), '01:01:01');
});

test('formatClock clamps negative input to zero', () => {
  assert.equal(formatClock(-5), '00:00:00');
});

test('formatClock keeps counting past 24 hours instead of wrapping', () => {
  // A running timer can span more than a day (AGENTS.md: "Long TimeEntries are allowed", tracked
  // time may exceed 24 hours). The previous Date-based implementation wrapped back to 00:00:00 at
  // exactly 86400 seconds because it round-tripped through a calendar Date.
  assert.equal(formatClock(86400), '24:00:00');
  assert.equal(formatClock(90061), '25:01:01');
});
