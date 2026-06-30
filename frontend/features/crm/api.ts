import { queryOptions } from "@tanstack/react-query";
import { apiFetch } from "@/lib/api/client";
import { queryRoots } from "@/lib/api/query-keys";

/* ----------------------------------------------------------------------------
 * Types — mirror the CRM module's response DTOs (ModularPlatform.Crm).
 * -------------------------------------------------------------------------- */

export interface ContactListItem {
  id: string;
  companyId: string | null;
  companyName: string | null;
  firstName: string;
  lastName: string;
  email: string | null;
  status: string;
  createdAt: string;
}

export interface Contact {
  id: string;
  companyId: string | null;
  companyName: string | null;
  firstName: string;
  lastName: string;
  email: string | null;
  phone: string | null;
  position: string | null;
  notes: string | null;
  tags: string[];
  status: string;
  createdAt: string;
  updatedAt: string | null;
}

export function contactDisplayName(contact: Pick<Contact, "firstName" | "lastName"> | Pick<ContactListItem, "firstName" | "lastName">): string {
  return [contact.firstName, contact.lastName].filter(Boolean).join(" ");
}

export interface ContactsPage {
  items: ContactListItem[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface Interaction {
  id: string;
  contactId: string;
  type: string;
  occurredAt: string;
  body: string | null;
}

export interface Meeting {
  id: string;
  contactId: string | null;
  contactName: string | null;
  dealId: string | null;
  title: string;
  scheduledAt: string;
  durationMinutes: number;
  location: string | null;
  notes: string | null;
  status: string;
  outcome: string | null;
  createdAt: string;
  updatedAt: string | null;
}

export interface MeetingsPage {
  items: Meeting[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface InteractionsPage {
  items: Interaction[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export const CONTACT_STATUSES = ["lead", "active", "customer", "archived"] as const;
export const INTERACTION_TYPES = ["call", "email", "note", "meeting"] as const;
export const DEAL_STAGES = ["lead", "qualified", "proposal", "negotiation", "won", "lost"] as const;
export const TASK_PRIORITIES = ["low", "normal", "high"] as const;

export interface Company {
  id: string;
  name: string;
  domain: string | null;
  industry: string | null;
  identificationNumber: string | null;
  taxIdentificationNumber: string | null;
  registeredAddress: string | null;
  city: string | null;
  postalCode: string | null;
  country: string | null;
  notes: string | null;
  createdAt: string;
  updatedAt: string | null;
}

export interface CompanyListItem {
  id: string;
  name: string;
  domain: string | null;
  industry: string | null;
  createdAt: string;
}

export interface CompaniesPage {
  items: CompanyListItem[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface KanbanColumn {
  id: string;
  name: string;
  position: number;
  color: string;
  group: string;
  isDefault: boolean;
  wipLimit: number | null;
}
export interface KanbanCard {
  id: string; columnId: string; position: number; title: string; description: string | null;
  contactId: string | null; dealId: string | null; meetingId: string | null; taskId: string | null;
  assigneeUserId: string | null; priority: string; labels: string[]; startAt: string | null; dueAt: string | null;
}
export interface KanbanBoardListItem { id: string; name: string; createdAt: string; }
export interface KanbanBoardDetail { id: string; name: string; columns: KanbanColumn[]; cards: KanbanCard[]; }

export interface CrmTask {
  id: string;
  contactId: string | null;
  dealId: string | null;
  title: string;
  description: string | null;
  dueAt: string | null;
  priority: string;
  status: string;
  completedAt: string | null;
  createdAt: string;
  updatedAt: string | null;
}

export interface TasksPage {
  items: CrmTask[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface Deal {
  id: string;
  contactId: string | null;
  companyId: string | null;
  title: string;
  amountCents: number;
  currency: string;
  stage: string;
  expectedCloseAt: string | null;
  closedAt: string | null;
  notes: string | null;
  createdAt: string;
  updatedAt: string | null;
}

export interface DealsPage {
  items: Deal[];
  page: number;
  pageSize: number;
  totalCount: number;
}

/* ----------------------------------------------------------------------------
 * Query factories — pages prefetch these; hooks consume them.
 * -------------------------------------------------------------------------- */

export interface ContactsParams {
  page?: number;
  pageSize?: number;
  status?: string;
  companyId?: string;
  email?: string;
}

export interface MeetingsParams {
  page?: number;
  pageSize?: number;
  status?: string;
  contactId?: string;
  companyId?: string;
  dealId?: string;
  from?: string;
  to?: string;
}

export interface DealsParams {
  page?: number;
  pageSize?: number;
  stage?: string;
  contactId?: string;
}

export interface TasksParams {
  page?: number;
  pageSize?: number;
  status?: string;
  dueBefore?: string;
  contactId?: string;
  dealId?: string;
}

export const crmQueries = {
  contacts: (params: ContactsParams = {}) => {
    const pageSize = params.pageSize ?? 20;
    return queryOptions({
      queryKey: [...queryRoots.crm, "contacts", params],
      queryFn: () => {
        const sp = new URLSearchParams();
        sp.set("page", String(params.page ?? 1));
        sp.set("pageSize", String(pageSize));
        if (params.status) sp.set("status", params.status);
        if (params.companyId) sp.set("companyId", params.companyId);
        if (params.email) sp.set("email", params.email);
        return apiFetch<ContactsPage>(`crm/contacts?${sp.toString()}`);
      },
      staleTime: 15_000,
    });
  },

  contact: (id: string) =>
    queryOptions({
      queryKey: [...queryRoots.crm, "contact", id],
      queryFn: () => apiFetch<Contact>(`crm/contacts/${id}`),
      enabled: id.length > 0,
    }),

  interactions: (contactId: string) =>
    queryOptions({
      queryKey: [...queryRoots.crm, "interactions", contactId],
      queryFn: () => apiFetch<InteractionsPage>(`crm/contacts/${contactId}/interactions`),
      enabled: contactId.length > 0,
    }),

  meetings: (params: MeetingsParams = {}) => {
    const pageSize = params.pageSize ?? 20;
    return queryOptions({
      queryKey: [...queryRoots.crm, "meetings", params],
      queryFn: () => {
        const sp = new URLSearchParams();
        sp.set("page", String(params.page ?? 1));
        sp.set("pageSize", String(pageSize));
        if (params.status) sp.set("status", params.status);
        if (params.contactId) sp.set("contactId", params.contactId);
        if (params.companyId) sp.set("companyId", params.companyId);
        if (params.dealId) sp.set("dealId", params.dealId);
        if (params.from) sp.set("from", params.from);
        if (params.to) sp.set("to", params.to);
        return apiFetch<MeetingsPage>(`crm/meetings?${sp.toString()}`);
      },
      staleTime: 15_000,
    });
  },

  deals: (params: DealsParams = {}) => {
    const pageSize = params.pageSize ?? 20;
    return queryOptions({
      queryKey: [...queryRoots.crm, "deals", params],
      queryFn: () => {
        const sp = new URLSearchParams();
        sp.set("page", String(params.page ?? 1));
        sp.set("pageSize", String(pageSize));
        if (params.stage) sp.set("stage", params.stage);
        if (params.contactId) sp.set("contactId", params.contactId);
        return apiFetch<DealsPage>(`crm/deals?${sp.toString()}`);
      },
      staleTime: 15_000,
    });
  },

  deal: (id: string) =>
    queryOptions({
      queryKey: [...queryRoots.crm, "deal", id],
      queryFn: () => apiFetch<Deal>(`crm/deals/${id}`),
      enabled: id.length > 0,
    }),

  tasks: (params: TasksParams = {}) => {
    const pageSize = params.pageSize ?? 20;
    return queryOptions({
      queryKey: [...queryRoots.crm, "tasks", params],
      queryFn: () => {
        const sp = new URLSearchParams();
        sp.set("page", String(params.page ?? 1));
        sp.set("pageSize", String(pageSize));
        if (params.status) sp.set("status", params.status);
        if (params.dueBefore) sp.set("dueBefore", params.dueBefore);
        if (params.contactId) sp.set("contactId", params.contactId);
        if (params.dealId) sp.set("dealId", params.dealId);
        return apiFetch<TasksPage>(`crm/tasks?${sp.toString()}`);
      },
      staleTime: 15_000,
    });
  },

  companies: (params: { page?: number; pageSize?: number; industry?: string; name?: string } = {}) => {
    const pageSize = params.pageSize ?? 20;
    return queryOptions({
      queryKey: [...queryRoots.crm, "companies", params],
      queryFn: () => {
        const sp = new URLSearchParams();
        sp.set("page", String(params.page ?? 1));
        sp.set("pageSize", String(pageSize));
        if (params.industry) sp.set("industry", params.industry);
        if (params.name) sp.set("name", params.name);
        return apiFetch<CompaniesPage>(`crm/companies?${sp.toString()}`);
      },
      staleTime: 15_000,
    });
  },

  company: (id: string) =>
    queryOptions({
      queryKey: [...queryRoots.crm, "company", id],
      queryFn: () => apiFetch<Company>(`crm/companies/${id}`),
      enabled: id.length > 0,
    }),

  boards: () =>
    queryOptions({
      queryKey: [...queryRoots.crm, "boards"],
      queryFn: () => apiFetch<{ items: KanbanBoardListItem[] }>("crm/boards?page=1&pageSize=50"),
      staleTime: 15_000,
    }),

  board: (id: string) =>
    queryOptions({
      queryKey: [...queryRoots.crm, "board", id],
      queryFn: () => apiFetch<KanbanBoardDetail>(`crm/boards/${id}`),
      enabled: id.length > 0,
    }),
};

/* ----------------------------------------------------------------------------
 * Mutations — plain functions; hooks wrap them with invalidation + toast.
 * -------------------------------------------------------------------------- */

export interface ContactInput {
  firstName: string;
  lastName: string;
  companyId?: string | null;
  email?: string | null;
  phone?: string | null;
  position?: string | null;
  notes?: string | null;
  tags?: string[];
  status: string;
}

export function createContact(input: ContactInput): Promise<{ id: string }> {
  return apiFetch<{ id: string }>("crm/contacts", { method: "POST", body: input });
}

export function updateContact(id: string, input: ContactInput): Promise<Contact> {
  return apiFetch<Contact>(`crm/contacts/${id}`, { method: "PATCH", body: input });
}

export function deleteContact(id: string): Promise<void> {
  return apiFetch<void>(`crm/contacts/${id}`, { method: "DELETE" });
}

export interface InteractionInput {
  type: string;
  occurredAt?: string | null;
  body?: string | null;
}

export function addInteraction(contactId: string, input: InteractionInput): Promise<{ id: string }> {
  return apiFetch<{ id: string }>(`crm/contacts/${contactId}/interactions`, {
    method: "POST",
    body: input,
  });
}

export interface MeetingInput {
  contactId?: string | null;
  dealId?: string | null;
  title: string;
  scheduledAt: string;
  durationMinutes: number;
  location?: string | null;
  notes?: string | null;
}

export function createMeeting(input: MeetingInput): Promise<{ id: string }> {
  return apiFetch<{ id: string }>("crm/meetings", { method: "POST", body: input });
}

export function updateMeeting(id: string, input: MeetingInput): Promise<Meeting> {
  return apiFetch<Meeting>(`crm/meetings/${id}`, { method: "PATCH", body: input });
}

export function cancelMeeting(id: string): Promise<void> {
  return apiFetch<void>(`crm/meetings/${id}/cancel`, { method: "POST" });
}

export function completeMeeting(id: string, outcome: string | null): Promise<void> {
  return apiFetch<void>(`crm/meetings/${id}/complete`, { method: "POST", body: { outcome } });
}

export interface DealInput {
  contactId?: string | null;
  title: string;
  amountCents: number;
  currency?: string | null;
  stage?: string | null;
  expectedCloseAt?: string | null;
  notes?: string | null;
}

export function createDeal(input: DealInput): Promise<{ id: string }> {
  return apiFetch<{ id: string }>("crm/deals", { method: "POST", body: input });
}

export function updateDeal(id: string, input: Partial<DealInput>): Promise<Deal> {
  return apiFetch<Deal>(`crm/deals/${id}`, { method: "PATCH", body: input });
}

export function moveDealStage(id: string, stage: string): Promise<Deal> {
  return apiFetch<Deal>(`crm/deals/${id}/stage`, { method: "POST", body: { stage } });
}

export function deleteDeal(id: string): Promise<void> {
  return apiFetch<void>(`crm/deals/${id}`, { method: "DELETE" });
}

export interface TaskInput {
  contactId?: string | null;
  dealId?: string | null;
  title: string;
  description?: string | null;
  dueAt?: string | null;
  priority?: string | null;
}

export function createTask(input: TaskInput): Promise<{ id: string }> {
  return apiFetch<{ id: string }>("crm/tasks", { method: "POST", body: input });
}

export function updateTask(id: string, input: Partial<TaskInput>): Promise<CrmTask> {
  return apiFetch<CrmTask>(`crm/tasks/${id}`, { method: "PATCH", body: input });
}

export function completeTask(id: string): Promise<void> {
  return apiFetch<void>(`crm/tasks/${id}/complete`, { method: "POST" });
}

export function deleteTask(id: string): Promise<void> {
  return apiFetch<void>(`crm/tasks/${id}`, { method: "DELETE" });
}

export interface CompanyInput {
  name: string;
  domain?: string | null;
  industry?: string | null;
  identificationNumber?: string | null;
  taxIdentificationNumber?: string | null;
  registeredAddress?: string | null;
  city?: string | null;
  postalCode?: string | null;
  country?: string | null;
  notes?: string | null;
}

export function createCompany(input: CompanyInput): Promise<{ id: string }> {
  return apiFetch<{ id: string }>("crm/companies", { method: "POST", body: input });
}

export function updateCompany(id: string, input: Partial<CompanyInput>): Promise<Company> {
  return apiFetch<Company>(`crm/companies/${id}`, { method: "PATCH", body: input });
}

export function deleteCompany(id: string): Promise<void> {
  return apiFetch<void>(`crm/companies/${id}`, { method: "DELETE" });
}

export function createBoard(name: string): Promise<{ id: string }> {
  return apiFetch<{ id: string }>("crm/boards", { method: "POST", body: { name } });
}

export interface KanbanCardInput {
  columnId: string;
  title: string;
  description?: string | null;
  contactId?: string | null;
  dealId?: string | null;
  meetingId?: string | null;
  taskId?: string | null;
  assigneeUserId?: string | null;
  priority?: string | null;
  labels?: string[] | null;
  startAt?: string | null;
  dueAt?: string | null;
}

export function createCard(boardId: string, input: KanbanCardInput): Promise<{ id: string }> {
  return apiFetch<{ id: string }>(`crm/boards/${boardId}/cards`, { method: "POST", body: input });
}

export function moveCard(cardId: string, columnId: string, position: number): Promise<void> {
  return apiFetch<void>(`crm/cards/${cardId}/move`, { method: "POST", body: { columnId, position } });
}

export function deleteCard(id: string): Promise<void> {
  return apiFetch<void>(`crm/cards/${id}`, { method: "DELETE" });
}
