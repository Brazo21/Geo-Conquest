// Settings page: binds every control to geoSettings, saves instantly, and offers test buttons.
(function () {
    'use strict';

    const S = window.geoSettings;
    if (!S) return;
    const $ = sel => document.querySelector(sel);

    function paint() {
        const s = S.get();
        document.querySelectorAll('[data-setting]').forEach(el => {
            const key = el.dataset.setting, v = s[key];
            if (el.type === 'checkbox') el.checked = !!v;
            else if (el.type === 'radio') el.checked = el.value === String(v);
            else el.value = String(v);
            const out = document.querySelector(`[data-out="${key}"]`);
            if (out) out.textContent = el.type === 'range' ? `${v}%` : '';
        });
        document.querySelectorAll('[data-theme-choice]').forEach(r => { r.checked = r.value === S.theme(); });
        document.querySelectorAll('[data-speed-choice]').forEach(r => { r.checked = Number(r.value) === S.speed(); });

        // Sub-controls are disabled when their master switch is off.
        document.querySelectorAll('[data-needs]').forEach(el => {
            const off = el.dataset.needs.split(' ').some(k => !s[k]);
            el.disabled = off;
            el.closest('.cr-set-row')?.classList.toggle('off', off);
        });
    }

    document.addEventListener('input', ev => {
        const el = ev.target;
        if (!el.dataset.setting) return;
        const key = el.dataset.setting;
        const value = el.type === 'checkbox' ? el.checked : el.type === 'range' ? Number(el.value) : el.value;
        S.set({ [key]: value });
        paint();
        flashSaved();
    });

    document.addEventListener('change', ev => {
        const el = ev.target;
        if (el.matches('[data-theme-choice]')) { S.setTheme(el.value); flashSaved(); }
        if (el.matches('[data-speed-choice]')) { S.setSpeed(Number(el.value)); flashSaved(); }
    });

    let savedTimer;
    function flashSaved() {
        const tag = $('#saved-tag');
        if (!tag) return;
        tag.classList.add('show');
        clearTimeout(savedTimer);
        savedTimer = setTimeout(() => tag.classList.remove('show'), 1400);
    }

    // Test buttons
    function rollPreview() {
        const box = $('#dice-preview');
        if (!box) return;
        const dice = [...box.querySelectorAll('.cr-die')];
        dice.forEach(d => { d.dataset.value = String(1 + Math.floor(Math.random() * 6)); d.classList.add('roll'); d.classList.remove('won'); });
        // Pairs: die 1 vs die 3, die 2 vs die 4 (attacker left, defender right). Higher die wins, ties nobody.
        [[0, 2], [1, 3]].forEach(([a, d]) => {
            const av = Number(dice[a].dataset.value), dv = Number(dice[d].dataset.value);
            if (av > dv) dice[a].classList.add('won'); else if (dv > av) dice[d].classList.add('won');
        });
        window.geoDice?.upgrade(box);
    }

    const TESTS = {
        click: () => window.geoAudio?.play('click'),
        dice: rollPreview,
        conquer: () => { window.geoAudio?.play('conquer'); window.geoAudio?.vibrate('conquer'); window.geoGeneral?.say('cheer', 'Brazil is ours!'); },
        victory: () => { window.geoAudio?.play('victory'); window.geoAudio?.vibrate('victory'); window.geoGeneral?.say('victory', 'The world is ours!'); },
        defeat: () => { window.geoAudio?.play('defeat'); window.geoAudio?.vibrate('defeat'); window.geoGeneral?.say('defeat', 'It is over, Commander…'); },
        vibrate: () => {
            const ok = window.geoAudio?.vibrate('victory');
            const note = $('#vibe-status');
            if (note && !ok) note.textContent = S.get().vibration ? 'This device or browser did not vibrate (iPhones and most laptops cannot).' : 'Vibration is switched off.';
        },
        salute: () => window.geoGeneral?.say('salute', 'Your orders, Commander?'),
        order: () => { window.geoAudio?.play('attack'); window.geoGeneral?.say('order', 'Advance on India!'); },
        cheer: () => window.geoGeneral?.say('cheer', 'Good roll!'),
        sad: () => { window.geoAudio?.play('attackFailed'); window.geoGeneral?.say('sad', 'Fall back and regroup.'); },
        reset: () => {
            S.reset();
            S.setTheme('dark');
            paint();
            flashSaved();
        }
    };

    document.addEventListener('click', ev => {
        const btn = ev.target.closest('[data-test]');
        if (btn && TESTS[btn.dataset.test]) TESTS[btn.dataset.test]();
    });

    const note = $('#vibe-status');
    if (note) note.textContent = window.geoAudio?.canVibrate() ? 'Supported on this device.' : 'Not supported on this device (iPhones and most laptops cannot vibrate). Phones running Android can.';

    window.addEventListener('geo:settings', paint);
    paint();
})();
