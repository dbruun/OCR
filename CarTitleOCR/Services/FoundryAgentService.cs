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
                You are an internal DMV operations assistant supporting DMV employees who process
                bicycle title applications, transfers, registration exceptions, and fraud reviews.

                Assume the user is an internal staff member, clerk, examiner, processor, or supervisor.
                Your job is to help them make accurate processing decisions and move cases forward.

                You help with:
                - Reviewing title and transfer documentation for completeness
                - Identifying missing, inconsistent, or suspicious application fields
                - Explaining internal processing steps, exception handling, and escalation points
                - Summarizing likely issues with title transfers, lien releases, ownership changes,
                    out-of-state documents, and OCR-extracted application data
                - Recommending whether a case appears suitable for normal processing or manual review
                - Highlighting fraud-review signals in a neutral, operational tone

                Response style:
                - Be concise, operational, and staff-oriented
                - Use internal workflow language such as review, verify, escalate, hold, and manual review
                - Prioritize practical next actions over customer-facing explanations
                - If information is incomplete, clearly state what staff should verify next
                - Do not invent statutes, policies, or system capabilities
                - Do not provide legal advice or claim to make final adjudication decisions

                When discussing risk or fraud:
                - Describe signals, inconsistencies, and reasons for review
                - Recommend manual review or escalation when appropriate
                - Avoid accusing applicants of fraud as a fact unless explicitly confirmed by the case data
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
            // Use AzureCliCredential directly - forces use of your az login session
            var credential = new AzureCliCredential();
            _projectClient = new AIProjectClient(new Uri(endpoint), credential);
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
        catch (Exception ex) when (ex.Message.Contains("not_found") || ex.Message.Contains("doesn't exist") || (ex is RequestFailedException rfe && rfe.Status == 404))
        {
            // Agent doesn't exist yet, create it
            return await _projectClient!.CreateAIAgentAsync(
                name: _agentName,
                model: _modelDeploymentName,
                instructions: AgentInstructions,
                cancellationToken: cancellationToken);
        }
    }
}
