const page = document.querySelector('#tag-page');

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

  function renderRow(tag) {
    const li = document.createElement('li');
    li.className = 'resource-row';

    const name = document.createElement('span');
    name.className = 'resource-name';
    name.textContent = tag.name;

    const actions = document.createElement('div');
    actions.className = 'resource-actions';

    const editButton = document.createElement('button');
    editButton.type = 'button';
    editButton.className = 'icon-btn';
    editButton.setAttribute('aria-label', `編輯「${tag.name}」`);
    editButton.textContent = '✎';
    editButton.addEventListener('click', () => openModal('edit', tag));

    const deleteButton = document.createElement('button');
    deleteButton.type = 'button';
    deleteButton.className = 'icon-btn icon-btn--danger';
    deleteButton.setAttribute('aria-label', `刪除「${tag.name}」`);
    deleteButton.textContent = '✕';
    deleteButton.addEventListener('click', () => deleteTag(tag));

    actions.append(editButton, deleteButton);
    li.append(name, actions);
    return li;
  }

  function render() {
    const list = $('#resource-list');
    list.innerHTML = '';
    state.items.forEach((tag) => list.appendChild(renderRow(tag)));
    $('#resource-count').textContent = `共 ${state.items.length} 個標籤`;
  }

  async function refresh() {
    try {
      state.items = await callApi('/api/tags');
      $('#resource-error').textContent = '';
      render();
    } catch (error) {
      $('#resource-error').textContent = error.message;
    }
  }

  async function deleteTag(tag) {
    if (!window.confirm(`刪除標籤後，既有紀錄將移除這個標籤。確定刪除「${tag.name}」嗎？`)) return;

    try {
      await callApi(`/api/tags/${tag.id}`, { method: 'DELETE' });
      await refresh();
    } catch (error) {
      window.alert(error.message);
    }
  }

  const modalEl = $('#resource-modal');

  function openModal(mode, tag) {
    state.editingId = mode === 'edit' ? tag.id : null;
    $('#resource-modal-title').textContent = mode === 'edit' ? '編輯標籤' : '新增標籤';
    $('#resource-form-error').textContent = '';
    $('#resource-name').value = tag?.name ?? '';
    modalEl.showModal();
  }

  $('#add-resource').addEventListener('click', () => openModal('create'));
  $('#resource-cancel').addEventListener('click', () => modalEl.close());

  $('#resource-form').addEventListener('submit', async (event) => {
    event.preventDefault();
    const body = { name: $('#resource-name').value.trim() };

    try {
      if (state.editingId) {
        await callApi(`/api/tags/${state.editingId}`, { method: 'PATCH', body });
      } else {
        await callApi('/api/tags', { method: 'POST', body });
      }
      modalEl.close();
      await refresh();
    } catch (error) {
      $('#resource-form-error').textContent = error.message;
    }
  });

  refresh();
}
