import { formatDuration, isValidDateRange } from './dashboard-state.mjs';

const page = document.querySelector('#dashboard-page');

if (page) {
  const $ = (selector) => document.querySelector(selector);
  const comparisonLabels = { today: '昨天', week: '上週', month: '上月', custom: '前一段區間' };

  let requestId = 0;

  function splitDate(iso) {
    const [year, month, day] = iso.split('-').map(Number);
    return { year, month, day };
  }

  function formatDateLong(iso) {
    const { year, month, day } = splitDate(iso);
    return `${year} 年 ${month} 月 ${day} 日`;
  }

  function formatDateShort(iso) {
    const { month, day } = splitDate(iso);
    return `${month} 月 ${day} 日`;
  }

  function formatMonthLong(iso) {
    const { year, month } = splitDate(iso);
    return `${year} 年 ${month} 月`;
  }

  function formatRangeLabel(mode, response) {
    switch (mode) {
      case 'today':
        return `今天 · ${formatDateLong(response.startDate)}`;
      case 'week':
        return `本週 · ${formatDateShort(response.startDate)} 至 ${formatDateShort(response.endDateInclusive)}`;
      case 'month':
        return `本月 · ${formatMonthLong(response.startDate)}`;
      default:
        return `自訂 · ${formatDateShort(response.startDate)} 至 ${formatDateShort(response.endDateInclusive)}`;
    }
  }

  function formatChangeSummary(mode, changeSeconds) {
    const label = comparisonLabels[mode] ?? comparisonLabels.custom;
    if (changeSeconds === 0) return `與${label}持平`;
    const amount = formatDuration(Math.abs(changeSeconds));
    return changeSeconds > 0 ? `比${label}多 ${amount}` : `比${label}少 ${amount}`;
  }

  function formatClockInZone(iso, timeZone) {
    return new Intl.DateTimeFormat('zh-TW', { timeZone, hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).format(new Date(iso));
  }

  function renderTopStat(nameSelector, durationSelector, items, emptyText) {
    const nameEl = $(nameSelector);
    const durationEl = $(durationSelector);
    if (!items || items.length === 0) {
      nameEl.textContent = emptyText;
      durationEl.textContent = '';
      return;
    }
    nameEl.textContent = items[0].name;
    durationEl.textContent = formatDuration(items[0].durationSeconds);
  }

  function renderTrend(dailyTotals) {
    const chart = $('#trend-chart');
    chart.innerHTML = '';
    const highest = Math.max(...dailyTotals.map((day) => day.durationSeconds), 1);
    dailyTotals.forEach((day) => {
      const bar = document.createElement('div');
      bar.className = 'trend-bar';
      const fill = document.createElement('span');
      fill.style.height = `${Math.max(4, (day.durationSeconds / highest) * 100)}%`;
      fill.title = `${formatDateShort(day.date)}：${formatDuration(day.durationSeconds)}`;
      const label = document.createElement('strong');
      label.textContent = String(splitDate(day.date).day);
      bar.append(fill, label);
      chart.appendChild(bar);
    });
    $('#trend-summary').textContent = dailyTotals
      .map((day) => `${formatDateShort(day.date)} ${formatDuration(day.durationSeconds)}`)
      .join('，');
  }

  function renderCategories(categories, totalSeconds) {
    const donut = $('#category-donut');
    const legend = $('#category-legend');
    legend.innerHTML = '';

    if (categories.length === 0 || totalSeconds <= 0) {
      donut.style.background = '#edf0f2';
      $('#donut-total').textContent = formatDuration(0);
      const empty = document.createElement('li');
      empty.textContent = '尚無分類紀錄';
      legend.appendChild(empty);
      return;
    }

    let cursor = 0;
    const stops = categories.map((item) => {
      const next = cursor + (item.durationSeconds / totalSeconds) * 100;
      const stop = `${item.color} ${cursor}% ${next}%`;
      cursor = next;
      return stop;
    });
    donut.style.background = `conic-gradient(${stops.join(',')})`;
    $('#donut-total').textContent = formatDuration(totalSeconds);

    categories.forEach((item) => {
      const li = document.createElement('li');
      const nameSpan = document.createElement('span');
      nameSpan.className = 'legend-name';
      const dot = document.createElement('i');
      dot.className = 'legend-dot';
      dot.style.background = item.color;
      nameSpan.append(dot, document.createTextNode(item.name));
      const strong = document.createElement('strong');
      strong.textContent = formatDuration(item.durationSeconds);
      li.append(nameSpan, strong);
      legend.appendChild(li);
    });
  }

  function renderTags(tags) {
    const chart = $('#tag-chart');
    chart.innerHTML = '';

    if (tags.length === 0) {
      const empty = document.createElement('p');
      empty.className = 'panel-note';
      empty.textContent = '尚無標籤紀錄';
      chart.appendChild(empty);
      return;
    }

    const highest = Math.max(...tags.map((tag) => tag.durationSeconds), 1);
    tags.forEach((item) => {
      const row = document.createElement('div');
      row.className = 'tag-row';
      const name = document.createElement('span');
      name.textContent = item.name;
      const track = document.createElement('div');
      track.className = 'tag-track';
      const fill = document.createElement('div');
      fill.className = 'tag-fill';
      fill.style.width = `${(item.durationSeconds / highest) * 100}%`;
      track.appendChild(fill);
      const strong = document.createElement('strong');
      strong.textContent = formatDuration(item.durationSeconds);
      row.append(name, track, strong);
      chart.appendChild(row);
    });
  }

  function renderRecent(entries, timeZone) {
    const list = $('#recent-list');
    list.innerHTML = '';

    if (entries.length === 0) {
      const li = document.createElement('li');
      li.className = 'recent-item';
      li.textContent = '尚無完成紀錄';
      list.appendChild(li);
      return;
    }

    entries.forEach((entry) => {
      const li = document.createElement('li');
      li.className = 'recent-item';
      const strong = document.createElement('strong');
      strong.textContent = entry.name || '未命名活動';
      const meta = document.createElement('span');
      meta.textContent = `${entry.categoryName ?? '未分類'} · ${formatDuration(entry.durationSeconds)}`;
      const time = document.createElement('span');
      time.textContent = `${formatClockInZone(entry.startTimeUtc, timeZone)} – ${formatClockInZone(entry.endTimeUtc, timeZone)}`;
      li.append(strong, meta, time);
      list.appendChild(li);
    });
  }

  function render(mode, response) {
    $('#range-label').textContent = formatRangeLabel(mode, response);
    $('#total-duration').textContent = formatDuration(response.totalSeconds);
    $('#change-summary').textContent = formatChangeSummary(mode, response.changeSeconds);
    renderTopStat('#top-category', '#top-category-duration', response.categoryTotals, '尚無分類紀錄');
    renderTopStat('#top-tag', '#top-tag-duration', response.tagTotals, '尚無標籤紀錄');
    renderTrend(response.dailyTotals);
    renderCategories(response.categoryTotals, response.totalSeconds);
    renderTags(response.tagTotals);
    renderRecent(response.recentEntries, response.timeZoneId);
  }

  async function runRequest(params, mode) {
    const myRequestId = ++requestId;
    let response;
    let payload;
    try {
      response = await fetch(`/api/dashboard?${params.toString()}`);
      payload = await response.json().catch(() => null);
    } catch {
      if (myRequestId === requestId) $('#range-error').textContent = '發生錯誤，請稍後再試。';
      return;
    }

    if (myRequestId !== requestId) return; // A newer range request has since superseded this one.

    if (!response.ok) {
      $('#range-error').textContent = payload?.detail || '發生錯誤，請稍後再試。';
      return;
    }

    $('#range-error').textContent = '';
    render(mode, payload);
  }

  function setActiveRangeButton(activeButton) {
    document.querySelectorAll('[data-range]').forEach((item) => {
      const selected = item === activeButton;
      item.classList.toggle('range-button--active', selected);
      item.setAttribute('aria-pressed', String(selected));
    });
  }

  document.querySelectorAll('[data-range]').forEach((button) => {
    button.addEventListener('click', () => {
      const range = button.dataset.range;
      if (range === 'custom') {
        $('#custom-range').hidden = false;
        return;
      }

      $('#custom-range').hidden = true;
      setActiveRangeButton(button);
      $('#range-error').textContent = '';
      runRequest(new URLSearchParams({ preset: range }), range);
    });
  });

  $('#apply-range').addEventListener('click', () => {
    const start = $('#range-start').value;
    const end = $('#range-end').value;
    if (!isValidDateRange(start, end)) {
      $('#range-error').textContent = '結束日期必須晚於或等於開始日期。';
      return;
    }

    $('#range-error').textContent = '';
    setActiveRangeButton($('#range-custom'));
    runRequest(new URLSearchParams({ startDate: start, endDate: end }), 'custom');
  });

  runRequest(new URLSearchParams({ preset: 'today' }), 'today');
}
