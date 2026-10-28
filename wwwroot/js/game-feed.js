// GeoConquest live feed for the command room.
// • Replay: after each move, plays every new event (your move, then each NPC turn) step by step.
// • Fast-forward: 1× / 2× / 4× only changes the pace — every move, die and NPC reason is still shown.
// • Spectators: polls for new events and plays them as they happen (real-time viewing).
(function () {
    'use strict';

    const cfg = window.geoConquest;
    if (!cfg) return;

    const BASE_DELAY_MS = 1500;
    const POLL_MS = 3000;
    const SPEED_KEY = 'geoconquest.speed';
    const $ = id => document.getElementById(id);

    let speed = readSpeed();
    let queue = [];
    let playing = false;
    let lastSequence = cfg.lastSequence;
    let feed = null;                         // latest GameFeedDto

    const playersById = Object.fromEntries(cfg.players.map(p => [p.id, p]));

    // ── Speed controls (there may be one set per layout) ───────────────────
    function paintSpeed() {
        document.querySelectorAll('[data-speed]').forEach(b => b.classList.toggle('active', Number(b.dataset.speed) === speed));
    }
    document.querySelectorAll('[data-speed]').forEach(btn => btn.addEventListener('click', () => {
        speed = Number(btn.dataset.speed);
        try { localStorage.setItem(SPEED_KEY, String(speed)); } catch { /* storage unavailable */ }
        paintSpeed();
    }));
    paintSpeed();

    function readSpeed() {
        try { return Number(localStorage.getItem(SPEED_KEY)) || 1; } catch { return 1; }
    }

    // ── Helpers ────────────────────────────────────────────────────────────
    const esc = s => String(s ?? '').replace(/[&<>"']/g,
        c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

    function feedPlayer(index) {
        return feed && index != null ? feed.players[index] : null;
    }

    function colorOfIndex(index) {
        const p = feedPlayer(index);
        return p ? p.color : '#9a7a44';
    }

    function decodeBoard(board) {
        if (!feed || !board) return [];
        return board.split(',').filter(Boolean).map(part => {
            const [code, owner, soldiers] = part.split('.');
            const p = feed.players[Number(owner)];
            return { code, ownerId: p ? p.id : null, soldiers: Number(soldiers) };
        });
    }

    // ── Board-wide updates from a snapshot ─────────────────────────────────
    function applyBoard(board) {
        const entries = decodeBoard(board);
        if (!entries.length) return;

        window.geoMap?.update(entries);

        // Ledger
        const ledger = $('ledger');
        entries.forEach(e => {
            const row = ledger?.querySelector(`tr[data-code="${e.code}"]`);
            if (!row) return;
            const p = playersById[e.ownerId];
            row.querySelector('.soldiers').textContent = e.soldiers;
            row.querySelector('.owner').innerHTML =
                `<span class="cr-swatch" style="background:${esc(p?.color)}"></span><span class="nm">${esc(p ? (p.isHuman ? 'You' : p.name.split(' · ')[0]) : '?')}</span>`;
            row.classList.toggle('mine', e.ownerId === cfg.humanId);
        });

        // Per-player totals: cards, standings, readout
        const totals = Object.fromEntries(cfg.players.map(p => [p.id, { countries: 0, soldiers: 0 }]));
        entries.forEach(e => { if (totals[e.ownerId]) { totals[e.ownerId].countries++; totals[e.ownerId].soldiers += e.soldiers; } });
        const max = Math.max(1, ...Object.values(totals).map(t => t.soldiers));

        Object.entries(totals).forEach(([id, t]) => {
            const card = document.querySelector(`.cr-card[data-player="${id}"]`);
            if (card) {
                card.querySelector('[data-f="countries"]').textContent = t.countries;
                card.querySelector('[data-f="soldiers"]').textContent = t.soldiers;
                card.classList.toggle('out', t.countries === 0);
                if (t.countries === 0) card.querySelector('[data-f="state"]').textContent = 'Eliminated';
            }
            const bar = document.querySelector(`[data-standing="${id}"]`);
            if (bar) {
                bar.querySelector('.fill').style.width = `${Math.round(t.soldiers * 100 / max)}%`;
                bar.querySelector('.val').textContent = t.soldiers;
            }
        });

        const standings = $('standings');
        if (standings) {
            [...standings.children]
                .sort((a, b) => Number(b.querySelector('.val').textContent) - Number(a.querySelector('.val').textContent))
                .forEach(r => standings.appendChild(r));
        }

        const mine = totals[cfg.humanId];
        if (mine) {
            const c = document.querySelector('[data-readout="countries"]');
            const s = document.querySelector('[data-readout="soldiers"]');
            if (c) c.textContent = mine.countries;
            if (s) s.textContent = mine.soldiers;
        }
    }

    function markCurrent(index) {
        const p = feedPlayer(index);
        if (!p) return;
        document.querySelectorAll('.cr-card').forEach(card => {
            const isNow = card.dataset.player === p.id;
            card.classList.toggle('current', isNow);
            const state = card.querySelector('[data-f="state"]');
            if (!card.classList.contains('out')) state.textContent = isNow ? (p.isHuman ? 'Your turn' : 'Moving') : 'Waiting';
        });
    }

    // ── Panels ─────────────────────────────────────────────────────────────
    function addToLog(e) {
        const log = $('event-log');
        if (!log) return;
        const div = document.createElement('div');
        div.className = 'entry';
        div.style.borderColor = colorOfIndex(e.actor);
        div.innerHTML =
            `<div class="round">R${e.round}</div>
             <div class="msg">${esc(e.message)}</div>
             ${e.attackerDice.length ? `<div class="dice">${e.attackerDice.join(' ')} vs ${e.defenderDice.join(' ')}</div>` : ''}
             ${e.commentary ? `<div class="mic">🎙 ${esc(e.commentary)}</div>` : ''}
             ${e.reasoning ? `<div class="why">Why: ${esc(e.reasoning)}</div>` : ''}`;
        log.prepend(div);
    }

    function showLastRoll(e) {
        const box = $('last-roll');
        if (!box || !e.attackerDice.length) return;
        const a = colorOfIndex(e.actor);
        const defOwner = decodeBoard(e.board).find(x => x.code === e.to);
        const d = defOwner && playersById[defOwner.ownerId] ? playersById[defOwner.ownerId].color : 'var(--cr-red)';
        box.innerHTML = e.attackerDice.slice(0, 4).map((av, i) => {
            const dv = e.defenderDice[i];
            const verdict = av > dv ? 'Attacker wins' : dv > av ? 'Defender wins' : 'Tie';
            return `<div class="d-flex align-items-center gap-2 mb-2">
                        <span class="cr-die sm roll ${av > dv ? 'won' : ''}" style="color:${a}"><span>${av}</span></span>
                        <span class="cr-muted small">vs</span>
                        <span class="cr-die sm roll ${dv > av ? 'won' : ''}" style="color:${d}"><span>${dv}</span></span>
                        <span class="small cr-upper ms-1">${verdict}</span>
                    </div>`;
        }).join('') + `<div class="small cr-muted mt-1">${esc(e.message)}</div>`;
    }

    function showDecision(e) {
        const box = $('npc-decisions');
        if (!box || !e.reasoning) return;
        box.querySelector('.cr-muted.small')?.remove();
        const div = document.createElement('div');
        div.className = 'd';
        div.innerHTML = `<div class="who" style="color:${colorOfIndex(e.actor)}">${esc(e.message)}</div><div class="why">Why: ${esc(e.reasoning)}</div>`;
        box.prepend(div);
        while (box.children.length > 6) box.lastElementChild.remove();
    }

    function showCommentary(e) {
        const tape = $('commentary-tape');
        if (!tape || !e.commentary) return;
        tape.innerHTML = `<span>“${esc(e.commentary)}”</span><span class="src">${e.commentaryIsAi ? 'Generated by an LLM' : 'Built-in line · AI was busy'}</span>`;
    }

    function showBanner(e) {
        const banner = $('banner');
        if (!banner) return;
        const actor = feedPlayer(e.actor);
        if (e.type === 'CountryConquered' && actor?.isHuman) {
            banner.className = 'cr-banner mt-3';
            banner.textContent = e.message;
        } else if (e.type === 'AttackFailed' && actor?.isHuman) {
            banner.className = 'cr-banner bad mt-3';
            banner.textContent = e.message;
        } else if (e.type === 'PlayerEliminated' || e.type === 'GameWon' || e.type === 'GameLost') {
            banner.className = `cr-banner mt-3 ${e.type === 'GameWon' && actor?.isHuman ? '' : 'bad'}`;
            banner.textContent = e.message;
        }
    }

    function setSim(active, text) {
        const dot = $('dot-sim');
        const label = $('sim-status');
        if (dot) dot.className = `cr-dot ${active ? 'live' : ''}`;
        if (label) label.textContent = text;
    }

    function show(e) {
        const round = $('round-label');
        if (round) round.textContent = `Round ${e.round}`;
        if (e.type === 'TurnStarted') markCurrent(e.actor);
        if (e.type === 'AttackStarted' || e.type === 'DiceRolled') window.geoMap?.setAttack(e.from, e.to);
        if (e.type === 'CountryConquered' || e.type === 'AttackFailed' || e.type === 'AttackRetreated') window.geoMap?.setAttack(null, null);

        applyBoard(e.board);
        addToLog(e);
        showLastRoll(e);
        showDecision(e);
        showCommentary(e);
        showBanner(e);

        const actor = feedPlayer(e.actor);
        setSim(true, actor && !actor.isHuman ? `NPC simulation active · ${actor.name.split(' · ')[0]}` : 'Simulation active');
    }

    // ── Playback ───────────────────────────────────────────────────────────
    function delayFor(e) {
        const actor = feedPlayer(e.actor);
        // Your own move was already seen on screen when you made it: show it, don't make you wait.
        if (actor?.isHuman && !cfg.isSpectator) return 250;
        const weight = e.type === 'DiceRolled' || e.type === 'CountryConquered' || e.type === 'PlayerEliminated'
            || e.type === 'GameWon' || e.type === 'GameLost' ? 1.0 : e.reasoning ? 0.95 : 0.5;
        return (BASE_DELAY_MS * weight) / speed;
    }

    async function play() {
        if (playing) return;
        playing = true;
        while (queue.length > 0) {
            const e = queue.shift();
            show(e);
            await new Promise(r => setTimeout(r, delayFor(e)));
        }
        playing = false;
        onQueueDrained();
    }

    function enqueue(events) {
        if (!events.length) return;
        queue.push(...events);
        lastSequence = Math.max(lastSequence, events[events.length - 1].seq);
        play();
    }

    function onQueueDrained() {
        setSim(false, cfg.isSpectator ? (feed && feed.status === 'InProgress' ? 'Watching live' : 'Game over') : 'NPC simulation idle');
        if (!cfg.isSpectator) {
            $('replay-wait')?.classList.add('d-none');
            $('after-replay')?.classList.remove('d-none');
            $('after-replay-banner')?.classList.remove('d-none');
            const dot = $('dot-player');
            if (dot && cfg.canAct) dot.className = 'cr-dot on';
        }
    }

    async function fetchEvents(after) {
        const res = await fetch(`/game/${encodeURIComponent(cfg.gameId)}/events?after=${after}`,
            { headers: { Accept: 'application/json' }, cache: 'no-store' });
        if (!res.ok) throw new Error(`Feed request failed (${res.status})`);
        return res.json();
    }

    async function startReplay() {
        try {
            const from = Math.max(0, cfg.replayFrom - 1);
            feed = await fetchEvents(from);
            let events = feed.events;
            if (cfg.replayFrom > 0 && events.length && events[0].seq === cfg.replayFrom) {
                applyBoard(events[0].board);                 // start from exactly what the player last saw
                events = events.slice(1);
            }
            if (events.length) enqueue(events); else onQueueDrained();
        } catch (err) {
            console.error(err);
            onQueueDrained();                                // never lock the player out
        }
    }

    async function poll() {
        try {
            const data = await fetchEvents(lastSequence);
            feed = data;
            enqueue(data.events);
            if (!playing) onQueueDrained();
            if (data.status !== 'InProgress') return;
        } catch (err) {
            console.error(err);
            setSim(false, 'Reconnecting…');
        }
        setTimeout(poll, POLL_MS);
    }

    if (cfg.replayFrom != null) {
        startReplay();
    } else if (cfg.isSpectator && cfg.inProgress) {
        setSim(false, 'Watching live');
        // Load player list first so snapshots can be decoded, then follow along.
        fetchEvents(lastSequence).then(d => { feed = d; }).finally(() => setTimeout(poll, POLL_MS));
    }
})();
