import { createServer } from "https";
import { readFileSync } from "fs";
import { parse } from "url";
import next from "next";

const dev = process.env.NODE_ENV !== "production";
const app = next({ dev });
const handle = app.getRequestHandler();

const certPath = process.env.TLS_CERT_PATH || "/etc/clustral-web/tls.crt";
const keyPath = process.env.TLS_KEY_PATH || "/etc/clustral-web/tls.key";
const port = parseInt(process.env.PORT || "3000", 10);

app.prepare().then(() => {
  const httpsOptions = {
    cert: readFileSync(certPath),
    key: readFileSync(keyPath),
  };

  createServer(httpsOptions, (req, res) => {
    const parsedUrl = parse(req.url || "/", true);
    handle(req, res, parsedUrl);
  }).listen(port, () => {
    console.log(`> Clustral Web ready on https://0.0.0.0:${port}`);
  });
});
