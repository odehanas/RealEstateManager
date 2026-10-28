/*
 * The service worker of the installed app (served at /sw.js, /portal/sw.js and /me/sw.js,
 * so its scope is the app it belongs to).
 *
 * It does two things only:
 *   - shows the notifications the server sends, and opens the right page when one is tapped;
 *   - shows a short "you are offline" page when there is no connection.
 * It never stores the application's pages or data: every screen comes fresh from the
 * server, as in the browser, so nothing private is left on the phone.
 */
var OFFLINE_PAGE = '/offline.html';
var CACHE = 'rem-offline-v1';

self.addEventListener('install', function (event) {
    event.waitUntil(
        caches.open(CACHE)
            .then(function (cache) {
                return cache.addAll([OFFLINE_PAGE, '/img/app/icon-192.png'].map(function (u) { return new Request(u, { cache: 'reload' }); }));
            })
            .then(function () { return self.skipWaiting(); }));
});

self.addEventListener('activate', function (event) {
    event.waitUntil(
        caches.keys()
            .then(function (keys) {
                return Promise.all(keys.filter(function (k) { return k !== CACHE; }).map(function (k) { return caches.delete(k); }));
            })
            .then(function () { return self.clients.claim(); }));
});

// Pages are always fetched from the server; only when that fails is the offline page shown.
self.addEventListener('fetch', function (event) {
    if (event.request.mode !== 'navigate')
        return;

    event.respondWith(fetch(event.request).catch(function () {
        return caches.match(OFFLINE_PAGE);
    }));
});

// The offline page's own picture, from the same small cache.
self.addEventListener('fetch', function (event) {
    if (event.request.mode === 'navigate' || new URL(event.request.url).pathname !== '/img/app/icon-192.png')
        return;

    event.respondWith(fetch(event.request).catch(function () { return caches.match('/img/app/icon-192.png'); }));
});

self.addEventListener('push', function (event) {
    var data = {};
    try { data = event.data ? event.data.json() : {}; } catch (e) { data = { body: event.data ? event.data.text() : '' }; }

    event.waitUntil(self.registration.showNotification(data.title || 'RealEstateManager', {
        body: data.body || '',
        tag: data.tag || undefined,
        renotify: !!data.tag,
        icon: '/img/app/icon-192.png',
        badge: '/img/app/badge-96.png',
        dir: data.dir || 'auto',
        lang: data.lang || '',
        data: { url: data.url || self.registration.scope }
    }));
});

// Tapping a notification opens its page, in the app's window when it is already open.
self.addEventListener('notificationclick', function (event) {
    event.notification.close();
    var target = new URL((event.notification.data && event.notification.data.url) || self.registration.scope, self.location.origin);
    if (target.origin !== self.location.origin)
        target = new URL(self.registration.scope);

    event.waitUntil(self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then(function (windows) {
        for (var i = 0; i < windows.length; i++) {
            var w = windows[i];
            if (w.url.indexOf(self.registration.scope) === 0 && 'focus' in w)
                return w.focus().then(function (focused) { return focused.navigate ? focused.navigate(target.href) : null; });
        }
        return self.clients.openWindow(target.href);
    }));
});
