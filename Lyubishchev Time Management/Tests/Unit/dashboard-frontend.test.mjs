import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

import { formatDuration, isValidDateRange } from '../../wwwroot/js/dashboard-state.mjs';

test('formatDuration converts seconds to readable Chinese hours and minutes', () => {
  assert.equal(formatDuration(0), '0 小時 0 分');
  assert.equal(formatDuration(5700), '1 小時 35 分');
});

test('date range validation rejects an end date before its start date', () => {
  assert.equal(isValidDateRange('2026-09-17', '2026-09-16'), false);
  assert.equal(isValidDateRange('2026-09-16', '2026-09-17'), true);
});

test('dashboard view exposes the timer, range controls, and accessible chart summaries', async () => {
  const view = await readFile(new URL('../../Views/Dashboard/Index.cshtml', import.meta.url), 'utf8');

  assert.match(view, /id="timer-toggle"/);
  assert.match(view, /id="range-custom"/);
  assert.match(view, /id="trend-chart"/);
  assert.match(view, /aria-live="polite"/);
});

test('the shared layout stays generic while the dashboard view loads its own module', async () => {
  const [layout, view] = await Promise.all([
    readFile(new URL('../../Views/Shared/_Layout.cshtml', import.meta.url), 'utf8'),
    readFile(new URL('../../Views/Dashboard/Index.cshtml', import.meta.url), 'utf8'),
  ]);

  assert.match(layout, /lang="zh-Hant"/);
  assert.doesNotMatch(layout, /dashboard\.(css|js)/);
  assert.match(view, /dashboard\.css/);
  assert.match(view, /dashboard\.js/);
});
