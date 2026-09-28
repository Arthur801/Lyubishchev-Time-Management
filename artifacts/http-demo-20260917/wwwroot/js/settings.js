const page = document.querySelector('#settings-page');

if (page) {
  const $ = (selector) => document.querySelector(selector);

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

  function showBrowserTimezoneHint() {
    try {
      const detected = Intl.DateTimeFormat().resolvedOptions().timeZone;
      if (detected) {
        $('#browser-timezone-hint').textContent = `瀏覽器偵測到的時區為 ${detected}（僅供參考，不會自動套用）。`;
      }
    } catch {
      // Intl API unavailable in this browser; skip the hint silently.
    }
  }

  function populateOptions(options, currentId) {
    const select = $('#timezone-select');
    select.innerHTML = '';
    for (const option of options) {
      const el = document.createElement('option');
      el.value = option.id;
      el.textContent = option.displayName;
      select.appendChild(el);
    }
    select.value = currentId;
  }

  async function load() {
    try {
      const settings = await callApi('/api/settings/timezone');
      populateOptions(settings.options, settings.timeZoneId);
      $('#timezone-message').textContent = '';
    } catch (error) {
      $('#timezone-message').textContent = error.message;
    }
  }

  $('#timezone-form').addEventListener('submit', async (event) => {
    event.preventDefault();

    try {
      const settings = await callApi('/api/settings/timezone', {
        method: 'PATCH',
        body: { timeZoneId: $('#timezone-select').value },
      });
      populateOptions(settings.options, settings.timeZoneId);
      $('#timezone-message').textContent = '已儲存時區設定。';
    } catch (error) {
      $('#timezone-message').textContent = error.message;
    }
  });

  showBrowserTimezoneHint();
  load();
}
