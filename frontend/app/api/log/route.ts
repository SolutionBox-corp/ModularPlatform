import { type NextRequest, NextResponse } from "next/server";

/**
 * Cheap client-error sink (no external SDK). The browser POSTs uncaught errors and
 * unhandled rejections here; we emit ONE structured line to stderr so the platform's
 * log aggregator picks them up. No auth (early-boot errors must report before login),
 * no DB, rate-conscious (oversized bodies are dropped). Always 204 — never make the
 * browser retry on our account.
 */

// Reject anything larger than this before parsing — a stack trace is small; bigger is abuse.
const MAX_BODY_BYTES = 8 * 1024;
// Logging may be disabled per-deployment (default on).
const ENABLED = (process.env.LOG_CLIENT_ERRORS ?? "true").toLowerCase() !== "false";

interface ClientErrorBody {
  type?: "error" | "rejection";
  message?: string;
  stack?: string;
  url?: string;
  /** Next.js error digest (global-error boundary only). */
  digest?: string;
  ts?: number;
}

export async function POST(request: NextRequest): Promise<NextResponse> {
  if (!ENABLED) return new NextResponse(null, { status: 204 });

  // Size guard: drop oversized bodies cheaply (don't buffer them into heap).
  const contentLength = request.headers.get("content-length");
  if (contentLength !== null) {
    const len = Number.parseInt(contentLength, 10);
    if (!Number.isNaN(len) && len > MAX_BODY_BYTES) {
      return new NextResponse(null, { status: 204 });
    }
  }

  let body: ClientErrorBody;
  try {
    const text = await request.text();
    if (text.length > MAX_BODY_BYTES) return new NextResponse(null, { status: 204 });
    body = JSON.parse(text) as ClientErrorBody;
  } catch {
    return new NextResponse(null, { status: 204 });
  }

  const type = body.type === "rejection" ? "rejection" : "error";
  const entry = {
    type,
    message: truncate(body.message, 1000),
    stack: truncate(body.stack, 4000),
    url: truncate(body.url, 500),
    digest: truncate(body.digest, 100),
    ts: typeof body.ts === "number" ? body.ts : Date.now(),
    ua: truncate(request.headers.get("user-agent"), 300),
  };

  // Single structured line, easy to grep/parse downstream.
  console.error(`[client-error] ${JSON.stringify(entry)}`);

  return new NextResponse(null, { status: 204 });
}

function truncate(value: string | null | undefined, max: number): string | undefined {
  if (!value) return undefined;
  return value.length > max ? value.slice(0, max) : value;
}

export const dynamic = "force-dynamic";
export const runtime = "nodejs";
