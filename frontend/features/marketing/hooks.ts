"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import { queryRoots } from "@/lib/api/query-keys";
import {
  marketingQueries,
  triggerPull,
  startConversation,
  sendMessage,
  deleteConversation,
  type SnapshotsParams,
} from "@/features/marketing/api";

// ---------------------------------------------------------------------------
// Query hooks
// ---------------------------------------------------------------------------

export function usePulls(page = 1, pageSize = 20) {
  return useQuery(marketingQueries.pulls(page, pageSize));
}

export function usePullStatus(id: string, enabled = true) {
  return useQuery({ ...marketingQueries.pullStatus(id), enabled: enabled && id.length > 0 });
}

export function useSnapshots(params: SnapshotsParams = {}) {
  return useQuery(marketingQueries.snapshots(params));
}

export function useAnalyses(page = 1, pageSize = 20) {
  return useQuery(marketingQueries.analyses(page, pageSize));
}

export function useAnalysis(id: string, enabled = true) {
  return useQuery({ ...marketingQueries.analysis(id), enabled: enabled && id.length > 0 });
}

export function useVibeConversations() {
  return useQuery(marketingQueries.vibeConversations());
}

export function useVibeConversation(id: string, enabled = true) {
  return useQuery({
    ...marketingQueries.vibeConversation(id),
    enabled: enabled && id.length > 0,
  });
}

// ---------------------------------------------------------------------------
// Mutation hooks — all invalidate [marketing, ...] on success.
// ---------------------------------------------------------------------------

/**
 * Trigger a data pull (202). On success invalidate the marketing root so the
 * pulls panel reflects the new pending pull; the realtime "marketing.pull_completed"
 * event will invalidate again when it finishes.
 */
export function useTriggerPull() {
  const t = useTranslations("marketing");
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (source: string) => triggerPull(source),
    onSuccess: () => {
      toast.success(t("pulls.triggered"));
      void queryClient.invalidateQueries({ queryKey: [...queryRoots.marketing, "pulls"] });
    },
  });
}

/** Start a new vibe conversation; invalidate the conversations list. */
export function useStartConversation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (title?: string) => startConversation(title),
    onSuccess: () => {
      void queryClient.invalidateQueries({
        queryKey: [...queryRoots.marketing, "conversations"],
      });
    },
  });
}

/**
 * Send a user message (202). On success invalidate the open conversation so the
 * optimistic user turn is reconciled with the persisted one. The assistant reply
 * arrives asynchronously via the "marketing.vibe_message_ready" realtime event,
 * which invalidates [marketing, "conversations"].
 */
export function useSendMessage(conversationId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (content: string) => sendMessage(conversationId, content),
    onSuccess: () => {
      void queryClient.invalidateQueries({
        queryKey: [...queryRoots.marketing, "conversations", conversationId],
      });
    },
  });
}

/** Delete a conversation; invalidate the conversations list. */
export function useDeleteConversation() {
  const t = useTranslations("marketing");
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (conversationId: string) => deleteConversation(conversationId),
    onSuccess: () => {
      toast.success(t("vibeChat.deleted"));
      void queryClient.invalidateQueries({
        queryKey: [...queryRoots.marketing, "conversations"],
      });
    },
  });
}
