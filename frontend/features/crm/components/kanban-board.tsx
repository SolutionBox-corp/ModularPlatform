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
import { PlusIcon, Trash2Icon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { crmQueries, type KanbanCard } from "@/features/crm/api";
import { useCreateCard, useDeleteCard, useMoveCard } from "@/features/crm/hooks";

function Card({ card }: { card: KanbanCard }) {
  const del = useDeleteCard();
  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({ id: card.id });
  return (
    <div
      ref={setNodeRef}
      style={transform ? { transform: `translate(${transform.x}px, ${transform.y}px)` } : undefined}
      className={`rounded-md border bg-card p-2 text-sm shadow-sm ${isDragging ? "opacity-50" : ""}`}
    >
      <div className="flex items-start justify-between gap-2">
        <span {...attributes} {...listeners} className="flex-1 cursor-grab">{card.title}</span>
        <button className="text-muted-foreground hover:text-destructive" onClick={() => del.mutate(card.id)} aria-label="delete">
          <Trash2Icon className="h-3.5 w-3.5" />
        </button>
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
    <div ref={setNodeRef} className={`flex w-72 shrink-0 flex-col gap-2 rounded-lg border p-3 ${isOver ? "bg-accent" : "bg-muted/30"}`}>
      <div className="text-sm font-semibold">{name} <span className="text-muted-foreground">({cards.length})</span></div>
      <div className="flex flex-col gap-2">{cards.map((c) => <Card key={c.id} card={c} />)}</div>
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
