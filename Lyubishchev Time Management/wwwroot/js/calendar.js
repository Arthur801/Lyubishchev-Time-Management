import { formatDuration } from './dashboard-state.mjs';
import {
  getZonedDateParts,
  addZonedDays,
  weekdayOfDate,
  zonedDayUtcBounds,
  formatTimeOfDayInZone,
} from './timezone.mjs';
import { splitEntryIntoDaySegments, layoutOverlappingSegments } from './calendar-state.mjs';

const section = document.querySelector('#calendar-section');

if (section) {
  const $ = (selector) => section.querySelector(selector);
  const weekdayLabels = ['日', '一', '二', '三', '四', '五', '六'];
  const desktopQuery = window.matchMedia('(min-width: 768px)');

  const state = {
    mode: desktopQuery.matches ? 'week' : 'day',
    anchor: null,
    // Overwritten from GET /api/settings/timezone before the first fetch; this fallback only
    // covers the brief window before that call resolves.
    timeZoneId: Intl.DateTimeFormat().resolvedOptions().timeZone,
    entries: [],
  };

  async function fetchJson(path) {
    const response = await fetch(path);
    const payload = await response.json().catch(() => null);
    if (!response.ok) {
      throw new Error(payload?.detail || '發生錯誤，請稍後再試。');
    }
    return payload;
  }

  function dateKeyOf({ year, month, day }) {
    const pad = (n) => String(n).padStart(2, '0');
    return `${year}-${pad(month)}-${pad(day)}`;
  }

  function visibleDates() {
    if (state.mode === 'day') {
      return [state.anchor];
    }

    const weekday = weekdayOfDate(state.anchor.year, state.anchor.month, state.anchor.day);
    const start = addZonedDays(state.anchor.year, state.anchor.month, state.anchor.day, -weekday);
    return Array.from({ length: 7 }, (_, index) => addZonedDays(start.year, start.month, start.day, index));
  }

  function formatRangeLabel(dates) {
    const first = dates[0];
    const last = dates[dates.length - 1];
    if (state.mode === 'day') {
      const weekday = weekdayLabels[weekdayOfDate(first.year, first.month, first.day)];
      return `${first.year} 年 ${first.month} 月 ${first.day} 日 · 週${weekday}`;
    }
    return `${first.year} 年 ${first.month} 月 ${first.day} 日 – ${last.month} 月 ${last.day} 日`;
  }

  function renderBlock(segment) {
    const { entry } = segment;
    const color = entry.categoryColor ?? '#a6adb7';
    const name = entry.name || '未命名活動';
    const startLabel = formatTimeOfDayInZone(entry.startTimeUtc, state.timeZoneId);
    const endLabel = formatTimeOfDayInZone(entry.endTimeUtc, state.timeZoneId);
    const durationLabel = formatDuration(entry.durationSeconds);

    const block = document.createElement('div');
    block.className = 'calendar-block';
    block.style.setProperty('--entry-color', color);
    block.style.top = `${(segment.startMinutes / 1440) * 100}%`;
    block.style.height = `${Math.max(((segment.endMinutes - segment.startMinutes) / 1440) * 100, 1.5)}%`;
    block.style.left = `${(segment.column / segment.columnCount) * 100}%`;
    block.style.width = `${100 / segment.columnCount}%`;
    block.title = `${name}\n${startLabel} – ${endLabel}（${durationLabel}）`;
    block.setAttribute('aria-label', `${name}，${startLabel}到${endLabel}，共 ${durationLabel}`);

    const nameEl = document.createElement('strong');
    nameEl.textContent = name;
    block.appendChild(nameEl);

    const timeEl = document.createElement('span');
    timeEl.className = 'calendar-block__time';
    timeEl.textContent = `${startLabel}–${endLabel}`;
    block.appendChild(timeEl);

    if (entry.tags.length > 0) {
      const tagsEl = document.createElement('div');
      tagsEl.className = 'calendar-block__tags';
      entry.tags.forEach((tag) => {
        const chip = document.createElement('span');
        chip.className = 'tag-chip';
        chip.textContent = tag;
        tagsEl.appendChild(chip);
      });
      block.appendChild(tagsEl);
    }

    return block;
  }

  function render(dates) {
    const dayBounds = dates.map((date) => ({
      dateKey: dateKeyOf(date),
      ...zonedDayUtcBounds(date.year, date.month, date.day, state.timeZoneId),
    }));

    const segmentsByDay = new Map(dayBounds.map((day) => [day.dateKey, []]));
    for (const entry of state.entries) {
      for (const segment of splitEntryIntoDaySegments(entry, dayBounds)) {
        segmentsByDay.get(segment.dateKey)?.push(segment);
      }
    }

    const todayKey = dateKeyOf(getZonedDateParts(new Date(), state.timeZoneId));

    const grid = $('#calendar-grid');
    grid.className = `calendar-grid calendar-grid--${state.mode}`;
    grid.innerHTML = '';

    dates.forEach((date) => {
      const dateKey = dateKeyOf(date);
      const column = document.createElement('div');
      column.className = 'calendar-day';
      if (dateKey === todayKey) column.classList.add('calendar-day--today');

      const heading = document.createElement('div');
      heading.className = 'calendar-day__heading';
      const weekday = weekdayLabels[weekdayOfDate(date.year, date.month, date.day)];
      heading.textContent = `${date.month}/${date.day} (${weekday})`;
      column.appendChild(heading);

      const track = document.createElement('div');
      track.className = 'calendar-day__track';
      layoutOverlappingSegments(segmentsByDay.get(dateKey) ?? []).forEach((segment) => {
        track.appendChild(renderBlock(segment));
      });
      column.appendChild(track);

      grid.appendChild(column);
    });
  }

  async function fetchAndRender() {
    const dates = visibleDates();
    $('#calendar-range-label').textContent = formatRangeLabel(dates);

    const first = dates[0];
    const last = dates[dates.length - 1];
    const { startUtc } = zonedDayUtcBounds(first.year, first.month, first.day, state.timeZoneId);
    const { endUtc } = zonedDayUtcBounds(last.year, last.month, last.day, state.timeZoneId);

    try {
      const params = new URLSearchParams({ startUtc, endUtc, page: '1', pageSize: '200' });
      const result = await fetchJson(`/api/time-entries?${params.toString()}`);
      state.entries = result.items;
      render(dates);
    } catch (error) {
      $('#calendar-grid').innerHTML = '';
      const message = document.createElement('p');
      message.className = 'calendar-error';
      message.textContent = error.message;
      $('#calendar-grid').appendChild(message);
    }
  }

  function moveAnchor(deltaDays) {
    state.anchor = addZonedDays(state.anchor.year, state.anchor.month, state.anchor.day, deltaDays);
    fetchAndRender();
  }

  $('#calendar-prev').addEventListener('click', () => moveAnchor(state.mode === 'day' ? -1 : -7));
  $('#calendar-next').addEventListener('click', () => moveAnchor(state.mode === 'day' ? 1 : 7));
  $('#calendar-today').addEventListener('click', () => {
    state.anchor = getZonedDateParts(new Date(), state.timeZoneId);
    fetchAndRender();
  });

  desktopQuery.addEventListener('change', (event) => {
    state.mode = event.matches ? 'week' : 'day';
    fetchAndRender();
  });

  async function init() {
    try {
      const settings = await fetchJson('/api/settings/timezone');
      state.timeZoneId = settings.timeZoneId;
    } catch {
      // Keep the browser-timezone fallback set on `state` above.
    }
    state.anchor = getZonedDateParts(new Date(), state.timeZoneId);
    await fetchAndRender();
  }

  init();
}
