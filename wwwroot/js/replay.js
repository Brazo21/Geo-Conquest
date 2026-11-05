(function () {
    'use strict';

    const data = window.geoReplay;
    const chart = document.getElementById('territory-chart');
    if (!data || !chart) return;

    const BASE_MS = 1100;
    const rounds = data.rounds || [];
    // Older pages without steps fall back to one step per round.
    const steps = (data.steps && data.steps.length)
        ? data.steps
        : rounds.map(r => ({ round: r.round, text: `Round ${r.round}`, owners: r.owners, soldiers: r.soldiers }));
    if (!steps.length) return;

    const human = data.players.findIndex(p => p.isHuman);
    // Stack order: you at the bottom, then everyone else in seat order.
    const order = [human, ...data.players.map((_, i) => i).filter(i => i !== human)];
    const lastRound = rounds.length ? rounds[rounds.length - 1].round : steps[steps.length - 1].round;

    let current = steps.length - 1;
    let speed = 2;
    let timer = null;

    const esc = s => String(s ?? '').replace(/[&<>"']/g,
        c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const nameOf = p => p ? (p.isHuman ? 'You' : p.name.split(' · ')[0]) : '?';

    // Chart: one column per round; clicking jumps to the end of that round
    rounds.forEach(r => {
        const col = document.createElement('button');
        col.type = 'button';
        col.className = 'flex-fill d-flex flex-column-reverse p-0 border-0';
        col.style.cssText = 'background:transparent;min-width:4px;outline-offset:2px;cursor:pointer';
        col.dataset.round = r.round;
        col.setAttribute('aria-label', `Round ${r.round}`);
        col.title = `Round ${r.round}: ` + order
            .map(pi => [nameOf(data.players[pi]), r.owners.filter(o => o === pi).length])
            .filter(([, n]) => n > 0).map(([n, c]) => `${n} ${c}`).join(' · ');

        order.forEach(pi => {
            const held = r.owners.filter(o => o === pi).length;
            if (!held) return;
            const seg = document.createElement('div');
            seg.style.cssText = `height:${held * 10}%;background:${data.players[pi].color};border-top:1px solid #0b0a06`;
            col.appendChild(seg);
        });

        col.addEventListener('click', () => {
            stop();
            let idx = -1;
            steps.forEach((s, i) => { if (s.round <= r.round) idx = i; });
            show(Math.max(0, idx));
        });
        chart.appendChild(col);
    });

    document.getElementById('replay-legend').innerHTML = order
        .map(pi => `<span><span class="cr-swatch" style="background:${esc(data.players[pi].color)}"></span>${esc(nameOf(data.players[pi]))}</span>`)
        .join('');

    // One step
    let previous = null;
    function show(index) {
        current = Math.max(0, Math.min(steps.length - 1, index));
        const s = steps[current];

        [...chart.children].forEach(col => {
            const rnd = Number(col.dataset.round);
            col.style.opacity = rnd <= s.round ? '1' : '.25';
            col.style.outline = rnd === s.round ? '2px solid var(--cr-amber-hi)' : 'none';
        });

        document.getElementById('replay-round-label').textContent =
            `Round ${s.round} of ${lastRound} · move ${current + 1} of ${steps.length}`;
        document.getElementById('rp-progress').style.width =
            `${steps.length === 1 ? 100 : Math.round((current / (steps.length - 1)) * 100)}%`;
        const caption = document.getElementById('rp-caption');
        if (caption) caption.textContent = s.text;

        const rows = data.countries
            .map((c, i) => ({
                name: c.name,
                owner: data.players[s.owners[i]],
                soldiers: s.soldiers[i],
                changed: previous && (previous.owners[i] !== s.owners[i] || previous.soldiers[i] !== s.soldiers[i])
            }))
            .sort((a, b) => b.soldiers - a.soldiers);
        document.getElementById('rp-ledger').innerHTML = rows.map(x =>
            `<tr class="${x.owner && x.owner.isHuman ? 'mine' : ''}${x.changed ? ' rp-changed' : ''}">
                <td>${esc(x.name)}</td>
                <td><span class="cr-swatch" style="background:${esc(x.owner?.color)}"></span>${esc(nameOf(x.owner))}</td>
                <td class="text-end fw-bold">${x.soldiers}</td>
            </tr>`).join('');
        previous = s;
    }

    // Playback
    const playBtn = document.getElementById('rp-play');

    function stop() {
        clearTimeout(timer);
        timer = null;
        playBtn.textContent = '▶ Play';
        playBtn.setAttribute('aria-label', 'Play');
    }

    function tick() {
        if (current >= steps.length - 1) { stop(); return; }
        show(current + 1);
        timer = setTimeout(tick, BASE_MS / speed);
    }

    playBtn.addEventListener('click', () => {
        if (timer) { stop(); return; }
        if (current >= steps.length - 1) { previous = null; show(0); }   // at the end: start over from move 1
        playBtn.textContent = '❚❚ Pause';
        playBtn.setAttribute('aria-label', 'Pause');
        timer = setTimeout(tick, BASE_MS / speed);
    });
    document.getElementById('rp-prev').addEventListener('click', () => { stop(); show(current - 1); });
    document.getElementById('rp-next').addEventListener('click', () => { stop(); show(current + 1); });

    document.querySelectorAll('[data-rp-speed]').forEach(b => b.addEventListener('click', () => {
        speed = Number(b.dataset.rpSpeed);
        document.querySelectorAll('[data-rp-speed]').forEach(x => x.classList.toggle('active', x === b));
    }));

    document.querySelectorAll('[data-jump]').forEach(b => b.addEventListener('click', () => {
        stop();
        const step = Number(b.dataset.step);
        if (Number.isFinite(step) && b.dataset.step !== undefined) { show(step); return; }
        let idx = -1;
        steps.forEach((s, i) => { if (s.round <= Number(b.dataset.jump)) idx = i; });
        show(Math.max(0, idx));
    }));

    show(current);
})();
