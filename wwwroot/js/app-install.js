/*
 * Installing the app and turning on notifications, for the three parts of the site:
 * staff (/), tenants (/portal/) and employees (/me/). Loaded by _AppHead on every page.
 *
 *  - Registers the service worker of this part of the site (see app-sw.js).
 *  - Fills the "App and notifications" panel (_AppInstallPanel) with what this device can do:
 *    install (Android, desktop), the iPhone's Add to Home Screen, turn notifications on or off.
 *  - Remembers that this device asked for notifications, re-attaches it to whoever is signed in,
 *    and forgets it on the server when they sign out.
 */
(function () {
    'use strict';

    var script = document.currentScript;
    var base = script.getAttribute('data-app-base') || '/';
    var pushKey = script.getAttribute('data-push-key') || '';
    var signedIn = script.getAttribute('data-signed-in') === 'true';
    var flagKey = 'rem-push:' + base;            // "on" once this person turned notifications on here
    var dismissKey = 'rem-app-panel-dismissed:' + base;

    var supportsWorker = 'serviceWorker' in navigator;
    var supportsPush = supportsWorker && 'PushManager' in window && 'Notification' in window;
    var isIos = /iphone|ipad|ipod/i.test(navigator.userAgent) ||
                (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
    var isInstalled = window.matchMedia('(display-mode: standalone)').matches || navigator.standalone === true;
    var installPrompt = null;
    var currentEndpoint = null;

    function store(key, value) {
        try { if (value === null) localStorage.removeItem(key); else localStorage.setItem(key, value); } catch (e) { /* private mode */ }
    }
    function read(key) {
        try { return localStorage.getItem(key); } catch (e) { return null; }
    }

    function token() {
        var field = document.querySelector('input[name="__RequestVerificationToken"]');
        return field ? field.value : '';
    }

    function keyBytes(text) {
        var padded = (text + '===='.slice((text.length + 3) % 4 + 1)).replace(/-/g, '+').replace(/_/g, '/');
        var raw = atob(padded);
        var bytes = new Uint8Array(raw.length);
        for (var i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
        return bytes;
    }

    function post(action, fields) {
        var data = new FormData();
        Object.keys(fields).forEach(function (k) { data.append(k, fields[k]); });
        data.append('__RequestVerificationToken', token());
        return fetch(base + 'push/' + action, { method: 'POST', body: data, credentials: 'same-origin' })
            .then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); });
    }

    function send(subscription) {
        var json = subscription.toJSON();
        currentEndpoint = json.endpoint;
        return post('subscribe', { endpoint: json.endpoint, p256dh: json.keys.p256dh, auth: json.keys.auth });
    }

    function registration() {
        return navigator.serviceWorker.ready;
    }

    // ---------------------------------------------------------------- the panel

    var panel = null;

    function show(state, visible) {
        var part = panel && panel.querySelector('[data-when="' + state + '"]');
        if (part) part.classList.toggle('d-none', !visible);
    }

    function refresh() {
        if (!panel) return Promise.resolve();
        var dismissible = panel.getAttribute('data-dismissible') === 'true';

        var canInstall = !!installPrompt && !isInstalled;
        var iosInstall = isIos && !isInstalled;
        // On iPhone, notifications are only available once the app is on the home screen.
        var pushPossible = signedIn && supportsPush && !!pushKey && !(isIos && !isInstalled);

        var check = pushPossible
            ? registration().then(function (r) { return r.pushManager.getSubscription(); })
            : Promise.resolve(null);

        return check.then(function (subscription) {
            var permission = pushPossible ? Notification.permission : 'default';
            var on = pushPossible && !!subscription && permission === 'granted' && read(flagKey) === 'on';
            var blocked = pushPossible && permission === 'denied';
            var off = pushPossible && !on && !blocked;

            show('install', canInstall);
            show('ios-install', iosInstall);
            show('push-off', off);
            show('push-on', on && !dismissible);
            show('push-blocked', blocked && !dismissible);

            var offers = canInstall || iosInstall || off;
            var visible = dismissible ? offers && read(dismissKey) !== 'yes' : offers || on || blocked;
            panel.classList.toggle('d-none', !visible);
        });
    }

    function fail() {
        var error = panel && panel.querySelector('[data-app-error]');
        if (error) error.classList.remove('d-none');
    }

    function turnOn() {
        return Notification.requestPermission().then(function (permission) {
            if (permission !== 'granted') return refresh();
            return registration()
                .then(function (r) {
                    return r.pushManager.getSubscription().then(function (existing) {
                        return existing || r.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: keyBytes(pushKey) });
                    });
                })
                .then(send)
                .then(function () { store(flagKey, 'on'); return refresh(); });
        }).catch(fail);
    }

    function turnOff() {
        return registration()
            .then(function (r) { return r.pushManager.getSubscription(); })
            .then(function (subscription) {
                if (!subscription) return null;
                return post('unsubscribe', { endpoint: subscription.endpoint })
                    .then(function () { return subscription.unsubscribe(); });
            })
            .then(function () { store(flagKey, null); return refresh(); })
            .catch(fail);
    }

    function wirePanel() {
        panel = document.querySelector('[data-app-panel]');
        if (!panel) return;

        panel.addEventListener('click', function (event) {
            var button = event.target.closest('[data-action]');
            if (!button) return;
            var action = button.getAttribute('data-action');

            if (action === 'install' && installPrompt) {
                installPrompt.prompt();
                installPrompt.userChoice.then(function () { installPrompt = null; refresh(); });
            } else if (action === 'push-on') {
                turnOn();
            } else if (action === 'push-off') {
                turnOff();
            } else if (action === 'dismiss') {
                store(dismissKey, 'yes');
                panel.classList.add('d-none');
            }
        });

        refresh();
    }

    // ---------------------------------------------------------------- start

    window.addEventListener('beforeinstallprompt', function (event) {
        event.preventDefault();          // offered from the panel instead of the browser's own banner
        installPrompt = event;
        refresh();
    });

    window.addEventListener('appinstalled', function () {
        installPrompt = null;
        isInstalled = true;
        refresh();
    });

    if (!supportsWorker) return;

    navigator.serviceWorker.register(base + 'sw.js', { scope: base }).catch(function () { /* the site works without it */ });

    document.addEventListener('DOMContentLoaded', function () {
        wirePanel();

        if (!signedIn || !supportsPush || !pushKey) return;

        // A device this person turned on: keep it attached to them (it may have been signed in
        // by someone else meanwhile, or the browser may have renewed its address).
        if (read(flagKey) === 'on' && Notification.permission === 'granted') {
            registration()
                .then(function (r) { return r.pushManager.getSubscription(); })
                .then(function (subscription) {
                    if (!subscription) { store(flagKey, null); return refresh(); }
                    currentEndpoint = subscription.endpoint;
                    if (sessionStorage.getItem(flagKey) === 'sent') return null;
                    return send(subscription).then(function () { sessionStorage.setItem(flagKey, 'sent'); });
                })
                .catch(function () { /* tried again on the next page */ });
        }

        // Signing out: the server forgets this device for them, so nothing more reaches it.
        document.addEventListener('submit', function (event) {
            var form = event.target;
            if (!currentEndpoint || !/logout/i.test(form.getAttribute('action') || '')) return;

            var data = new FormData();
            data.append('endpoint', currentEndpoint);
            data.append('__RequestVerificationToken', token());
            if (navigator.sendBeacon) navigator.sendBeacon(base + 'push/unsubscribe', data);
            store(flagKey, null);
            try { sessionStorage.removeItem(flagKey); } catch (e) { /* ignore */ }
        }, true);
    });
})();
