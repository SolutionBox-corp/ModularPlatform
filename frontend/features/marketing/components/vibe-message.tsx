"use client";

import { useLocale, useTranslations } from "next-intl";
import { cn } from "@/lib/utils";
import { ToolTrace } from "@/features/marketing/components/tool-trace";
import type { ConversationMessage } from "@/features/marketing/api";

/**
 * A single chat bubble. User turns align right (primary), assistant turns align
 * left (muted). Content is plain text — rendered with `whitespace-pre-wrap`, NO
 * markdown dependency. Assistant tool calls render as a collapsible trace below.
 *
 * `pending` flags an optimistic user turn that hasn't been persisted yet.
 */
export function VibeMessage({
  message,
  pending = false,
}: {
  message: ConversationMessage;
  pending?: boolean;
}) {
  const t = useTranslations("marketing");
  const locale = useLocale();
  const isUser = message.role === "user";

  return (
    <div className={cn("flex flex-col gap-1", isUser ? "items-end" : "items-start")}>
      <div
        className={cn(
          "max-w-[85%] rounded-2xl px-3.5 py-2 text-sm",
          isUser
            ? "bg-primary text-primary-foreground"
            : "bg-muted text-foreground",
          pending && "opacity-70",
        )}
      >
        <p className="whitespace-pre-wrap break-words">{message.content}</p>
        {!isUser && message.toolCallsJson && (
          <ToolTrace toolCallsJson={message.toolCallsJson} />
        )}
      </div>
      <span className="px-1 text-[0.7rem] text-muted-foreground">
        {pending
          ? t("vibeChat.sending")
          : new Date(message.createdAt).toLocaleTimeString(locale, {
              hour: "2-digit",
              minute: "2-digit",
            })}
      </span>
    </div>
  );
}
