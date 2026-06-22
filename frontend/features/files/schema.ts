import { useCallback } from "react";
import { useTranslations } from "next-intl";

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

const FILE_MAX_SIZE_MB = FILE_MAX_SIZE_BYTES / (1024 * 1024);

/**
 * Returns a file validator bound to the active locale. Returns a localized error
 * string if the file is invalid, otherwise null. Mirrors FileUploadPolicy.cs.
 */
export function useValidateFile(): (file: File) => string | null {
  const t = useTranslations("files");
  return useCallback(
    (file: File): string | null => {
      if (file.size > FILE_MAX_SIZE_BYTES) {
        return t("validation.tooLarge", { maxMb: FILE_MAX_SIZE_MB });
      }
      if (!FILE_ALLOWED_CONTENT_TYPES.has(file.type)) {
        return t("validation.typeNotAllowed", {
          type: file.type || t("validation.unknownType"),
          types: FILE_ALLOWED_EXTENSIONS_LABEL,
        });
      }
      return null;
    },
    [t],
  );
}
