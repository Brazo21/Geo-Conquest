// Site-wide behaviour. Kept out of inline attributes so the Content-Security-Policy can block injected scripts.
(function () {
    'use strict';

    // ── Light / dark mode ────────────────────────────────────────────────
    const THEME_KEY = 'geoconquest.theme';

    function applyTheme(theme) {
        document.documentElement.setAttribute('data-theme', theme);
        document.querySelectorAll('[data-theme-toggle]').forEach(btn => {
            btn.textContent = theme === 'light' ? '☾ Dark' : '☀ Light';
            btn.setAttribute('aria-label', theme === 'light' ? 'Switch to dark mode' : 'Switch to light mode');
        });
    }

    document.addEventListener('click', ev => {
        const t = ev.target.closest('[data-theme-toggle]');
        if (t) {
            const next = document.documentElement.getAttribute('data-theme') === 'light' ? 'dark' : 'light';
            try { localStorage.setItem(THEME_KEY, next); } catch { /* storage unavailable */ }
            applyTheme(next);
            window.dispatchEvent(new CustomEvent('geo:theme', { detail: next }));
            return;
        }

        // Dismiss alert banners.
        const dismiss = ev.target.closest('[data-dismiss-alert]');
        if (dismiss) { dismiss.parentElement.remove(); return; }

        // "Copy share link" buttons.
        const copy = ev.target.closest('[data-copy]');
        if (copy) {
            const text = copy.dataset.copy;
            const done = ok => {
                const label = copy.textContent;
                copy.textContent = ok ? '✔ Link copied' : 'Copy failed · ' + text;
                setTimeout(() => { copy.textContent = label; }, 1800);
            };
            if (navigator.clipboard?.writeText) navigator.clipboard.writeText(text).then(() => done(true), () => done(false));
            else done(false);
            return;
        }

        // "Go back" buttons on error pages.
        if (ev.target.closest('[data-go-back]')) { history.length > 1 ? history.back() : location.assign('/'); }
    });

    // Click-to-select read-only fields (share links).
    document.addEventListener('focusin', ev => {
        if (ev.target.matches?.('[data-select-on-click]')) ev.target.select();
    });

    // Form checks: once a field has shown an error, re-check it on every change (typing, pasting, autofill),
    // so a corrected field never keeps blocking the form. Also re-checks a linked field such as "confirm password".
    document.addEventListener('input', ev => {
        const form = ev.target.form;
        if (!form || !window.jQuery || !jQuery.fn.valid) return;
        form.querySelectorAll('.input-validation-error').forEach(el => jQuery(el).valid());
    });

    // Dropdowns that submit their form when changed.
    document.addEventListener('change', ev => {
        if (ev.target.matches?.('[data-autosubmit]')) ev.target.form?.submit();
    });

    // The settings page changes the theme through this event.
    window.addEventListener('geo:set-theme', ev => {
        applyTheme(ev.detail);
        window.dispatchEvent(new CustomEvent('geo:theme', { detail: ev.detail }));
    });

    // Remember which Intel tab was open (per device, cosmetic only).
    document.addEventListener('shown.bs.tab', ev => {
        try { localStorage.setItem('geoconquest.intelTab', ev.target.id); } catch { }
    });
    try {
        const tab = document.getElementById(localStorage.getItem('geoconquest.intelTab') || '');
        if (tab && window.bootstrap) window.bootstrap.Tab.getOrCreateInstance(tab).show();
    } catch { }

    // ── Show / hide password ─────────────────────────────────────────────
    // Every password box gets an eye button. The field stays type="password" until the player asks,
    // and it is hidden again before the form is sent so the browser never saves it as plain text.
    const EYE = '<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M1.5 12S5.5 4.5 12 4.5 22.5 12 22.5 12 18.5 19.5 12 19.5 1.5 12 1.5 12Z"/><circle cx="12" cy="12" r="3.2"/><path class="slash" d="M3 3l18 18"/></svg>';

    document.querySelectorAll('input[type="password"]').forEach(input => {
        if (input.closest('.cr-pw')) return;
        const wrap = document.createElement('div');
        wrap.className = 'cr-pw';
        input.parentNode.insertBefore(wrap, input);
        wrap.appendChild(input);

        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'cr-pw-eye';
        btn.innerHTML = EYE;
        btn.setAttribute('aria-label', 'Show password');
        btn.setAttribute('aria-pressed', 'false');
        if (input.id) btn.setAttribute('aria-controls', input.id);
        wrap.appendChild(btn);

        btn.addEventListener('click', () => {
            const show = input.type === 'password';
            input.type = show ? 'text' : 'password';
            btn.classList.toggle('on', show);
            btn.setAttribute('aria-pressed', String(show));
            btn.setAttribute('aria-label', show ? 'Hide password' : 'Show password');
            input.focus({ preventScroll: true });
        });

        input.form?.addEventListener('submit', () => {
            input.type = 'password';
            btn.classList.remove('on');
            btn.setAttribute('aria-pressed', 'false');
            btn.setAttribute('aria-label', 'Show password');
        });
    });

    // ── Installable app (PWA) ─────────────────────────────────────────────
    if ('serviceWorker' in navigator && (location.protocol === 'https:' || location.hostname === 'localhost')) {
        window.addEventListener('load', () => {
            navigator.serviceWorker?.register('/sw.js', { scope: '/' }).catch(err => console.warn('Service worker not registered', err));
        });
    }

    const standalone = window.matchMedia?.('(display-mode: standalone)').matches || window.navigator.standalone === true;
    const ua = navigator.userAgent;
    const isIos = /iPhone|iPad|iPod/.test(ua) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
    let installPrompt = null;

    function paintInstall() {
        const state = standalone ? 'installed' : installPrompt ? 'ready' : isIos ? 'ios' : 'manual';
        document.querySelectorAll('[data-install-state]').forEach(el => {
            el.hidden = !el.dataset.installState.split(' ').includes(state);
        });
        document.documentElement.classList.toggle('is-app', standalone);
    }

    // Chrome / Edge / Samsung Internet offer installation through this event.
    window.addEventListener('beforeinstallprompt', ev => {
        ev.preventDefault();
        installPrompt = ev;
        paintInstall();
    });

    window.addEventListener('appinstalled', () => {
        installPrompt = null;
        document.querySelectorAll('[data-install-state]').forEach(el => { el.hidden = el.dataset.installState !== 'done'; });
    });

    document.addEventListener('click', async ev => {
        const btn = ev.target.closest('[data-install]');
        if (!btn || !installPrompt) return;
        installPrompt.prompt();
        const choice = await installPrompt.userChoice.catch(() => null);
        installPrompt = null;
        if (choice?.outcome !== 'accepted') paintInstall();
    });

    paintInstall();

    applyTheme(document.documentElement.getAttribute('data-theme') || 'dark');
})();
