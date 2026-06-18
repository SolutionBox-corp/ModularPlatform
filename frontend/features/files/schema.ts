/**
 * Client-side validation constants — mirror FileUploadPolicy.cs exactly.
 * Backend enforces these server-side; we mirror them for a fast pre-check before the network round-trip.
 */
export const FILE_MAX_SIZE_BYTES = 10 * 1024 * 1024; // 10 MB

/**
 * Allowed MIME types, matching the backend allowlist in FileUploadPolicy.cs.
 */
export const FILE_ALLOWED_CONTENT_TYPES = new Set([
  "image/png",
  "image/jpeg",
  "application/pdf",
  "text/plain",
]);

/** Human-readable label for each allowed MIME type (for the dropzone hint). */
export const FILE_ALLOWED_EXTENSIONS_LABEL = "PNG, JPEG, PDF, TXT";

export function validateFile(file: File): string | null {
  if (file.size > FILE_MAX_SIZE_BYTES) {
    return `File is too large. Maximum size is 10 MB.`;
  }
  if (!FILE_ALLOWED_CONTENT_TYPES.has(file.type)) {
    return `File type "${file.type || "unknown"}" is not allowed. Accepted types: ${FILE_ALLOWED_EXTENSIONS_LABEL}.`;
  }
  return null;
}
