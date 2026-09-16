import { formatDuration } from './dashboard-state.mjs';

const page = document.querySelector('#history-page');

if (page) {
  const CATEGORY_COLORS = { 工作: '#e5533d', 學習: '#51758c', 生活: '#c69650', 未分類: '#a6adb7' };
  const today = new Date('2026-09-17T00:00:00');

  const seedEntries = [
    { id: 1, name: '整理本週筆記', category: '工作', tags: ['規劃', '深度工作'], date: '2026-09-17', start: '09:20', end: '11:00', minutes: 100 },
    { id: 2, name: '閱讀技術文章', category: '學習', tags: ['閱讀'], date: '2026-09-17', start: '07:40', end: '08:50', minutes: 70 },
    { id: 3, name: '設計系統盤點', category: '工作', tags: ['深度工作'], date: '2026-09-16', start: '14:00', end: '16:20', minutes: 140 },
    { id: 4, name: '健身', category: '生活', tags: [], date: '2026-09-16', start: '18:30', end: '19:15', minutes: 45 },
    { id: 5, name: '閱讀研究資料', category: '學習', tags: ['閱讀', '研究'], date: '2026-09-15', start: '10:00', end: '12:00', minutes: 120 },
    { id: 6, name: '採買日用品', category: '生活', tags: [], date: '2026-09-15', start: '17:00', end: '17:40', minutes: 40 },
    { id: 7, name: '月度回顧籌備', category: '工作', tags: ['規劃'], date: '2026-09-12', start: '09:00', end: '10:10', minutes: 70 },
    { id: 8, name: '閱讀技術文章', category: '學習', tags: ['閱讀'], date: '2026-09-12', start: '20:00', end: '20:30', minutes: 30 },
    { id: 9, name: '季度歸檔', category: '工作', tags: ['整理'], date: '2026-08-28', start: '13:00', end: '14:30', minutes: 90 },
    { id: 10, name: '運動', category: '生活', tags: [], date: '2026-08-28', start: '07:00', end: '07:30', minutes: 30 }
  ];

  const state = { entries: seedEntries, range: 'today', category: '', search: '' };
  const $ = selector => document.querySelector(selector);

  function diffDays(dateStr) {
    return Math.round((today - new Date(`${dateStr}T00:00:00`)) / 86400000);
  }

  function matchesRange(entry) {
    const days = diffDays(entry.date);
    if (state.range === 'today') return days === 0;
    if (state.range === 'week') return days >= 0 && days <= 6;
    if (state.range === 'month') {
      const entryDate = new Date(`${entry.date}T00:00:00`);
      return entryDate.getFullYear() === today.getFullYear() && entryDate.getMonth() === today.getMonth();
    }
    return true;
  }

  function getFilteredEntries() {
    const query = state.search.trim().toLowerCase();
    return state.entries.filter(entry => {
      if (!matchesRange(entry)) return false;
      if (state.category && entry.category !== state.category) return false;
      if (query) {
        const haystack = [entry.name, ...entry.tags].join(' ').toLowerCase();
        if (!haystack.includes(query)) return false;
      }
      return true;
    });
  }

  function formatGroupHeading(dateStr) {
    const date = new Date(`${dateStr}T00:00:00`);
    const days = diffDays(dateStr);
    const label = days === 0 ? '今天' : days === 1 ? '昨天' : `${date.getMonth() + 1} 月 ${date.getDate()} 日`;
    const weekday = ['日', '一', '二', '三', '四', '五', '六'][date.getDay()];
    return `${label} · 週${weekday}`;
  }

  function populateCategoryFilter() {
    const categories = [...new Set(state.entries.map(entry => entry.category))];
    $('#filter-category').innerHTML = ['<option value="">全部分類</option>', ...categories.map(name => `<option value="${name}">${name}</option>`)].join('');
  }

  function renderEntryRow(entry) {
    const color = CATEGORY_COLORS[entry.category] ?? CATEGORY_COLORS['未分類'];
    const tagsMarkup = entry.tags.map(tag => `<span class="tag-chip">${tag}</span>`).join('');
    return `<li class="entry-row" data-id="${entry.id}">
      <div class="entry-row__main">
        <span class="entry-name"><i class="legend-dot" style="background:${color}"></i>${entry.name}</span>
        <div class="entry-meta">${tagsMarkup}</div>
      </div>
      <span class="entry-time">${entry.start} – ${entry.end}</span>
      <span class="entry-duration">${formatDuration(entry.minutes)}</span>
      <div class="entry-actions"><button class="icon-btn icon-btn--danger" type="button" data-delete="${entry.id}" aria-label="刪除「${entry.name}」">✕</button></div>
    </li>`;
  }

  function renderGroups() {
    const filtered = getFilteredEntries();
    $('#history-count').textContent = `共 ${filtered.length} 筆紀錄`;
    $('#empty-state').hidden = filtered.length > 0;

    const byDate = new Map();
    filtered.forEach(entry => {
      if (!byDate.has(entry.date)) byDate.set(entry.date, []);
      byDate.get(entry.date).push(entry);
    });

    const dates = [...byDate.keys()].sort((a, b) => (a < b ? 1 : -1));
    $('#entry-groups').innerHTML = dates.map(date => {
      const entries = byDate.get(date).sort((a, b) => (a.start < b.start ? 1 : -1));
      const totalMinutes = entries.reduce((sum, entry) => sum + entry.minutes, 0);
      return `<div class="date-group">
        <div class="date-group__heading"><strong>${formatGroupHeading(date)}</strong><span>${formatDuration(totalMinutes)}</span></div>
        <ul class="date-group__list">${entries.map(renderEntryRow).join('')}</ul>
      </div>`;
    }).join('');
  }

  document.querySelectorAll('[data-range]').forEach(button => button.addEventListener('click', () => {
    state.range = button.dataset.range;
    document.querySelectorAll('[data-range]').forEach(item => {
      const selected = item === button;
      item.classList.toggle('range-button--active', selected);
      item.setAttribute('aria-pressed', String(selected));
    });
    renderGroups();
  }));

  $('#filter-category').addEventListener('change', event => { state.category = event.target.value; renderGroups(); });
  $('#filter-search').addEventListener('input', event => { state.search = event.target.value; renderGroups(); });

  $('#entry-groups').addEventListener('click', event => {
    const id = event.target.dataset.delete;
    if (!id) return;
    const entry = state.entries.find(item => String(item.id) === id);
    if (entry && window.confirm(`確定要刪除「${entry.name}」嗎？`)) {
      state.entries = state.entries.filter(item => String(item.id) !== id);
      renderGroups();
    }
  });

  populateCategoryFilter();
  renderGroups();
}
