package tunnel

import (
	"context"
	"sync"

	tunnelredis "clustral-tunnel/internal/redis"
)

// SessionManager manages active tunnel sessions, both locally and in Redis.
type SessionManager struct {
	sessions sync.Map // map[string]*Session
	registry *tunnelredis.SessionRegistry
	podName  string
}

// NewSessionManager creates a new session manager.
func NewSessionManager(registry *tunnelredis.SessionRegistry, podName string) *SessionManager {
	return &SessionManager{
		registry: registry,
		podName:  podName,
	}
}

// Register stores a session locally and in Redis.
func (m *SessionManager) Register(ctx context.Context, clusterID string, session *Session) error {
	m.sessions.Store(clusterID, session)
	return m.registry.Register(ctx, clusterID, m.podName)
}

// GetSession returns the local session for a cluster, if it exists on this pod.
func (m *SessionManager) GetSession(clusterID string) (*Session, bool) {
	v, ok := m.sessions.Load(clusterID)
	if !ok {
		return nil, false
	}
	return v.(*Session), true
}

// Unregister removes a session locally and from Redis.
func (m *SessionManager) Unregister(ctx context.Context, clusterID string) error {
	m.sessions.Delete(clusterID)
	return m.registry.Unregister(ctx, clusterID)
}

// IsConnected checks if a session exists locally.
func (m *SessionManager) IsConnected(clusterID string) bool {
	_, ok := m.sessions.Load(clusterID)
	return ok
}

// Refresh refreshes the Redis TTL for a session.
func (m *SessionManager) Refresh(ctx context.Context, clusterID string) error {
	return m.registry.Refresh(ctx, clusterID)
}
