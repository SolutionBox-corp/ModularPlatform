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
  createDeal,
  createMeeting,
  createTask,
  deleteContact,
  deleteDeal,
  deleteTask,
  completeTask,
  moveDealStage,
  updateContact,
  updateDeal,
  updateMeeting,
  updateTask,
  type ContactInput,
  type DealInput,
  type InteractionInput,
  type MeetingInput,
  type TaskInput,
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

export function useCreateDeal() {
  const invalidate = useInvalidateCrm();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: (input: DealInput) => createDeal(input),
    onSuccess: async () => {
      await invalidate();
      toast.success(t("toast.dealCreated"));
    },
  });
}

export function useUpdateDeal(id: string) {
  const invalidate = useInvalidateCrm();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: (input: Partial<DealInput>) => updateDeal(id, input),
    onSuccess: async () => {
      await invalidate();
      toast.success(t("toast.dealUpdated"));
    },
  });
}

export function useMoveDealStage() {
  const invalidate = useInvalidateCrm();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: ({ id, stage }: { id: string; stage: string }) => moveDealStage(id, stage),
    onSuccess: async () => {
      await invalidate();
      toast.success(t("toast.dealStageMoved"));
    },
  });
}

export function useDeleteDeal() {
  const invalidate = useInvalidateCrm();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: (id: string) => deleteDeal(id),
    onSuccess: async () => {
      await invalidate();
      toast.success(t("toast.dealDeleted"));
    },
  });
}

export function useCreateTask() {
  const invalidate = useInvalidateCrm();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: (input: TaskInput) => createTask(input),
    onSuccess: async () => { await invalidate(); toast.success(t("toast.taskCreated")); },
  });
}

export function useCompleteTask() {
  const invalidate = useInvalidateCrm();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: (id: string) => completeTask(id),
    onSuccess: async () => { await invalidate(); toast.success(t("toast.taskCompleted")); },
  });
}

export function useDeleteTask() {
  const invalidate = useInvalidateCrm();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: (id: string) => deleteTask(id),
    onSuccess: async () => { await invalidate(); toast.success(t("toast.taskDeleted")); },
  });
}

export function useUpdateTask(id: string) {
  const invalidate = useInvalidateCrm();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: (input: Partial<TaskInput>) => updateTask(id, input),
    onSuccess: async () => { await invalidate(); toast.success(t("toast.taskUpdated")); },
  });
}
