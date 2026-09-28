import { formatDuration, isValidDateRange } from './dashboard-state.mjs';

const page = document.querySelector('#report-page');

if (page) {
  const $ = (selector) => document.querySelector(selector);

  let requestId = 0;
  let selectedRange = { preset: 'month' };

  function getCsrfToken() {
    return document.querySelector('meta[name="csrf-token"]')?.content ?? '';
  }

  function clearTrendAnalysis() {
    $('#trend-analysis-result').replaceChildren();
    $('#trend-analysis-result').hidden = true;
    $('#trend-analysis-status').textContent = '';
  }

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
    clearTrendAnalysis();
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
    selectedRange = mode === 'custom'
      ? { startDate: params.get('startDate'), endDate: params.get('endDate') }
      : { preset: mode };
  }

  function appendSection(container, heading, items, renderItem) {
    if (!items?.length) return;
    const section = document.createElement('section');
    section.className = 'trend-analysis-result__section';
    const title = document.createElement('h3');
    title.textContent = heading;
    const list = document.createElement('ul');
    items.forEach((item) => list.appendChild(renderItem(item)));
    section.append(title, list);
    container.appendChild(section);
  }

  function renderTrendAnalysis(payload) {
    const result = $('#trend-analysis-result');
    const analysis = payload.analysis;
    result.replaceChildren();
    const summary = document.createElement('p');
    summary.className = 'trend-analysis-result__summary';
    summary.textContent = analysis.summary;
    result.appendChild(summary);
    appendSection(result, '觀察', analysis.observations, (observation) => {
      const li = document.createElement('li');
      const claim = document.createElement('span');
      claim.textContent = observation.claim;
      const evidence = document.createElement('div');
      evidence.className = 'trend-analysis-result__evidence';
      evidence.textContent = observation.evidence;
      li.append(claim, evidence);
      return li;
    });
    appendSection(result, '建議', analysis.suggestions, (suggestion) => {
      const li = document.createElement('li');
      li.textContent = suggestion;
      return li;
    });
    const disclaimer = document.createElement('p');
    disclaimer.className = 'panel-note';
    disclaimer.textContent = analysis.disclaimer;
    result.appendChild(disclaimer);
    result.hidden = false;
  }

  async function analyzeTrend() {
    const button = $('#analyze-trend');
    button.disabled = true;
    $('#trend-analysis-status').textContent = '正在分析目前區間的時間趨勢…';
    $('#trend-analysis-result').hidden = true;
    try {
      const response = await fetch('/api/reports/trend-analysis', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': getCsrfToken() },
        body: JSON.stringify(selectedRange),
      });
      const payload = await response.json().catch(() => null);
      if (!response.ok) throw new Error(payload?.detail || '時間趨勢分析暫時無法使用。');
      renderTrendAnalysis(payload);
      $('#trend-analysis-status').textContent = '';
    } catch (error) {
      $('#trend-analysis-status').textContent = error.message;
    } finally {
      button.disabled = false;
    }
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

  $('#analyze-trend').addEventListener('click', analyzeTrend);

  runRequest(new URLSearchParams({ preset: 'month' }), 'month');
}
