/*
  Guarda a casca do Nexus Móvel no aparelho para abrir rápido.
  Os dados nunca são guardados: eles vêm sempre do computador da loja.
*/
const VERSAO = "nexus-movel-v1";
const CASCA = [
  "/",
  "/estilo.css",
  "/app.js",
  "/manifest.webmanifest",
  "/vendor/zxing.min.js",
  "/icone-192.png",
  "/icone-512.png"
];

self.addEventListener("install", (evento) => {
  evento.waitUntil(
    caches.open(VERSAO)
      .then((cache) => cache.addAll(CASCA))
      .then(() => self.skipWaiting())
  );
});

self.addEventListener("activate", (evento) => {
  evento.waitUntil(
    caches.keys()
      .then((chaves) => Promise.all(chaves.filter((c) => c !== VERSAO).map((c) => caches.delete(c))))
      .then(() => self.clients.claim())
  );
});

self.addEventListener("fetch", (evento) => {
  const url = new URL(evento.request.url);

  // Dados da loja: sempre da rede, nunca do cache.
  if (url.pathname.startsWith("/api/")) return;

  if (evento.request.mode === "navigate") {
    evento.respondWith(
      fetch(evento.request)
        .then((resposta) => {
          const copia = resposta.clone();
          caches.open(VERSAO).then((cache) => cache.put("/", copia));
          return resposta;
        })
        .catch(() => caches.match("/"))
    );
    return;
  }

  evento.respondWith(
    caches.match(evento.request).then((guardado) => guardado || fetch(evento.request))
  );
});
