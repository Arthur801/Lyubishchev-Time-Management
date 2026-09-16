import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

import {
  formatDuration,
  getPresetSnapshot,
  isValidDateRange,
  addCompletedEntry
} from '../../wwwroot/js/dashboard-state.mjs';

test('formatDuration converts minutes to readable Chinese hours and minutes', () => {
  assert.equal(formatDuration(0), '0 小時 0 分');
  assert.equal(formatDuration(95), '1 小時 35 分');
});

test('preset snapshots include uncategorized time and additive tag totals', () => {
  const snapshot = getPresetSnapshot('week');

  assert.ok(snapshot.categories.some(category => category.name === '未分類'));
  assert.ok(snapshot.tags.some(tag => tag.name === '深度工作'));
  assert.ok(snapshot.tags.reduce((sum, tag) => sum + tag.minutes, 0) > snapshot.totalMinutes);
});

test('date range validation rejects an end date before its start date', () => {
  assert.equal(isValidDateRange('2026-09-17', '2026-09-16'), false);
  assert.equal(isValidDateRange('2026-09-16', '2026-09-17'), true);
});

test('stopping a timer adds its completed entry to the current summary', () => {
  const updated = addCompletedEntry(getPresetSnapshot('today'), {
    name: '閱讀筆記',
    category: '學習',
    tags: ['閱讀', '深度工作'],
    minutes: 30
  });

  assert.equal(updated.totalMinutes, getPresetSnapshot('today').totalMinutes + 30);
  assert.equal(updated.recent[0].name, '閱讀筆記');
  assert.equal(updated.tags.find(tag => tag.name === '深度工作').minutes, 150);
});

test('dashboard view exposes the timer, range controls, and accessible chart summaries', async () => {
  const view = await readFile(new URL('../../Views/Dashboard/Index.cshtml', import.meta.url), 'utf8');

  assert.match(view, /id="timer-toggle"/);
  assert.match(view, /id="range-custom"/);
  assert.match(view, /id="trend-chart"/);
  assert.match(view, /aria-live="polite"/);
});

test('application layout loads the dashboard module without changing the auth layout', async () => {
  const layout = await readFile(new URL('../../Views/Shared/_Layout.cshtml', import.meta.url), 'utf8');

  assert.match(layout, /lang="zh-Hant"/);
  assert.match(layout, /dashboard\.css/);
  assert.match(layout, /dashboard\.js/);
});
