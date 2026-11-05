// Player preferences (sound, vibration, brightness, dice, general…).
// Kept on this device in localStorage: they describe the screen and speakers in front of the player,
// not the game, so they need no server round-trip and also work for spectators.
// Every value is validated on load, so a hand-edited or corrupted entry can never break the page.
(function () {
    'use strict';

    const KEY = 'geoconquest.settings';
    const SPEED_KEY = 'geoconquest.speed';
    const THEME_KEY = 'geoconquest.theme';

    const DEFAULTS = Object.freeze({
        sound: true,          // master switch
        master: 80,           // 0–100
        musicOn: true,
        music: 35,            // 0–100
        effects: 80,          // 0–100
        vibration: true,
        brightness: 100,      // 60–140 %
        scanlines: true,
        textSize: 'md',       // sm | md | lg | xl
        dice: 'full',         // full | quick | off
        general: true,
        generalVoice: true,
        generalSkin: 'medium',   // light | medium | dark | deep
        generalHair: 'moustache' // moustache | beard | clean
    });

    const CHOICES = {
        textSize: ['sm', 'md', 'lg', 'xl'],
        dice: ['full', 'quick', 'off'],
        generalSkin: ['light', 'medium', 'dark', 'deep'],
        generalHair: ['moustache', 'beard', 'clean']
    };
    const RANGES = { master: [0, 100], music: [0, 100], effects: [0, 100], brightness: [60, 140] };

    function sanitize(raw) {
        const out = { ...DEFAULTS };
        if (!raw || typeof raw !== 'object') return out;
        for (const key of Object.keys(DEFAULTS)) {
            const v = raw[key];
            if (v === undefined) continue;
            if (typeof DEFAULTS[key] === 'boolean') out[key] = v === true;
            else if (RANGES[key]) {
                const n = Math.round(Number(v));
                if (Number.isFinite(n)) out[key] = Math.min(RANGES[key][1], Math.max(RANGES[key][0], n));
            } else if (CHOICES[key] && CHOICES[key].includes(v)) out[key] = v;
        }
        return out;
    }

    function load() {
        try { return sanitize(JSON.parse(localStorage.getItem(KEY) || '{}')); } catch { return { ...DEFAULTS }; }
    }

    let current = load();

    function apply(s) {
        const root = document.documentElement;
        if (s.brightness !== 100) {
            root.style.setProperty('--cr-brightness', String(s.brightness / 100));
            root.dataset.bright = '1';
        } else {
            root.style.removeProperty('--cr-brightness');
            delete root.dataset.bright;
        }
        root.dataset.scanlines = s.scanlines ? 'on' : 'off';
        root.dataset.text = s.textSize;
        root.dataset.dice = s.dice;
        root.dataset.showGeneral = s.general ? 'on' : 'off';
    }

    function save() {
        try { localStorage.setItem(KEY, JSON.stringify(current)); } catch { /* storage unavailable: keep in memory */ }
    }

    window.geoSettings = {
        DEFAULTS,
        CHOICES,
        RANGES,
        get: () => ({ ...current }),
        set(patch) {
            current = sanitize({ ...current, ...patch });
            save();
            apply(current);
            window.dispatchEvent(new CustomEvent('geo:settings', { detail: { ...current } }));
        },
        reset() {
            current = { ...DEFAULTS };
            save();
            try { localStorage.setItem(SPEED_KEY, '1'); } catch { }
            apply(current);
            window.dispatchEvent(new CustomEvent('geo:settings', { detail: { ...current } }));
        },
        speed() {
            try { const n = Number(localStorage.getItem(SPEED_KEY)); return [1, 2, 4].includes(n) ? n : 1; } catch { return 1; }
        },
        setSpeed(n) {
            if (![1, 2, 4].includes(n)) return;
            try { localStorage.setItem(SPEED_KEY, String(n)); } catch { }
        },
        theme() {
            return document.documentElement.getAttribute('data-theme') === 'light' ? 'light' : 'dark';
        },
        setTheme(t) {
            if (t !== 'light' && t !== 'dark') return;
            try { localStorage.setItem(THEME_KEY, t); } catch { }
            window.dispatchEvent(new CustomEvent('geo:set-theme', { detail: t }));
        }
    };

    apply(current);
})();
