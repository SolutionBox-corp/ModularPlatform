"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { useTranslations } from "next-intl";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { PlusIcon, SendIcon, Trash2Icon, MessageSquareIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { ScrollArea } from "@/components/ui/scroll-area";
import { Skeleton } from "@/components/ui/skeleton";
import { cn } from "@/lib/utils";
import {
  useVibeConversations,
  useVibeConversation,
  useStartConversation,
  useSendMessage,
  useDeleteConversation,
} from "@/features/marketing/hooks";
import {
  buildChatMessageSchema,
  type ChatMessageInput,
} from "@/features/marketing/schema";
import { VibeMessage } from "@/features/marketing/components/vibe-message";
import type { ConversationMessage } from "@/features/marketing/api";

/**
 * Vibe AI chat. Left rail = conversation list (+ new); right = the open thread with
 * an optimistic user bubble. The assistant reply is NOT awaited here — it arrives
 * asynchronously: the backend processes the durable turn and publishes
 * "marketing.vibe_message_ready", which the realtime provider maps to an
 * invalidation of [marketing, "conversations"], refetching the open thread.
 */
export function VibeChat() {
  const t = useTranslations("marketing");
  // `selectedId` is the user's explicit pick; until they choose one we fall back to
  // the first conversation. Deriving the active id during render (instead of syncing
  // via an effect) keeps the component React-Compiler-clean.
  const [selectedId, setSelectedId] = useState<string>("");

  const { data: conversations, isLoading: listLoading } = useVibeConversations();
  const startConversation = useStartConversation();

  const selectionExists = !!selectedId && (conversations?.some((c) => c.id === selectedId) ?? true);
  const activeId = selectionExists ? selectedId : (conversations?.[0]?.id ?? "");

  const onNewConversation = () => {
    startConversation.mutate(undefined, {
      onSuccess: (data) => setSelectedId(data.conversationId),
    });
  };

  return (
    <div className="grid gap-4 rounded-xl border border-border md:grid-cols-[220px_1fr]">
      {/* Conversation list */}
      <div className="flex flex-col gap-2 border-b border-border p-3 md:border-b-0 md:border-r">
        <Button
          variant="outline"
          size="sm"
          className="w-full justify-start"
          onClick={onNewConversation}
          disabled={startConversation.isPending}
        >
          <PlusIcon className="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
          {t("vibeChat.newConversation")}
        </Button>

        {listLoading ? (
          <div className="space-y-1.5 pt-1">
            <Skeleton className="h-8 w-full" />
            <Skeleton className="h-8 w-full" />
          </div>
        ) : conversations && conversations.length > 0 ? (
          <ul className="space-y-0.5">
            {conversations.map((c) => (
              <li key={c.id}>
                <button
                  type="button"
                  onClick={() => setSelectedId(c.id)}
                  className={cn(
                    "flex w-full items-center gap-2 rounded-lg px-2.5 py-1.5 text-left text-sm transition-colors",
                    c.id === activeId
                      ? "bg-muted font-medium text-foreground"
                      : "text-muted-foreground hover:bg-muted/50 hover:text-foreground",
                  )}
                >
                  <MessageSquareIcon className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                  <span className="truncate">{c.title}</span>
                </button>
              </li>
            ))}
          </ul>
        ) : (
          <p className="px-1 pt-1 text-xs text-muted-foreground">
            {t("vibeChat.noConversations")}
          </p>
        )}
      </div>

      {/* Thread */}
      <div className="min-h-[28rem]">
        {activeId ? (
          <VibeThread conversationId={activeId} onDeleted={() => setSelectedId("")} />
        ) : (
          <div className="flex h-full min-h-[28rem] items-center justify-center p-6 text-center text-sm text-muted-foreground">
            {t("vibeChat.emptyThread")}
          </div>
        )}
      </div>
    </div>
  );
}

