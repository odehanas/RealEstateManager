// The AI assistant panel.
//
// The browser talks to OpenAI's realtime model directly over WebRTC, with a short-lived
// key the server mints for this user. When the model wants data or to prepare a change,
// the request comes back to our server (/Assistant/Tool), which checks the user's own
// sign-in, company and permissions before anything is read. Changes are only ever
// proposals: the user confirms them by clicking, never by voice.
(function () {
    'use strict';

    var panel = document.getElementById('assistantPanel');
    if (!panel) { return; }

    var log = document.getElementById('assistantLog');
    var statusLine = document.getElementById('assistantStatus');
    var form = document.getElementById('assistantForm');
    var input = document.getElementById('assistantInput');
    var micButton = document.getElementById('assistantMic');
    var muteButton = document.getElementById('assistantMute');
    var endButton = document.getElementById('assistantEnd');
    var screen = (panel.getAttribute('data-controller') || '') + '/' + (panel.getAttribute('data-action') || '');

    var session = null;     // { id, mode, pc, dc, mic, audio, timer }
    var starting = null;    // promise while a session is being set up
    var current = null;     // the assistant bubble being written
    var queuedText = [];    // typed while the connection was opening

    function token() {
        var field = document.querySelector('input[name="__RequestVerificationToken"]');
        return field ? field.value : '';
    }

    function post(url, body) {
        return fetch(url, {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() },
            body: body === undefined ? '{}' : JSON.stringify(body)
        }).then(function (response) {
            if (response.redirected || (response.headers.get('content-type') || '').indexOf('json') < 0) {
                throw new Error('Your session or subscription no longer allows the assistant. Reload the page.');
            }
            return response.json().then(function (data) {
                if (!response.ok) { throw new Error(data.error || ('Error ' + response.status)); }
                return data;
            });
        });
    }

    // ------------------------------------------------------------------ display

    function setStatus(text) { statusLine.textContent = text; }

    function scroll() { log.scrollTop = log.scrollHeight; }

    function bubble(role, text) {
        var hint = log.querySelector('.assistant-hint');
        if (hint) { hint.remove(); }
        var div = document.createElement('div');
        div.className = 'assistant-msg assistant-msg-' + role;
        div.textContent = text || '';
        log.appendChild(div);
        scroll();
        return div;
    }

    function note(text, kind) {
        var div = bubble('note', text);
        if (kind) { div.classList.add('assistant-note-' + kind); }
        return div;
    }

    function actionCard(action) {
        var card = document.createElement('div');
        card.className = 'assistant-card';
        var title = document.createElement('div');
        title.className = 'assistant-card-title';
        title.textContent = 'Please confirm';
        var summary = document.createElement('div');
        summary.textContent = action.summary;
        card.appendChild(title);
        card.appendChild(summary);

        (action.warnings || []).forEach(function (w) {
            var warn = document.createElement('div');
            warn.className = 'assistant-card-warning';
            warn.textContent = w;
            card.appendChild(warn);
        });

        var buttons = document.createElement('div');
        buttons.className = 'assistant-card-buttons';
        var confirm = document.createElement('button');
        confirm.type = 'button';
        confirm.className = 'btn btn-sm btn-success';
        confirm.textContent = 'Confirm';
        var cancel = document.createElement('button');
        cancel.type = 'button';
        cancel.className = 'btn btn-sm btn-outline-secondary';
        cancel.textContent = 'Cancel';
        buttons.appendChild(confirm);
        buttons.appendChild(cancel);
        card.appendChild(buttons);

        confirm.addEventListener('click', function () {
            confirm.disabled = cancel.disabled = true;
            confirm.textContent = 'Saving…';
            post('/Assistant/Confirm/' + action.id).then(function (result) {
                buttons.remove();
                showResult(card, result);
                tellModel('The user clicked Confirm on "' + action.summary + '". Result: ' +
                    (result.ok ? 'done' : 'failed') + ' - ' + result.message +
                    (result.reference ? ' Reference: ' + result.reference + '.' : '') +
                    (result.journalEntryNumber ? ' Journal entry: ' + result.journalEntryNumber + '.' : ''));
            }).catch(function (error) {
                confirm.disabled = cancel.disabled = false;
                confirm.textContent = 'Confirm';
                note(error.message, 'error');
            });
        });

        cancel.addEventListener('click', function () {
            confirm.disabled = cancel.disabled = true;
            post('/Assistant/Cancel/' + action.id).finally(function () {
                buttons.remove();
                var done = document.createElement('div');
                done.className = 'assistant-card-result text-muted';
                done.textContent = 'Cancelled - nothing was saved.';
                card.appendChild(done);
                tellModel('The user clicked Cancel on "' + action.summary + '". Nothing was saved.');
            });
        });

        log.appendChild(card);
        scroll();
    }

    function showResult(card, result) {
        var done = document.createElement('div');
        done.className = 'assistant-card-result ' + (result.ok ? 'text-success' : 'text-danger');
        done.textContent = result.message;
        card.appendChild(done);
        if (result.ok && result.link) {
            var link = document.createElement('a');
            link.href = result.link;
            link.textContent = 'Open ' + (result.reference || 'record');
            link.className = 'd-block small';
            card.appendChild(link);
        }
        scroll();
    }

    // ------------------------------------------------------------------ session

    function ensureSession(mode) {
        if (session && (mode !== 'voice' || session.mode === 'voice')) { return Promise.resolve(session); }
        if (starting) { return starting; }

        var restart = session ? endSession(true) : Promise.resolve();
        starting = restart.then(function () { return startSession(mode); })
            .finally(function () { starting = null; });
        return starting;
    }

    function startSession(mode) {
        setStatus('Connecting…');
        var micStream = null;

        var getMic = mode === 'voice'
            ? (navigator.mediaDevices && navigator.mediaDevices.getUserMedia
                ? navigator.mediaDevices.getUserMedia({ audio: true })
                : Promise.reject(new Error('Voice needs a secure (https) connection. You can still type.')))
            : Promise.resolve(null);

        return getMic.then(function (stream) {
            micStream = stream;
            return post('/Assistant/Session', { mode: mode, screen: screen });
        }).then(function (info) {
            var pc = new RTCPeerConnection();
            var audio = null;
            if (mode === 'voice') {
                audio = document.createElement('audio');
                audio.autoplay = true;
                pc.ontrack = function (event) { audio.srcObject = event.streams[0]; };
                micStream.getTracks().forEach(function (track) { pc.addTrack(track, micStream); });
            } else {
                pc.addTransceiver('audio', { direction: 'recvonly' });
            }

            var dc = pc.createDataChannel('oai-events');
            var s = { id: info.sessionId, mode: mode, pc: pc, dc: dc, mic: micStream, audio: audio, timer: null, muted: false };

            dc.addEventListener('message', function (event) {
                try { handleEvent(s, JSON.parse(event.data)); } catch (e) { /* ignore malformed */ }
            });
            pc.addEventListener('connectionstatechange', function () {
                if ((pc.connectionState === 'failed' || pc.connectionState === 'disconnected') && session === s) {
                    note('The connection to the assistant dropped. Send a message to reconnect.', 'error');
                    endSession(true);
                }
            });

            return pc.createOffer().then(function (offer) {
                return pc.setLocalDescription(offer).then(function () { return offer; });
            }).then(function (offer) {
                return fetch('https://api.openai.com/v1/realtime/calls', {
                    method: 'POST',
                    body: offer.sdp,
                    headers: { 'Authorization': 'Bearer ' + info.clientSecret, 'Content-Type': 'application/sdp' }
                });
            }).then(function (response) {
                if (!response.ok) { throw new Error('OpenAI did not accept the connection (' + response.status + ').'); }
                return response.text();
            }).then(function (sdp) {
                return pc.setRemoteDescription({ type: 'answer', sdp: sdp });
            }).then(function () {
                return new Promise(function (resolve, reject) {
                    if (dc.readyState === 'open') { resolve(); return; }
                    dc.addEventListener('open', resolve, { once: true });
                    setTimeout(function () { reject(new Error('The assistant did not answer in time.')); }, 15000);
                });
            }).then(function () {
                session = s;
                s.timer = setTimeout(function () {
                    note('Sessions last ' + info.maxMinutes + ' minutes. Send a message or tap the microphone to start a new one.');
                    endSession(true);
                }, info.maxMinutes * 60000);
                endButton.hidden = false;
                muteButton.hidden = mode !== 'voice';
                micButton.classList.toggle('active', mode === 'voice');
                micButton.setAttribute('aria-pressed', mode === 'voice' ? 'true' : 'false');
                setStatus(mode === 'voice' ? 'Listening… speak naturally.' : 'Connected. Type your question.');
                while (queuedText.length) { sendText(queuedText.shift()); }
                return s;
            }).catch(function (error) {
                pc.close();
                throw error;
            });
        }).catch(function (error) {
            if (micStream) { micStream.getTracks().forEach(function (t) { t.stop(); }); }
            var message = error && error.name === 'NotAllowedError'
                ? 'Microphone permission was refused. You can still type your questions.'
                : (error.message || 'The assistant could not start.');
            setStatus(message);
            note(message, 'error');
            throw error;
        });
    }

    function endSession(silent) {
        var s = session;
        session = null;
        current = null;
        if (!s) { return Promise.resolve(); }
        clearTimeout(s.timer);
        try { s.dc.close(); } catch (e) { /* already closed */ }
        try { s.pc.close(); } catch (e) { /* already closed */ }
        if (s.mic) { s.mic.getTracks().forEach(function (t) { t.stop(); }); }
        if (s.audio) { s.audio.srcObject = null; }
        endButton.hidden = true;
        muteButton.hidden = true;
        micButton.classList.remove('active');
        micButton.setAttribute('aria-pressed', 'false');
        if (!silent) { setStatus('Session ended. Ask again any time.'); }
        return post('/Assistant/End', { sessionId: s.id }).catch(function () { /* best effort */ });
    }

    function send(event) {
        if (session && session.dc.readyState === 'open') { session.dc.send(JSON.stringify(event)); }
    }

    function sendText(text) {
        send({ type: 'conversation.item.create', item: { type: 'message', role: 'user', content: [{ type: 'input_text', text: text }] } });
        send({ type: 'response.create' });
        post('/Assistant/Log', { sessionId: session.id, role: 'user', text: text }).catch(function () { });
    }

    // Results of the user's clicks go back to the model so it can tell them.
    function tellModel(text) {
        if (!session) { return; }
        send({ type: 'conversation.item.create', item: { type: 'message', role: 'system', content: [{ type: 'input_text', text: text }] } });
        send({ type: 'response.create' });
    }

    // ------------------------------------------------------------------ model events

    function handleEvent(s, event) {
        switch (event.type) {
            case 'input_audio_buffer.speech_started':
                setStatus('Listening…');
                break;
            case 'conversation.item.input_audio_transcription.completed':
                if (event.transcript && event.transcript.trim()) {
                    bubble('user', event.transcript.trim());
                    post('/Assistant/Log', { sessionId: s.id, role: 'user', text: event.transcript }).catch(function () { });
                }
                break;
            case 'response.created':
                setStatus('Thinking…');
                current = null;
                break;
            case 'response.output_text.delta':
            case 'response.output_audio_transcript.delta':
                if (!current) { current = bubble('assistant', ''); }
                current.textContent += event.delta || '';
                if (s.mode === 'voice') { setStatus('Speaking…'); }
                scroll();
                break;
            case 'response.output_text.done':
            case 'response.output_audio_transcript.done':
                if (current && current.textContent) {
                    post('/Assistant/Log', { sessionId: s.id, role: 'assistant', text: current.textContent }).catch(function () { });
                }
                current = null;
                break;
            case 'response.done':
                onResponseDone(s, event.response || {});
                break;
            case 'error':
                note((event.error && event.error.message) || 'The assistant reported an error.', 'error');
                break;
        }
    }

    function onResponseDone(s, response) {
        if (response.usage) {
            post('/Assistant/Usage', { sessionId: s.id, usage: response.usage }).catch(function () { });
        }

        var calls = (response.output || []).filter(function (item) { return item.type === 'function_call'; });
        if (!calls.length) {
            setStatus(s.mode === 'voice' ? 'Listening… speak naturally.' : 'Ready.');
            return;
        }

        setStatus('Looking that up…');
        Promise.all(calls.map(function (call) {
            return post('/Assistant/Tool', { sessionId: s.id, name: call.name, arguments: call.arguments, screen: screen })
                .then(function (result) {
                    if (result.pendingAction) { actionCard(result.pendingAction); }
                    return { call: call, output: result.output };
                })
                .catch(function (error) {
                    return { call: call, output: JSON.stringify({ error: error.message }) };
                });
        })).then(function (results) {
            if (session !== s) { return; }
            results.forEach(function (r) {
                send({ type: 'conversation.item.create', item: { type: 'function_call_output', call_id: r.call.call_id, output: r.output } });
            });
            send({ type: 'response.create' });
        });
    }

    // ------------------------------------------------------------------ controls

    form.addEventListener('submit', function (event) {
        event.preventDefault();
        var text = input.value.trim();
        if (!text) { return; }
        input.value = '';
        bubble('user', text);
        if (session) { sendText(text); return; }
        queuedText.push(text);
        ensureSession('text').catch(function () { queuedText = []; });
    });

    micButton.addEventListener('click', function () {
        if (session && session.mode === 'voice') { endSession(); return; }
        if (session) { note('Switching to voice starts a new conversation.'); }
        ensureSession('voice').catch(function () { });
    });

    muteButton.addEventListener('click', function () {
        if (!session || !session.mic) { return; }
        session.muted = !session.muted;
        session.mic.getAudioTracks().forEach(function (t) { t.enabled = !session.muted; });
        muteButton.textContent = session.muted ? 'Unmute' : 'Mute';
        setStatus(session.muted ? 'Microphone muted.' : 'Listening… speak naturally.');
    });

    endButton.addEventListener('click', function () { endSession(); });

    // Closing the panel ends the conversation, so the microphone is never left open.
    panel.addEventListener('hidden.bs.offcanvas', function () { endSession(true); });
    panel.addEventListener('shown.bs.offcanvas', function () { input.focus(); });
    // Leaving the page drops the connection; the server also ends sessions after their time limit.
    window.addEventListener('pagehide', function () {
        if (session) { session.pc.close(); }
    });
})();
