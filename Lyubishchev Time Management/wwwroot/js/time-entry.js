import { formatDuration } from './dashboard-state.mjs';

const page = document.querySelector('#history-page');

if (page) {
  const $ = (selector) => document.querySelector(selector);

  const state = { range: 'today', categoryId: '', search: '', page: 1, pageSize: 50, items: [], totalCount: 0 };
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

  function startOfLocalDay(date) {
    return new Date(date.getFullYear(), date.getMonth(), date.getDate());
  }

  function startOfLocalWeek(date) {
    const start = startOfLocalDay(date);
    start.setDate(start.getDate() - start.getDay());
    return start;
  }

  function getUtcRangeForPreset(preset) {
    const now = new Date();
    if (preset === 'today') {
      const start = startOfLocalDay(now);
      const end = new Date(start);
      end.setDate(end.getDate() + 1);
      return { startUtc: start.toISOString(), endUtc: end.toISOString() };
    }
    if (preset === 'week') {
      const start = startOfLocalWeek(now);
      const end = new Date(start);
      end.setDate(end.getDate() + 7);
      return { startUtc: start.toISOString(), endUtc: end.toISOString() };
    }
    if (preset === 'month') {
      const start = new Date(now.getFullYear(), now.getMonth(), 1);
      const end = new Date(now.getFullYear(), now.getMonth() + 1, 1);
      return { startUtc: start.toISOString(), endUtc: end.toISOString() };
    }
    return { startUtc: null, endUtc: null };
  }

  function buildListQuery() {
    const { startUtc, endUtc } = getUtcRangeForPreset(state.range);
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

  function localDateKey(isoString) {
    const date = new Date(isoString);
    return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
  }

  function formatGroupHeading(dateKey) {
    const [year, month, day] = dateKey.split('-').map(Number);
    const date = new Date(year, month - 1, day);
    const days = Math.round((startOfLocalDay(new Date()) - date) / 86400000);
    const label = days === 0 ? '今天' : days === 1 ? '昨天' : `${month} 月 ${day} 日`;
    const weekday = ['日', '一', '二', '三', '四', '五', '六'][date.getDay()];
    return `${label} · 週${weekday}`;
  }

  function formatTimeOfDay(isoString) {
    const date = new Date(isoString);
    return `${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`;
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
      <span class="entry-time">${formatTimeOfDay(entry.startTimeUtc)} – ${formatTimeOfDay(entry.endTimeUtc)}</span>
      <span class="entry-duration">${formatDuration(entry.durationSeconds / 60)}</span>
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
      const key = localDateKey(entry.startTimeUtc);
      if (!byDate.has(key)) byDate.set(key, []);
      byDate.get(key).push(entry);
    });

    const dates = [...byDate.keys()].sort((a, b) => (a < b ? 1 : -1));
    $('#entry-groups').innerHTML = dates
      .map((dateKey) => {
        const entries = byDate.get(dateKey);
        const totalMinutes = entries.reduce((sum, entry) => sum + entry.durationSeconds / 60, 0);
        return `<div class="date-group">
          <div class="date-group__heading"><strong>${formatGroupHeading(dateKey)}</strong><span>${formatDuration(totalMinutes)}</span></div>
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

  function toDateTimeLocalValue(isoString) {
    const date = new Date(isoString);
    const pad = (n) => String(n).padStart(2, '0');
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
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
    $('#entry-start').value = toDateTimeLocalValue(entry ? entry.startTimeUtc : anchor);
    $('#entry-end').value = toDateTimeLocalValue(entry ? entry.endTimeUtc : anchor);

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
      startTimeUtc: new Date(start).toISOString(),
      endTimeUtc: new Date(end).toISOString(),
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

  fetchEntries();
}
