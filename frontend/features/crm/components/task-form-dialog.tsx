"use client";

import { useState, type ReactNode } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { TASK_PRIORITIES, type TaskInput } from "@/features/crm/api";
import { useCreateTask } from "@/features/crm/hooks";
import { buildTaskSchema, type TaskFormValues } from "@/features/crm/schema";

interface TaskFormDialogProps {
  contactId?: string | null;
  dealId?: string | null;
  trigger: ReactNode;
}

export function TaskFormDialog({ contactId, dealId, trigger }: TaskFormDialogProps) {
  const t = useTranslations("crm");
  const [open, setOpen] = useState(false);
  const [priority, setPriority] = useState<(typeof TASK_PRIORITIES)[number]>("normal");
  const createMutation = useCreateTask();

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<TaskFormValues>({
    resolver: zodResolver(buildTaskSchema(t)),
    values: { title: "", description: "", dueAt: "", priority },
  });

  const onSubmit = handleSubmit(async (values) => {
    const input: TaskInput = {
      contactId: contactId ?? null,
      dealId: dealId ?? null,
      title: values.title.trim(),
      description: values.description?.trim() || null,
      dueAt: values.dueAt ? new Date(values.dueAt).toISOString() : null,
      priority,
    };
    try {
      await createMutation.mutateAsync(input);
      reset();
      setOpen(false);
    } catch (err: unknown) {
      if (err && typeof err === "object" && "fieldErrors" in err && err.fieldErrors) {
        const fieldErrors = err.fieldErrors as Record<string, string[]>;
        (Object.keys(fieldErrors) as (keyof TaskFormValues)[]).forEach((field) => {
          setError(field, { message: fieldErrors[field]?.[0] });
        });
      }
    }
  });

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger render={trigger as React.ReactElement} />
      <DialogContent>
        <form onSubmit={onSubmit} noValidate>
          <DialogHeader>
            <DialogTitle>{t("taskForm.createTitle")}</DialogTitle>
          </DialogHeader>

          <div className="space-y-3 py-4">
            <div className="space-y-1.5">
              <Label htmlFor="tk-title">{t("taskForm.title")}</Label>
              <Input id="tk-title" aria-invalid={!!errors.title} {...register("title")} />
              {errors.title && <p className="text-xs text-destructive">{errors.title.message}</p>}
            </div>

            <div className="grid grid-cols-2 gap-3">
              <div className="space-y-1.5">
                <Label htmlFor="tk-due">{t("taskForm.dueAt")}</Label>
                <Input id="tk-due" type="datetime-local" {...register("dueAt")} />
              </div>
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
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="tk-desc">{t("taskForm.description")}</Label>
              <Textarea id="tk-desc" rows={3} {...register("description")} />
            </div>
          </div>

          <DialogFooter>
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting ? t("taskForm.saving") : t("taskForm.save")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
