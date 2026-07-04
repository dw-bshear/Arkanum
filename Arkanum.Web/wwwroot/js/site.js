(() => {
  const root = document.documentElement;
  const buttons = document.querySelectorAll('[data-theme-choice]');
  const toggle = document.querySelector('[data-theme-toggle]');

  function normalize(choice) {
    return choice === 'dark' ? 'dark' : 'light';
  }

  function apply(choice) {
    const selected = normalize(choice || localStorage.getItem('arkanum-theme') || 'light');
    root.dataset.theme = selected;
    root.dataset.activeTheme = selected;
    buttons.forEach(button => {
      const active = button.dataset.themeChoice === selected;
      button.classList.toggle('active', active);
      button.setAttribute('aria-pressed', active ? 'true' : 'false');
    });
    if (toggle) {
      toggle.setAttribute('aria-pressed', selected === 'dark' ? 'true' : 'false');
      toggle.setAttribute('title', selected === 'dark' ? 'Switch to light theme' : 'Switch to dark theme');
    }
  }

  function setTheme(choice) {
    const selected = normalize(choice);
    localStorage.setItem('arkanum-theme', selected);
    apply(selected);
  }

  buttons.forEach(button => button.addEventListener('click', () => setTheme(button.dataset.themeChoice)));
  toggle?.addEventListener('click', () => setTheme(root.dataset.activeTheme === 'dark' ? 'light' : 'dark'));
  apply();

  function generatePassword() {
    const upper = 'ABCDEFGHJKLMNPQRSTUVWXYZ';
    const lower = 'abcdefghijkmnopqrstuvwxyz';
    const numbers = '23456789';
    const symbols = '!@#$%^&*()-_=+[]{}:,.?';
    const all = upper + lower + numbers + symbols;
    const chars = [upper, lower, numbers, symbols].map(set => set[crypto.getRandomValues(new Uint32Array(1))[0] % set.length]);
    while (chars.length < 24) chars.push(all[crypto.getRandomValues(new Uint32Array(1))[0] % all.length]);
    for (let i = chars.length - 1; i > 0; i--) {
      const j = crypto.getRandomValues(new Uint32Array(1))[0] % (i + 1);
      [chars[i], chars[j]] = [chars[j], chars[i]];
    }
    return chars.join('');
  }

  document.querySelectorAll('[data-generate-password], [data-demo-password]').forEach(button => {
    button.addEventListener('click', () => {
      const password = generatePassword();
      const outputId = button.dataset.passwordOutput || 'generatedPassword';
      const targetId = button.dataset.passwordTarget;
      const output = document.getElementById(outputId);
      const target = targetId ? document.getElementById(targetId) : null;
      if (output) output.textContent = password;
      if (target) target.value = password;
    });
  });

  const copyButton = document.querySelector('[data-copy-revealed]');
  if (copyButton) {
    copyButton.addEventListener('click', async () => {
      const value = document.getElementById('revealedSecret')?.textContent || '';
      if (navigator.clipboard && value) await navigator.clipboard.writeText(value);
      copyButton.textContent = 'Copied locally';
    });
  }
})();
