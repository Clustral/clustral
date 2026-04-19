package redis

import (
	"context"
	"testing"
	"time"

	"github.com/alicebob/miniredis/v2"
	goredis "github.com/redis/go-redis/v9"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

func TestSessionRegistry_RegisterAndLookup(t *testing.T) {
	mr := miniredis.RunT(t)
	client := goredis.NewClient(&goredis.Options{Addr: mr.Addr()})
	registry := NewSessionRegistryFromClient(client, 5*time.Minute)
	ctx := context.Background()

	err := registry.Register(ctx, "cluster-1", "tunnel-0")
	require.NoError(t, err)

	pod, err := registry.Lookup(ctx, "cluster-1")
	require.NoError(t, err)
	assert.Equal(t, "tunnel-0", pod)
}

func TestSessionRegistry_TTLExpires(t *testing.T) {
	mr := miniredis.RunT(t)
	client := goredis.NewClient(&goredis.Options{Addr: mr.Addr()})
	registry := NewSessionRegistryFromClient(client, 1*time.Second)
	ctx := context.Background()

	err := registry.Register(ctx, "cluster-1", "tunnel-0")
	require.NoError(t, err)

	// Fast-forward time in miniredis.
	mr.FastForward(2 * time.Second)

	pod, err := registry.Lookup(ctx, "cluster-1")
	require.NoError(t, err)
	assert.Empty(t, pod)
}

func TestSessionRegistry_Refresh(t *testing.T) {
	mr := miniredis.RunT(t)
	client := goredis.NewClient(&goredis.Options{Addr: mr.Addr()})
	registry := NewSessionRegistryFromClient(client, 5*time.Second)
	ctx := context.Background()

	_ = registry.Register(ctx, "cluster-1", "tunnel-0")

	// Fast-forward 3 seconds, then refresh.
	mr.FastForward(3 * time.Second)
	err := registry.Refresh(ctx, "cluster-1")
	require.NoError(t, err)

	// Fast-forward another 3 seconds — should still be alive.
	mr.FastForward(3 * time.Second)
	pod, err := registry.Lookup(ctx, "cluster-1")
	require.NoError(t, err)
	assert.Equal(t, "tunnel-0", pod)
}

func TestSessionRegistry_Unregister(t *testing.T) {
	mr := miniredis.RunT(t)
	client := goredis.NewClient(&goredis.Options{Addr: mr.Addr()})
	registry := NewSessionRegistryFromClient(client, 5*time.Minute)
	ctx := context.Background()

	_ = registry.Register(ctx, "cluster-1", "tunnel-0")

	err := registry.Unregister(ctx, "cluster-1")
	require.NoError(t, err)

	pod, err := registry.Lookup(ctx, "cluster-1")
	require.NoError(t, err)
	assert.Empty(t, pod)
}
