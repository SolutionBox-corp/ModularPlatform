import type { Instrumentation } from "next";

export const onRequestError: Instrumentation.onRequestError = (error, request) => {
  console.error("[server] request error", {
    message: error instanceof Error ? error.message : String(error),
    path: request.path,
    method: request.method,
  });
};
