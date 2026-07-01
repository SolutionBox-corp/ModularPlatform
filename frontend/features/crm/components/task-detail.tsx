"use client";

import { useState, type FormEvent } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { useQuery } from "@tanstack/react-query";
import { MessageSquareIcon } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { Textarea } from "@/components/ui/textarea";
import { accountQueries } from "@/features/account/api";
import { crmQueries } from "@/features/crm/api";
import { useAddTaskComment } from "@/features/crm/hooks";

const PRIORITY_VARIANT: Record<string, "default" | "secondary" | "outline" | "destructive"> = {
  low: "outline",
  normal: "secondary",
  high: "destructive",
};

export function TaskDetail({ taskId }: { taskId: string }) {
  const t = useTranslations("crm");
  const { data: task, isLoading } = useQuery(crmQueries.task(taskId));
  const { data: comments } = useQuery(crmQueries.taskComments(taskId));
  const { data: users } = useQuery(accountQueries.users({ page: 1, pageSize: 50 }));
  const addComment = useAddTaskComment(taskId);
  const [body, setBody] = useState("");

  if (isLoading) return <Skeleton className="h-40 w-full" />;
  if (!task) return <p className="text-sm text-muted-foreground">{t("tasks.notFound")}</p>;

  const assignee = task.assigneeUserId
    ? users?.items.find((user) => user.id === task.assigneeUserId)
    : null;

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!body.trim()) return;
    await addComment.mutateAsync(body.trim());
    setBody("");
  };

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader className="space-y-2">
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <CardTitle>{task.title}</CardTitle>
              <CardDescription>
                {task.dueAt ? new Date(task.dueAt).toLocaleString() : t("tasks.noDueDate")}
              </CardDescription>
            </div>
            <div className="flex flex-wrap gap-1.5">
              <Badge variant={task.status === "done" ? "default" : "secondary"}>{t(`taskStatus.${task.status}`)}</Badge>
              <Badge variant={PRIORITY_VARIANT[task.priority] ?? "secondary"}>{t(`taskPriority.${task.priority}`)}</Badge>
            </div>
          </div>
        </CardHeader>
        <CardContent className="grid gap-3 text-sm md:grid-cols-2">
          <div>
            <span className="text-muted-foreground">{t("board.assignee")}: </span>
            {assignee ? assignee.displayName ?? assignee.email : t("board.unassigned")}
          </div>
          <div>
            <span className="text-muted-foreground">{t("deals.links")}: </span>
            {task.dealId ? <Link href={`/crm/deals/${task.dealId}`} className="hover:underline">{t("board.linkedDeal")}</Link> : "—"}
          </div>
          {task.description && <p className="md:col-span-2 text-muted-foreground">{task.description}</p>}
        </CardContent>
      </Card>

      <section className="space-y-3">
        <h2 className="flex items-center gap-1.5 text-sm font-medium text-muted-foreground">
          <MessageSquareIcon className="h-4 w-4" />
          {t("tasks.workNotes")}
        </h2>
        <form onSubmit={submit} className="space-y-2 rounded-xl border bg-card p-3">
          <Label htmlFor="task-comment">{t("tasks.addWorkNote")}</Label>
          <Textarea id="task-comment" rows={3} value={body} onChange={(event) => setBody(event.target.value)} />
          <div className="flex justify-end">
            <Button type="submit" size="sm" disabled={addComment.isPending || !body.trim()}>
              {addComment.isPending ? t("contactForm.saving") : t("interactionForm.add")}
            </Button>
          </div>
        </form>
        <div className="space-y-2">
          {(comments?.items ?? []).length === 0 ? (
            <p className="text-sm text-muted-foreground">{t("tasks.noWorkNotes")}</p>
          ) : (
            comments!.items.map((comment) => (
              <div key={comment.id} className="rounded-xl border bg-muted/20 p-3">
                <div className="mb-1 text-xs text-muted-foreground">{new Date(comment.createdAt).toLocaleString()}</div>
                <p className="text-sm whitespace-pre-wrap">{comment.body}</p>
              </div>
            ))
          )}
        </div>
      </section>
    </div>
  );
}
