"use client";

import { useState, type FormEvent } from "react";
import { useTranslations } from "next-intl";
import { useQuery } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  DndContext,
  PointerSensor,
  useSensor,
  useSensors,
  useDroppable,
  useDraggable,
  type DragEndEvent,
} from "@dnd-kit/core";
import {
  AlertTriangleIcon,
  BriefcaseIcon,
  CalendarIcon,
  ContactIcon,
  FilterIcon,
  GripVerticalIcon,
  Maximize2Icon,
  Minimize2Icon,
  PencilIcon,
  PlusIcon,
  SearchIcon,
  Trash2Icon,
  UsersIcon,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Progress } from "@/components/ui/progress";
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { accountQueries, type TenantUserListItem } from "@/features/account/api";
import { crmQueries, TASK_PRIORITIES, type CrmTask, type KanbanCard, type KanbanColumn } from "@/features/crm/api";
import { useCreateCard, useDeleteCard, useMoveCard, useUpdateCard } from "@/features/crm/hooks";

const TODAY_START = new Date();
TODAY_START.setHours(0, 0, 0, 0);
const ALL = "all";
const UNASSIGNED = "unassigned";
const DISPLAY_KEYS = ["description", "links", "assignee", "labels", "due"] as const;
type DisplayKey = (typeof DISPLAY_KEYS)[number];
type DisplayPrefs = Record<DisplayKey, boolean>;
const DEFAULT_DISPLAY_PREFS: DisplayPrefs = {
  description: true,
  links: true,
  assignee: true,
  labels: true,
  due: true,
};

const PRIORITY_VARIANT: Record<string, "default" | "secondary" | "outline" | "destructive"> = {
  low: "outline",
  normal: "secondary",
  high: "destructive",
};

const PRIORITY_COLOR: Record<string, string> = {
  low: "#94A3B8",
  normal: "#2563EB",
  high: "#DC2626",
};

function toDateInput(iso: string | null): string {
  return iso ? new Date(iso).toISOString().slice(0, 10) : "";
}

function formatMoney(cents: number, currency = "USD") {
  return new Intl.NumberFormat(undefined, { style: "currency", currency, maximumFractionDigits: 0 }).format(cents / 100);
}

function isOverdue(card: KanbanCard): boolean {
  return card.dueAt ? new Date(card.dueAt).getTime() < TODAY_START.getTime() : false;
}

function taskLabel(taskOptions: CrmTask[], taskId: string | null): string | null {
  return taskId ? taskOptions.find((task) => task.id === taskId)?.title ?? null : null;
}

function userLabel(users: TenantUserListItem[], userId: string | null): string | null {
  const user = userId ? users.find((item) => item.id === userId) : null;
  return user ? user.displayName ?? user.email : null;
}

function matchesBoardFilters(
  card: KanbanCard,
  taskOptions: CrmTask[],
  assigneeFilter: string,
  priorityFilter: string,
  search: string,
) {
  if (priorityFilter !== ALL && card.priority !== priorityFilter) return false;
  if (assigneeFilter === UNASSIGNED && card.assigneeUserId) return false;
  if (assigneeFilter !== ALL && assigneeFilter !== UNASSIGNED && card.assigneeUserId !== assigneeFilter) return false;

  const term = search.trim().toLowerCase();
  if (!term) return true;

  const task = taskLabel(taskOptions, card.taskId);
  const haystack = [
    card.title,
    card.description ?? "",
    task ?? "",
    ...card.labels,
  ].join(" ").toLowerCase();
  return haystack.includes(term);
}

