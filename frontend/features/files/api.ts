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
  list: (page = 1, pageSize = 20) =>
    queryOptions({
      queryKey: [...queryRoots.files, "list", page, pageSize],
      queryFn: () => {
        const sp = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
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
