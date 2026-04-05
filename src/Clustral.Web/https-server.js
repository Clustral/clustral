const { createServer } = require("https");
const { readFileSync } = require("fs");
const { parse } = require("url");

const certPath = process.env.TLS_CERT_PATH || "/etc/clustral-web/tls.crt";
const keyPath = process.env.TLS_KEY_PATH || "/etc/clustral-web/tls.key";
const port = parseInt(process.env.PORT || "3000", 10);

// Override HOSTNAME so Next.js binds to all interfaces.
process.env.HOSTNAME = "0.0.0.0";

const NextServer = require("next/dist/server/next.js").default;
const app = NextServer({ dev: false, dir: __dirname, port, hostname: "0.0.0.0" });
const handle = app.getRequestHandler();

app.prepare().then(() => {
  createServer(
    { cert: readFileSync(certPath), key: readFileSync(keyPath) },
    (req, res) => handle(req, res, parse(req.url || "/", true))
  ).listen(port, "0.0.0.0", () => {
    console.log(`> Clustral Web (HTTPS) ready on https://0.0.0.0:${port}`);
  });
});
