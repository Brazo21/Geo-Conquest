// GeoConquest service worker: makes the site installable as a phone app and keeps it quick.
//  • Static files (styles, scripts, icons) are cached and refreshed in the background.
//  • Pages always come from the server, so the board, dice and turns are never stale or faked.
//    Pages are NOT stored: they can hold personal details and anti-forgery tokens (POPIA).
//  • With no connection, page loads show a friendly offline screen instead of a browser error.
//  • Only GET requests to this site are touched. Moves (POST), the live feed and other sites pass straight through.
const VERSION = 'gc-v1';
const STATIC_CACHE = `${VERSION}-static`;
const OFFLINE_URL = '/offline.html';
const PRECACHE = [
  OFFLINE_URL,
  '/css/command-room.css',
  '/lib/bootstrap/dist/css/bootstrap.min.css',
  '/icons/icon-192.png',
  '/icons/icon-512.png',
  '/favicon.svg'
];

self.addEventListener('install', event => {
  event.waitUntil(caches.open(STATIC_CACHE).then(cache => cache.addAll(PRECACHE)).then(() => self.skipWaiting()));
});

self.addEventListener('activate', event => {
  event.waitUntil(
    caches.keys()
      .then(keys => Promise.all(keys.filter(k => !k.startsWith(VERSION)).map(k => caches.delete(k))))
      .then(() => self.clients.claim()));
});

const isStatic = url => /^\/(css|js|lib|icons|img)\//.test(url.pathname) || /^\/favicon\.(svg|ico)$/.test(url.pathname);

self.addEventListener('fetch', event => {
  const request = event.request;
  if (request.method !== 'GET') return;
  const url = new URL(request.url);
  if (url.origin !== self.location.origin) return;           // CDNs and APIs: browser handles them normally

  if (request.mode === 'navigate') {
    // Network only for pages; offline screen as the fallback.
    event.respondWith(fetch(request).catch(() => caches.match(OFFLINE_URL)));
    return;
  }

  if (isStatic(url)) {
    // Stale-while-revalidate: answer from cache instantly, refresh it in the background.
    event.respondWith(caches.open(STATIC_CACHE).then(async cache => {
      const cached = await cache.match(request);
      const network = fetch(request).then(response => {
        if (response.ok) cache.put(request, response.clone());
        return response;
      }).catch(() => cached);
      return cached || network;
    }));
  }
  // Everything else (live game feed, health check…) goes straight to the network.
});
