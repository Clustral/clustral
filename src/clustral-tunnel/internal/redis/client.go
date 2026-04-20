package redis

import (
	"context"
	"fmt"
	"time"

	goredis "github.com/redis/go-redis/v9"
)

// SessionRegistry manages tunnel session ownership in Redis.
type SessionRegistry struct {
	client *goredis.Client
	ttl    time.Duration
}

// NewSessionRegistry creates a new Redis session registry.
func NewSessionRegistry(redisURL string, ttl time.Duration) (*SessionRegistry, error) {
	opts, err := goredis.ParseURL("redis://" + redisURL)
	if err != nil {
		// Fallback: treat as host:port.
		opts = &goredis.Options{Addr: redisURL}
	}

	client := goredis.NewClient(opts)

	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()

	if err := client.Ping(ctx).Err(); err != nil {
		return nil, fmt.Errorf("redis ping: %w", err)
	}

	return &SessionRegistry{client: client, ttl: ttl}, nil
}

// NewSessionRegistryFromClient creates a registry from an existing Redis client.
func NewSessionRegistryFromClient(client *goredis.Client, ttl time.Duration) *SessionRegistry {
	return &SessionRegistry{client: client, ttl: ttl}
}

func sessionKey(clusterID string) string {
	return "tunnel:session:" + clusterID
}

// Register stores a session mapping in Redis.
func (r *SessionRegistry) Register(ctx context.Context, clusterID, podName string) error {
	return r.client.Set(ctx, sessionKey(clusterID), podName, r.ttl).Err()
}

// Lookup returns the pod name that owns a cluster session.
func (r *SessionRegistry) Lookup(ctx context.Context, clusterID string) (string, error) {
	val, err := r.client.Get(ctx, sessionKey(clusterID)).Result()
	if err == goredis.Nil {
		return "", nil
	}
	return val, err
}

// Refresh extends the TTL on a session key.
func (r *SessionRegistry) Refresh(ctx context.Context, clusterID string) error {
	return r.client.Expire(ctx, sessionKey(clusterID), r.ttl).Err()
}

// Unregister removes a session mapping from Redis.
func (r *SessionRegistry) Unregister(ctx context.Context, clusterID string) error {
	return r.client.Del(ctx, sessionKey(clusterID)).Err()
}
