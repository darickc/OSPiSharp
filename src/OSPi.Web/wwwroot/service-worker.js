// OSPiSharp service worker.
//
// This is a Blazor *Server* app: the interactive UI runs over a live SignalR circuit
// (`/_blazor`) to the Pi on the LAN. The app is therefore INSTALLABLE but NOT
// offline-functional — the service worker caches only the static app shell so the
// install/launch experience is fast; it must never cache or intercept the circuit, the
// framework files, or non-GET requests, or the real-time connection breaks.

const CACHE = 'ospisharp-shell-v4';

// Static assets only — never the root HTML document. Cache-first serving of '/' is incompatible
// with Blazor enhanced navigation (it reconciles fresh server-rendered component-operation
// markers against live state; a stale cached document throws "The list of component operations
// is not valid"). The document must always come from the network.
const SHELL = [
  '/app.css',
  '/OSPi.Web.styles.css',
  '/_content/MudBlazor/MudBlazor.min.css',
  '/manifest.webmanifest',
  '/favicon.png',
  '/icon-192.png',
  '/icon-512.png',
  '/icon-maskable-512.png',
];

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(CACHE).then((cache) => cache.addAll(SHELL)).then(() => self.skipWaiting())
  );
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys()
      .then((keys) => Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k))))
      .then(() => self.clients.claim())
  );
});

self.addEventListener('fetch', (event) => {
  const { request } = event;
  const url = new URL(request.url);

  // Never intercept document navigations (they must hit the network for enhanced navigation to
  // reconcile against the live circuit), the Blazor circuit, framework assets, the MCP endpoint
  // (its SSE stream must always reach the network), or non-GET requests.
  if (request.mode === 'navigate'
      || request.method !== 'GET'
      || url.pathname.startsWith('/_blazor')
      || url.pathname.startsWith('/_framework')
      || url.pathname.startsWith('/mcp')) {
    return;
  }

  // Cache-first only for the known static shell; everything else hits the network.
  if (SHELL.includes(url.pathname)) {
    event.respondWith(caches.match(request).then((cached) => cached || fetch(request)));
  }
});
