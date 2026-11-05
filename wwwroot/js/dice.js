// Animated 3-D dice. The server rolls the dice (so nobody can cheat from the browser);
// this script only shows the result: each die tumbles through the air and lands on the
// value the server sent. Any element <span class="cr-die" data-value="4"> becomes a cube;
// elements that also carry class "roll" are animated, the rest are drawn at rest.
(function () {
    'use strict';

    // Pip positions on a 3×3 grid (1 = top-left … 9 = bottom-right).
    const PIPS = { 1: [5], 2: [3, 7], 3: [3, 5, 7], 4: [1, 3, 7, 9], 5: [1, 3, 5, 7, 9], 6: [1, 3, 4, 6, 7, 9] };
    // Cube rotation that brings face n to the front (the inverse of that face's own rotation).
    const SHOW = { 1: [0, 0], 2: [0, -90], 3: [-90, 0], 4: [90, 0], 5: [0, 90], 6: [0, 180] };

    const mode = () => (window.geoSettings ? window.geoSettings.get().dice : 'full');
    const reducedMotion = () => window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;

    function durationMs(speed) {
        const m = mode();
        if (m === 'off' || reducedMotion()) return 0;
        const sp = speed ?? (window.geoSettings ? window.geoSettings.speed() : 1);
        const base = m === 'quick' ? 550 : 1150;
        return Math.max(260, Math.round(base / sp));
    }

    function face(n) {
        let cells = '';
        for (let i = 1; i <= 9; i++) cells += `<i class="${PIPS[n].includes(i) ? 'p' : ''}"></i>`;
        return `<span class="face f${n}">${cells}</span>`;
    }

    function valueOf(el) {
        const v = parseInt(el.dataset.value, 10);
        return v >= 1 && v <= 6 ? v : 1;
    }

    function restTransform(v, extraX = 0, extraY = 0) {
        const [x, y] = SHOW[v];
        return `rotateX(${x + extraX}deg) rotateY(${y + extraY}deg) rotateZ(0deg)`;
    }

    function build(el) {
        const v = valueOf(el);
        if (!el.dataset.built) {
            el.dataset.built = '1';
            el.classList.add('d3');
            el.setAttribute('role', 'img');
            el.innerHTML = `<span class="cube">${[1, 2, 3, 4, 5, 6].map(face).join('')}</span>`;
        }
        el.setAttribute('aria-label', `Die showing ${v}`);
        const cube = el.querySelector('.cube');
        cube.style.transform = restTransform(v);
        return cube;
    }

    /** Tumble one die and land it on its data-value. Resolves when it lands. */
    function roll(el, { duration, delay = 0 } = {}) {
        const cube = build(el);
        const v = valueOf(el);
        const ms = duration ?? durationMs();
        const won = el.classList.contains('won');
        const duel = el.closest('.cr-duel');

        if (ms === 0 || !cube.animate) {
            el.classList.add('landed');
            return Promise.resolve();
        }

        el.classList.remove('won');
        duel?.classList.add('pending');
        const spinX = 360 * (2 + Math.floor(Math.random() * 2)) * (Math.random() < 0.5 ? -1 : 1);
        const spinY = 360 * (1 + Math.floor(Math.random() * 2)) * (Math.random() < 0.5 ? -1 : 1);
        const end = restTransform(v, spinX, spinY);
        cube.style.transform = end;

        const timing = { duration: ms, delay, easing: 'cubic-bezier(.15,.75,.3,1)', fill: 'backwards' };
        const spin = cube.animate([
            { transform: `rotateX(${Math.random() * 90 - 45}deg) rotateY(${Math.random() * 90 - 45}deg) rotateZ(${Math.random() * 120 - 60}deg)` },
            { transform: end }
        ], timing);
        el.animate([
            { transform: 'translateY(-1.4rem) scale(.7)', opacity: 0.2 },
            { transform: 'translateY(0) scale(1.08)', opacity: 1, offset: 0.55 },
            { transform: 'translateY(-.35rem) scale(1)', offset: 0.75 },
            { transform: 'none', opacity: 1 }
        ], { ...timing, easing: 'ease-out' });

        return spin.finished.then(() => {
            if (won) el.classList.add('won');
            el.classList.add('landed');
            duel?.classList.remove('pending');
        }).catch(() => { });
    }

    /** Turn every die inside root into a cube; roll the ones marked "roll". */
    function upgrade(root = document, opts = {}) {
        const all = [...root.querySelectorAll('.cr-die[data-value]')];
        const rolling = all.filter(el => el.classList.contains('roll'));
        all.forEach(el => { el.classList.remove('roll'); build(el); });
        if (!rolling.length) return Promise.resolve();

        const ms = opts.duration ?? durationMs();
        if (ms > 0 && !opts.silent) window.geoAudio?.play('diceRoll', { duration: ms });
        const jobs = rolling.map((el, i) => roll(el, { duration: ms, delay: i * 45 }));
        return Promise.all(jobs).then(() => {
            if (ms > 0 && !opts.silent) window.geoAudio?.play('diceLand');
            window.dispatchEvent(new CustomEvent('geo:dice-landed', { detail: { count: rolling.length } }));
        });
    }

    window.geoDice = { upgrade, roll, durationMs };

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', () => upgrade());
    else upgrade();
})();
