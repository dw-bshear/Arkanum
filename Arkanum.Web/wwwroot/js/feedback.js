(() => {
    'use strict';
    const dialog = document.getElementById('feedback-dialog');
    const form = document.getElementById('feedback-form');
    const opener = document.querySelector('[data-feedback-open]');
    if (!dialog || !form || !opener) return;
    const text = document.getElementById('feedback-text');
    const status = document.getElementById('feedback-status');
    opener.addEventListener('click', () => dialog.showModal());
    dialog.querySelectorAll('[data-feedback-close]').forEach(button => button.addEventListener('click', () => dialog.close()));
    dialog.addEventListener('close', () => opener.focus());
    text.addEventListener('input', () => { document.getElementById('feedback-count').textContent = `${text.value.length} / 1000`; });
    const encode = file => new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => resolve({ name: file.name, data: String(reader.result).split(',')[1] });
        reader.onerror = () => reject(new Error('read'));
        reader.readAsDataURL(file);
    });
    form.addEventListener('submit', async event => {
        event.preventDefault();
        const files = Array.from(document.getElementById('feedback-screenshots').files);
        const answers = Array.from(form.querySelectorAll('[data-feedback-answer]'), answer => answer.value);
        if (!text.value.trim() || text.value.length > 1000 || answers.some(answer => answer.length > 500)) {
            status.textContent = 'Enter feedback of 1 to 1000 characters. Answers must be at most 500 characters.'; return;
        }
        if (files.length > 2 || files.some(file => file.size > 2097152 || file.size === 0 || !/\.(png|jpe?g)$/i.test(file.name)) || files.reduce((sum,file) => sum + file.size, 0) > 4194304) {
            status.textContent = 'Attach at most two PNG or JPEG screenshots, up to 2 MiB each (4 MiB total).'; return;
        }
        const button = form.querySelector('[type="submit"]');
        button.disabled = true;
        status.textContent = 'Sending feedback…';
        try {
            const response = await fetch(form.action, {
                method: 'POST', credentials: 'same-origin', redirect: 'error',
                headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': form.querySelector('[name="__RequestVerificationToken"]').value },
                body: JSON.stringify({ text: text.value, answers, screenshots: await Promise.all(files.map(encode)) })
            });
            const result = await response.json();
            status.textContent = result.message || 'Feedback could not be delivered. Please try again later.';
            if (response.ok && result.success) { form.reset(); document.getElementById('feedback-count').textContent = '0 / 1000'; }
        } catch { status.textContent = 'Feedback could not be delivered. Please try again later.'; }
        finally { button.disabled = false; }
    });
})();
