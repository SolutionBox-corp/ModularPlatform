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
  createCompany,
  createBoard,
  createCard,
  createDeal,
  createMeeting,
  createTask,
  deleteContact,
  deleteCompany,
  deleteCard,
  deleteDeal,
  deleteTask,
  completeTask,
  moveCard,
  moveDealStage,
  updateContact,
  updateCompany,
  updateDeal,
  updateMeeting,
  updateTask,
  type ContactInput,
  type CompanyInput,
  type DealInput,
  type InteractionInput,
  type MeetingInput,
  type TaskInput,
} from "@/features/crm/api";

export function useCreateContact() {
  const queryClient = useQueryClient();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: (input: ContactInput) => createContact(input),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryRoots.crm });
      toast.success(t("toast.contactCreated"));
    },
  });
}

export function useUpdateContact(id: string) {
  const queryClient = useQueryClient();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: (input: ContactInput) => updateContact(id, input),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryRoots.crm });
      toast.success(t("toast.contactUpdated"));
    },
  });
}

export function useDeleteContact() {
  const queryClient = useQueryClient();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: (id: string) => deleteContact(id),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryRoots.crm });
      toast.success(t("toast.contactDeleted"));
    },
  });
}

export function useAddInteraction(contactId: string) {
  const queryClient = useQueryClient();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: (input: InteractionInput) => addInteraction(contactId, input),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryRoots.crm });
      toast.success(t("toast.interactionAdded"));
    },
  });
}

export function useCreateMeeting() {
  const queryClient = useQueryClient();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: (input: MeetingInput) => createMeeting(input),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryRoots.crm });
      toast.success(t("toast.meetingCreated"));
    },
  });
}

export function useUpdateMeeting(id: string) {
  const queryClient = useQueryClient();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: (input: MeetingInput) => updateMeeting(id, input),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryRoots.crm });
      toast.success(t("toast.meetingUpdated"));
    },
  });
}

export function useCancelMeeting() {
  const queryClient = useQueryClient();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: (id: string) => cancelMeeting(id),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryRoots.crm });
      toast.success(t("toast.meetingCanceled"));
    },
  });
}

export function useCompleteMeeting() {
  const queryClient = useQueryClient();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: ({ id, outcome }: { id: string; outcome: string | null }) =>
      completeMeeting(id, outcome),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryRoots.crm });
      toast.success(t("toast.meetingCompleted"));
    },
  });
}

export function useCreateDeal() {
  const queryClient = useQueryClient();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: (input: DealInput) => createDeal(input),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryRoots.crm });
      toast.success(t("toast.dealCreated"));
    },
  });
}

export function useUpdateDeal(id: string) {
  const queryClient = useQueryClient();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: (input: Partial<DealInput>) => updateDeal(id, input),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryRoots.crm });
      toast.success(t("toast.dealUpdated"));
    },
  });
}

export function useMoveDealStage() {
  const queryClient = useQueryClient();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: ({ id, stage }: { id: string; stage: string }) => moveDealStage(id, stage),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryRoots.crm });
      toast.success(t("toast.dealStageMoved"));
    },
  });
}

export function useDeleteDeal() {
  const queryClient = useQueryClient();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: (id: string) => deleteDeal(id),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryRoots.crm });
      toast.success(t("toast.dealDeleted"));
    },
  });
}

export function useCreateTask() {
  const queryClient = useQueryClient();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: (input: TaskInput) => createTask(input),
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: queryRoots.crm }); toast.success(t("toast.taskCreated")); },
  });
}

export function useCompleteTask() {
  const queryClient = useQueryClient();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: (id: string) => completeTask(id),
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: queryRoots.crm }); toast.success(t("toast.taskCompleted")); },
  });
}

export function useDeleteTask() {
  const queryClient = useQueryClient();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: (id: string) => deleteTask(id),
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: queryRoots.crm }); toast.success(t("toast.taskDeleted")); },
  });
}

export function useUpdateTask(id: string) {
  const queryClient = useQueryClient();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: (input: Partial<TaskInput>) => updateTask(id, input),
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: queryRoots.crm }); toast.success(t("toast.taskUpdated")); },
  });
}

export function useCreateCompany() {
  const queryClient = useQueryClient();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: (input: CompanyInput) => createCompany(input),
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: queryRoots.crm }); toast.success(t("toast.companyCreated")); },
  });
}

export function useUpdateCompany(id: string) {
  const queryClient = useQueryClient();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: (input: Partial<CompanyInput>) => updateCompany(id, input),
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: queryRoots.crm }); toast.success(t("toast.companyUpdated")); },
  });
}

export function useDeleteCompany() {
  const queryClient = useQueryClient();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: (id: string) => deleteCompany(id),
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: queryRoots.crm }); toast.success(t("toast.companyDeleted")); },
  });
}

export function useCreateBoard() {
  const queryClient = useQueryClient();
  const t = useTranslations("crm");
  return useMutation({
    mutationFn: (name: string) => createBoard(name),
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: queryRoots.crm }); toast.success(t("toast.boardCreated")); },
  });
}

export function useCreateCard(boardId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ columnId, title }: { columnId: string; title: string }) => createCard(boardId, columnId, title),
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: queryRoots.crm }); },
  });
}

export function useMoveCard() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ cardId, columnId, position }: { cardId: string; columnId: string; position: number }) =>
      moveCard(cardId, columnId, position),
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: queryRoots.crm }); },
  });
}

export function useDeleteCard() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => deleteCard(id),
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: queryRoots.crm }); },
  });
}
