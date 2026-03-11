using Azure;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using System.Runtime.CompilerServices;

namespace CarTitleOCR.Services;

/// <summary>
/// Chat agent service backed by a Microsoft Foundry persistent agent
/// (Azure AI Projects + Microsoft Agent Framework).
/// Each Blazor circuit gets its own <see cref="AgentSession"/> so conversation
/// history is isolated per user while the server-side agent definition is shared.
/// Authentication uses <see cref="DefaultAzureCredential"/> which supports
/// Azure CLI (<c>az login</c>), Managed Identity, and service-principal
/// environment variables (AZURE_CLIENT_ID / AZURE_CLIENT_SECRET / AZURE_TENANT_ID).
/// </summary>
public sealed class FoundryAgentService : IAgentService, IAsyncDisposable
{
    private const string AgentInstructions =
        """
        You are a knowledgeable and friendly assistant specializing in vehicle title
        applications and registrations. You help users understand:
        - How to complete a car title transfer application
        - What information is required on a title (VIN, make, model, year, odometer,
          owner details, lienholder, etc.)
        - How to transfer a vehicle title from another state
        - Lienholder and lien-release requirements
        - Common fees, timelines, and DMV processes
        - How to correct errors or omissions on a title application

        Keep answers concise and practical. When a rule varies by state, tell the user
        to confirm with their local DMV. Do not provide legal advice.
        """;

    private readonly AIProjectClient? _projectClient;
    private readonly string _agentName;
    private readonly string _modelDeploymentName;

    private AIAgent? _agent;
    private AgentSession? _session;

    public FoundryAgentService(IConfiguration configuration)
    {
        _agentName = configuration["AzureAIFoundry:AgentName"] ?? "TitleRegistrationAssistant";
        _modelDeploymentName = configuration["AzureAIFoundry:ModelDeploymentName"] ?? "gpt-4o";

        var endpoint = configuration["AzureAIFoundry:Endpoint"];
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            // DefaultAzureCredential tries Azure CLI, managed identity, and service-principal
            // environment variables automatically — no secrets stored in config.
            _projectClient = new AIProjectClient(new Uri(endpoint), new DefaultAzureCredential());
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> ChatAsync(
        string userMessage,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_projectClient is null)
        {
            yield return "⚠ The AI assistant is not configured. Please set **AzureAIFoundry:Endpoint** in appsettings.json and authenticate via `az login` or managed identity.";
            yield break;
        }

        await EnsureInitializedAsync(cancellationToken);

        await foreach (var chunk in _agent!.RunStreamingAsync(userMessage, _session, cancellationToken: cancellationToken))
        {
            var text = chunk.Text;
            if (!string.IsNullOrEmpty(text))
                yield return text;
        }
    }

    /// <inheritdoc />
    public void ClearHistory()
    {
        // Null out the session; EnsureInitializedAsync will create a fresh one on next chat.
        _session = null;
    }

    public async ValueTask DisposeAsync()
    {
        // Sessions are server-side resources; let the platform clean them up on expiry.
        await ValueTask.CompletedTask;
    }

    // -------------------------------------------------------------------------

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_agent is null)
        {
            _agent = await GetOrCreateAgentAsync(cancellationToken);
        }

        if (_session is null)
        {
            _session = await _agent.CreateSessionAsync(cancellationToken);
        }
    }

    private async Task<AIAgent> GetOrCreateAgentAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Reuse an existing agent definition if one already exists with this name.
            return await _projectClient!.GetAIAgentAsync(_agentName, cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return await _projectClient!.CreateAIAgentAsync(
                name: _agentName,
                model: _modelDeploymentName,
                instructions: AgentInstructions,
                cancellationToken: cancellationToken);
        }
    }
}
