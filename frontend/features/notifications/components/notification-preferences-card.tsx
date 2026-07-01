"use client";

import { useTranslations } from "next-intl";
import { toast } from "sonner";
import {
  useNotificationPreferences,
  useSetNotificationPreference,
} from "@/features/notifications/hooks";
import type { NotificationPreferenceItem } from "@/features/notifications/api";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { Switch } from "@/components/ui/switch";

const CHANNEL_ORDER: NotificationPreferenceItem["channel"][] = [
  "inapp",
  "email",
  "push",
];

export function NotificationPreferencesCard() {
  const t = useTranslations("account.notificationPreferences");
  const { data, isLoading } = useNotificationPreferences();
  const mutation = useSetNotificationPreference();
  const items = data?.items ?? [];

  function itemFor(channel: NotificationPreferenceItem["channel"]) {
    return items.find((item) => item.channel === channel);
  }

  function update(channel: "email" | "push", enabled: boolean) {
    mutation.mutate(
      { channel, enabled },
      {
        onSuccess: () => toast.success(t("saved")),
        onError: () => toast.error(t("error")),
      },
    );
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base font-semibold">{t("title")}</CardTitle>
        <CardDescription className="text-sm">
          {t("description")}
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        {isLoading ? (
          <>
            <Skeleton className="h-12 w-full" />
            <Skeleton className="h-12 w-full" />
            <Skeleton className="h-12 w-full" />
          </>
        ) : (
          CHANNEL_ORDER.map((channel) => {
            const item = itemFor(channel);
            const configurable = item?.configurable ?? false;
            const enabled = item?.enabled ?? true;

            return (
              <div
                key={channel}
                className="flex items-center justify-between gap-4 rounded-lg border border-border px-3 py-3"
              >
                <div className="min-w-0 space-y-0.5">
                  <Label htmlFor={`notification-pref-${channel}`}>
                    {t(`channels.${channel}.label`)}
                  </Label>
                  <p className="text-xs text-muted-foreground">
                    {t(`channels.${channel}.description`)}
                  </p>
                </div>
                <Switch
                  id={`notification-pref-${channel}`}
                  checked={enabled}
                  disabled={!configurable || mutation.isPending}
                  onCheckedChange={(checked) => {
                    if (channel === "email" || channel === "push") {
                      update(channel, checked);
                    }
                  }}
                  aria-label={t(`channels.${channel}.label`)}
                />
              </div>
            );
          })
        )}
      </CardContent>
    </Card>
  );
}
