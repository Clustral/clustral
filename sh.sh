openssl req -x509 -nodes -days 3650 \
  -newkey rsa:2048 \
  -keyout infra/ca/ca.key \
  -out infra/ca/ca.crt \
  -subj "/CN=Clustral CA" \
  -addext "subjectAltName=DNS:clustral,DNS:localhost,IP:127.0.0.1,IP:192.168.88.4,IP:0.0.0.0"
  
TOKEN=$(grep -A1 'user:' ~/.kube/config | grep token: | awk '{print $2}')
curl -ski -H "Authorization: Bearer $TOKEN" \
  "https://192.168.88.4/api/proxy/bcd0ef99-7602-4fd5-b5cb-c7a300f5504a/api?timeout=32s"