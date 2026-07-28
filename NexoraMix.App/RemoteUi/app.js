(() => {
  'use strict';

  const elements = {
    pairing: document.getElementById('pairing'),
    app: document.getElementById('app'),
    pairCode: document.getElementById('pairCode'),
    connectButton: document.getElementById('connectButton'),
    pairError: document.getElementById('pairError'),
    connectionDot: document.getElementById('connectionDot'),
    connectionText: document.getElementById('connectionText'),
    masterDeck: document.getElementById('masterDeck'),
    globalStatus: document.getElementById('globalStatus'),
    syncHealth: document.getElementById('syncHealth'),
    masterPeak: document.getElementById('masterPeak'),
    clipping: document.getElementById('clipping'),
    recordButton: document.getElementById('recordButton'),
    masterCueButton: document.getElementById('masterCueButton'),
    cueVolume: document.getElementById('cueVolume'),
    mashupButton: document.getElementById('mashupButton'),
    stopAllButton: document.getElementById('stopAllButton'),
    viewClassic: document.getElementById('viewClassic'),
    viewFour: document.getElementById('viewFour'),
    viewMixer: document.getElementById('viewMixer'),
    fullscreenButton: document.getElementById('fullscreenButton'),
    disconnectButton: document.getElementById('disconnectButton'),
    quickMixer: document.getElementById('quickMixer'),
    quickCueA: document.getElementById('quickCueA'),
    quickCueB: document.getElementById('quickCueB'),
    quickCenter: document.getElementById('quickCenter'),
    quickCrossfader: document.getElementById('quickCrossfader'),
    deckWorkspace: document.getElementById('deckWorkspace'),
    mixerWorkspace: document.getElementById('mixerWorkspace'),
    channelStrips: document.getElementById('channelStrips'),
    crossfader: document.getElementById('crossfader'),
    centerCrossfaderButton: document.getElementById('centerCrossfaderButton'),
    footerStatus: document.getElementById('footerStatus'),
    latencyStatus: document.getElementById('latencyStatus'),
    deckTemplate: document.getElementById('deckTemplate'),
    channelTemplate: document.getElementById('channelTemplate')
  };

  const deckIds = ['A', 'B', 'C', 'D'];
  const deckViews = new Map();
  const channelViews = new Map();
  const activeInputs = new WeakSet();
  const commandThrottle = new Map();

  let token = localStorage.getItem('nexoraPairingCode') || '';
  let socket = null;
  let reconnectTimer = 0;
  let reconnectAttempt = 0;
  let state = null;
  let lastSnapshotReceivedAt = performance.now();
  let currentView = localStorage.getItem('nexoraTabletView') || 'classic';
  let wakeLock = null;

  function createDecks() {
    for (const deckId of deckIds) {
      const fragment = elements.deckTemplate.content.cloneNode(true);
      const card = fragment.querySelector('.deck-card');
      card.dataset.deck = deckId;
      card.querySelector('.deck-badge').textContent = deckId;
      card.querySelector('.platter-deck').textContent = `DECK ${deckId}`;
      elements.deckWorkspace.appendChild(fragment);
      const created = elements.deckWorkspace.lastElementChild;
      deckViews.set(deckId, buildDeckView(created, deckId));

      const channelFragment = elements.channelTemplate.content.cloneNode(true);
      const channel = channelFragment.querySelector('.channel-strip');
      channel.dataset.deck = deckId;
      channel.querySelector('header strong').textContent = `DECK ${deckId}`;
      elements.channelStrips.appendChild(channelFragment);
      channelViews.set(deckId, buildChannelView(elements.channelStrips.lastElementChild, deckId));
    }
  }

  function buildDeckView(card, deckId) {
    const query = selector => card.querySelector(selector);
    const queryAll = selector => [...card.querySelectorAll(selector)];
    const view = {
      card,
      deckId,
      title: query('.track-title'),
      artist: query('.track-artist'),
      bpm: query('.bpm-value'),
      platter: query('.platter'),
      platterMarker: query('.platter-marker'),
      platterState: query('.platter-state'),
      position: query('.position-time'),
      remaining: query('.remaining-time'),
      waveform: query('.waveform'),
      barBeat: query('.bar-beat'),
      beatCount: query('.beat-count'),
      targetBpm: query('.target-bpm'),
      tempoSlider: query('.tempo-slider'),
      tempoPercent: query('.tempo-percent'),
      play: query('.play-button'),
      cut: query('.cut-button'),
      cue: query('.cue-button'),
      setCue: query('.set-cue-button'),
      sync: query('.sync-button'),
      master: query('.master-button'),
      stop: query('.stop-button'),
      bpmDown: query('.bpm-down'),
      bpmUp: query('.bpm-up'),
      loopToggle: query('.loop-toggle'),
      loopButtons: queryAll('.loop-grid button[data-beats]'),
      hotCues: queryAll('.pad-grid button[data-cue]'),
      effectInputs: queryAll('[data-effect]'),
      eqInputs: queryAll('[data-eq]'),
      lastSnapshot: null,
      lastSnapshotAt: performance.now(),
      lastAngle: 0,
      jogPointerAngle: null,
      jogPointerId: null,
      waveZoom: 1,
      waveCenter: 0,
      wavePointers: new Map(),
      wavePinchDistance: 0,
      waveTouchMoved: false
    };

    view.play.addEventListener('click', () => sendCommand('deck.playpause', deckId));
    view.cut.addEventListener('pointerdown', event => {
      if (!view.lastSnapshot?.loaded) return;
      view.cut.setPointerCapture(event.pointerId);
      sendCommand('deck.cutdown', deckId);
      event.preventDefault();
    });
    const releaseCut = event => {
      if (!view.lastSnapshot?.loaded) return;
      sendCommand('deck.cutup', deckId);
      try { view.cut.releasePointerCapture(event.pointerId); } catch { }
      event.preventDefault();
    };
    view.cut.addEventListener('pointerup', releaseCut);
    view.cut.addEventListener('pointercancel', releaseCut);
    view.cut.addEventListener('lostpointercapture', () => sendCommand('deck.cutup', deckId));
    view.cue.addEventListener('click', () => sendCommand('deck.cue', deckId));
    view.setCue.addEventListener('click', () => sendCommand('deck.setcue', deckId));
    view.sync.addEventListener('click', () => sendCommand('deck.sync', deckId));
    view.master.addEventListener('click', () => sendCommand('deck.master', deckId));
    view.stop.addEventListener('click', () => sendCommand('deck.stop', deckId));
    view.loopToggle.addEventListener('click', () => sendCommand('deck.loop', deckId, null, currentLoopBeats(view)));
    view.bpmDown.addEventListener('click', () => adjustTargetBpm(view, -0.1));
    view.bpmUp.addEventListener('click', () => adjustTargetBpm(view, 0.1));

    bindRange(view.tempoSlider, value => sendCommandThrottled(`tempo-${deckId}`, 'deck.tempo', deckId, value, null, 35));

    for (const button of view.loopButtons) {
      button.addEventListener('click', () => {
        const beats = Number(button.dataset.beats || 8);
        view.loopButtons.forEach(item => item.classList.toggle('selected', item === button));
        sendCommand('deck.loopsize', deckId, null, beats);
      });
    }

    for (const button of view.hotCues) {
      button.addEventListener('click', () => sendCommand('deck.hotcue', deckId, null, Number(button.dataset.cue)));
    }

    for (const input of view.effectInputs) {
      bindRange(input, value => sendCommandThrottled(`fx-${deckId}-${input.dataset.effect}`, `deck.${input.dataset.effect}`, deckId, value, null, 35));
    }

    const eqAction = { low: 'deck.low', mid: 'deck.mid', high: 'deck.high', volume: 'deck.volume' };
    for (const input of view.eqInputs) {
      bindRange(input, value => sendCommandThrottled(`eq-${deckId}-${input.dataset.eq}`, eqAction[input.dataset.eq], deckId, value, null, 35));
    }

    for (const tab of queryAll('.performance-tab')) {
      tab.addEventListener('click', () => {
        queryAll('.performance-tab').forEach(item => item.classList.toggle('active', item === tab));
        queryAll('.performance-panel').forEach(panel => panel.classList.toggle('active', panel.classList.contains(`${tab.dataset.panel}-panel`)));
      });
    }

    view.waveform.addEventListener('wheel', event => {
      if (!view.lastSnapshot?.loaded) return;
      const rect = view.waveform.getBoundingClientRect();
      zoomWaveform(view, clamp((event.clientX - rect.left) / rect.width, 0, 1), event.deltaY < 0 ? 1.25 : 0.80);
      event.preventDefault();
    }, { passive: false });

    view.waveform.addEventListener('pointerdown', event => {
      if (!view.lastSnapshot?.loaded) return;
      view.waveform.setPointerCapture(event.pointerId);
      if (event.pointerType === 'touch') {
        view.wavePointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
        view.waveTouchMoved = false;
        if (view.wavePointers.size >= 2) view.wavePinchDistance = pointerDistance([...view.wavePointers.values()]);
        event.preventDefault();
        return;
      }
      seekWaveform(view, deckId, event.clientX);
      event.preventDefault();
    });
    view.waveform.addEventListener('pointermove', event => {
      if (!view.wavePointers.has(event.pointerId)) return;
      view.wavePointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
      if (view.wavePointers.size >= 2) {
        const points = [...view.wavePointers.values()];
        const distance = pointerDistance(points);
        if (view.wavePinchDistance > 0) zoomWaveform(view, 0.5, clamp(distance / view.wavePinchDistance, 0.75, 1.35));
        view.wavePinchDistance = distance;
        view.waveTouchMoved = true;
      }
      event.preventDefault();
    });
    const finishWaveTouch = event => {
      if (!view.wavePointers.has(event.pointerId)) return;
      const wasTap = view.wavePointers.size === 1 && !view.waveTouchMoved;
      view.wavePointers.delete(event.pointerId);
      view.wavePinchDistance = 0;
      if (wasTap) seekWaveform(view, deckId, event.clientX);
      try { view.waveform.releasePointerCapture(event.pointerId); } catch { }
      event.preventDefault();
    };
    view.waveform.addEventListener('pointerup', finishWaveTouch);
    view.waveform.addEventListener('pointercancel', finishWaveTouch);

    function seekWaveform(view, deckId, clientX) {
      const rect = view.waveform.getBoundingClientRect();
      const range = visibleWaveRange(view, view.lastSnapshot);
      const ratio = clamp((clientX - rect.left) / rect.width, 0, 1);
      sendCommand('deck.seek', deckId, range.start + ratio * range.duration);
    }

    function zoomWaveform(view, anchorRatio, factor) {
      const deck = view.lastSnapshot;
      if (!deck?.duration) return;
      const before = visibleWaveRange(view, deck);
      const anchor = before.start + anchorRatio * before.duration;
      view.waveZoom = clamp(view.waveZoom * factor, 1, 64);
      const afterDuration = deck.duration / view.waveZoom;
      view.waveCenter = clamp(anchor + (0.5 - anchorRatio) * afterDuration, afterDuration / 2, Math.max(afterDuration / 2, deck.duration - afterDuration / 2));
      drawWaveform(view.waveform, deck, view);
    }

    function pointerDistance(points) {
      if (points.length < 2) return 0;
      return Math.hypot(points[0].x - points[1].x, points[0].y - points[1].y);
    }

    const platter = view.platter;
    platter.addEventListener('pointerdown', event => {
      if (!view.lastSnapshot?.loaded) return;
      view.jogPointerId = event.pointerId;
      view.jogPointerAngle = pointerAngle(platter, event.clientX, event.clientY);
      platter.setPointerCapture(event.pointerId);
      event.preventDefault();
    });
    platter.addEventListener('pointermove', event => {
      if (view.jogPointerId !== event.pointerId || view.jogPointerAngle === null) return;
      const angle = pointerAngle(platter, event.clientX, event.clientY);
      let delta = angle - view.jogPointerAngle;
      if (delta > 180) delta -= 360;
      if (delta < -180) delta += 360;
      view.jogPointerAngle = angle;
      const seconds = clamp((delta / 360) * 1.8, -0.18, 0.18);
      sendCommandThrottled(`jog-${deckId}`, 'deck.seekrelative', deckId, seconds, null, 28);
      event.preventDefault();
    });
    const finishJog = event => {
      if (view.jogPointerId === event.pointerId) {
        view.jogPointerId = null;
        view.jogPointerAngle = null;
      }
    };
    platter.addEventListener('pointerup', finishJog);
    platter.addEventListener('pointercancel', finishJog);

    return view;
  }

  function buildChannelView(element, deckId) {
    const query = selector => element.querySelector(selector);
    const view = {
      element,
      bpm: query('.channel-bpm'),
      meter: query('.meter-fill'),
      inputs: [...element.querySelectorAll('[data-channel]')],
      cue: query('.channel-cue'),
      sync: query('.channel-sync'),
      master: query('.channel-master')
    };
    const actionMap = { volume: 'deck.volume', high: 'deck.high', mid: 'deck.mid', low: 'deck.low', filter: 'deck.filter' };
    for (const input of view.inputs) {
      bindRange(input, value => sendCommandThrottled(`channel-${deckId}-${input.dataset.channel}`, actionMap[input.dataset.channel], deckId, value, null, 35));
    }
    view.cue.addEventListener('click', () => sendCommand('deck.cuemonitor', deckId));
    view.sync.addEventListener('click', () => sendCommand('deck.sync', deckId));
    view.master.addEventListener('click', () => sendCommand('deck.master', deckId));
    return view;
  }

  function bindRange(input, handler) {
    input.addEventListener('pointerdown', () => activeInputs.add(input));
    input.addEventListener('pointerup', () => activeInputs.delete(input));
    input.addEventListener('pointercancel', () => activeInputs.delete(input));
    input.addEventListener('change', () => activeInputs.delete(input));
    input.addEventListener('input', () => handler(Number(input.value)));
  }

  function currentLoopBeats(view) {
    return Number(view.loopButtons.find(button => button.classList.contains('selected'))?.dataset.beats || 8);
  }

  function adjustTargetBpm(view, delta) {
    const current = Number(view.lastSnapshot?.effectiveBpm || view.lastSnapshot?.bpm || 120);
    sendCommand('deck.targetbpm', view.deckId, Math.round((current + delta) * 100) / 100);
  }

  function pointerAngle(element, clientX, clientY) {
    const rect = element.getBoundingClientRect();
    return Math.atan2(clientY - (rect.top + rect.height / 2), clientX - (rect.left + rect.width / 2)) * 180 / Math.PI;
  }

  async function validateToken(pairingCode) {
    const response = await fetch(`/api/state?token=${encodeURIComponent(pairingCode)}`, { cache: 'no-store' });
    if (!response.ok) throw new Error(response.status === 401 ? 'Codice non valido.' : `Errore server ${response.status}.`);
    return response.json();
  }

  async function connect(pairingCode) {
    token = pairingCode.trim();
    if (!/^\d{6}$/.test(token)) {
      elements.pairError.textContent = 'Inserisci il codice a 6 cifre mostrato sul PC.';
      return;
    }

    elements.connectButton.disabled = true;
    elements.pairError.textContent = '';
    try {
      const initial = await validateToken(token);
      localStorage.setItem('nexoraPairingCode', token);
      elements.pairing.classList.add('hidden');
      elements.app.classList.remove('hidden');
      applyState(initial);
      openSocket();
      requestWakeLock();
    } catch (error) {
      elements.pairError.textContent = error instanceof Error ? error.message : 'Connessione non riuscita.';
      setConnectionState('disconnected', 'PC non raggiungibile');
    } finally {
      elements.connectButton.disabled = false;
    }
  }

  function openSocket() {
    clearTimeout(reconnectTimer);
    if (socket && (socket.readyState === WebSocket.OPEN || socket.readyState === WebSocket.CONNECTING)) return;
    const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
    socket = new WebSocket(`${protocol}//${location.host}/ws?token=${encodeURIComponent(token)}`);
    setConnectionState('', 'Connessione…');

    socket.addEventListener('open', () => {
      reconnectAttempt = 0;
      setConnectionState('connected', 'Connesso al motore PC');
      elements.footerStatus.textContent = 'Console pronta';
    });
    socket.addEventListener('message', event => {
      try {
        const snapshot = JSON.parse(event.data);
        applyState(snapshot);
      } catch (error) {
        console.error('Snapshot non valido', error);
      }
    });
    socket.addEventListener('close', () => scheduleReconnect());
    socket.addEventListener('error', () => scheduleReconnect());
  }

  function scheduleReconnect() {
    if (!token || elements.app.classList.contains('hidden')) return;
    setConnectionState('disconnected', 'Connessione persa · nuovo tentativo');
    clearTimeout(reconnectTimer);
    const delay = Math.min(6000, 600 * Math.pow(1.65, reconnectAttempt++));
    reconnectTimer = setTimeout(openSocket, delay);
  }

  function setConnectionState(cssClass, text) {
    elements.connectionDot.className = `status-dot ${cssClass}`.trim();
    elements.connectionText.textContent = text;
  }

  function sendCommand(action, deck = null, value = null, intValue = null, text = null) {
    const payload = JSON.stringify({ action, deck, value, intValue, text });
    if (socket?.readyState === WebSocket.OPEN) {
      socket.send(payload);
      return;
    }
    fetch(`/api/command?token=${encodeURIComponent(token)}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: payload
    }).catch(() => scheduleReconnect());
  }

  function sendCommandThrottled(key, action, deck, value, intValue, intervalMs) {
    const previous = commandThrottle.get(key);
    if (previous) {
      previous.value = value;
      previous.intValue = intValue;
      return;
    }
    sendCommand(action, deck, value, intValue);
    const holder = { value, intValue };
    commandThrottle.set(key, holder);
    setTimeout(() => {
      const latest = commandThrottle.get(key);
      commandThrottle.delete(key);
      if (latest && (latest.value !== value || latest.intValue !== intValue))
        sendCommand(action, deck, latest.value, latest.intValue);
    }, intervalMs);
  }

  function applyState(snapshot) {
    state = snapshot;
    lastSnapshotReceivedAt = performance.now();
    elements.masterDeck.textContent = snapshot.masterDeck || '—';
    elements.globalStatus.textContent = snapshot.status || 'Pronto';
    elements.syncHealth.textContent = snapshot.syncHealth || '—';
    elements.masterPeak.textContent = snapshot.masterPeak || '—';
    elements.clipping.textContent = snapshot.clipping || '0';
    elements.recordButton.classList.toggle('active', Boolean(snapshot.recording));
    elements.recordButton.textContent = snapshot.recording ? 'STOP REC' : 'REGISTRA';
    elements.masterCueButton.classList.toggle('active', Boolean(snapshot.masterCue));
    elements.masterCueButton.textContent = snapshot.masterCue ? 'MASTER CUE ON' : 'MASTER CUE';
    if (!activeInputs.has(elements.cueVolume)) elements.cueVolume.value = String(snapshot.cueVolume ?? 0.75);
    elements.mashupButton.classList.toggle('active', Boolean(snapshot.mashupRunning));
    elements.mashupButton.textContent = snapshot.mashupRunning ? 'STOP MASHUP' : 'AUTO MASHUP';
    elements.footerStatus.textContent = snapshot.status || 'Pronto';
    if (!activeInputs.has(elements.crossfader)) elements.crossfader.value = String(snapshot.crossfader ?? 0);
    if (!activeInputs.has(elements.quickCrossfader)) elements.quickCrossfader.value = String(snapshot.crossfader ?? 0);

    for (const deck of snapshot.decks || []) {
      updateDeckView(deckViews.get(deck.id), deck);
      updateChannelView(channelViews.get(deck.id), deck);
    }
  }

  function updateDeckView(view, deck) {
    if (!view) return;
    view.lastSnapshot = deck;
    view.lastSnapshotAt = performance.now();
    view.title.textContent = deck.title || 'Deck vuoto';
    view.artist.textContent = deck.artist || `DECK ${deck.id}`;
    view.bpm.textContent = deck.effectiveBpm > 0 ? deck.effectiveBpm.toFixed(2) : '—';
    view.targetBpm.textContent = deck.effectiveBpm > 0 ? deck.effectiveBpm.toFixed(2) : '—';
    view.position.textContent = formatTime(deck.position, true);
    view.remaining.textContent = `-${formatTime(Math.max(0, deck.duration - deck.position), true)}`;
    view.barBeat.textContent = `MISURA ${deck.bar || 0} · BATTUTA ${deck.beatInBar || 0}/4`;
    view.beatCount.textContent = `BEAT ${deck.beat || 0}`;
    view.tempoPercent.textContent = `${formatSigned(deck.tempoPercent, 1)}%`;
    view.platter.classList.toggle('playing', Boolean(deck.playing));
    view.platterState.textContent = deck.playing ? 'RUNNING' : deck.loaded ? 'PAUSED' : 'EMPTY';
    view.play.textContent = deck.playing ? 'PAUSA' : 'PLAY';
    view.sync.classList.toggle('active', Boolean(deck.sync));
    view.sync.textContent = deck.sync ? 'SYNC ON' : 'SYNC';
    view.master.classList.toggle('active', Boolean(deck.master));
    view.master.textContent = deck.master ? 'MASTER' : 'MASTER';
    view.card.classList.toggle('not-loaded', !deck.loaded);
    if (deck.id === 'A') {
      elements.quickCueA.classList.toggle('active', Boolean(deck.cue));
      elements.quickCueA.textContent = deck.cue ? 'CUE A ON' : 'CUE A';
      elements.quickCueA.disabled = !deck.loaded;
    }
    if (deck.id === 'B') {
      elements.quickCueB.classList.toggle('active', Boolean(deck.cue));
      elements.quickCueB.textContent = deck.cue ? 'CUE B ON' : 'CUE B';
      elements.quickCueB.disabled = !deck.loaded;
    }

    const controls = [view.play, view.cut, view.cue, view.setCue, view.sync, view.master, view.stop, view.loopToggle, view.bpmDown, view.bpmUp, ...view.loopButtons, ...view.hotCues, ...view.effectInputs, ...view.eqInputs];
    controls.forEach(control => { control.disabled = !deck.loaded; });

    updateRange(view.tempoSlider, deck.tempoPercent);
    const effectValues = {
      filter: deck.filter,
      echo: deck.echo,
      crush: deck.crush,
      saturation: deck.saturation,
      gate: deck.gate,
      compressor: deck.compressor,
      roll: deck.roll,
      brake: deck.brake
    };
    for (const input of view.effectInputs) updateRange(input, effectValues[input.dataset.effect] ?? 0);
    const eqValues = { low: deck.lowEq, mid: deck.midEq, high: deck.highEq, volume: deck.volume };
    for (const input of view.eqInputs) updateRange(input, eqValues[input.dataset.eq] ?? 0);
    view.loopButtons.forEach(button => button.classList.toggle('selected', Number(button.dataset.beats) === deck.loopBeats));
    drawWaveform(view.waveform, deck, view);
  }

  function updateChannelView(view, deck) {
    if (!view) return;
    view.bpm.textContent = deck.effectiveBpm > 0 ? `${deck.effectiveBpm.toFixed(1)} BPM` : '— BPM';
    view.meter.style.width = `${clamp(deck.meter * 100, 0, 100)}%`;
    const values = { volume: deck.volume, high: deck.highEq, mid: deck.midEq, low: deck.lowEq, filter: deck.filter };
    for (const input of view.inputs) updateRange(input, values[input.dataset.channel] ?? 0);
    view.sync.classList.toggle('active', Boolean(deck.sync));
    view.cue.classList.toggle('active', Boolean(deck.cue));
    view.cue.textContent = deck.cue ? 'CUE ON' : 'CUE';
    view.master.classList.toggle('active', Boolean(deck.master));
  }

  function updateRange(input, value) {
    if (!activeInputs.has(input) && Number.isFinite(value)) input.value = String(value);
  }

  function drawWaveform(canvas, deck, view = null) {
    const rect = canvas.getBoundingClientRect();
    const width = Math.max(1, Math.floor(rect.width * devicePixelRatio));
    const height = Math.max(1, Math.floor(rect.height * devicePixelRatio));
    if (canvas.width !== width || canvas.height !== height) {
      canvas.width = width;
      canvas.height = height;
    }
    const context = canvas.getContext('2d');
    context.clearRect(0, 0, width, height);
    context.fillStyle = '#060a10';
    context.fillRect(0, 0, width, height);

    const range = view ? visibleWaveRange(view, deck) : { start: 0, duration: Math.max(0, deck.duration || 0) };
    const samples = Array.isArray(deck.waveform) ? deck.waveform : [];
    const mid = height / 2;
    drawBeatGrid(context, deck, range, width, height);
    if (samples.length > 0) {
      context.strokeStyle = deck.id === 'B' || deck.id === 'D' ? '#9b84ff' : '#25e5c4';
      context.lineWidth = Math.max(1, devicePixelRatio);
      context.beginPath();
      const firstSample = deck.duration > 0 ? Math.max(0, Math.floor(range.start / deck.duration * samples.length)) : 0;
      const lastSample = deck.duration > 0 ? Math.min(samples.length, Math.ceil((range.start + range.duration) / deck.duration * samples.length)) : samples.length;
      for (let x = 0; x < width; x++) {
        const index = Math.min(samples.length - 1, firstSample + Math.floor(x / width * Math.max(1, lastSample - firstSample)));
        const amplitude = clamp(Number(samples[index] || 0), 0, 1) * mid * .88;
        context.moveTo(x, mid - amplitude);
        context.lineTo(x, mid + amplitude);
      }
      context.stroke();
    }

    context.strokeStyle = 'rgba(255,255,255,.15)';
    context.lineWidth = 1;
    context.beginPath();
    context.moveTo(0, mid);
    context.lineTo(width, mid);
    context.stroke();

    const progress = range.duration > 0 ? clamp((deck.position - range.start) / range.duration, 0, 1) : 0;
    context.fillStyle = 'rgba(255,255,255,.08)';
    context.fillRect(0, 0, progress * width, height);
    context.strokeStyle = '#ffffff';
    context.lineWidth = Math.max(2, 2 * devicePixelRatio);
    context.beginPath();
    context.moveTo(progress * width, 0);
    context.lineTo(progress * width, height);
    context.stroke();
  }

  function visibleWaveRange(view, deck) {
    const duration = Math.max(0, deck?.duration || 0);
    if (duration <= 0 || view.waveZoom <= 1.001) return { start: 0, duration };
    const visibleDuration = Math.max(0.05, duration / view.waveZoom);
    if (!view.waveCenter || view.waveCenter > duration) view.waveCenter = clamp(deck.position || 0, 0, duration);
    if (Math.abs((deck.position || 0) - view.waveCenter) > visibleDuration * 0.55)
      view.waveCenter = deck.position || view.waveCenter;
    view.waveCenter = clamp(view.waveCenter, visibleDuration / 2, Math.max(visibleDuration / 2, duration - visibleDuration / 2));
    return {
      start: clamp(view.waveCenter - visibleDuration / 2, 0, Math.max(0, duration - visibleDuration)),
      duration: visibleDuration
    };
  }

  function drawBeatGrid(context, deck, range, width, height) {
    if (!deck?.bpm || !range.duration) return;
    const beatLength = 60 / Math.max(1, deck.bpm);
    const beatOffset = Number(deck.beatOffset || 0);
    const beatsPerBar = Math.max(1, Number(deck.beatsPerBar || 4));
    const firstBeat = Math.ceil((range.start - beatOffset) / beatLength);
    for (let beat = firstBeat; ; beat++) {
      const time = beatOffset + beat * beatLength;
      if (time > range.start + range.duration) break;
      if (time < 0) continue;
      const x = (time - range.start) / range.duration * width;
      const strong = positiveModulo(beat, beatsPerBar) === 0;
      context.strokeStyle = strong ? 'rgba(155,132,255,.65)' : 'rgba(155,132,255,.23)';
      context.lineWidth = strong ? 2 : 1;
      context.beginPath();
      context.moveTo(x, 0);
      context.lineTo(x, height);
      context.stroke();
      if (strong && beat >= 0 && beatLength * beatsPerBar / range.duration * width > 44) {
        context.fillStyle = 'rgba(247,251,255,.72)';
        context.font = `${10 * devicePixelRatio}px Segoe UI`;
        context.fillText(`M${Math.floor(beat / beatsPerBar) + 1}`, Math.min(width - 36, x + 4), height - 8);
      }
    }
  }

  function positiveModulo(value, modulo) {
    const result = value % modulo;
    return result < 0 ? result + modulo : result;
  }

  function renderAnimation(now) {
    for (const view of deckViews.values()) {
      const deck = view.lastSnapshot;
      if (!deck) continue;
      let position = deck.position || 0;
      if (deck.playing) {
        const elapsed = Math.max(0, (now - view.lastSnapshotAt) / 1000);
        position += elapsed * Math.max(.1, (deck.effectiveBpm > 0 && deck.bpm > 0) ? deck.effectiveBpm / deck.bpm : 1);
      }
      if (deck.duration > 0) position = Math.min(position, deck.duration);
      const ratio = deck.bpm > 0 && deck.effectiveBpm > 0 ? clamp(deck.effectiveBpm / deck.bpm, .5, 2) : 1;
      const angle = (position * 200 * ratio) % 360;
      view.lastAngle = angle;
      const radius = Math.max(34, view.platter.clientWidth * .40);
      view.platterMarker.style.transform = `translate(-50%, -50%) rotate(${angle}deg) translateY(-${radius}px)`;
      if (deck.playing) {
        view.position.textContent = formatTime(position, true);
        view.remaining.textContent = `-${formatTime(Math.max(0, deck.duration - position), true)}`;
      }
    }
    elements.latencyStatus.textContent = `Ultimo stato ${Math.round(now - lastSnapshotReceivedAt)} ms fa`;
    requestAnimationFrame(renderAnimation);
  }

  function setView(viewName) {
    currentView = viewName;
    localStorage.setItem('nexoraTabletView', viewName);
    elements.viewClassic.classList.toggle('active', viewName === 'classic');
    elements.viewFour.classList.toggle('active', viewName === 'four');
    elements.viewMixer.classList.toggle('active', viewName === 'mixer');
    elements.deckWorkspace.classList.toggle('mixer-only', viewName === 'mixer');
    elements.deckWorkspace.classList.toggle('four-mode', viewName === 'four');
    elements.deckWorkspace.classList.toggle('classic-mode', viewName === 'classic');
    elements.mixerWorkspace.classList.toggle('hidden-mixer', viewName !== 'mixer');
    elements.quickMixer.classList.toggle('hidden', viewName === 'mixer');
    for (const [deckId, view] of deckViews) {
      const hide = viewName === 'classic' && (deckId === 'C' || deckId === 'D');
      view.card.classList.toggle('hidden-deck', hide);
    }
    if (viewName === 'mixer') elements.mixerWorkspace.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  async function requestWakeLock() {
    try {
      if ('wakeLock' in navigator) wakeLock = await navigator.wakeLock.request('screen');
    } catch {
      wakeLock = null;
    }
  }

  function formatTime(seconds, tenths = false) {
    if (!Number.isFinite(seconds) || seconds < 0) seconds = 0;
    const minutes = Math.floor(seconds / 60);
    const whole = Math.floor(seconds % 60);
    if (!tenths) return `${minutes}:${String(whole).padStart(2, '0')}`;
    const tenth = Math.floor((seconds - Math.floor(seconds)) * 10);
    return `${minutes}:${String(whole).padStart(2, '0')}.${tenth}`;
  }

  function formatSigned(value, digits) {
    if (!Number.isFinite(value)) return '0.0';
    return `${value > 0 ? '+' : ''}${value.toFixed(digits)}`;
  }

  function clamp(value, min, max) { return Math.min(max, Math.max(min, value)); }

  function isConsoleControl(target) {
    return Boolean(target?.closest?.('.app-shell button, .deck-card, .mixer-workspace, .quick-mixer, .platter, .waveform'));
  }

  document.addEventListener('contextmenu', event => {
    if (isConsoleControl(event.target)) event.preventDefault();
  });
  document.addEventListener('selectstart', event => {
    if (isConsoleControl(event.target)) event.preventDefault();
  });

  elements.connectButton.addEventListener('click', () => connect(elements.pairCode.value));
  elements.pairCode.addEventListener('keydown', event => { if (event.key === 'Enter') connect(elements.pairCode.value); });
  elements.disconnectButton.addEventListener('click', () => {
    localStorage.removeItem('nexoraPairingCode');
    token = '';
    if (socket) socket.close();
    socket = null;
    elements.app.classList.add('hidden');
    elements.pairing.classList.remove('hidden');
    elements.pairCode.value = '';
  });
  elements.viewClassic.addEventListener('click', () => setView('classic'));
  elements.viewFour.addEventListener('click', () => setView('four'));
  elements.viewMixer.addEventListener('click', () => setView('mixer'));
  elements.fullscreenButton.addEventListener('click', async () => {
    try {
      if (!document.fullscreenElement) await document.documentElement.requestFullscreen();
      else await document.exitFullscreen();
    } catch { }
  });
  elements.recordButton.addEventListener('click', () => sendCommand('global.record'));
  elements.masterCueButton.addEventListener('click', () => sendCommand('global.mastercue'));
  bindRange(elements.cueVolume, value => sendCommandThrottled('cue-volume', 'global.cuevolume', null, value, null, 35));
  elements.mashupButton.addEventListener('click', () => sendCommand(state?.mashupRunning ? 'global.stopmashup' : 'global.mashup'));
  elements.stopAllButton.addEventListener('click', () => sendCommand('global.stopall'));
  elements.quickCueA.addEventListener('click', () => sendCommand('deck.cuemonitor', 'A'));
  elements.quickCueB.addEventListener('click', () => sendCommand('deck.cuemonitor', 'B'));
  elements.quickCenter.addEventListener('click', () => sendCommand('global.center'));
  bindRange(elements.quickCrossfader, value => sendCommandThrottled('quick-crossfader', 'global.crossfader', null, value, null, 30));
  elements.centerCrossfaderButton.addEventListener('click', () => sendCommand('global.center'));
  bindRange(elements.crossfader, value => sendCommandThrottled('crossfader', 'global.crossfader', null, value, null, 30));
  document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'visible') requestWakeLock(); });

  createDecks();
  setView(currentView);
  requestAnimationFrame(renderAnimation);

  if ('serviceWorker' in navigator) {
    navigator.serviceWorker.register('sw.js').catch(() => { /* HTTP LAN può non essere un secure context. */ });
  }

  const urlPairingCode = new URLSearchParams(location.search).get('pair') || '';
  if (/^\d{6}$/.test(urlPairingCode)) {
    elements.pairCode.value = urlPairingCode;
    history.replaceState(null, '', location.pathname);
    connect(urlPairingCode);
  } else if (token) {
    elements.pairCode.value = token;
    connect(token);
  }
})();
