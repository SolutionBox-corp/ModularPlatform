import { queryOptions } from "@tanstack/react-query";
import { apiFetch } from "@/lib/api/client";
import { queryRoots } from "@/lib/api/query-keys";
import type { Paged } from "@/lib/api/types";

export interface FileListItem {
  id: string;
  fileName: string;
  contentType: string;
  size: number;
  createdAt: string;
}

export interface UploadFileResponse {
  id: string;
  fileName: string;
  contentType: string;
  size: number;
}

export const fileQueries = {
  list: (page = 1, pageSize = 20, search = "") =>
    queryOptions({
      queryKey: [...queryRoots.files, "list", page, pageSize, search],
      queryFn: () => {
        const sp = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
        if (search.trim()) sp.set("search", search.trim());
        return apiFetch<Paged<FileListItem>>(`files?${sp.toString()}`);
      },
      staleTime: 30_000,
    }),
};

export async function uploadFile(file: File): Promise<UploadFileResponse> {
  const body = new FormData();
  body.append("file", file);
  return apiFetch<UploadFileResponse>("files", { method: "POST", body });
}

/** DELETE /v1/files/{id} — removes the caller's own file (blob + metadata). */
export async function deleteFile(id: string): Promise<void> {
  await apiFetch<void>(`files/${id}`, { method: "DELETE" });
}

/** PATCH /v1/files/{id} — renames the caller's own file (metadata only). */
export async function renameFile(
  id: string,
  fileName: string,
): Promise<FileListItem> {
  return apiFetch<FileListItem>(`files/${id}`, {
    method: "PATCH",
    body: { fileName },
  });
}
