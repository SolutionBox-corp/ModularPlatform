"use client";

import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useTranslations } from "next-intl";
import { useQuery } from "@tanstack/react-query";
import { PencilIcon, PhoneIcon, MailIcon, StickyNoteIcon, CalendarIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { crmQueries, INTERACTION_TYPES, type Interaction } from "@/features/crm/api";
import { useAddInteraction } from "@/features/crm/hooks";
import { buildInteractionSchema, type InteractionFormValues } from "@/features/crm/schema";
import { ContactFormDialog } from "@/features/crm/components/contact-form-dialog";
import { MeetingsTable } from "@/features/crm/components/meetings-table";

const TYPE_ICON: Record<string, typeof PhoneIcon> = {
  call: PhoneIcon,
  email: MailIcon,
  note: StickyNoteIcon,
  meeting: CalendarIcon,
};

function AddInteractionForm({ contactId }: { contactId: string }) {
  const t = useTranslations("crm");
  const addMutation = useAddInteraction(contactId);
  const {
    register,
    control,
    handleSubmit,
    reset,
    formState: { isSubmitting },
  } = useForm<InteractionFormValues>({
    resolver: zodResolver(buildInteractionSchema(t)),
    defaultValues: { type: "note", body: "" },
  });

  const onSubmit = handleSubmit(async (values) => {
    await addMutation.mutateAsync({ type: values.type, body: values.body?.trim() || null });
    reset({ type: "note", body: "" });
  });

  return (
    <form onSubmit={onSubmit} className="flex items-end gap-2">
      <div className="space-y-1.5 w-32">
        <Label htmlFor="i-type">{t("interactionForm.type")}</Label>
        <Controller
          control={control}
          name="type"
          render={({ field }) => (
            <Select value={field.value} onValueChange={field.onChange}>
              <SelectTrigger id="i-type">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {INTERACTION_TYPES.map((ty) => (
                  <SelectItem key={ty} value={ty}>
                    {t(`interactionType.${ty}`)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        />
      </div>
      <div className="space-y-1.5 flex-1">
        <Label htmlFor="i-body">{t("interactionForm.body")}</Label>
        <Input id="i-body" placeholder={t("interactionForm.bodyPlaceholder")} {...register("body")} />
      </div>
      <Button type="submit" size="sm" disabled={isSubmitting}>
        {t("interactionForm.add")}
      </Button>
    </form>
  );
}

function Timeline({ contactId }: { contactId: string }) {
  const t = useTranslations("crm");
  const { data, isLoading } = useQuery(crmQueries.interactions(contactId));

  if (isLoading) {
    return <Skeleton className="h-20 w-full" />;
  }

  if (!data || data.length === 0) {
    return <p className="text-sm text-muted-foreground">{t("timeline.empty")}</p>;
  }

  return (
    <ul className="space-y-3">
      {data.map((i: Interaction) => {
        const Icon = TYPE_ICON[i.type] ?? StickyNoteIcon;
        return (
          <li key={i.id} className="flex gap-3">
            <div className="mt-0.5 text-muted-foreground">
              <Icon className="h-4 w-4" aria-hidden="true" />
            </div>
            <div className="flex-1 space-y-0.5">
              <div className="flex items-center gap-2">
                <Badge variant="outline" className="text-xs">
                  {t(`interactionType.${i.type}`)}
                </Badge>
                <span className="text-xs text-muted-foreground">
                  {new Date(i.occurredAt).toLocaleString()}
                </span>
              </div>
              {i.body && <p className="text-sm">{i.body}</p>}
            </div>
          </li>
        );
      })}
    </ul>
  );
}

export function ContactDetail({ contactId }: { contactId: string }) {
  const t = useTranslations("crm");
  const { data: contact, isLoading } = useQuery(crmQueries.contact(contactId));

  if (isLoading) {
    return <Skeleton className="h-40 w-full" />;
  }

  if (!contact) {
    return <p className="text-sm text-muted-foreground">{t("contacts.notFound")}</p>;
  }

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader className="flex flex-row items-start justify-between">
          <div>
            <CardTitle className="flex items-center gap-2">
              {contact.fullName}
              <Badge variant="secondary">{t(`status.${contact.status}`)}</Badge>
            </CardTitle>
            <CardDescription>
              {[contact.position, contact.company].filter(Boolean).join(" · ") || "—"}
            </CardDescription>
          </div>
          <ContactFormDialog
            contact={contact}
            trigger={
              <Button variant="outline" size="sm">
                <PencilIcon className="h-3.5 w-3.5 mr-1.5" />
                {t("contacts.edit")}
              </Button>
            }
          />
        </CardHeader>
        <CardContent className="grid grid-cols-2 gap-3 text-sm">
          <div>
            <span className="text-muted-foreground">{t("contactForm.email")}: </span>
            {contact.email ?? "—"}
          </div>
          <div>
            <span className="text-muted-foreground">{t("contactForm.phone")}: </span>
            {contact.phone ?? "—"}
          </div>
          {contact.tags.length > 0 && (
            <div className="col-span-2 flex flex-wrap gap-1">
              {contact.tags.map((tag) => (
                <Badge key={tag} variant="outline">
                  {tag}
                </Badge>
              ))}
            </div>
          )}
          {contact.notes && <p className="col-span-2 text-muted-foreground">{contact.notes}</p>}
        </CardContent>
      </Card>

      <section className="space-y-3">
        <h2 className="text-sm font-medium text-muted-foreground">{t("timeline.heading")}</h2>
        <AddInteractionForm contactId={contactId} />
        <Timeline contactId={contactId} />
      </section>

      <section className="space-y-3">
        <h2 className="text-sm font-medium text-muted-foreground">{t("meetings.heading")}</h2>
        <MeetingsTable contactId={contactId} />
      </section>
    </div>
  );
}
