import { formatDuration, isValidDateRange } from './dashboard-state.mjs';

const page = document.querySelector('#report-page');

if (page) {
  const $ = (selector) => document.querySelector(selector);

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

  function renderCategory(response) {
    const donut = $('#category-donut');
    const legend = $('#category-legend');
    legend.innerHTML = '';

    if (response.categoryTotals.length === 0 || response.totalSeconds <= 0) {
      donut.style.background = '#edf0f2';
      $('#donut-total').textContent = formatDuration(0);
      const empty = document.createElement('li');
      empty.textContent = '尚無分類紀錄';
      legend.appendChild(empty);
      return;
    }

    let cursor = 0;
    const stops = response.categoryTotals.map((item) => {
      const next = cursor + (item.durationSeconds / response.totalSeconds) * 100;
      const stop = `${item.color} ${cursor}% ${next}%`;
      cursor = next;
      return stop;
    });
    donut.style.background = `conic-gradient(${stops.join(',')})`;
    $('#donut-total').textContent = formatDuration(response.totalSeconds);

    response.categoryTotals.forEach((item) => {
      const li = document.createElement('li');
      const nameSpan = document.createElement('span');
      nameSpan.className = 'legend-name';
      const dot = document.createElement('i');
      dot.className = 'legend-dot';
      dot.style.background = item.color;
      nameSpan.append(dot, document.createTextNode(item.name));
      const strong = document.createElement('strong');
      const percentage = Math.round((item.durationSeconds / response.totalSeconds) * 100);
      strong.textContent = `${formatDuration(item.durationSeconds)}（${percentage}%）`;
      li.append(nameSpan, strong);
      legend.appendChild(li);
    });
  }

  function renderTag(response) {
    const chart = $('#tag-chart');
    chart.innerHTML = '';

    if (response.tagTotals.length === 0) {
      const empty = document.createElement('p');
      empty.className = 'panel-note';
      empty.textContent = '尚無標籤紀錄';
      chart.appendChild(empty);
      return;
    }

    const highest = Math.max(...response.tagTotals.map((tag) => tag.durationSeconds), 1);
    response.tagTotals.forEach((item) => {
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

  async function runRequest(params, mode) {
    const myRequestId = ++requestId;
    let categoryResponse;
    let tagResponse;
    let categoryPayload;
    let tagPayload;
    try {
      [categoryResponse, tagResponse] = await Promise.all([
        fetch(`/api/reports/category?${params.toString()}`),
        fetch(`/api/reports/tag?${params.toString()}`),
      ]);
      [categoryPayload, tagPayload] = await Promise.all([
        categoryResponse.json().catch(() => null),
        tagResponse.json().catch(() => null),
      ]);
    } catch {
      if (myRequestId === requestId) $('#report-range-error').textContent = '發生錯誤，請稍後再試。';
      return;
    }

    if (myRequestId !== requestId) return; // A newer range request has since superseded this one.

    if (!categoryResponse.ok || !tagResponse.ok) {
      $('#report-range-error').textContent = categoryPayload?.detail || tagPayload?.detail || '發生錯誤，請稍後再試。';
      return;
    }

    $('#report-range-error').textContent = '';
    $('#report-range-label').textContent = formatRangeLabel(mode, categoryPayload);
    renderCategory(categoryPayload);
    renderTag(tagPayload);
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
      $('#report-range-error').textContent = '';
      runRequest(new URLSearchParams({ preset: range }), range);
    });
  });

  $('#apply-range').addEventListener('click', () => {
    const start = $('#range-start').value;
    const end = $('#range-end').value;
    if (!isValidDateRange(start, end)) {
      $('#report-range-error').textContent = '結束日期必須晚於或等於開始日期。';
      return;
    }

    $('#report-range-error').textContent = '';
    setActiveRangeButton($('#range-custom'));
    runRequest(new URLSearchParams({ startDate: start, endDate: end }), 'custom');
  });

  runRequest(new URLSearchParams({ preset: 'month' }), 'month');
}
