// Exact conquest odds — a browser copy of Services/BattleOddsCalculator.cs, used for the live
// "WIN ODDS" readout while choosing a target. The server recalculates everything itself.
(function () {
    'use strict';

    const EXACT_LIMIT = 60;
    const WIN = 15 / 36, LOSS = 15 / 36, TIE = 6 / 36;
    const logFact = [0, 0];
    for (let i = 2; i <= EXACT_LIMIT + 1; i++) logFact[i] = logFact[i - 1] + Math.log(i);
    const memo = new Map();

    function solve(a, d) {
        if (d <= 0) return 1;
        if (a <= 1) return 0;
        const key = a * 1000 + d;
        if (memo.has(key)) return memo.get(key);

        const n = Math.min(a - 1, d);
        let total = 0;
        for (let k = 0; k <= n; k++) {
            for (let j = 0; j + k <= n; j++) {
                if (k === 0 && j === 0) continue;
                const t = n - k - j;
                const logP = logFact[n] - logFact[k] - logFact[j] - logFact[t]
                    + j * Math.log(WIN) + k * Math.log(LOSS) + t * Math.log(TIE);
                total += Math.exp(logP) * solve(a - k, d - j);
            }
        }
        const result = total / (1 - Math.pow(TIE, n));
        memo.set(key, result);
        return result;
    }

    function attackerWinProbability(attackers, defenders) {
        if (defenders <= 0) return 1;
        if (attackers <= 1) return 0;
        const largest = Math.max(attackers, defenders);
        if (largest > EXACT_LIMIT) {
            const f = EXACT_LIMIT / largest;
            attackers = Math.max(2, Math.round(attackers * f));
            defenders = Math.max(1, Math.round(defenders * f));
        }
        return solve(attackers, defenders);
    }

    window.battleOdds = { attackerWinProbability };

    // ── Wire up the controls console, if present ──────────────────────────
    document.addEventListener('DOMContentLoaded', () => {
        const from = document.getElementById('a-from');
        const to = document.getElementById('a-to');
        const out = document.getElementById('odds-value');
        if (!from || !to || !out) return;

        const spoils = document.getElementById('odds-spoils');
        const dial = document.getElementById('odds-dial');
        const btn = document.getElementById('attack-btn');

        function refresh() {
            const f = from.selectedOptions[0], t = to.selectedOptions[0];
            if (!f || !t) { out.textContent = '—'; return; }
            const p = attackerWinProbability(Number(f.dataset.soldiers), Number(t.dataset.soldiers));
            const pct = Math.round(p * 100);
            out.textContent = `${pct}%`;
            out.classList.toggle('good', p >= 0.5);
            spoils.textContent = `+${Math.max(2, Math.floor(Number(t.dataset.value) / 2))}`;
            if (dial) dial.style.setProperty('--dial', `${-135 + pct * 2.7}deg`);
            if (btn) btn.textContent = `Attack ${t.textContent.split(' (')[0]}`;
            window.geoMap?.setAttack(f.value, t.value);
        }

        from.addEventListener('change', refresh);
        to.addEventListener('change', refresh);
        window.addEventListener('geomap:ready', refresh);
        refresh();
    });
})();
