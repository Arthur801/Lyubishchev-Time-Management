import { formatDuration, getPresetSnapshot, isValidDateRange } from './dashboard-state.mjs';

const page = document.querySelector('#dashboard-page');

if (page) {
  const state = { range: 'today', snapshot: getPresetSnapshot('today') };
  const $ = selector => document.querySelector(selector);

  function renderSnapshot() {
    const { snapshot } = state;
    $('#range-label').textContent = snapshot.label;
    $('#total-duration').textContent = formatDuration(snapshot.totalMinutes);
    $('#change-summary').textContent = snapshot.change;
    const topCategory = [...snapshot.categories].sort((a, b) => b.minutes - a.minutes)[0];
    const topTag = [...snapshot.tags].sort((a, b) => b.minutes - a.minutes)[0];
    $('#top-category').textContent = topCategory.name;
    $('#top-category-duration').textContent = formatDuration(topCategory.minutes);
    $('#top-tag').textContent = topTag.name;
    $('#top-tag-duration').textContent = formatDuration(topTag.minutes);
    renderTrend(snapshot.daily); renderCategories(snapshot.categories, snapshot.totalMinutes); renderTags(snapshot.tags); renderRecent(snapshot.recent);
  }

  function renderTrend(daily) {
    const highest = Math.max(...daily.map(item => item.minutes), 1);
    $('#trend-chart').innerHTML = daily.map(item => `<div class="trend-bar"><span style="height:${Math.max(8, item.minutes / highest * 100)}%" title="${item.label}：${formatDuration(item.minutes)}"></span><strong>${item.label}</strong></div>`).join('');
    $('#trend-summary').textContent = daily.map(item => `${item.label} ${formatDuration(item.minutes)}`).join('，');
  }

  function renderCategories(categories, total) {
    let cursor = 0;
    const stops = categories.map(item => { const next = cursor + item.minutes / total * 100; const value = `${item.color} ${cursor}% ${next}%`; cursor = next; return value; });
    $('#category-donut').style.background = `conic-gradient(${stops.join(',')})`;
    $('#donut-total').textContent = formatDuration(total);
    $('#category-legend').innerHTML = categories.map(item => `<li><span class="legend-name"><i class="legend-dot" style="background:${item.color}"></i>${item.name}</span><strong>${formatDuration(item.minutes)}</strong></li>`).join('');
  }

  function renderTags(tags) {
    const highest = Math.max(...tags.map(item => item.minutes), 1);
    $('#tag-chart').innerHTML = tags.map(item => `<div class="tag-row"><span>${item.name}</span><div class="tag-track"><div class="tag-fill" style="width:${item.minutes / highest * 100}%"></div></div><strong>${formatDuration(item.minutes)}</strong></div>`).join('');
  }

  function renderRecent(recent) { $('#recent-list').innerHTML = recent.slice(0, 3).map(item => `<li class="recent-item"><strong>${item.name}</strong><span>${item.category} · ${item.duration}</span><span>${item.time}</span></li>`).join(''); }

  document.querySelectorAll('[data-range]').forEach(button => button.addEventListener('click', () => {
    const range = button.dataset.range;
    if (range === 'custom') { $('#custom-range').hidden = false; return; }
    state.range = range; state.snapshot = getPresetSnapshot(range); $('#custom-range').hidden = true; $('#range-error').textContent = '';
    document.querySelectorAll('[data-range]').forEach(item => { const selected = item === button; item.classList.toggle('range-button--active', selected); item.setAttribute('aria-pressed', String(selected)); }); renderSnapshot();
  }));
  $('#apply-range').addEventListener('click', () => {
    const start = $('#range-start').value, end = $('#range-end').value;
    if (!isValidDateRange(start, end)) { $('#range-error').textContent = '結束日期必須晚於或等於開始日期。'; return; }
    state.range = 'custom'; state.snapshot = getPresetSnapshot('week'); state.snapshot.label = `自訂 · ${start} 至 ${end}`; state.snapshot.change = '自訂區間的前端範例資料'; $('#range-error').textContent = ''; renderSnapshot();
  });
  renderSnapshot();
}
