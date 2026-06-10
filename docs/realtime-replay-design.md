# Realtime replay buffer — design (Last-Event-ID over Redis Streams)

> Closes the plan §6 replay gap: a client reconnecting to `GET /v1/realtime/stream` with `Last-Event-ID`
> receives the events it missed (bounded last-N / TTL), then continues live.

## Shape

- **Event ids become real.** `RedisRealtimePublisher.PublishToUserAsync` first `XADD`s the envelope to a
  per-user stream `rts:user:{userId}` with `MAXLEN ~ N` (config `Realtime:Replay:MaxEvents`, default 100)
  + sliding key TTL (`Realtime:Replay:TtlMinutes`, default 60); the returned stream id (e.g. `1718000000000-0`)
  IS the event id, carried in the pub/sub `Envelope` (new `Id` field) and delivered as the SSE `id:` field.
  The existing fan-out (pub/sub channel `rt:user:{guid}`) is unchanged — streams are a replay LOG, not the
  delivery path.
- **Replay port:** `IRealtimeReplay` in the Realtime building-block:
  `Task<IReadOnlyList<RealtimeMessage>> ReadSinceAsync(Guid userId, string lastEventId, CancellationToken)`.
  Redis impl = `XRANGE (lastEventId, +]`. **Local fallback** (no Redis configured) = per-user in-memory ring
  buffer (bounded `MaxEvents`) inside `LocalRealtimePublisher` — keeps tests and single-node dev working and
  makes the contract testable without a Redis container.
- **SSE endpoint:** on connect, read the `Last-Event-ID` header (standard SSE reconnect contract); when
  present, emit the replayed messages first (their original ids), then bridge to live. No header → live only.
- Non-goals: cross-user/tenant replay (the dead `rt:tenant:*` path stays as-is — flagged, not silently
  "fixed"), durable guaranteed delivery (this is best-effort UX smoothing; durable facts live in modules).

## Config

```
Realtime:Replay:Enabled      bool, default true
Realtime:Replay:MaxEvents    int, default 100   (XADD MAXLEN ~)
Realtime:Replay:TtlMinutes   int, default 60    (stream key TTL, refreshed on write)
```

## Welcome template seeding (bundled small item)

`NotificationsSeeder` (IHostedService, mirrors `IdentitySeeder`): idempotently upserts
`NotificationTemplate` rows `welcome` (en + cs) and `purchase_completed` (en + cs); UNIQUE(Key,Locale) +
catch `DbUpdateException` absorbs multi-host races. EV-2's "missing template is non-fatal" test is rewritten
to assert the welcome row IS created (the non-fatal path keeps its own targeted test via a bogus key).
Notifications also gains a consumer for `CreditPurchaseCompletedIntegrationEvent` → `purchase_completed`
in-app notification (public shell handler + `Discovery.IncludeType`, standard pattern).
