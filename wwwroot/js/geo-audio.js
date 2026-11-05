// GeoConquest sound engine. Every sound is synthesised with the Web Audio API, so there are no
// audio files to download, nothing for the Content-Security-Policy to allow, and no copyright worries.
//   • music:   a quiet war-room loop (Am–F–C–G) with a radar blip
//   • effects: clicks, dice rattle, duel results, conquest, alarms, victory and defeat fanfares
//   • haptics: navigator.vibrate patterns for wins and losses (Android; iPhones ignore vibration)
// Browsers only allow sound after the player has touched the page, so the engine unlocks on the
// first click / tap / key press and shows a small hint if it is still waiting.
(function () {
    'use strict';

    const settings = () => (window.geoSettings ? window.geoSettings.get() : { sound: true, master: 80, musicOn: true, music: 35, effects: 80, vibration: true });

    let ctx = null, master, musicBus, musicFilter, fxBus, noiseBuf;
    let musicTimer = null, nextStepTime = 0, step = 0;

    const mtof = m => 440 * Math.pow(2, (m - 69) / 12);

    function ensure() {
        if (ctx) return ctx;
        const AC = window.AudioContext || window.webkitAudioContext;
        if (!AC) return null;
        try { ctx = new AC(); } catch { return null; }
        master = ctx.createGain();
        musicFilter = ctx.createBiquadFilter();
        musicFilter.type = 'lowpass';
        musicFilter.frequency.value = 2400;
        musicBus = ctx.createGain();
        fxBus = ctx.createGain();
        musicBus.connect(musicFilter);
        musicFilter.connect(master);
        fxBus.connect(master);
        master.connect(ctx.destination);

        noiseBuf = ctx.createBuffer(1, ctx.sampleRate, ctx.sampleRate);
        const data = noiseBuf.getChannelData(0);
        for (let i = 0; i < data.length; i++) data[i] = Math.random() * 2 - 1;

        levels(true);
        return ctx;
    }

    function levels(instant) {
        if (!ctx) return;
        const s = settings();
        const t = ctx.currentTime, k = instant ? 0.001 : 0.08;
        master.gain.setTargetAtTime(s.sound ? s.master / 100 : 0, t, k);
        musicBus.gain.setTargetAtTime(s.musicOn ? (s.music / 100) * 0.55 : 0, t, k);
        fxBus.gain.setTargetAtTime(s.effects / 100, t, k);
    }

    // ── Building blocks ────────────────────────────────────────────────────
    function tone({ freq, type = 'square', dur = 0.15, vol = 0.2, at = 0, when = null, slide = null, attack = 0.005, bus = null }) {
        if (!ensure()) return;
        const t = when ?? (ctx.currentTime + at);
        const o = ctx.createOscillator();
        const g = ctx.createGain();
        o.type = type;
        o.frequency.setValueAtTime(freq, t);
        if (slide) o.frequency.exponentialRampToValueAtTime(slide, t + dur);
        g.gain.setValueAtTime(0.0001, t);
        g.gain.exponentialRampToValueAtTime(Math.max(vol, 0.0002), t + attack);
        g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
        o.connect(g);
        g.connect(bus || fxBus);
        o.start(t);
        o.stop(t + dur + 0.05);
    }

    function noise({ dur = 0.05, vol = 0.2, at = 0, when = null, hp = 1200, bus = null }) {
        if (!ensure()) return;
        const t = when ?? (ctx.currentTime + at);
        const src = ctx.createBufferSource();
        src.buffer = noiseBuf;
        const f = ctx.createBiquadFilter();
        f.type = 'highpass';
        f.frequency.value = hp;
        const g = ctx.createGain();
        g.gain.setValueAtTime(vol, t);
        g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
        src.connect(f); f.connect(g); g.connect(bus || fxBus);
        src.start(t, Math.random() * 0.8, dur + 0.05);
    }

    function melody(notes, { type = 'square', vol = 0.16, gap = 0.12, dur = 0.14 } = {}) {
        notes.forEach((n, i) => {
            if (n == null) return;
            const [midi, len = 1] = Array.isArray(n) ? n : [n];
            tone({ freq: mtof(midi), type, vol, at: i * gap, dur: dur * len });
        });
    }

    // ── Sound effects ──────────────────────────────────────────────────────
    const SFX = {
        click: () => tone({ freq: 1100, type: 'square', dur: 0.035, vol: 0.05 }),
        toggle: () => { tone({ freq: 700, dur: 0.04, vol: 0.06 }); tone({ freq: 1050, dur: 0.04, vol: 0.06, at: 0.05 }); },
        diceRoll: ({ duration = 900 } = {}) => {
            const n = Math.max(3, Math.round(duration / 85));
            for (let i = 0; i < n; i++) {
                const at = (i / n) * (duration / 1000) * (0.6 + Math.random() * 0.25);
                noise({ at, dur: 0.025, vol: 0.18 * (1 - i / (n * 1.3)), hp: 2500 + Math.random() * 2500 });
            }
        },
        diceLand: () => { noise({ dur: 0.06, vol: 0.25, hp: 900 }); tone({ freq: 170, type: 'triangle', dur: 0.12, vol: 0.2, slide: 90 }); },
        duelWin: () => { tone({ freq: 660, dur: 0.09, vol: 0.12 }); tone({ freq: 990, dur: 0.16, vol: 0.12, at: 0.09 }); },
        duelLoss: () => tone({ freq: 320, type: 'sawtooth', dur: 0.3, vol: 0.12, slide: 140 }),
        duelTie: () => tone({ freq: 440, type: 'triangle', dur: 0.12, vol: 0.1 }),
        attack: () => { tone({ freq: 220, type: 'sawtooth', dur: 0.12, vol: 0.1 }); tone({ freq: 330, type: 'sawtooth', dur: 0.2, vol: 0.1, at: 0.1 }); },
        alarm: () => { for (let i = 0; i < 3; i++) { tone({ freq: 880, dur: 0.14, vol: 0.09, at: i * 0.3 }); tone({ freq: 660, dur: 0.14, vol: 0.09, at: i * 0.3 + 0.15 }); } },
        place: () => { tone({ freq: 520, type: 'triangle', dur: 0.06, vol: 0.1 }); tone({ freq: 780, type: 'triangle', dur: 0.08, vol: 0.1, at: 0.06 }); },
        npcMove: () => tone({ freq: 1320, type: 'sine', dur: 0.12, vol: 0.05 }),
        yourTurn: () => melody([72, 79], { type: 'triangle', vol: 0.14, gap: 0.14, dur: 0.3 }),
        retreat: () => melody([67, 64, 60], { type: 'triangle', vol: 0.1, gap: 0.1 }),
        conquer: () => melody([60, 64, 67, [72, 3]], { vol: 0.13, gap: 0.1 }),
        defended: () => melody([67, 72], { vol: 0.12, gap: 0.12, dur: 0.2 }),
        attackFailed: () => melody([64, 60, [55, 3]], { type: 'sawtooth', vol: 0.09, gap: 0.16 }),
        countryLost: () => { SFX.alarm(); tone({ freq: 140, type: 'sawtooth', dur: 0.9, vol: 0.12, at: 0.9, slide: 70 }); },
        eliminated: () => tone({ freq: 400, type: 'sawtooth', dur: 0.8, vol: 0.09, slide: 60 }),
        victory: () => {
            melody([67, 72, 76, [79, 2], null, 76, [79, 5]], { vol: 0.15, gap: 0.14, dur: 0.15 });
            [60, 64, 67, 72].forEach(m => tone({ freq: mtof(m), type: 'triangle', vol: 0.08, at: 1.0, dur: 1.6, attack: 0.05 }));
            for (let i = 0; i < 4; i++) noise({ at: 1.0 + i * 0.12, dur: 0.15, vol: 0.06, hp: 5000 });
        },
        defeat: () => {
            melody([[64, 2], [62, 2], [60, 2], [59, 5]], { type: 'sawtooth', vol: 0.09, gap: 0.42, dur: 0.22 });
            [45, 52, 57].forEach(m => tone({ freq: mtof(m), type: 'triangle', vol: 0.12, at: 1.7, dur: 2.2, attack: 0.1 }));
        },
        talk: () => tone({ freq: 220 + Math.random() * 160, type: 'square', dur: 0.035, vol: 0.035 })
    };

    // ── Haptics ────────────────────────────────────────────────────────────
    const VIBES = {
        tap: [15],
        turn: [40, 60, 40],
        conquer: [80, 50, 80],
        loss: [260],
        alarm: [120, 80, 120, 80, 120],
        victory: [100, 60, 100, 60, 400],
        defeat: [600, 200, 600]
    };

    function vibrate(name) {
        const s = settings();
        if (!s.vibration || !('vibrate' in navigator)) return false;
        try { return navigator.vibrate(VIBES[name] || VIBES.tap); } catch { return false; }
    }

    // ── Music: a lookahead scheduler keeps the beat steady even when the tab is busy ──
    const BPM = 76, STEP = 60 / BPM / 2;
    const CHORDS = [[45, 57, 60, 64], [41, 53, 57, 60], [48, 55, 60, 64], [43, 55, 59, 62]];
    const ARP = [1, 2, 3, 2, 1, 3, 2, 3];

    function scheduleStep(t) {
        const bar = Math.floor(step / 8) % 4, s = step % 8, ch = CHORDS[bar];
        if (s === 0) {
            tone({ freq: mtof(ch[0]), type: 'triangle', dur: STEP * 8, vol: 0.2, attack: 0.25, when: t, bus: musicBus });
            tone({ freq: mtof(ch[0] + 12), type: 'sine', dur: STEP * 8, vol: 0.05, attack: 0.4, when: t, bus: musicBus });
        }
        if (s === 0 || s === 4) tone({ freq: 110, type: 'sine', dur: 0.22, vol: 0.22, slide: 42, when: t, bus: musicBus });
        if (s % 2 === 1) noise({ dur: 0.03, vol: 0.02, hp: 7000, when: t, bus: musicBus });
        tone({ freq: mtof(ch[ARP[s]] + 12), type: 'triangle', dur: STEP * 1.5, vol: 0.04, attack: 0.01, when: t, bus: musicBus });
        if (bar === 3 && s === 6) tone({ freq: 1480, type: 'sine', dur: 0.35, vol: 0.025, when: t, bus: musicBus });
        step++;
    }

    function startMusic() {
        if (musicTimer || !ensure()) return;
        const s = settings();
        if (!s.sound || !s.musicOn || s.music === 0) return;
        nextStepTime = ctx.currentTime + 0.1;
        musicTimer = setInterval(() => {
            if (!ctx || ctx.state !== 'running') return;
            while (nextStepTime < ctx.currentTime + 0.3) { scheduleStep(nextStepTime); nextStepTime += STEP; }
        }, 60);
    }

    function stopMusic() {
        clearInterval(musicTimer);
        musicTimer = null;
    }

    // ── Unlocking (browser autoplay rules) ─────────────────────────────────
    let hint = null;
    function showHint(show) {
        if (show && !hint && settings().sound) {
            hint = document.createElement('div');
            hint.className = 'cr-sound-hint';
            hint.setAttribute('role', 'status');
            hint.textContent = '🔈 Tap anywhere to turn on sound';
            document.body.appendChild(hint);
        } else if (!show && hint) {
            hint.remove();
            hint = null;
        }
    }

    function unlock() {
        if (!ensure()) return;
        const go = () => { showHint(false); refresh(); };
        if (ctx.state === 'suspended') ctx.resume().then(go).catch(() => { }); else go();
    }

    function refresh() {
        levels(false);
        const s = settings();
        if (s.sound && s.musicOn && s.music > 0) startMusic(); else stopMusic();
        paintToggle();
    }

    function play(name, opts) {
        const s = settings();
        if (!s.sound || !SFX[name]) return;
        if (!ensure()) return;
        if (ctx.state === 'suspended') { showHint(true); return; }
        try { SFX[name](opts); } catch (err) { console.warn('sound failed', err); }
    }

    // Mute button in the top bar.
    function paintToggle() {
        const on = settings().sound;
        document.querySelectorAll('[data-sound-toggle]').forEach(b => {
            b.textContent = on ? '🔊 Sound' : '🔇 Muted';
            b.setAttribute('aria-pressed', String(on));
            b.setAttribute('aria-label', on ? 'Mute all sound' : 'Turn sound on');
        });
    }

    ['pointerdown', 'keydown', 'touchstart'].forEach(type =>
        document.addEventListener(type, unlock, { passive: true, capture: true }));

    document.addEventListener('click', ev => {
        if (ev.target.closest('[data-sound-toggle]')) {
            window.geoSettings?.set({ sound: !settings().sound });
            play('toggle');
            return;
        }
        if (ev.target.closest('.cr-btn, .cr-nav a, .cr-nav button, .cr-pick label')) play('click');
    });

    document.addEventListener('visibilitychange', () => {
        if (!ctx) return;
        if (document.hidden) ctx.suspend().catch(() => { });
        else ctx.resume().catch(() => { });
    });

    window.addEventListener('geo:settings', refresh);

    // Try straight away: browsers that remember the player's earlier taps allow it.
    if (settings().sound && ensure()) {
        if (ctx.state === 'running') refresh();
        else ctx.resume().then(refresh).catch(() => { });
        setTimeout(() => { if (ctx && ctx.state === 'suspended') showHint(true); }, 1200);
    }
    paintToggle();

    window.geoAudio = {
        play, vibrate, startMusic, stopMusic, refresh,
        canVibrate: () => 'vibrate' in navigator,
        sounds: Object.keys(SFX)
    };
})();
