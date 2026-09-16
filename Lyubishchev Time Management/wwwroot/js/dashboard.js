import { addCompletedEntry, formatDuration, getPresetSnapshot, isValidDateRange } from './dashboard-state.mjs';

const page = document.querySelector('#dashboard-page');

if (page) {
  const state = {
    range: 'today', snapshot: getPresetSnapshot('today'), timerRunning: false, startedAt: null,
    timerTags: ['規劃', '深度工作'], elapsedSeconds: 0, interval: null
  };
  const $ = selector => document.querySelector(selector);
  const formatClock = seconds => new Date(Math.max(0, seconds) * 1000).toISOString().slice(11, 19);

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
  function renderTagsEditor() { $('#timer-tags').innerHTML = state.timerTags.map(tag => `<span class="tag-chip">${tag}<button type="button" data-remove-tag="${tag}" aria-label="移除標籤 ${tag}">×</button></span>`).join(''); }
  function renderTimer() { $('#timer-display').textContent = formatClock(state.elapsedSeconds); $('#timer-toggle').textContent = state.timerRunning ? '停止計時' : '開始計時'; $('#timer-toggle').classList.toggle('is-running', state.timerRunning); $('#timer-toggle').setAttribute('aria-pressed', String(state.timerRunning)); $('#timer-status').textContent = state.timerRunning ? '正在記錄，專注進行中' : '尚未開始計時'; }

  function stopTimer() {
    window.clearInterval(state.interval); state.timerRunning = false;
    const minutes = Math.max(1, Math.round(state.elapsedSeconds / 60));
    state.snapshot = addCompletedEntry(state.snapshot, { name: $('#timer-name').value.trim(), category: $('#timer-category').value, tags: state.timerTags, minutes });
    state.elapsedSeconds = 0; renderTimer(); renderSnapshot();
  }

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
  $('#timer-toggle').addEventListener('click', () => { if (state.timerRunning) { stopTimer(); return; } state.timerRunning = true; state.startedAt = Date.now() - state.elapsedSeconds * 1000; state.interval = window.setInterval(() => { state.elapsedSeconds = Math.floor((Date.now() - state.startedAt) / 1000); renderTimer(); }, 1000); renderTimer(); });
  $('#add-tag').addEventListener('click', () => { const tag = $('#tag-input').value.trim(); if (tag && !state.timerTags.includes(tag)) state.timerTags.push(tag); $('#tag-input').value = ''; renderTagsEditor(); });
  $('#tag-input').addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); $('#add-tag').click(); } });
  $('#timer-tags').addEventListener('click', event => { const tag = event.target.dataset.removeTag; if (tag) { state.timerTags = state.timerTags.filter(item => item !== tag); renderTagsEditor(); } });
  renderTagsEditor(); renderTimer(); renderSnapshot();
}
