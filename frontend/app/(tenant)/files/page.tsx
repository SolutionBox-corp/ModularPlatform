import type { Metadata } from "next";
import { HydrationBoundary, dehydrate } from "@tanstack/react-query";
import { getQueryClient } from "@/lib/api/query-client";
import { fileQueries } from "@/features/files/api";
import { FileDropzone } from "@/features/files/components/file-dropzone";
import { FileTable } from "@/features/files/components/file-table";

export const metadata: Metadata = {
  title: "Files — ModularPlatform",
};

export default async function FilesPage() {
  const queryClient = getQueryClient();

  // Prefetch page 1 — streamed into HydrationBoundary, no loading flash.
  void queryClient.prefetchQuery(fileQueries.list(1, 20));

  return (
    <HydrationBoundary state={dehydrate(queryClient)}>
      <div className="space-y-6">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">Files</h1>
          <p className="text-sm text-muted-foreground mt-0.5">
            Upload and manage your files. Each file is private to your account.
          </p>
        </div>

        <FileDropzone />

        <div className="space-y-2">
          <h2 className="text-sm font-medium text-muted-foreground">Your files</h2>
          <FileTable />
        </div>
      </div>
    </HydrationBoundary>
  );
}
