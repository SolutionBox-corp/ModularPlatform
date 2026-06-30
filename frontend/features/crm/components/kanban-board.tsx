"use client";

import { useState, type FormEvent } from "react";
import { useTranslations } from "next-intl";
import { useQuery } from "@tanstack/react-query";
import {
  DndContext,
  PointerSensor,
  useSensor,
  useSensors,
  useDroppable,
  useDraggable,
  type DragEndEvent,
} from "@dnd-kit/core";
import { BriefcaseIcon, CalendarIcon, ContactIcon, GripVerticalIcon, PencilIcon, PlusIcon, Trash2Icon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
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

const PRIORITY_VARIANT: Record<string, "default" | "secondary" | "outline" | "destructive"> = {
  low: "outline",
  normal: "secondary",
  high: "destructive",
};

function toDateInput(iso: string | null): string {
  return iso ? new Date(iso).toISOString().slice(0, 10) : "";
}

function taskLabel(taskOptions: CrmTask[], taskId: string | null): string | null {
  return taskId ? taskOptions.find((task) => task.id === taskId)?.title ?? null : null;
}

function userLabel(users: TenantUserListItem[], userId: string | null): string | null {
  const user = userId ? users.find((item) => item.id === userId) : null;
  return user ? user.displayName ?? user.email : null;
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

function Card({ card, taskOptions, users }: { card: KanbanCard; taskOptions: CrmTask[]; users: TenantUserListItem[] }) {
  const t = useTranslations("crm");
  const del = useDeleteCard();
  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({ id: card.id });
  const due = card.dueAt ? new Date(card.dueAt) : null;
  const isOverdue = due ? due.getTime() < TODAY_START.getTime() : false;

  return (
    <div
      ref={setNodeRef}
      style={transform ? { transform: `translate(${transform.x}px, ${transform.y}px)` } : undefined}
      className={`rounded-lg border bg-card p-3 text-sm shadow-sm transition-shadow hover:shadow-md ${isDragging ? "opacity-50" : ""}`}
    >
      <div className="flex items-start justify-between gap-2">
        <div {...attributes} {...listeners} className="flex flex-1 cursor-grab items-start gap-1.5">
          <GripVerticalIcon className="mt-0.5 h-3.5 w-3.5 shrink-0 text-muted-foreground" />
          <div className="min-w-0 space-y-1">
            <div className="font-medium leading-snug">{card.title}</div>
            {card.description && (
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
        {due && (
          <Badge variant={isOverdue ? "destructive" : "secondary"} className="gap-1 text-[11px]">
            <CalendarIcon className="h-3 w-3" />
            {due.toLocaleDateString()}
          </Badge>
        )}
        {card.contactId && (
          <Badge variant="outline" className="gap-1 text-[11px]">
            <ContactIcon className="h-3 w-3" />
            {t("board.linkedContact")}
          </Badge>
        )}
        {card.dealId && (
          <Badge variant="outline" className="gap-1 text-[11px]">
            <BriefcaseIcon className="h-3 w-3" />
            {t("board.linkedDeal")}
          </Badge>
        )}
        {card.taskId && <Badge variant="outline" className="text-[11px]">{taskLabel(taskOptions, card.taskId) ?? t("board.linkedTask")}</Badge>}
        {card.meetingId && <Badge variant="outline" className="text-[11px]">{t("board.linkedMeeting")}</Badge>}
        {card.assigneeUserId && <Badge variant="secondary" className="text-[11px]">{userLabel(users, card.assigneeUserId) ?? t("board.assigned")}</Badge>}
        {card.labels.slice(0, 3).map((label) => (
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
}: {
  column: KanbanColumn;
  cards: KanbanCard[];
  boardId: string;
  taskOptions: CrmTask[];
  users: TenantUserListItem[];
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
  const overWip = column.wipLimit !== null && cards.length > column.wipLimit;

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
    <div ref={setNodeRef} className={`flex w-80 shrink-0 flex-col gap-3 rounded-xl border p-3 ${isOver ? "bg-accent" : "bg-muted/30"}`}>
      <div className="h-1 rounded-full" style={{ backgroundColor: column.color }} />
      <div className="flex items-center justify-between gap-2">
        <div>
          <div className="text-sm font-semibold">{column.name}</div>
          <div className="text-xs text-muted-foreground">{t(`board.group.${column.group}`)}</div>
        </div>
        <Badge variant={overWip ? "destructive" : "secondary"}>
          {column.wipLimit ? `${cards.length}/${column.wipLimit}` : cards.length}
        </Badge>
      </div>
      <div className="flex min-h-20 flex-col gap-2">{cards.map((c) => <Card key={c.id} card={c} taskOptions={taskOptions} users={users} />)}</div>
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
  );
}

export function KanbanBoardView({ boardId }: { boardId: string }) {
  const { data } = useQuery(crmQueries.board(boardId));
  const { data: tasks } = useQuery(crmQueries.tasks({ page: 1, pageSize: 100, status: "open" }));
  const { data: users } = useQuery(accountQueries.users({ page: 1, pageSize: 50 }));
  const move = useMoveCard();
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 4 } }));

  if (!data) return null;

  const onDragEnd = (e: DragEndEvent) => {
    const cardId = String(e.active.id);
    const columnId = e.over ? String(e.over.id) : null;
    if (!columnId) return;
    const target = data.cards.filter((c) => c.columnId === columnId);
    move.mutate({ cardId, columnId, position: target.length });
  };

  return (
    <DndContext sensors={sensors} onDragEnd={onDragEnd}>
      <div className="flex gap-3 overflow-x-auto pb-4">
        {data.columns.map((col) => (
          <Column key={col.id} column={col} boardId={boardId}
            taskOptions={tasks?.items ?? []}
            users={users?.items ?? []}
            cards={data.cards.filter((c) => c.columnId === col.id).sort((a, b) => a.position - b.position)} />
        ))}
      </div>
    </DndContext>
  );
}
