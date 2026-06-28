"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { queryRoots } from "@/lib/api/query-keys";
import { deleteFile, linkFileToOwner, renameFile, unlinkFile } from "@/features/files/api";

/** Delete a file; invalidate every page of the files list so the row disappears. */
export function useDeleteFile() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => deleteFile(id),
    onSuccess: () => {
      void queryClient.invalidateQueries({
        queryKey: [...queryRoots.files, "list"],
      });
    },
  });
}

/** Rename a file; invalidate the files list so the new name shows everywhere. */
export function useRenameFile() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, fileName }: { id: string; fileName: string }) =>
      renameFile(id, fileName),
    onSuccess: () => {
      void queryClient.invalidateQueries({
        queryKey: [...queryRoots.files, "list"],
      });
    },
  });
}

export function useLinkFileToOwner() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: linkFileToOwner,
    onSuccess: (_, variables) => {
      void queryClient.invalidateQueries({
        queryKey: [...queryRoots.files, "links", variables.ownerType, variables.ownerId],
      });
    },
  });
}

export function useUnlinkFileFromOwner() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      linkId,
    }: {
      linkId: string;
      ownerType: string;
      ownerId: string;
    }) => unlinkFile(linkId),
    onSuccess: (_, variables) => {
      void queryClient.invalidateQueries({
        queryKey: [...queryRoots.files, "links", variables.ownerType, variables.ownerId],
      });
      void queryClient.invalidateQueries({
        queryKey: [...queryRoots.files, "list"],
      });
    },
  });
}
