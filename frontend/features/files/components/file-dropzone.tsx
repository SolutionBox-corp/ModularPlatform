"use client";

import { useRef, useState, useCallback } from "react";
import { useTranslations } from "next-intl";
import { useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { UploadCloudIcon } from "lucide-react";
import { Progress } from "@/components/ui/progress";
import { cn } from "@/lib/utils";
import { uploadFile } from "@/features/files/api";
import {
  FILE_MAX_SIZE_BYTES,
  FILE_ALLOWED_CONTENT_TYPES,
  FILE_ALLOWED_EXTENSIONS_LABEL,
  useValidateFile,
} from "@/features/files/schema";
import { queryRoots } from "@/lib/api/query-keys";

const ACCEPTED_ATTR = [...FILE_ALLOWED_CONTENT_TYPES].join(",");

export function FileDropzone() {
  const t = useTranslations("files");
  const validateFile = useValidateFile();
  const queryClient = useQueryClient();
  const inputRef = useRef<HTMLInputElement>(null);
  const [isDragging, setIsDragging] = useState(false);
  const [isUploading, setIsUploading] = useState(false);
  const [progress, setProgress] = useState(0);

  const handleUpload = useCallback(
    // Uploads one or more files SEQUENTIALLY. Each file is validated independently; invalid
    // files are reported and skipped so a single bad file never blocks the rest of the batch.
    async (files: File[]) => {
      if (files.length === 0) return;

      setIsUploading(true);
      setProgress(0);

      // Fake smooth progress — the BFF is a simple proxy so we can't track real upload bytes.
      // We animate to 90% while awaiting each file, then jump to 100 on success.
      const intervalId = setInterval(() => {
        setProgress((p) => (p < 90 ? p + 10 : p));
      }, 200);

      try {
        let uploaded = false;
        for (const file of files) {
          const err = validateFile(file);
          if (err) {
            toast.error(err);
            continue;
          }
          setProgress(0);
          await uploadFile(file);
          setProgress(100);
          toast.success(t("dropzone.uploadSuccess", { name: file.name }));
          uploaded = true;
        }
        if (uploaded) {
          // Invalidate all pages of the files list once, after the whole batch.
          await queryClient.invalidateQueries({ queryKey: [...queryRoots.files, "list"] });
        }
      } finally {
        clearInterval(intervalId);
        setTimeout(() => {
          setIsUploading(false);
          setProgress(0);
        }, 600);
      }
    },
    [queryClient, t, validateFile],
  );

  const onFileSelected = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      const files = Array.from(e.target.files ?? []);
      if (files.length > 0) handleUpload(files);
      // Reset so the same file can be re-selected after an error.
      e.target.value = "";
    },
    [handleUpload],
  );

  const onDrop = useCallback(
    (e: React.DragEvent<HTMLDivElement>) => {
      e.preventDefault();
      setIsDragging(false);
      const files = Array.from(e.dataTransfer.files);
      if (files.length > 0) handleUpload(files);
    },
    [handleUpload],
  );

  const onDragOver = useCallback((e: React.DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    setIsDragging(true);
  }, []);

  const onDragLeave = useCallback(() => {
    setIsDragging(false);
  }, []);

  return (
    <div
      role="button"
      tabIndex={0}
      aria-label={t("dropzone.ariaLabel")}
      aria-disabled={isUploading}
      onDrop={isUploading ? undefined : onDrop}
      onDragOver={isUploading ? undefined : onDragOver}
      onDragLeave={onDragLeave}
      onClick={isUploading ? undefined : () => inputRef.current?.click()}
      onKeyDown={(e) => {
        if (!isUploading && (e.key === "Enter" || e.key === " ")) {
          e.preventDefault();
          inputRef.current?.click();
        }
      }}
      className={cn(
        "relative flex flex-col items-center justify-center gap-3 rounded-xl border-2 border-dashed px-6 py-12 text-center transition-colors outline-none",
        "focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2",
        isDragging
          ? "border-primary bg-primary/5"
          : "border-border hover:border-primary/50 hover:bg-muted/30",
        isUploading && "pointer-events-none select-none opacity-60",
      )}
    >
      <input
        ref={inputRef}
        type="file"
        className="sr-only"
        accept={ACCEPTED_ATTR}
        multiple
        tabIndex={-1}
        aria-hidden="true"
        onChange={onFileSelected}
      />

      <span className="flex h-12 w-12 items-center justify-center rounded-full bg-muted text-muted-foreground">
        <UploadCloudIcon className="h-6 w-6" aria-hidden="true" />
      </span>

      <div className="space-y-1">
        <p className="text-sm font-medium">
          {isUploading ? t("dropzone.uploading") : t("dropzone.prompt")}
        </p>
        <p className="text-xs text-muted-foreground">
          {t("dropzone.hint", {
            types: FILE_ALLOWED_EXTENSIONS_LABEL,
            maxMb: FILE_MAX_SIZE_BYTES / (1024 * 1024),
          })}
        </p>
      </div>

      {isUploading && (
        <div className="w-full max-w-xs">
          <Progress value={progress} aria-label={t("dropzone.progressLabel")} />
        </div>
      )}
    </div>
  );
}
