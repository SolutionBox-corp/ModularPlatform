"use client";

import { useState } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { useQuery } from "@tanstack/react-query";
import { PlusIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { crmQueries } from "@/features/crm/api";
import { useCreateBoard } from "@/features/crm/hooks";

export function BoardList() {
  const t = useTranslations("crm");
  const { data } = useQuery(crmQueries.boards());
  const create = useCreateBoard();
  const [name, setName] = useState("");

  return (
    <div className="space-y-4">
      <form
        className="flex gap-2"
        onSubmit={(e) => { e.preventDefault(); if (name.trim()) { create.mutate(name.trim()); setName(""); } }}
      >
        <Input value={name} onChange={(e) => setName(e.target.value)} placeholder={t("board.newPlaceholder")} className="max-w-xs" />
        <Button type="submit" size="sm"><PlusIcon className="h-3.5 w-3.5 mr-1.5" />{t("board.new")}</Button>
      </form>
      <ul className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
        {(data?.items ?? []).map((b) => (
          <li key={b.id}>
            <Link href={`/crm/board/${b.id}`} className="block rounded-lg border p-4 hover:bg-accent">
              <span className="font-medium">{b.name}</span>
            </Link>
          </li>
        ))}
      </ul>
      {(data?.items.length ?? 0) === 0 && <p className="text-sm text-muted-foreground">{t("board.empty")}</p>}
    </div>
  );
}
