(function () {
    'use strict';

    const cfg = window.geoConquest;
    if (!cfg) return;

    const names = Object.fromEntries((cfg.countries || []).map(c => [c.code, c.name]));
    const nameOf = code => names[code] || code || 'the target';
    const A = () => window.geoAudio;
    const G = (...args) => window.geoGeneral?.say(...args);
    const reduced = () => window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
        || window.geoSettings?.get().dice === 'off';

    function buzz(name) {
        if (!cfg.isSpectator) A()?.vibrate(name);
    }

    // Full-screen moments (victory / defeat)
    function overlay(kind, text) {
        const fx = document.createElement('div');
        fx.className = `cr-fx ${kind}`;
        fx.setAttribute('aria-hidden', 'true');
        fx.innerHTML = `<div class="cr-fx-text">${text}</div>`;
        if (kind === 'victory' && !reduced()) {
            for (let i = 0; i < 40; i++) {
                const c = document.createElement('i');
                c.style.left = `${Math.random() * 100}%`;
                c.style.animationDelay = `${Math.random() * 1.2}s`;
                c.style.animationDuration = `${2 + Math.random() * 1.8}s`;
                c.style.background = ['#ffc35a', '#3ddc84', '#f6ead0', '#4d9bff'][i % 4];
                fx.appendChild(c);
            }
        }
        document.body.appendChild(fx);
        if (kind === 'defeat' && !reduced()) document.body.classList.add('cr-shake');
        setTimeout(() => document.body.classList.remove('cr-shake'), 900);
        setTimeout(() => fx.classList.add('out'), 3600);
        setTimeout(() => fx.remove(), 4600);
    }

    // ── Anti-cheat: warn before leaving in the middle of your own battle ──
    // The battle is saved on the server either way; leaving for 5+ minutes counts as a retreat.
    if (cfg.guardLeave && !cfg.isSpectator) {
        const warn = ev => { ev.preventDefault(); ev.returnValue = ''; };
        window.addEventListener('beforeunload', warn);
        document.addEventListener('submit', () => window.removeEventListener('beforeunload', warn), true);
    }

    // Event reactions
    window.addEventListener('geo:event', ({ detail }) => {
        const { e, actor, prevOwnerId } = detail;
        const mine = !!actor?.isHuman;
        const vsMe = !mine && prevOwnerId === cfg.humanId;     // an NPC is hitting one of my countries
        const npc = actor && !actor.isHuman ? actor.name.split(' · ')[0] : 'The enemy';

        switch (e.type) {
            case 'TurnStarted':
                if (mine) {
                    A()?.play('yourTurn'); buzz('turn');
                    G('salute', cfg.isSpectator ? 'The Commander takes the field.' : 'Your orders, Commander?');
                } else {
                    A()?.play('npcMove');
                    G('think', `${npc} is moving… watch them.`, { minor: true });
                }
                break;

            case 'Reinforced':
            case 'Transferred':
                A()?.play(mine ? 'place' : 'npcMove');
                if (mine) G('salute', e.type === 'Reinforced' ? `Troops in position at ${nameOf(e.to)}.` : 'Redeployment complete.', { minor: true });
                break;

            case 'AttackStarted':
                if (mine) { A()?.play('attack'); G('order', `Advance on ${nameOf(e.to)}!`); }
                else if (vsMe) { A()?.play('alarm'); buzz('alarm'); G('alarm', `${npc} is hitting ${nameOf(e.to)}!`); }
                else A()?.play('npcMove');
                break;

            case 'DiceRolled': {
                if (!mine && !vsMe) break;
                const a = e.attackerDice || [], d = e.defenderDice || [];
                let won = 0, lost = 0;
                a.forEach((av, i) => { if (av > d[i]) mine ? won++ : lost++; else if (d[i] > av) mine ? lost++ : won++; });
                const net = won - lost;
                const wait = (window.geoDice ? window.geoDice.durationMs() : 0) + 120;
                setTimeout(() => {
                    A()?.play(net > 0 ? 'duelWin' : net < 0 ? 'duelLoss' : 'duelTie');
                    if (net > 0) G('cheer', won > 1 ? `${won} enemy soldiers down!` : 'Good roll!', { minor: true });
                    else if (net < 0) G('sad', mine ? 'Hold the line!' : 'They broke through our dice…', { minor: true });
                }, wait);
                break;
            }

            case 'CountryConquered':
                if (mine) { A()?.play('conquer'); buzz('conquer'); G('cheer', `${nameOf(e.to)} is ours!`); }
                else if (vsMe) { A()?.play('countryLost'); buzz('loss'); G('sad', `We lost ${nameOf(e.to)}…`); }
                else A()?.play('npcMove');
                break;

            case 'AttackFailed':
                if (mine) { A()?.play('attackFailed'); buzz('loss'); G('sad', 'Fall back and regroup.'); }
                else if (vsMe) { A()?.play('defended'); buzz('conquer'); G('cheer', `${nameOf(e.to)} held! Well defended.`); }
                break;

            case 'AttackRetreated':
                if (mine) { A()?.play('retreat'); G('idle', 'A wise retreat. We fight another day.'); }
                else if (vsMe) { A()?.play('defended'); G('cheer', `${npc} pulled back!`, { minor: true }); }
                break;

            case 'PlayerEliminated':
                A()?.play('eliminated');
                if (!mine) G('cheer', `${npc} is out of the war!`, { minor: true });
                break;

            case 'GameWon':
                if (mine) {
                    A()?.play('victory'); buzz('victory');
                    G('victory', 'The world is ours!', { hold: true });
                    overlay('victory', 'Victory · the world is yours');
                } else {
                    A()?.play('defeat'); buzz('defeat');
                    G('defeat', 'It is over, Commander…', { hold: true });
                    overlay('defeat', 'Defeat');
                }
                break;

            case 'AllianceFormed':
                if (mine || e.message.includes(cfg.players.find(p => p.isHuman)?.name ?? '\u0000')) {
                    A()?.play('defended'); buzz('conquer');
                    G('cheer', 'Alliance sealed. Watch their borders, not ours.');
                } else {
                    A()?.play('alarm');
                    G('alarm', 'Two generals have teamed up. Careful, Commander.', { minor: true });
                }
                break;

            case 'AllianceRejected':
                A()?.play('attackFailed');
                G('sad', 'They turned us down. Their loss.', { minor: true });
                break;

            case 'AllianceBroken':
                A()?.play('alarm');
                if (mine) G('order', 'No more friends. Strike while they reel!');
                break;

            case 'AllianceExpired':
                A()?.play('npcMove');
                break;

            case 'TurnTimedOut':
                A()?.play('alarm'); buzz('alarm');
                G('sad', 'We were away too long. The turn has passed.');
                break;

            case 'GameLost':
                A()?.play('defeat'); buzz('defeat');
                G('defeat', 'It is over, Commander…', { hold: true });
                overlay('defeat', 'Defeat');
                break;
        }
    });

    // Previews from the settings page reuse the same moments.
    window.geoFx = { overlay };
})();
