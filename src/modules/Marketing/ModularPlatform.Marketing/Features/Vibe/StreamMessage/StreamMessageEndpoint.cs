using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Marketing.Integrations;
using ModularPlatform.Web;

namespace ModularPlatform.Marketing.Features.Vibe.StreamMessage;

/// <summary>
/// INTERACTIVE token-by-token streaming send (ADDED ALONGSIDE the durable 202 <c>SendMessage</c> path, which is
/// untouched). The LLM runs IN the request — interactive streaming is request-scoped by nature, a deliberate exception
/// to "slow work goes durable". The user turn is persisted (and ownership verified → 404) BEFORE the SSE stream opens,
/// so client errors surface as normal RFC 9457 responses, not mid-stream. The stream then yields each assistant text
/// delta as a <c>delta</c> SSE event, accumulates the full text, persists the assistant turn after completion, and
/// emits a final <c>done</c> event.
/// <para>
/// DISCONNECT SAFETY: the post-stream assistant save is dispatched with <see cref="CancellationToken.None"/> (NOT the
/// request-aborted token), so a dropped client still persists whatever was generated. The streaming enumerator itself
/// is driven by the client connection — if the client leaves mid-stream, the <c>finally</c> still saves the partial
/// accumulated text (or nothing, if no delta arrived). Choosing "save partial" over "save nothing" keeps the user's
/// visible tokens durable.
/// </para>
/// </summary>
internal static class StreamMessageEndpoint
{
    public static void MapStreamMessage(this IEndpointRouteBuilder app)
    {
        app.MapPost("/marketing/vibe/conversations/{conversationId:guid}/messages/stream", async (
                Guid conversationId,
                StreamMessageRequest request,
                ITenantContext tenant,
                IDispatcher dispatcher,
                HttpContext http,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");

                // Verify ownership + persist the USER turn BEFORE opening the stream: a 404 / validation error is then
                // a normal RFC 9457 response, never an error injected mid-SSE.
                var begin = await dispatcher.Send(
                    new BeginStreamMessageCommand(conversationId, userId, request.Content), ct);

                var gateway = http.RequestServices.GetRequiredService<IVibeAgentGateway>();

                return TypedResults.ServerSentEvents(
                    StreamTurn(conversationId, userId, begin.History, gateway, dispatcher, ct),
                    eventType: "delta");
            })
            .RequireAuthorization()
            .RequireModule("marketing")
            .WithTags("Marketing")
            .WithName("StreamVibeMessage");
    }

    private static async IAsyncEnumerable<SseItem<string>> StreamTurn(
        Guid conversationId,
        Guid userId,
        IReadOnlyList<VibeTurnInput> history,
        IVibeAgentGateway gateway,
        IDispatcher dispatcher,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var fullText = new System.Text.StringBuilder();
        try
        {
            await foreach (var delta in gateway.RunTurnStreamingAsync(userId, history, ct))
            {
                fullText.Append(delta);
                yield return new SseItem<string>(delta, "delta");
            }
        }
        finally
        {
            // Persist the assistant turn even if the client disconnected mid-stream. Use CancellationToken.None so a
            // dropped connection (which cancels `ct`) does NOT abandon the save — what the user saw stays durable.
            var text = fullText.ToString();
            if (text.Length > 0)
            {
                await dispatcher.Send(
                    new CompleteStreamMessageCommand(conversationId, userId, text), CancellationToken.None);
            }
        }

        // Final completion marker so the FE knows the assistant turn is persisted and the stream is done.
        yield return new SseItem<string>("[DONE]", "done");
    }
}
