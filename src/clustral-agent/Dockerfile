FROM golang:1.23-alpine AS build
ARG VERSION=0.0.0-dev
WORKDIR /app
COPY go.mod go.sum ./
RUN go mod download
COPY . .
RUN CGO_ENABLED=0 go build -ldflags="-s -w -X main.version=${VERSION}" -o clustral-agent .

FROM alpine:3.20
RUN adduser -D -u 1001 clustral
RUN mkdir -p /etc/clustral && chown clustral:clustral /etc/clustral
COPY --from=build /app/clustral-agent /usr/local/bin/
USER clustral
ENTRYPOINT ["clustral-agent"]
