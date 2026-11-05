// Game replay for the Analytics page: territory-over-time chart + round-by-round playback.
(function () {
    'use strict';

    const data = window.geoReplay;
    const chart = document.getElementById('territory-chart');
    if (!data || !chart) return;

    const BASE_MS = 1600;
    const rounds = data.rounds;
    const human = data.players.findIndex(p => p.isHuman);
    // Stack order: you at the bottom, then everyone else in seat order.
    const order = [human, ...data.players.map((_, i) => i).filter(i => i !== human)];

    let current = rounds.length - 1;
    let speed = 2;
    let timer = null;

    const esc = s => String(s ?? '').replace(/[&<>"']/g,
        c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

    // ── Chart ──────────────────────────────────────────────────────────────
    rounds.forEach((r, ri) => {
        const col = document.createElement('button');
        col.type = 'button';
        col.className = 'flex-fill d-flex flex-column-reverse p-0 border-0';
        col.style.cssText = 'background:transparent;min-width:4px;outline-offset:2px;cursor:pointer';
        col.setAttribute('aria-label', `Round ${r.round}`);
        col.title = `Round ${r.round}: ` + order
            .map(pi => [data.players[pi].isHuman ? 'You' : data.players[pi].name, r.owners.filter(o => o === pi).length])
            .filter(([, n]) => n > 0).map(([n, c]) => `${n} ${c}`).join(' · ');

        order.forEach(pi => {
            const held = r.owners.filter(o => o === pi).length;
            if (!held) return;
            const seg = document.createElement('div');
            seg.style.cssText = `height:${held * 10}%;background:${data.players[pi].color};border-top:1px solid #0b0a06`;
            col.appendChild(seg);
        });

        col.addEventListener('click', () => { stop(); show(ri); });
        chart.appendChild(col);
    });

    document.getElementById('replay-legend').innerHTML = order
        .map(pi => `<span><span class="cr-swatch" style="background:${esc(data.players[pi].color)}"></span>${esc(data.players[pi].isHuman ? 'You' : data.players[pi].name)}</span>`)
        .join('');

    // ── Round view ─────────────────────────────────────────────────────────
    function show(index) {
        current = Math.max(0, Math.min(rounds.length - 1, index));
        const r = rounds[current];

        [...chart.children].forEach((col, i) => {
            col.style.opacity = i <= current ? '1' : '.25';
            col.style.outline = i === current ? '2px solid var(--cr-amber-hi)' : 'none';
        });

        document.getElementById('replay-round-label').textContent = `Round ${r.round} of ${rounds[rounds.length - 1].round}`;
        document.getElementById('rp-progress').style.width = `${Math.round(((current + 1) / rounds.length) * 100)}%`;

        const rows = data.countries
            .map((c, i) => ({ name: c.name, owner: data.players[r.owners[i]], soldiers: r.soldiers[i] }))
            .sort((a, b) => b.soldiers - a.soldiers);
        document.getElementById('rp-ledger').innerHTML = rows.map(x =>
            `<tr class="${x.owner && x.owner.isHuman ? 'mine' : ''}">
                <td>${esc(x.name)}</td>
                <td><span class="cr-swatch" style="background:${esc(x.owner?.color)}"></span>${esc(x.owner ? (x.owner.isHuman ? 'You' : x.owner.name.split(' · ')[0]) : '?')}</td>
                <td class="text-end fw-bold">${x.soldiers}</td>
            </tr>`).join('');
    }

    // ── Playback ───────────────────────────────────────────────────────────
    const playBtn = document.getElementById('rp-play');

    function stop() {
        clearTimeout(timer);
        timer = null;
        playBtn.textContent = '▶ Play';
    }

    function tick() {
        if (current >= rounds.length - 1) { stop(); return; }
        show(current + 1);
        timer = setTimeout(tick, BASE_MS / speed);
    }

    playBtn.addEventListener('click', () => {
        if (timer) { stop(); return; }
        if (current >= rounds.length - 1) show(0);
        playBtn.textContent = '❚❚ Pause';
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
        const idx = rounds.findIndex(r => r.round === Number(b.dataset.jump));
        if (idx >= 0) show(idx);
    }));

    show(current);
})();
