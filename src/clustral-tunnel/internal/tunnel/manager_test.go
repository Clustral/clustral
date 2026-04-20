package tunnel

import (
	"context"
	"testing"
	"time"

	tunnelredis "clustral-tunnel/internal/redis"

	"github.com/alicebob/miniredis/v2"
	goredis "github.com/redis/go-redis/v9"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

func setupManagerWithMiniredis(t *testing.T) (*SessionManager, *miniredis.Miniredis) {
	t.Helper()
	mr := miniredis.RunT(t)
	client := goredis.NewClient(&goredis.Options{Addr: mr.Addr()})
	registry := tunnelredis.NewSessionRegistryFromClient(client, 5*time.Minute)
	mgr := NewSessionManager(registry, "tunnel-0")
	return mgr, mr
}

func TestManager_RegisterAndGet(t *testing.T) {
	mgr, mr := setupManagerWithMiniredis(t)
	ctx := context.Background()

	stream := &mockStream{}
	session := NewSession("cluster-1", stream)

	err := mgr.Register(ctx, "cluster-1", session)
	require.NoError(t, err)

	// Local lookup.
	got, ok := mgr.GetSession("cluster-1")
	assert.True(t, ok)
	assert.Equal(t, "cluster-1", got.ClusterID)

	// Redis key exists.
	val, err := mr.Get("tunnel:session:cluster-1")
	require.NoError(t, err)
	assert.Equal(t, "tunnel-0", val)
}

func TestManager_Unregister(t *testing.T) {
	mgr, mr := setupManagerWithMiniredis(t)
	ctx := context.Background()

	stream := &mockStream{}
	session := NewSession("cluster-1", stream)
	_ = mgr.Register(ctx, "cluster-1", session)

	err := mgr.Unregister(ctx, "cluster-1")
	require.NoError(t, err)

	_, ok := mgr.GetSession("cluster-1")
	assert.False(t, ok)

	assert.False(t, mr.Exists("tunnel:session:cluster-1"))
}

func TestManager_IsConnected(t *testing.T) {
	mgr, _ := setupManagerWithMiniredis(t)
	ctx := context.Background()

	assert.False(t, mgr.IsConnected("cluster-1"))

	stream := &mockStream{}
	session := NewSession("cluster-1", stream)
	_ = mgr.Register(ctx, "cluster-1", session)

	assert.True(t, mgr.IsConnected("cluster-1"))
}