function EditCardDialog({ card, taskOptions, users }: { card: KanbanCard; taskOptions: CrmTask[]; users: TenantUserListItem[] }) {
  const t = useTranslations("crm");
  const [open, setOpen] = useState(false);
  const [title, setTitle] = useState(card.title);
  const [description, setDescription] = useState(card.description ?? "");
  const [priority, setPriority] = useState<(typeof TASK_PRIORITIES)[number]>(card.priority as (typeof TASK_PRIORITIES)[number]);
  const [labels, setLabels] = useState(card.labels.join(", "));
  const [dueAt, setDueAt] = useState(toDateInput(card.dueAt));
  const [taskId, setTaskId] = useState(card.taskId ?? "none");
  const [assigneeUserId, setAssigneeUserId] = useState(card.assigneeUserId ?? "none");
  const update = useUpdateCard(card.id);

  const submit = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    if (!title.trim()) return;
    await update.mutateAsync({
      title: title.trim(),
      description: description.trim() || null,
      priority,
      ...(taskId === "none" ? {} : { taskId }),
      assigneeUserId: assigneeUserId === "none" ? null : assigneeUserId,
      labels: labels.split(",").map((label) => label.trim()).filter(Boolean),
      dueAt: dueAt ? new Date(dueAt).toISOString() : null,
    });
    setOpen(false);
  };

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger
        render={
          <button className="text-muted-foreground hover:text-foreground" aria-label={t("board.editCard")}>
            <PencilIcon className="h-3.5 w-3.5" />
          </button>
        }
      />
      <DialogContent>
        <form onSubmit={submit} noValidate>
          <DialogHeader>
            <DialogTitle>{t("board.editCard")}</DialogTitle>
          </DialogHeader>
          <div className="space-y-3 py-4">
            <div className="space-y-1.5">
              <Label htmlFor={`card-title-${card.id}`}>{t("board.cardTitle")}</Label>
              <Input id={`card-title-${card.id}`} value={title} onChange={(e) => setTitle(e.target.value)} />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor={`card-desc-${card.id}`}>{t("board.cardDescription")}</Label>
              <Textarea id={`card-desc-${card.id}`} rows={3} value={description} onChange={(e) => setDescription(e.target.value)} />
            </div>
            <div className="grid grid-cols-2 gap-3">
              <div className="space-y-1.5">
                <Label>{t("taskForm.priority")}</Label>
                <Select value={priority} onValueChange={(v) => setPriority((v ?? "normal") as (typeof TASK_PRIORITIES)[number])}>
                  <SelectTrigger className="w-full">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {TASK_PRIORITIES.map((p) => (
                      <SelectItem key={p} value={p}>
                        {t(`taskPriority.${p}`)}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-1.5">
                <Label htmlFor={`card-due-${card.id}`}>{t("board.dueAt")}</Label>
                <Input id={`card-due-${card.id}`} type="date" value={dueAt} onChange={(e) => setDueAt(e.target.value)} />
              </div>
            </div>
            <div className="space-y-1.5">
              <Label htmlFor={`card-labels-${card.id}`}>{t("board.labels")}</Label>
              <Input id={`card-labels-${card.id}`} value={labels} onChange={(e) => setLabels(e.target.value)} />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor={`card-task-${card.id}`}>{t("board.linkedTask")}</Label>
              <Select value={taskId} onValueChange={(value) => setTaskId(value ?? "none")}>
                <SelectTrigger id={`card-task-${card.id}`} className="w-full">
                  <span data-slot="select-value" className="flex flex-1 text-left">
                    {taskId === "none" ? t("board.noTask") : taskLabel(taskOptions, taskId) ?? t("board.unknownTask")}
                  </span>
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="none">{t("board.noTask")}</SelectItem>
                  {taskOptions.map((task) => (
                    <SelectItem key={task.id} value={task.id}>
                      {task.title}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-1.5">
              <Label htmlFor={`card-assignee-${card.id}`}>{t("board.assignee")}</Label>
              <Select value={assigneeUserId} onValueChange={(value) => setAssigneeUserId(value ?? "none")}>
                <SelectTrigger id={`card-assignee-${card.id}`} className="w-full">
                  <span data-slot="select-value" className="flex flex-1 text-left">
                    {assigneeUserId === "none" ? t("board.unassigned") : userLabel(users, assigneeUserId) ?? t("board.unknownAssignee")}
                  </span>
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="none">{t("board.unassigned")}</SelectItem>
                  {users.map((user) => (
                    <SelectItem key={user.id} value={user.id}>
                      {user.displayName ?? user.email}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>
          <DialogFooter>
            <Button type="submit" disabled={update.isPending}>
              {update.isPending ? t("contactForm.saving") : t("contactForm.save")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function Card({
  card,
  taskOptions,
  users,
  display,
}: {
  card: KanbanCard;
  taskOptions: CrmTask[];
  users: TenantUserListItem[];
  display: DisplayPrefs;
}) {
  const t = useTranslations("crm");
  const del = useDeleteCard();
  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({ id: card.id });
  const due = card.dueAt ? new Date(card.dueAt) : null;
  const overdue = isOverdue(card);

  return (
    <div
      ref={setNodeRef}
      style={transform ? { transform: `translate(${transform.x}px, ${transform.y}px)` } : undefined}
      className={`relative overflow-hidden rounded-lg border bg-card p-3 pl-4 text-sm shadow-sm transition-all hover:-translate-y-0.5 hover:shadow-md ${isDragging ? "opacity-50" : ""}`}
    >
      <div className="absolute inset-y-0 left-0 w-1" style={{ backgroundColor: PRIORITY_COLOR[card.priority] ?? PRIORITY_COLOR.normal }} />
      <div className="flex items-start justify-between gap-2">
        <div {...attributes} {...listeners} className="flex flex-1 cursor-grab items-start gap-1.5">
          <GripVerticalIcon className="mt-0.5 h-3.5 w-3.5 shrink-0 text-muted-foreground" />
          <div className="min-w-0 space-y-1">
            <div className="font-medium leading-snug">{card.title}</div>
            {display.description && card.description && (
              <p className="line-clamp-2 text-xs leading-relaxed text-muted-foreground">{card.description}</p>
            )}
          </div>
        </div>
        <div className="flex items-center gap-1">
          <EditCardDialog card={card} taskOptions={taskOptions} users={users} />
          <button className="text-muted-foreground hover:text-destructive" onClick={() => del.mutate(card.id)} aria-label={t("board.deleteCard")}>
            <Trash2Icon className="h-3.5 w-3.5" />
          </button>
        </div>
      </div>
      <div className="mt-3 flex flex-wrap items-center gap-1.5">
        <Badge variant={PRIORITY_VARIANT[card.priority] ?? "secondary"} className="text-[11px]">
          {t(`taskPriority.${card.priority}`)}
        </Badge>
        {display.due && due && (
          <Badge variant={overdue ? "destructive" : "secondary"} className="gap-1 text-[11px]">
            <CalendarIcon className="h-3 w-3" />
            {due.toLocaleDateString()}
          </Badge>
        )}
        {display.links && card.contactId && (
          <Badge variant="outline" className="gap-1 text-[11px]">
            <ContactIcon className="h-3 w-3" />
            {t("board.linkedContact")}
          </Badge>
        )}
        {display.links && card.dealId && (
          <Badge variant="outline" className="gap-1 text-[11px]">
            <BriefcaseIcon className="h-3 w-3" />
            {card.dealAmountCents !== null ? formatMoney(card.dealAmountCents, card.dealCurrency ?? "USD") : t("board.linkedDeal")}
          </Badge>
        )}
        {display.links && card.taskId && <Badge variant="outline" className="text-[11px]">{taskLabel(taskOptions, card.taskId) ?? t("board.linkedTask")}</Badge>}
        {display.links && card.meetingId && <Badge variant="outline" className="text-[11px]">{t("board.linkedMeeting")}</Badge>}
        {display.assignee && card.assigneeUserId && <Badge variant="secondary" className="text-[11px]">{userLabel(users, card.assigneeUserId) ?? t("board.assigned")}</Badge>}
        {display.labels && card.labels.slice(0, 3).map((label) => (
          <Badge key={label} variant="outline" className="text-[11px]">
            {label}
          </Badge>
        ))}
      </div>
    </div>
  );
}

function Column({
  column,
  cards,
  boardId,
  taskOptions,
  users,
  allCards,
  collapsed,
  onToggleCollapse,
  display,
}: {
  column: KanbanColumn;
  cards: KanbanCard[];
  boardId: string;
  taskOptions: CrmTask[];
  users: TenantUserListItem[];
  allCards: KanbanCard[];
  collapsed: boolean;
  onToggleCollapse: () => void;
  display: DisplayPrefs;
}) {
  const t = useTranslations("crm");
  const { setNodeRef, isOver } = useDroppable({ id: column.id });
  const create = useCreateCard(boardId);
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [priority, setPriority] = useState<(typeof TASK_PRIORITIES)[number]>("normal");
  const [labels, setLabels] = useState("");
  const [dueAt, setDueAt] = useState("");
  const [taskId, setTaskId] = useState("none");
  const [assigneeUserId, setAssigneeUserId] = useState("none");
  const allColumnCardsCount = allCards.filter((card) => card.columnId === column.id).length;
  const overWip = column.wipLimit !== null && allColumnCardsCount > column.wipLimit;
  const atWipLimit = column.wipLimit !== null && allColumnCardsCount >= column.wipLimit;
  const wipProgress = column.wipLimit ? Math.min(100, Math.round((allColumnCardsCount / column.wipLimit) * 100)) : null;
  const columnDealAmount = allCards
    .filter((card) => card.columnId === column.id)
    .reduce((sum, card) => sum + (card.dealAmountCents ?? 0), 0);

  const submit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    if (!title.trim()) return;
    create.mutate({
      columnId: column.id,
      title: title.trim(),
      description: description.trim() || null,
      priority,
      taskId: taskId === "none" ? null : taskId,
      assigneeUserId: assigneeUserId === "none" ? null : assigneeUserId,
      labels: labels.split(",").map((label) => label.trim()).filter(Boolean),
      dueAt: dueAt ? new Date(dueAt).toISOString() : null,
    });
    setTitle("");
    setDescription("");
    setPriority("normal");
    setLabels("");
    setDueAt("");
    setTaskId("none");
    setAssigneeUserId("none");
  };

  return (
    collapsed ? (
      <button
        ref={setNodeRef}
        type="button"
        onClick={onToggleCollapse}
        className="flex h-[34rem] w-12 shrink-0 flex-col items-center gap-3 rounded-xl border bg-muted/30 p-2 text-sm shadow-sm hover:bg-accent"
        aria-label={t("board.expandColumn")}
      >
        <div className="h-1 w-8 rounded-full" style={{ backgroundColor: column.color }} />
        <Maximize2Icon className="h-3.5 w-3.5 text-muted-foreground" />
        <div className="mt-2 [writing-mode:vertical-rl] text-xs font-medium">{column.name}</div>
        <Badge variant={overWip || atWipLimit ? "destructive" : "secondary"}>{allColumnCardsCount}</Badge>
      </button>
    ) : (
    <div ref={setNodeRef} className={`flex w-80 shrink-0 flex-col gap-3 rounded-xl border p-3 shadow-sm transition-colors ${isOver ? "bg-accent" : "bg-muted/30"}`}>
      <div className="h-1 rounded-full" style={{ backgroundColor: column.color }} />
      <div className="flex items-center justify-between gap-2">
        <div>
          <div className="text-sm font-semibold">{column.name}</div>
          <div className="text-xs text-muted-foreground">{t(`board.group.${column.group}`)} · {formatMoney(columnDealAmount)}</div>
        </div>
        <div className="flex items-center gap-1">
          <Badge variant={overWip || atWipLimit ? "destructive" : "secondary"}>
            {column.wipLimit ? `${allColumnCardsCount}/${column.wipLimit}` : allColumnCardsCount}
          </Badge>
          <button className="text-muted-foreground hover:text-foreground" onClick={onToggleCollapse} aria-label={t("board.collapseColumn")}>
            <Minimize2Icon className="h-3.5 w-3.5" />
          </button>
        </div>
      </div>
      {atWipLimit && column.wipLimit && (
        <div className="rounded-md border border-destructive/30 bg-destructive/10 px-2 py-1 text-xs text-destructive">
          {t("board.wipLimitReached", { limit: column.wipLimit })}
        </div>
      )}
      {wipProgress !== null && <Progress value={wipProgress} className="h-1.5" />}
      <div className="flex min-h-24 flex-col gap-2">
        {cards.length === 0 ? (
          <div className="rounded-lg border border-dashed bg-background/50 p-4 text-center text-xs text-muted-foreground">
            {t("board.emptyColumn")}
          </div>
        ) : (
          cards.map((c) => <Card key={c.id} card={c} taskOptions={taskOptions} users={users} display={display} />)
        )}
      </div>
      <form
        className="space-y-2 rounded-lg border border-dashed bg-background/60 p-2"
        onSubmit={submit}
      >
        <Input value={title} onChange={(e) => setTitle(e.target.value)} placeholder={t("board.addCard")} className="h-8" />
        <Input value={description} onChange={(e) => setDescription(e.target.value)} placeholder={t("board.descriptionPlaceholder")} className="h-8" />
        <div className="grid grid-cols-2 gap-1.5">
          <Select value={priority} onValueChange={(v) => setPriority((v ?? "normal") as (typeof TASK_PRIORITIES)[number])}>
            <SelectTrigger className="h-8 w-full">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {TASK_PRIORITIES.map((p) => (
                <SelectItem key={p} value={p}>
                  {t(`taskPriority.${p}`)}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <Input value={dueAt} onChange={(e) => setDueAt(e.target.value)} type="date" className="h-8" aria-label={t("board.dueAt")} />
        </div>
        <div className="flex gap-1.5">
          <Input value={labels} onChange={(e) => setLabels(e.target.value)} placeholder={t("board.labelsPlaceholder")} className="h-8" />
          <Button size="icon" className="h-8 w-8 shrink-0" type="submit"><PlusIcon className="h-3.5 w-3.5" /></Button>
        </div>
        <Select value={taskId} onValueChange={(value) => setTaskId(value ?? "none")}>
          <SelectTrigger className="h-8 w-full">
            <span data-slot="select-value" className="flex flex-1 text-left">
              {taskId === "none" ? t("board.noTask") : taskLabel(taskOptions, taskId) ?? t("board.unknownTask")}
            </span>
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="none">{t("board.noTask")}</SelectItem>
            {taskOptions.map((task) => (
              <SelectItem key={task.id} value={task.id}>
                {task.title}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Select value={assigneeUserId} onValueChange={(value) => setAssigneeUserId(value ?? "none")}>
          <SelectTrigger className="h-8 w-full">
            <span data-slot="select-value" className="flex flex-1 text-left">
              {assigneeUserId === "none" ? t("board.unassigned") : userLabel(users, assigneeUserId) ?? t("board.unknownAssignee")}
            </span>
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="none">{t("board.unassigned")}</SelectItem>
            {users.map((user) => (
              <SelectItem key={user.id} value={user.id}>
                {user.displayName ?? user.email}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </form>
    </div>
    )
  );
}

export function KanbanBoardView({ boardId }: { boardId: string }) {
  const { data } = useQuery(crmQueries.board(boardId));
  const { data: tasks } = useQuery(crmQueries.tasks({ page: 1, pageSize: 100, status: "open" }));
  const { data: users } = useQuery(accountQueries.users({ page: 1, pageSize: 50 }));
  const t = useTranslations("crm");
  const [search, setSearch] = useState("");
  const [priorityFilter, setPriorityFilter] = useState(ALL);
  const [assigneeFilter, setAssigneeFilter] = useState(ALL);
  const [collapsedColumns, setCollapsedColumns] = useState<Set<string>>(() => new Set());
  const [display, setDisplay] = useState<DisplayPrefs>(DEFAULT_DISPLAY_PREFS);
  const move = useMoveCard();
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 4 } }));

  if (!data) return null;
  const taskOptions = tasks?.items ?? [];
  const userOptions = users?.items ?? [];
  const filteredCards = data.cards.filter((card) =>
    matchesBoardFilters(card, taskOptions, assigneeFilter, priorityFilter, search));
  const highPriorityCount = data.cards.filter((card) => card.priority === "high").length;
  const overdueCount = data.cards.filter(isOverdue).length;
  const assignedCount = data.cards.filter((card) => !!card.assigneeUserId).length;

  const toggleCollapsed = (columnId: string) => {
    setCollapsedColumns((current) => {
      const next = new Set(current);
      if (next.has(columnId)) next.delete(columnId);
      else next.add(columnId);
      return next;
    });
  };

  const toggleDisplay = (key: DisplayKey) => {
    setDisplay((current) => ({ ...current, [key]: !current[key] }));
  };

  const onDragEnd = (e: DragEndEvent) => {
    const cardId = String(e.active.id);
    const columnId = e.over ? String(e.over.id) : null;
    if (!columnId) return;
    const card = data.cards.find((candidate) => candidate.id === cardId);
    const targetColumn = data.columns.find((column) => column.id === columnId);
    if (!card || !targetColumn) return;
    if (card.columnId !== columnId && targetColumn.wipLimit !== null) {
      const targetCount = data.cards.filter((candidate) => candidate.columnId === columnId).length;
      if (targetCount >= targetColumn.wipLimit) {
        toast.warning(t("board.wipLimitReached", { limit: targetColumn.wipLimit }));
        return;
      }
    }
    const target = data.cards.filter((c) => c.columnId === columnId);
    move.mutate({ cardId, columnId, position: target.length });
  };

  return (
    <DndContext sensors={sensors} onDragEnd={onDragEnd}>
      <div className="space-y-4">
        <div className="rounded-xl border bg-card p-4 shadow-sm">
          <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
            <div>
              <div className="text-sm font-medium">{t("board.boardFocus")}</div>
              <div className="text-xs text-muted-foreground">
                {t("board.showingCards", { shown: filteredCards.length, total: data.cards.length })}
              </div>
            </div>
            <div className="flex flex-wrap gap-2">
              <Badge variant="secondary" className="gap-1"><UsersIcon className="h-3 w-3" />{assignedCount}</Badge>
              <Badge variant="destructive" className="gap-1"><AlertTriangleIcon className="h-3 w-3" />{overdueCount}</Badge>
              <Badge variant="outline">{t("taskPriority.high")}: {highPriorityCount}</Badge>
            </div>
          </div>
          <div className="grid gap-2 md:grid-cols-[1fr_180px_220px]">
            <div className="relative">
              <SearchIcon className="pointer-events-none absolute left-2 top-2 h-4 w-4 text-muted-foreground" />
              <Input
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder={t("board.searchPlaceholder")}
                className="h-8 pl-8"
              />
            </div>
            <Select value={priorityFilter} onValueChange={(value) => setPriorityFilter(value ?? ALL)}>
              <SelectTrigger className="h-8 w-full">
                <FilterIcon className="mr-1.5 h-3.5 w-3.5 text-muted-foreground" />
                <span data-slot="select-value" className="flex flex-1 text-left">
                  {priorityFilter === ALL ? t("board.allPriorities") : t(`taskPriority.${priorityFilter}`)}
                </span>
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={ALL}>{t("board.allPriorities")}</SelectItem>
                {TASK_PRIORITIES.map((priority) => (
                  <SelectItem key={priority} value={priority}>{t(`taskPriority.${priority}`)}</SelectItem>
                ))}
              </SelectContent>
            </Select>
            <Select value={assigneeFilter} onValueChange={(value) => setAssigneeFilter(value ?? ALL)}>
              <SelectTrigger className="h-8 w-full">
                <span data-slot="select-value" className="flex flex-1 text-left">
                  {assigneeFilter === ALL
                    ? t("filter.allAssignees")
                    : assigneeFilter === UNASSIGNED
                      ? t("board.unassigned")
                      : userLabel(userOptions, assigneeFilter) ?? t("board.unknownAssignee")}
                </span>
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={ALL}>{t("filter.allAssignees")}</SelectItem>
                <SelectItem value={UNASSIGNED}>{t("board.unassigned")}</SelectItem>
                {userOptions.map((user) => (
                  <SelectItem key={user.id} value={user.id}>{user.displayName ?? user.email}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="mt-3 flex flex-wrap gap-1.5">
            {DISPLAY_KEYS.map((key) => (
              <button
                key={key}
                type="button"
                onClick={() => toggleDisplay(key)}
                className={`rounded-full border px-2.5 py-1 text-xs ${display[key] ? "bg-primary text-primary-foreground" : "bg-background text-muted-foreground"}`}
              >
                {t(`board.display.${key}`)}
              </button>
            ))}
          </div>
        </div>

        <div className="flex gap-3 overflow-x-auto pb-4">
          {data.columns.map((col) => (
            <Column key={col.id} column={col} boardId={boardId}
              taskOptions={taskOptions}
              users={userOptions}
              allCards={data.cards}
              collapsed={collapsedColumns.has(col.id)}
              onToggleCollapse={() => toggleCollapsed(col.id)}
              display={display}
              cards={filteredCards.filter((c) => c.columnId === col.id).sort((a, b) => a.position - b.position)} />
          ))}
        </div>
      </div>
    </DndContext>
  );
}
