import "server-only";
import Redis from "ioredis";
import { serverConfig } from "@/lib/config";

/**
 * Lazy Redis singleton for multi-node server coordination (currently the single-flight
 * refresh lock in backend.ts). Returns `null` when `REDIS_URL` is unset — single-node
 * deployments keep the existing in-process behavior and never load a connection.
 *
 * The client is cached on `globalThis` so Next.js dev hot-reload (which re-evaluates
 * modules) doesn't leak a new connection on every reload.
 */
const globalForRedis = globalThis as unknown as { __mpRedis?: Redis | null };

export function getRedis(): Redis | null {
  if (!serverConfig.redisUrl) return null;
  if (globalForRedis.__mpRedis !== undefined) return globalForRedis.__mpRedis;

  const client = new Redis(serverConfig.redisUrl, {
    // Don't crash the process on a transient Redis outage; the lock path degrades
    // gracefully (falls back to in-proc coalescing) on any command error.
    maxRetriesPerRequest: 1,
    enableReadyCheck: true,
    lazyConnect: false,
  });
  // Swallow connection errors — every caller already try/catches the command and
  // falls back. An unhandled 'error' event would otherwise crash Node.
  client.on("error", () => {
    /* handled per-command */
  });

  globalForRedis.__mpRedis = client;
  return client;
}
