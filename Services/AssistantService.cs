using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace LakesideMedical.Services;

public sealed class AssistantOptions
{
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "claude-haiku-5-5";
}

public sealed record ChatRequest(IReadOnlyList<ChatMessage>? Messages);
public sealed record ChatMessage(string Role, string Content);
public sealed record ChatResponse(string Reply);

public sealed class AssistantNotConfiguredException()
    : InvalidOperationException("ANTHROPIC_API_KEY is not configured.");

public sealed class AssistantService(HttpClient httpClient, IOptions<AssistantOptions> options)
{
    private const int MaxMessages = 12;
    private const int MaxMessageLength = 2000;
    private const int MaxConversationLength = 10000;
    private const string SignOff = "— Lakeside Medical Clinic, powered by London International · lisrc.ae";

    private const string SystemPrompt = """
        You are Ask Lakeside, the friendly information assistant for Lakeside Medical Clinic.
        Answer using only the verified clinic information below. Do not invent or infer clinic facts.

        Specialties: GP (General Physician), ENT, Dentist, Cardiologist.
        Doctors: Dr. Sara Ahmed (GP), Dr. Omar Khan (ENT), Dr. Lina Haddad (Dentist), Dr. Raj Mehta (Cardiologist).
        Hours: Monday-Saturday, 9am-9pm; closed Sunday. Dubai time.
        Insurance: The clinic's accepted insurers have not been confirmed. Do not claim that Daman, AXA, or any other insurer is accepted. If asked, say you don't know and suggest calling the clinic.
        Location: Cluster X, JLT, Dubai, UAE.
        Deposit: AED 100 refundable consultation deposit.

        If requested information is not above, say you don't know and suggest calling the clinic.
        Do not give medical advice or discuss symptoms, diagnoses, or treatments. If asked for medical advice, say you can't give medical advice and offer to help book the appropriate specialist. If the situation sounds serious or urgent, suggest seeking urgent/emergency care.
        Keep replies short and friendly. End every reply with this exact one-line sign-off:
        — Lakeside Medical Clinic, powered by London International · lisrc.ae
        """;

    public static bool IsValidConversation(IReadOnlyList<ChatMessage> messages)
    {
        if (messages.Count is < 1 or > MaxMessages)
        {
            return false;
        }

        var totalLength = 0;
        for (var index = 0; index < messages.Count; index++)
        {
            var message = messages[index];
            var expectedRole = index % 2 == 0 ? "user" : "assistant";
            if (message is null
                || !string.Equals(message.Role, expectedRole, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(message.Content)
                || message.Content.Length > MaxMessageLength)
            {
                return false;
            }

            totalLength += message.Content.Length;
            if (totalLength > MaxConversationLength)
            {
                return false;
            }
        }

        return messages[^1].Role == "user";
    }

    public async Task<string> GetReplyAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.ApiKey))
        {
            throw new AssistantNotConfiguredException();
        }

        if (!IsValidConversation(messages))
        {
            throw new ArgumentException("The conversation is invalid.", nameof(messages));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", options.Value.ApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = JsonContent.Create(new AnthropicRequest(
            options.Value.Model,
            300,
            SystemPrompt,
            messages.Select(message => new AnthropicMessage(message.Role, message.Content)).ToArray()));

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Anthropic returned HTTP {(int)response.StatusCode}.",
                null,
                response.StatusCode);
        }

        var result = await response.Content.ReadFromJsonAsync<AnthropicResponse>(
            cancellationToken: cancellationToken);
        var reply = result?.Content?.FirstOrDefault(block => block.Type == "text")?.Text;
        if (string.IsNullOrWhiteSpace(reply))
        {
            throw new HttpRequestException("Anthropic returned an empty or invalid response.", null, HttpStatusCode.BadGateway);
        }

        reply = reply.Trim();
        return reply.EndsWith(SignOff, StringComparison.Ordinal)
            ? reply
            : $"{reply}\n{SignOff}";
    }

    private sealed record AnthropicRequest(
        string Model,
        int MaxTokens,
        string System,
        IReadOnlyList<AnthropicMessage> Messages);

    private sealed record AnthropicMessage(string Role, string Content);
    private sealed record AnthropicResponse(IReadOnlyList<AnthropicContent>? Content);
    private sealed record AnthropicContent(string Type, string? Text);
}