/** One open conversation: scrolling message thread + the composer. */
function VibeThread({
  conversationId,
  onDeleted,
}: {
  conversationId: string;
  onDeleted: () => void;
}) {
  const t = useTranslations("marketing");
  const { data, isLoading } = useVibeConversation(conversationId);
  const deleteConversation = useDeleteConversation();
  const endRef = useRef<HTMLDivElement>(null);

  // Optimistic user turn(s) — appended on send, cleared once the send settles (the
  // refetch the mutation triggers brings back the persisted turn). Clearing in the
  // mutation lifecycle (not an effect) keeps this React-Compiler-clean.
  const [optimistic, setOptimistic] = useState<ConversationMessage[]>([]);
  const sendMessage = useSendMessage(conversationId);

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<ChatMessageInput>({
    resolver: zodResolver(buildChatMessageSchema(t)),
    defaultValues: { content: "" },
  });

  const messages = useMemo(
    () => [...(data?.messages ?? []), ...optimistic],
    [data?.messages, optimistic],
  );

  // Scroll to newest on any message change. This synchronizes the DOM scroll
  // position with React state (a legitimate effect — no setState).
  useEffect(() => {
    endRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages.length]);

  const onSubmit = (values: ChatMessageInput) => {
    const content = values.content;
    const createdAt = new Date().toISOString();
    setOptimistic((prev) => [
      ...prev,
      {
        // Sequence from the current optimistic count → stable, no impure id source.
        id: `optimistic-${conversationId}-${prev.length}`,
        role: "user",
        content,
        toolCallsJson: null,
        createdAt,
      },
    ]);
    reset();
    sendMessage.mutate(content, {
      onSettled: () => setOptimistic([]),
    });
  };

  return (
    <div className="flex h-full min-h-[28rem] flex-col">
      {/* Header */}
      <div className="flex items-center justify-between gap-2 border-b border-border px-4 py-2.5">
        <h3 className="truncate text-sm font-medium">
          {data?.title ?? t("vibeChat.loading")}
        </h3>
        <Button
          variant="ghost"
          size="icon-sm"
          aria-label={t("vibeChat.deleteConversation")}
          disabled={deleteConversation.isPending}
          onClick={() =>
            deleteConversation.mutate(conversationId, { onSuccess: onDeleted })
          }
        >
          <Trash2Icon className="h-4 w-4" aria-hidden="true" />
        </Button>
      </div>

      {/* Messages */}
      <ScrollArea className="flex-1">
        <div className="space-y-3 p-4">
          {isLoading ? (
            <div className="space-y-3">
              <Skeleton className="h-10 w-2/3" />
              <Skeleton className="ml-auto h-10 w-1/2" />
            </div>
          ) : messages.length > 0 ? (
            messages.map((m) => (
              <VibeMessage
                key={m.id}
                message={m}
                pending={m.id.startsWith("optimistic-")}
              />
            ))
          ) : (
            <p className="py-8 text-center text-sm text-muted-foreground">
              {t("vibeChat.startPrompt")}
            </p>
          )}
          {sendMessage.isPending && (
            <p
              role="status"
              aria-live="polite"
              className="text-center text-xs text-muted-foreground"
            >
              {t("vibeChat.assistantThinking")}
            </p>
          )}
          <div ref={endRef} />
        </div>
      </ScrollArea>

      {/* Composer */}
      <form
        onSubmit={handleSubmit(onSubmit)}
        noValidate
        className="border-t border-border p-3"
      >
        <div className="flex items-end gap-2">
          <div className="flex-1 space-y-1">
            <Textarea
              {...register("content")}
              rows={2}
              placeholder={t("vibeChat.inputPlaceholder")}
              aria-label={t("vibeChat.inputLabel")}
              aria-invalid={!!errors.content}
              onKeyDown={(e) => {
                if (e.key === "Enter" && !e.shiftKey) {
                  e.preventDefault();
                  void handleSubmit(onSubmit)();
                }
              }}
              className="resize-none"
            />
            {errors.content && (
              <p className="text-xs text-destructive">{errors.content.message}</p>
            )}
          </div>
          <Button
            type="submit"
            size="icon"
            disabled={sendMessage.isPending}
            aria-label={t("vibeChat.send")}
          >
            <SendIcon className="h-4 w-4" aria-hidden="true" />
          </Button>
        </div>
      </form>
    </div>
  );
}
