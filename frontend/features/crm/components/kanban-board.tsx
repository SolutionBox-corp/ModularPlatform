"use client";

import { useState } from "react";
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
import { BriefcaseIcon, CalendarIcon, ContactIcon, GripVerticalIcon, PlusIcon, Trash2Icon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { crmQueries, type KanbanCard } from "@/features/crm/api";
import { useCreateCard, useDeleteCard, useMoveCard } from "@/features/crm/hooks";

const TODAY_START = new Date();
TODAY_START.setHours(0, 0, 0, 0);

function Card({ card }: { card: KanbanCard }) {
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
        <button className="text-muted-foreground hover:text-destructive" onClick={() => del.mutate(card.id)} aria-label={t("board.deleteCard")}>
          <Trash2Icon className="h-3.5 w-3.5" />
        </button>
      </div>
      <div className="mt-3 flex flex-wrap items-center gap-1.5">
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
      </div>
    </div>
  );
}

function Column({ id, name, cards, boardId }: { id: string; name: string; cards: KanbanCard[]; boardId: string }) {
  const t = useTranslations("crm");
  const { setNodeRef, isOver } = useDroppable({ id });
  const create = useCreateCard(boardId);
  const [title, setTitle] = useState("");
  return (
    <div ref={setNodeRef} className={`flex w-80 shrink-0 flex-col gap-3 rounded-xl border p-3 ${isOver ? "bg-accent" : "bg-muted/30"}`}>
      <div className="flex items-center justify-between gap-2">
        <div className="text-sm font-semibold">{name}</div>
        <Badge variant="secondary">{cards.length}</Badge>
      </div>
      <div className="flex min-h-20 flex-col gap-2">{cards.map((c) => <Card key={c.id} card={c} />)}</div>
      <form
        className="flex gap-1"
        onSubmit={(e) => { e.preventDefault(); if (title.trim()) { create.mutate({ columnId: id, title: title.trim() }); setTitle(""); } }}
      >
        <Input value={title} onChange={(e) => setTitle(e.target.value)} placeholder={t("board.addCard")} className="h-8" />
        <Button size="icon" className="h-8 w-8" type="submit"><PlusIcon className="h-3.5 w-3.5" /></Button>
      </form>
    </div>
  );
}

export function KanbanBoardView({ boardId }: { boardId: string }) {
  const { data } = useQuery(crmQueries.board(boardId));
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
          <Column key={col.id} id={col.id} name={col.name} boardId={boardId}
            cards={data.cards.filter((c) => c.columnId === col.id).sort((a, b) => a.position - b.position)} />
        ))}
      </div>
    </DndContext>
  );
}
