// HTTPS wrapper for Next.js standalone server.
// The standalone build outputs its own server.js which we rename to
// _next-standalone.js in the Dockerfile. This file wraps it with TLS.

const { createServer: createHttpsServer } = require("https");
const { createServer: createHttpServer } = require("http");
const { readFileSync, existsSync } = require("fs");

const certPath = process.env.TLS_CERT_PATH || "/etc/clustral-web/tls.crt";
const keyPath = process.env.TLS_KEY_PATH || "/etc/clustral-web/tls.key";
const port = parseInt(process.env.PORT || "3000", 10);
const hostname = process.env.HOSTNAME || "0.0.0.0";

// The Next.js standalone handler is set up by requiring the renamed file.
// It sets up the request handler and listens — but we need to intercept that.
// Instead, we use Next.js programmatically.
const next = require("next/dist/server/next.js").default;

const app = next({
  dev: false,
  hostname,
  port,
  dir: __dirname,
});

const handle = app.getRequestHandler();

app.prepare().then(() => {
  const hasCert = existsSync(certPath) && existsSync(keyPath);

  if (hasCert) {
    createHttpsServer(
      { cert: readFileSync(certPath), key: readFileSync(keyPath) },
      (req, res) => handle(req, res)
    ).listen(port, hostname, () => {
      console.log(`> Clustral Web ready on https://${hostname}:${port}`);
    });
  } else {
    // Fallback to HTTP if no certs (local dev).
    createHttpServer((req, res) => handle(req, res)).listen(
      port,
      hostname,
      () => {
        console.log(`> Clustral Web ready on http://${hostname}:${port}`);
      }
    );
  }
});
