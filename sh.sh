openssl req -x509 -nodes -days 3650 \
  -newkey rsa:2048 \
  -keyout infra/ca/ca.key \
  -out infra/ca/ca.crt \
  -subj "/CN=Clustral CA" \
  -addext "subjectAltName=DNS:clustral,DNS:localhost,IP:127.0.0.1,IP:192.168.88.4,IP:0.0.0.0"