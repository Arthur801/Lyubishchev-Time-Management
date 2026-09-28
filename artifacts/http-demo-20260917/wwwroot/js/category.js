const page = document.querySelector('#category-page');

if (page) {
  const $ = (selector) => document.querySelector(selector);
  const state = { items: [], editingId: null };

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

  function renderRow(category) {
    const li = document.createElement('li');
    li.className = 'resource-row';

    const swatch = document.createElement('span');
    swatch.className = 'resource-swatch';
    swatch.style.background = category.color;

    const name = document.createElement('span');
    name.className = 'resource-name';
    name.textContent = category.name;

    const actions = document.createElement('div');
    actions.className = 'resource-actions';

    const editButton = document.createElement('button');
    editButton.type = 'button';
    editButton.className = 'icon-btn';
    editButton.setAttribute('aria-label', `編輯「${category.name}」`);
    editButton.textContent = '✎';
    editButton.addEventListener('click', () => openModal('edit', category));

    const deleteButton = document.createElement('button');
    deleteButton.type = 'button';
    deleteButton.className = 'icon-btn icon-btn--danger';
    deleteButton.setAttribute('aria-label', `刪除「${category.name}」`);
    deleteButton.textContent = '✕';
    deleteButton.addEventListener('click', () => deleteCategory(category));

    actions.append(editButton, deleteButton);
    li.append(swatch, name, actions);
    return li;
  }

  function render() {
    const list = $('#resource-list');
    list.innerHTML = '';
    state.items.forEach((category) => list.appendChild(renderRow(category)));
    $('#resource-count').textContent = `共 ${state.items.length} 個分類`;
  }

  async function refresh() {
    try {
      state.items = await callApi('/api/categories');
      $('#resource-error').textContent = '';
      render();
    } catch (error) {
      $('#resource-error').textContent = error.message;
    }
  }

  async function deleteCategory(category) {
    if (!window.confirm(`刪除分類後，既有紀錄會改為未分類。確定刪除「${category.name}」嗎？`)) return;

    try {
      await callApi(`/api/categories/${category.id}`, { method: 'DELETE' });
      await refresh();
    } catch (error) {
      window.alert(error.message);
    }
  }

  const modalEl = $('#resource-modal');

  function openModal(mode, category) {
    state.editingId = mode === 'edit' ? category.id : null;
    $('#resource-modal-title').textContent = mode === 'edit' ? '編輯分類' : '新增分類';
    $('#resource-form-error').textContent = '';
    $('#resource-name').value = category?.name ?? '';
    $('#resource-color').value = category?.color ?? '#e5533d';
    modalEl.showModal();
  }

  $('#add-resource').addEventListener('click', () => openModal('create'));
  $('#resource-cancel').addEventListener('click', () => modalEl.close());

  $('#resource-form').addEventListener('submit', async (event) => {
    event.preventDefault();
    const body = {
      name: $('#resource-name').value.trim(),
      color: $('#resource-color').value,
    };

    try {
      if (state.editingId) {
        await callApi(`/api/categories/${state.editingId}`, { method: 'PATCH', body });
      } else {
        await callApi('/api/categories', { method: 'POST', body });
      }
      modalEl.close();
      await refresh();
    } catch (error) {
      $('#resource-form-error').textContent = error.message;
    }
  });

  refresh();
}
