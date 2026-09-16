const timerCard = document.querySelector('#dashboard-page .timer-card');

if (timerCard) {
  const $ = (selector) => timerCard.querySelector(selector);
  const nameInput = $('#timer-name');
  const tagList = $('#timer-tags');
  const tagInput = $('#tag-input');
  const addTagButton = $('#add-tag');
  const display = $('#timer-display');
  const status = $('#timer-status');
  const toggleButton = $('#timer-toggle');

  const state = { isRunning: false, startedAtUtc: null, tags: [] };

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

  function formatClock(totalSeconds) {
    return new Date(Math.max(0, totalSeconds) * 1000).toISOString().slice(11, 19);
  }

  function tick() {
    if (!state.isRunning || !state.startedAtUtc) return;
    const elapsedSeconds = Math.floor((Date.now() - state.startedAtUtc.getTime()) / 1000);
    display.textContent = formatClock(elapsedSeconds);
  }

  function renderIdle() {
    state.isRunning = false;
    state.startedAtUtc = null;
    display.textContent = '00:00:00';
    status.textContent = '尚未開始計時';
    toggleButton.textContent = '開始計時';
    toggleButton.classList.remove('is-running');
    toggleButton.setAttribute('aria-pressed', 'false');
  }

  function renderRunning() {
    status.textContent = '正在記錄，專注進行中';
    toggleButton.textContent = '停止計時';
    toggleButton.classList.add('is-running');
    toggleButton.setAttribute('aria-pressed', 'true');
    tick();
  }

  function renderTags() {
    tagList.innerHTML = state.tags
      .map((tag) => `<span class="tag-chip">${tag}<button type="button" data-remove-tag="${tag}" aria-label="移除標籤 ${tag}">×</button></span>`)
      .join('');
  }

  async function loadStatus() {
    try {
      const timer = await callApi('/api/timer');
      if (timer.isRunning) {
        state.isRunning = true;
        state.startedAtUtc = new Date(timer.startedAtUtc);
        renderRunning();
      } else {
        renderIdle();
      }
    } catch {
      renderIdle();
    }
  }

  async function startTimer() {
    toggleButton.disabled = true;
    try {
      const timer = await callApi('/api/timer/start', { method: 'POST' });
      state.isRunning = true;
      state.startedAtUtc = new Date(timer.startedAtUtc);
      renderRunning();
    } catch (error) {
      status.textContent = error.message;
    } finally {
      toggleButton.disabled = false;
    }
  }

  async function stopTimer() {
    toggleButton.disabled = true;
    try {
      await callApi('/api/timer/stop', { method: 'POST', body: { name: nameInput.value.trim() } });
      nameInput.value = '';
      renderIdle();
    } catch (error) {
      status.textContent = error.message;
    } finally {
      toggleButton.disabled = false;
    }
  }

  toggleButton.addEventListener('click', () => {
    if (state.isRunning) stopTimer();
    else startTimer();
  });

  addTagButton.addEventListener('click', () => {
    const tag = tagInput.value.trim();
    if (tag && !state.tags.includes(tag)) state.tags.push(tag);
    tagInput.value = '';
    renderTags();
  });
  tagInput.addEventListener('keydown', (event) => {
    if (event.key === 'Enter') {
      event.preventDefault();
      addTagButton.click();
    }
  });
  tagList.addEventListener('click', (event) => {
    const tag = event.target.dataset.removeTag;
    if (tag) {
      state.tags = state.tags.filter((item) => item !== tag);
      renderTags();
    }
  });

  window.setInterval(tick, 1000);
  renderTags();
  loadStatus();
}
