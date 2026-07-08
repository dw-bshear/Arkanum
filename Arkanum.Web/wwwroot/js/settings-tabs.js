(() => {
  const buttons = Array.from(document.querySelectorAll('[data-settings-tab]'));
  if (!buttons.length) return;
  const panels = Array.from(document.querySelectorAll('.settings-tab-panel'));
  const scrollKey = 'arkanum.settings.scrollY';

  function activate(id, updateHash = true) {
    const targetId = panels.some(panel => panel.id === id) ? id : buttons[0].dataset.settingsTab;
    buttons.forEach(button => {
      const active = button.dataset.settingsTab === targetId;
      button.classList.toggle('active', active);
      button.setAttribute('aria-selected', active ? 'true' : 'false');
    });
    panels.forEach(panel => {
      const active = panel.id === targetId;
      panel.hidden = !active;
      panel.classList.toggle('is-active', active);
    });
    if (updateHash) history.replaceState(null, '', `#${targetId}`);
  }

  buttons.forEach(button => button.addEventListener('click', () => activate(button.dataset.settingsTab)));
  const initial = location.hash ? location.hash.slice(1) : buttons[0].dataset.settingsTab;
  activate(initial, false);

  document.querySelectorAll('form').forEach(form => {
    form.addEventListener('submit', () => {
      try { sessionStorage.setItem(scrollKey, String(window.scrollY)); } catch { /* ignore */ }
    });
  });

  window.addEventListener('load', () => {
    try {
      const saved = sessionStorage.getItem(scrollKey);
      if (saved !== null) {
        sessionStorage.removeItem(scrollKey);
        window.scrollTo({ top: Number(saved) || 0, behavior: 'instant' });
      }
    } catch { /* ignore */ }
  });

  const userSearch = document.querySelector('#settings-user-search');
  const userRows = Array.from(document.querySelectorAll('[data-user-row]'));
  if (userSearch && userRows.length) {
    const filterUsers = () => {
      const terms = userSearch.value.toLowerCase().split(/\s+/).filter(Boolean);
      userRows.forEach(row => {
        const haystack = row.dataset.search || row.textContent.toLowerCase();
        const visible = terms.every(term => haystack.includes(term));
        row.hidden = !visible;
      });
    };
    userSearch.addEventListener('input', filterUsers);
    filterUsers();
  }
})();
