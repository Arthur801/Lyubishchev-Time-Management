import { formatDuration } from './dashboard-state.mjs';
import {
  getZonedRangeForPreset,
  isoToDateTimeLocalValue,
  dateTimeLocalValueToUtcIso,
  formatTimeOfDayInZone,
  zonedDateKey,
} from './timezone.mjs';

const page = document.querySelector('#history-page');

if (page) {
  const $ = (selector) => document.querySelector(selector);

  const state = {
    range: 'today',
    categoryId: '',
    search: '',
    page: 1,
    pageSize: 50,
    items: [],
    totalCount: 0,
    // Overwritten from GET /api/settings/timezone before the first fetch; this fallback only
    // covers the brief window before that call resolves.
    timeZoneId: Intl.DateTimeFormat().resolvedOptions().timeZone,
  };
  const modal = { tags: [], editingId: null };

  function getCsrfToken() {
    return document.querySelector('meta[name="csrf-token"]')?.content ?? '';
  }

  async function callApi(path, { method = 'GET', body } = {}) {
    const headers = { 'Content-Type': 'application/json' };
    if (method !== 'GET') headers['X-CSRF-TOKEN'] = getCsrfToken();

    const response = await fetch(path, { method, headers, body: body ? JSON.stringify(body) : undefined });
    const payload = await response.json().catch(() => null);
    if (!response.ok) {
      throw new Error(payload?.detail || '發生錯誤，請稍後再試。');
    }
    return payload;
  }

  function buildListQuery() {
    const { startUtc, endUtc } = getZonedRangeForPreset(state.range, state.timeZoneId);
    const params = new URLSearchParams();
    if (startUtc) params.set('startUtc', startUtc);
    if (endUtc) params.set('endUtc', endUtc);
    if (state.categoryId) params.set('categoryId', state.categoryId);
    if (state.search.trim()) params.set('search', state.search.trim());
    params.set('page', String(state.page));
    params.set('pageSize', String(state.pageSize));
    return params.toString();
  }

  async function fetchEntries() {
    try {
      const result = await callApi(`/api/time-entries?${buildListQuery()}`);
      const totalPages = Math.max(1, Math.ceil(result.totalCount / state.pageSize));
      if (state.page > totalPages) {
        state.page = totalPages;
        await fetchEntries();
        return;
      }
      state.items = result.items;
      state.totalCount = result.totalCount;
      renderGroups();
      renderPagination();
    } catch (error) {
      $('#history-count').textContent = error.message;
    }
  }

  function formatGroupHeading(dateKey) {
    const [year, month, day] = dateKey.split('-').map(Number);
    const dateUtc = Date.UTC(year, month - 1, day);
    const todayKey = zonedDateKey(new Date().toISOString(), state.timeZoneId);
    const [todayYear, todayMonth, todayDay] = todayKey.split('-').map(Number);
    const todayUtc = Date.UTC(todayYear, todayMonth - 1, todayDay);
    const days = Math.round((todayUtc - dateUtc) / 86400000);
    const label = days === 0 ? '今天' : days === 1 ? '昨天' : `${month} 月 ${day} 日`;
    const weekday = ['日', '一', '二', '三', '四', '五', '六'][new Date(dateUtc).getUTCDay()];
    return `${label} · 週${weekday}`;
  }

  function renderEntryRow(entry) {
    const color = entry.categoryColor ?? '#a6adb7';
    const name = entry.name || '未命名活動';
    const tagsMarkup = entry.tags.map((tag) => `<span class="tag-chip">${tag}</span>`).join('');
    return `<li class="entry-row" data-id="${entry.id}">
      <div class="entry-row__main">
        <span class="entry-name"><i class="legend-dot" style="background:${color}"></i>${name}</span>
        <div class="entry-meta">${tagsMarkup}</div>
      </div>
      <span class="entry-time">${formatTimeOfDayInZone(entry.startTimeUtc, state.timeZoneId)} – ${formatTimeOfDayInZone(entry.endTimeUtc, state.timeZoneId)}</span>
      <span class="entry-duration">${formatDuration(entry.durationSeconds)}</span>
      <div class="entry-actions">
        <button class="icon-btn" type="button" data-edit="${entry.id}" aria-label="編輯「${name}」">✎</button>
        <button class="icon-btn icon-btn--danger" type="button" data-delete="${entry.id}" aria-label="刪除「${name}」">✕</button>
      </div>
    </li>`;
  }

  function renderGroups() {
    $('#history-count').textContent = `共 ${state.totalCount} 筆紀錄`;
    $('#empty-state').hidden = state.items.length > 0;

    const byDate = new Map();
    state.items.forEach((entry) => {
      const key = zonedDateKey(entry.startTimeUtc, state.timeZoneId);
      if (!byDate.has(key)) byDate.set(key, []);
      byDate.get(key).push(entry);
    });

    const dates = [...byDate.keys()].sort((a, b) => (a < b ? 1 : -1));
    $('#entry-groups').innerHTML = dates
      .map((dateKey) => {
        const entries = byDate.get(dateKey);
        const totalSeconds = entries.reduce((sum, entry) => sum + entry.durationSeconds, 0);
        return `<div class="date-group">
          <div class="date-group__heading"><strong>${formatGroupHeading(dateKey)}</strong><span>${formatDuration(totalSeconds)}</span></div>
          <ul class="date-group__list">${entries.map(renderEntryRow).join('')}</ul>
        </div>`;
      })
      .join('');
  }

  function renderPagination() {
    const totalPages = Math.max(1, Math.ceil(state.totalCount / state.pageSize));
    $('#pagination').hidden = totalPages <= 1;
    $('#pagination-label').textContent = `第 ${state.page} / ${totalPages} 頁`;
    $('#pagination-prev').disabled = state.page <= 1;
    $('#pagination-next').disabled = state.page >= totalPages;
  }

  document.querySelectorAll('[data-range]').forEach((button) =>
    button.addEventListener('click', () => {
      state.range = button.dataset.range;
      state.page = 1;
      document.querySelectorAll('[data-range]').forEach((item) => {
        const selected = item === button;
        item.classList.toggle('range-button--active', selected);
        item.setAttribute('aria-pressed', String(selected));
      });
      fetchEntries();
    }),
  );

  $('#filter-category').addEventListener('change', (event) => {
    state.categoryId = event.target.value;
    state.page = 1;
    fetchEntries();
  });

  let searchTimeout;
  $('#filter-search').addEventListener('input', (event) => {
    state.search = event.target.value;
    state.page = 1;
    window.clearTimeout(searchTimeout);
    searchTimeout = window.setTimeout(fetchEntries, 300);
  });

  $('#pagination-prev').addEventListener('click', () => {
    if (state.page > 1) {
      state.page -= 1;
      fetchEntries();
    }
  });
  $('#pagination-next').addEventListener('click', () => {
    const totalPages = Math.max(1, Math.ceil(state.totalCount / state.pageSize));
    if (state.page < totalPages) {
      state.page += 1;
      fetchEntries();
    }
  });

  $('#entry-groups').addEventListener('click', async (event) => {
    const editId = event.target.dataset.edit;
    if (editId) {
      openModal('edit', state.items.find((item) => String(item.id) === editId));
      return;
    }

    const deleteId = event.target.dataset.delete;
    if (!deleteId) return;
    const entry = state.items.find((item) => String(item.id) === deleteId);
    if (!entry || !window.confirm(`確定要刪除「${entry.name || '這筆紀錄'}」嗎？`)) return;

    try {
      await callApi(`/api/time-entries/${deleteId}`, { method: 'DELETE' });
      await fetchEntries();
    } catch (error) {
      window.alert(error.message);
    }
  });

  const modalEl = $('#entry-modal');
  const tagListEl = $('#entry-tags');
  const tagInputEl = $('#entry-tag-input');

  function renderModalTags() {
    tagListEl.innerHTML = modal.tags
      .map((tag) => `<span class="tag-chip">${tag}<button type="button" data-remove-tag="${tag}" aria-label="移除標籤 ${tag}">×</button></span>`)
      .join('');
  }

  function openModal(mode, entry) {
    modal.editingId = mode === 'edit' ? entry.id : null;
    $('#entry-modal-title').textContent = mode === 'edit' ? '編輯紀錄' : '新增紀錄';
    $('#entry-modal-error').textContent = '';
    $('#entry-name').value = entry?.name ?? '';
    $('#entry-category').value = entry?.categoryId ?? '';
    modal.tags = entry ? [...entry.tags] : [];
    renderModalTags();

    const anchor = entry ? entry.startTimeUtc : new Date().toISOString();
    $('#entry-start').value = isoToDateTimeLocalValue(entry ? entry.startTimeUtc : anchor, state.timeZoneId);
    $('#entry-end').value = isoToDateTimeLocalValue(entry ? entry.endTimeUtc : anchor, state.timeZoneId);

    modalEl.showModal();
  }

  $('#add-entry').addEventListener('click', () => openModal('create'));
  $('#entry-modal-cancel').addEventListener('click', () => modalEl.close());

  $('#entry-tag-add').addEventListener('click', () => {
    const tag = tagInputEl.value.trim();
    if (tag && !modal.tags.includes(tag)) modal.tags.push(tag);
    tagInputEl.value = '';
    renderModalTags();
  });
  tagInputEl.addEventListener('keydown', (event) => {
    if (event.key === 'Enter') {
      event.preventDefault();
      $('#entry-tag-add').click();
    }
  });
  tagListEl.addEventListener('click', (event) => {
    const tag = event.target.dataset.removeTag;
    if (tag) {
      modal.tags = modal.tags.filter((item) => item !== tag);
      renderModalTags();
    }
  });

  $('#entry-form').addEventListener('submit', async (event) => {
    event.preventDefault();
    const start = $('#entry-start').value;
    const end = $('#entry-end').value;
    if (!start || !end) return;

    const categoryValue = $('#entry-category').value;
    const body = {
      name: $('#entry-name').value.trim() || null,
      startTimeUtc: dateTimeLocalValueToUtcIso(start, state.timeZoneId),
      endTimeUtc: dateTimeLocalValueToUtcIso(end, state.timeZoneId),
      categoryId: categoryValue ? Number(categoryValue) : null,
      tags: modal.tags,
    };

    try {
      if (modal.editingId) {
        await callApi(`/api/time-entries/${modal.editingId}`, { method: 'PATCH', body });
      } else {
        await callApi('/api/time-entries', { method: 'POST', body });
      }
      modalEl.close();
      state.page = 1;
      await fetchEntries();
    } catch (error) {
      $('#entry-modal-error').textContent = error.message;
    }
  });

  document.querySelectorAll('[data-history-view]').forEach((button) =>
    button.addEventListener('click', () => {
      const view = button.dataset.historyView;
      document.querySelectorAll('[data-history-view]').forEach((item) => {
        const selected = item === button;
        item.classList.toggle('range-button--active', selected);
        item.setAttribute('aria-pressed', String(selected));
      });
      $('#list-view').hidden = view !== 'list';
      $('#calendar-section').hidden = view !== 'calendar';
    }),
  );

  async function init() {
    try {
      const settings = await callApi('/api/settings/timezone');
      state.timeZoneId = settings.timeZoneId;
    } catch {
      // Keep the browser-timezone fallback set on `state` above.
    }
    await fetchEntries();
  }

  init();
}
