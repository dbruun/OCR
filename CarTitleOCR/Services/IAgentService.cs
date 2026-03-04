namespace CarTitleOCR.Services;

public interface IAgentService
{
    /// <summary>
    /// Sends a user message to the Foundry agent and streams the response tokens.
    /// Conversation history is maintained for the lifetime of this scoped instance.
    /// </summary>
    IAsyncEnumerable<string> ChatAsync(string userMessage, CancellationToken cancellationToken = default);

    /// <summary>Clears the conversation history by resetting the agent session.</summary>
    void ClearHistory();
}
