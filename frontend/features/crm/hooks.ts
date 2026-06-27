"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { useTranslations } from "next-intl";
import { queryRoots } from "@/lib/api/query-keys";
import {
  addInteraction,
  cancelMeeting,
  completeMeeting,
  createContact,
  createMeeting,
  deleteContact,
  updateContact,
  updateMeeting,
  type ContactInput,
  type InteractionInput,
  type MeetingInput,
} from "@/features/crm/api";

/** Invalidates the whole CRM root so lists/details/timelines refetch after any mutation. */
function useInvalidateCrm() {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: queryRoots.crm });
}

export function useCreateContact() {
  const invalidate = useInvalidateCrm();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: (input: ContactInput) => createContact(input),
    onSuccess: async () => {
      await invalidate();
      toast.success(t("toast.contactCreated"));
    },
  });
}

export function useUpdateContact(id: string) {
  const invalidate = useInvalidateCrm();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: (input: ContactInput) => updateContact(id, input),
    onSuccess: async () => {
      await invalidate();
      toast.success(t("toast.contactUpdated"));
    },
  });
}

export function useDeleteContact() {
  const invalidate = useInvalidateCrm();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: (id: string) => deleteContact(id),
    onSuccess: async () => {
      await invalidate();
      toast.success(t("toast.contactDeleted"));
    },
  });
}

export function useAddInteraction(contactId: string) {
  const invalidate = useInvalidateCrm();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: (input: InteractionInput) => addInteraction(contactId, input),
    onSuccess: async () => {
      await invalidate();
      toast.success(t("toast.interactionAdded"));
    },
  });
}

export function useCreateMeeting() {
  const invalidate = useInvalidateCrm();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: (input: MeetingInput) => createMeeting(input),
    onSuccess: async () => {
      await invalidate();
      toast.success(t("toast.meetingCreated"));
    },
  });
}

export function useUpdateMeeting(id: string) {
  const invalidate = useInvalidateCrm();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: (input: MeetingInput) => updateMeeting(id, input),
    onSuccess: async () => {
      await invalidate();
      toast.success(t("toast.meetingUpdated"));
    },
  });
}

export function useCancelMeeting() {
  const invalidate = useInvalidateCrm();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: (id: string) => cancelMeeting(id),
    onSuccess: async () => {
      await invalidate();
      toast.success(t("toast.meetingCanceled"));
    },
  });
}

export function useCompleteMeeting() {
  const invalidate = useInvalidateCrm();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: ({ id, outcome }: { id: string; outcome: string | null }) =>
      completeMeeting(id, outcome),
    onSuccess: async () => {
      await invalidate();
      toast.success(t("toast.meetingCompleted"));
    },
  });
}
