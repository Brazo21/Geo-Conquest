(function () {
    'use strict';

    const SKIN = { light: '#f1c9a5', medium: '#d9a173', dark: '#a8693f', deep: '#6e4428' };
    const settings = () => (window.geoSettings ? window.geoSettings.get() : { generalSkin: 'medium', generalHair: 'moustache', generalVoice: true, general: true });

    const STAR = (cx, cy, r) => {
        const pts = [];
        for (let i = 0; i < 10; i++) {
            const a = -Math.PI / 2 + i * Math.PI / 5, rr = i % 2 ? r * 0.45 : r;
            pts.push(`${(cx + rr * Math.cos(a)).toFixed(1)},${(cy + rr * Math.sin(a)).toFixed(1)}`);
        }
        return `<polygon points="${pts.join(' ')}" class="gold"/>`;
    };

    function svg() {
        return `
<svg viewBox="0 0 120 150" class="gen-svg" aria-hidden="true" focusable="false">
  <g class="gen-sparkle">${STAR(14, 30, 6)}${STAR(104, 52, 5)}${STAR(20, 84, 4)}</g>
  <g class="gen-think"><circle cx="94" cy="32" r="2.4"/><circle cx="101" cy="23" r="3.2"/><circle cx="109" cy="13" r="4.2"/></g>
  <g class="gen-body">
    <g class="gen-arm-l"><rect x="23" y="95" width="12" height="38" rx="6" class="coat"/><circle cx="29" cy="135" r="6" class="skin"/></g>
    <path class="coat" d="M24 150 L29 101 Q33 90 47 88 L73 88 Q87 90 91 101 L96 150 Z"/>
    <path class="lapel" d="M47 88 L60 110 L73 88"/>
    <line x1="60" y1="108" x2="60" y2="150" class="seam"/>
    <circle cx="60" cy="118" r="2" class="gold"/><circle cx="60" cy="129" r="2" class="gold"/><circle cx="60" cy="140" r="2" class="gold"/>
    <rect x="28" y="127" width="64" height="6" class="belt"/><rect x="56" y="126" width="8" height="8" class="gold"/>
    <g class="medals">
      <rect x="36" y="97" width="5" height="6" fill="#c0322a"/><circle cx="38.5" cy="106" r="2.6" class="gold shine"/>
      <rect x="43" y="97" width="5" height="6" fill="#2a66c8"/><circle cx="45.5" cy="106" r="2.6" class="gold shine"/>
      <rect x="72" y="97" width="10" height="4" class="pc-fill"/><rect x="72" y="102" width="10" height="4" fill="#e8b84a"/>
    </g>
    <rect x="24" y="91" width="20" height="6" rx="3" class="epaulette"/><rect x="76" y="91" width="20" height="6" rx="3" class="epaulette"/>
  </g>
  <g class="gen-head">
    <rect x="54" y="78" width="12" height="12" class="skin"/>
    <circle cx="40" cy="64" r="4" class="skin"/><circle cx="80" cy="64" r="4" class="skin"/>
    <circle cx="60" cy="62" r="20" class="skin"/>
    <path class="hair beard" d="M41 66 Q43 87 60 89 Q77 87 79 66 Q71 78 60 78 Q49 78 41 66Z"/>
    <g class="gen-brows"><path d="M46 52 L56 53.5" class="brow b-l"/><path d="M64 53.5 L74 52" class="brow b-r"/></g>
    <g class="gen-eyes"><ellipse cx="51.5" cy="59" rx="2.4" ry="2.8" class="eye"/><ellipse cx="68.5" cy="59" rx="2.4" ry="2.8" class="eye"/></g>
    <path d="M60 60 q3.5 6 0 8.5" class="nose"/>
    <path class="mouth m-neutral" d="M54 77 h12"/>
    <path class="mouth m-smile" d="M52.5 75.5 q7.5 7 15 0"/>
    <path class="mouth m-frown" d="M53 79 q7 -5.5 14 0"/>
    <ellipse class="mouth m-open" cx="60" cy="77.5" rx="4.2" ry="3.4"/>
    <path class="hair moustache" d="M60 70 q-6 -4.5 -12 -.5 q-4 3 -8.5 -1.5 q2.5 8 10.5 6 q6 -1.2 10 -3.4 q4 2.2 10 3.4 q8 2 10.5 -6 q-4.5 4.5 -8.5 1.5 q-6 -4 -12 .5z"/>
    <path class="gen-sweat" d="M82 47 q3.5 5.5 0 8 q-3.5 -2.5 0 -8z"/>
    <path class="cap-top" d="M35 45 Q36 26 60 24 Q87 25 86 43 Z"/>
    <rect x="37" y="39" width="47" height="8" class="pc-fill"/>
    <path class="visor" d="M36 47 Q60 54 85 47 L86 50 Q60 59 35 50Z"/>
    ${STAR(60, 33, 4.6)}
  </g>
  <g class="gen-arm-r"><rect x="79" y="95" width="12" height="38" rx="6" class="coat"/><circle cx="85" cy="135" r="6" class="skin"/></g>
</svg>`;
    }

    const generals = [];

    function paintLook(g) {
        const s = settings();
        g.el.style.setProperty('--gen-skin', SKIN[s.generalSkin] || SKIN.medium);
        g.el.dataset.hair = s.generalHair;
    }

    function mount(el) {
        if (el.dataset.mounted) return;
        el.dataset.mounted = '1';
        const name = el.dataset.name || 'Commander';
        el.innerHTML = `
            <div class="gen-figure">${svg()}<div class="gen-name">Gen. ${escapeHtml(name)}</div></div>
            <div class="gen-bubble" aria-live="polite"><span class="txt"></span></div>`;
        const g = { el, bubble: el.querySelector('.gen-bubble'), txt: el.querySelector('.gen-bubble .txt'), timer: null, typing: null, busyUntil: 0 };
        generals.push(g);
        paintLook(g);
        setState(g, el.dataset.state || 'idle');
        if (el.dataset.line) say(el.dataset.state || 'idle', el.dataset.line, { hold: true });
    }

    function escapeHtml(s) {
        return String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    }

    function setState(g, state) {
        g.el.dataset.state = state;
        // Restart one-shot animations (jump, shake) when the same state comes twice.
        g.el.classList.remove('gen-again');
        void g.el.offsetWidth;
        g.el.classList.add('gen-again');
    }

    function type(g, text) {
        clearInterval(g.typing);
        g.txt.textContent = '';
        g.bubble.classList.add('show');
        let i = 0;
        const voice = settings().generalVoice;
        g.typing = setInterval(() => {
            g.txt.textContent = text.slice(0, ++i);
            if (voice && i % 3 === 1 && text[i - 1] !== ' ') window.geoAudio?.play('talk');
            if (i >= text.length) clearInterval(g.typing);
        }, 28);
    }

    /**
     * Make the General react.
     * state: idle | salute | order | cheer | sad | think | alarm | victory | defeat
     * opts.hold: keep the pose and bubble until the next reaction.
     * opts.minor: skip if a more important line is still on screen.
     */
    function say(state, text, opts = {}) {
        const now = Date.now();
        generals.forEach(g => {
            if (opts.minor && now < g.busyUntil) return;
            clearTimeout(g.timer);
            setState(g, state);
            if (text) type(g, text); else g.bubble.classList.remove('show');
            g.busyUntil = opts.minor ? now : now + 1800;
            if (!opts.hold) {
                g.timer = setTimeout(() => {
                    g.bubble.classList.remove('show');
                    setState(g, 'idle');
                }, opts.ms || 3600);
            }
        });
    }

    window.addEventListener('geo:settings', () => generals.forEach(paintLook));

    window.geoGeneral = { say, mount };

    const start = () => document.querySelectorAll('div[data-general]').forEach(mount);
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
})();
