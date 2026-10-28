// World map for the command room: real country shapes (Natural Earth via world-atlas),
// game countries filled in their owner's colour, and a pin per country showing its soldiers.
// Clicking an enemy pin picks it as the attack target. If the map data can't load, pins still draw.
(function () {
    'use strict';

    const cfg = window.geoConquest;
    const host = document.getElementById('world-map');
    if (!cfg || !host) { window.geoMap = null; return; }

    const W = 960, H = 470, PIN_R = 12, MIN_GAP = 30;
    const ATLAS_URL = 'https://cdn.jsdelivr.net/npm/world-atlas@2.0.2/countries-110m.json';

    const playersById = Object.fromEntries(cfg.players.map(p => [p.id, p]));
    // Live board state: code -> { ownerId, soldiers }
    const board = Object.fromEntries(cfg.countries.map(c => [c.code, { ownerId: c.ownerId, soldiers: c.soldiers }]));
    const meta = Object.fromEntries(cfg.countries.map(c => [c.code, c]));

    let svg, projection, shapeById = {}, pins = {}, attackLine;

    const colorOf = code => (playersById[board[code].ownerId] || {}).color || '#888';
    const isMine = code => board[code].ownerId === cfg.humanId;

    function init() {
        if (!window.d3) { setTimeout(init, 60); return; }   // scripts load deferred

        svg = d3.select(host).append('svg').attr('viewBox', `0 0 ${W} ${H}`).attr('preserveAspectRatio', 'xMidYMid meet');
        projection = d3.geoNaturalEarth1().scale(172).translate([W / 2, H / 2 + 22]);
        const path = d3.geoPath(projection);

        const gGrid = svg.append('g');
        const gLand = svg.append('g');
        const gLines = svg.append('g');
        const gPins = svg.append('g');

        gGrid.append('path').attr('class', 'graticule').attr('d', path(d3.geoGraticule10()));
        attackLine = gLines.append('path').attr('class', 'attack-line').style('display', 'none');

        drawPins(gPins);
        window.dispatchEvent(new Event('geomap:ready'));

        fetch(ATLAS_URL)
            .then(r => r.json())
            .then(world => {
                const features = topojson.feature(world, world.objects.countries).features
                    .filter(f => f.id !== '010');                 // drop Antarctica
                gLand.selectAll('path')
                    .data(features)
                    .join('path')
                    .attr('d', path)
                    .attr('class', 'land')
                    .each(function (f) { shapeById[f.id] = this; });
                paintShapes();
            })
            .catch(() => {
                const note = document.createElement('div');
                note.className = 'cr-map-fallback';
                note.textContent = 'Map outlines unavailable offline · pins still live';
                host.appendChild(note);
            });
    }

    // Place pins at their real coordinates, then nudge overlapping ones apart (Europe gets crowded).
    function layoutPins() {
        const pts = cfg.countries.map(c => {
            const [x, y] = projection([c.lon, c.lat]);
            return { code: c.code, x, y, ox: x, oy: y };
        });

        for (let iter = 0; iter < 80; iter++) {
            let moved = false;
            for (let i = 0; i < pts.length; i++) {
                for (let j = i + 1; j < pts.length; j++) {
                    const a = pts[i], b = pts[j];
                    let dx = b.x - a.x, dy = b.y - a.y;
                    let dist = Math.hypot(dx, dy);
                    if (dist >= MIN_GAP) continue;
                    if (dist < 0.01) { dx = 1; dy = 0; dist = 1; }
                    const push = (MIN_GAP - dist) / 2;
                    a.x -= (dx / dist) * push; a.y -= (dy / dist) * push;
                    b.x += (dx / dist) * push; b.y += (dy / dist) * push;
                    moved = true;
                }
            }
            if (!moved) break;
        }

        pts.forEach(p => {
            p.x = Math.min(W - PIN_R - 2, Math.max(PIN_R + 2, p.x));
            p.y = Math.min(H - 22, Math.max(PIN_R + 2, p.y));
        });
        return pts;
    }

    function drawPins(g) {
        layoutPins().forEach(p => {
            const c = meta[p.code];
            const pin = g.append('g').attr('class', 'pin').attr('data-code', p.code)
                .attr('transform', `translate(${p.x},${p.y})`)
                .attr('tabindex', 0)
                .attr('role', 'button');

            if (Math.hypot(p.x - p.ox, p.y - p.oy) > 4) {
                g.insert('line', ':first-child')
                    .attr('x1', p.ox).attr('y1', p.oy).attr('x2', p.x).attr('y2', p.y)
                    .attr('stroke', 'rgba(233,166,60,.35)').attr('stroke-width', 1);
                g.insert('circle', ':first-child').attr('cx', p.ox).attr('cy', p.oy).attr('r', 2).attr('fill', 'rgba(233,166,60,.6)');
            }

            pin.append('circle').attr('class', 'ring').attr('r', PIN_R);
            pin.append('text').attr('class', 'count');
            pin.append('text').attr('class', 'name').attr('y', PIN_R + 10).text(c.name);
            pin.append('title');

            pin.on('click', () => pick(p.code));
            pin.on('keydown', ev => { if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); pick(p.code); } });

            pins[p.code] = { node: pin, x: p.x, y: p.y };
        });
        paintPins();
    }

    function paintPins() {
        Object.keys(pins).forEach(code => {
            const color = colorOf(code);
            const pin = pins[code].node;
            const owner = playersById[board[code].ownerId];
            pin.select('circle.ring').attr('stroke', color);
            pin.select('text.count').attr('fill', color).text(board[code].soldiers);
            pin.select('title').text(`${meta[code].name}: ${board[code].soldiers} soldiers · ${owner ? owner.name : ''}`);
            pin.classed('targetable', cfg.canAct && !isMine(code));
            pin.attr('aria-label', `${meta[code].name}, ${board[code].soldiers} soldiers, held by ${owner ? owner.name : 'unknown'}`);
        });
    }

    function paintShapes() {
        cfg.countries.forEach(c => {
            const shape = c.mapId && shapeById[c.mapId];
            if (!shape) return;
            d3.select(shape).classed('owned', true).attr('fill', colorOf(c.code)).attr('stroke', colorOf(c.code));
        });
    }

    // Click a pin: an enemy becomes the target, one of your strong countries becomes the source.
    function pick(code) {
        if (!cfg.canAct) return;
        const select = document.getElementById(isMine(code) ? 'a-from' : 'a-to');
        if (!select || select.disabled || !select.querySelector(`option[value="${code}"]`)) return;
        select.value = code;
        select.dispatchEvent(new Event('change'));
    }

    function setAttack(from, to) {
        Object.values(pins).forEach(p => p.node.classed('selected', false));
        if (!from || !to || !pins[from] || !pins[to]) { attackLine?.style('display', 'none'); return; }
        pins[to].node.classed('selected', true);
        const a = pins[from], b = pins[to];
        const mx = (a.x + b.x) / 2, my = Math.min(a.y, b.y) - Math.abs(a.x - b.x) * 0.18 - 10;
        attackLine.style('display', null).attr('d', `M${a.x},${a.y} Q${mx},${my} ${b.x},${b.y}`);
    }

    // Called by the live feed with a decoded board snapshot: [{ code, ownerId, soldiers }]
    function update(entries) {
        entries.forEach(e => {
            if (!board[e.code]) return;
            const changed = board[e.code].soldiers !== e.soldiers || board[e.code].ownerId !== e.ownerId;
            const lost = e.soldiers < board[e.code].soldiers;
            board[e.code] = { ownerId: e.ownerId, soldiers: e.soldiers };
            if (changed && pins[e.code]) {
                const n = pins[e.code].node;
                n.classed('flash', false).classed('hit', false);
                void n.node().getBBox();
                n.classed('flash', true).classed('hit', lost);
                setTimeout(() => n.classed('hit', false), 900);
            }
        });
        if (svg) { paintPins(); paintShapes(); }
    }

    window.geoMap = { update, setAttack, board, colorOf };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
    if (cfg.battle) window.addEventListener('geomap:ready', () => setAttack(cfg.battle.from, cfg.battle.to));
})();
