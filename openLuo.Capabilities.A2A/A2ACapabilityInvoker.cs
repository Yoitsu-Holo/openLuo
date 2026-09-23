using A2A;
using System.Text;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;

namespace openLuo.Capabilities.A2A;

/// <summary>Invokes one remote A2A skill through the A2A message endpoint.</summary>
public sealed class A2ACapabilityInvoker : ICapabilityInvoker
{
    private readonly A2AClient _client;
    private readonly A2AAgentConfig _config;

    public A2ACapabilityInvoker(A2AClient client, A2AAgentConfig config)
    {
        _client = client;
        _config = config;
    }

    public async Task<CapabilityResult> InvokeAsync(
        CapabilityCall call,
        CapabilityExecutionContext context,
        CancellationToken ct = default)
    {
        var message = BuildMessage(call);
        try
        {
            var response = await _client.SendMessageAsync(new SendMessageRequest
            {
                Tenant = _config.Tenant,
                Message = message
            }, ct);

            var text = ExtractText(response);
            return new CapabilityResult
            {
                InvocationId = call.InvocationId,
                Success = response.PayloadCase is SendMessageResponseCase.Message or SendMessageResponseCase.Task,
                Status = response.PayloadCase is SendMessageResponseCase.None ? CapabilityStatus.Failed : CapabilityStatus.Ok,
                Text = text,
                Error = response.PayloadCase is SendMessageResponseCase.None ? "A2A response contained neither a message nor a task." : null
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new CapabilityResult
            {
                InvocationId = call.InvocationId,
                Success = false,
                Status = CapabilityStatus.Cancelled,
                Error = "cancelled"
            };
        }
        catch (Exception ex)
        {
            return new CapabilityResult
            {
                InvocationId = call.InvocationId,
                Success = false,
                Status = CapabilityStatus.Failed,
                Error = ex.Message
            };
        }
    }

    private static Message BuildMessage(CapabilityCall call)
    {
        var body = new StringBuilder(call.CanonicalId);
        if (call.Args.Length > 0)
            body.Append("\nargs: ").Append(string.Join(", ", call.Args));
        foreach (var option in call.Options)
            body.Append('\n').Append(option.Key).Append(": ").Append(option.Value);

        return new Message
        {
            MessageId = string.IsNullOrWhiteSpace(call.InvocationId) ? Guid.NewGuid().ToString("N") : call.InvocationId,
            Role = Role.User,
            Parts = [Part.FromText(body.ToString())]
        };
    }

    private static string? ExtractText(SendMessageResponse response)
    {
        if (response.Message is { } message)
            return string.Join("\n", message.Parts.Where(p => p.Text is not null).Select(p => p.Text));

        if (response.Task is { } task)
        {
            var statusText = task.Status.Message is { } status
                ? string.Join("\n", status.Parts.Where(p => p.Text is not null).Select(p => p.Text))
                : null;
            var artifactText = task.Artifacts is null
                ? null
                : string.Join("\n", task.Artifacts.SelectMany(a => a.Parts).Where(p => p.Text is not null).Select(p => p.Text));
            return string.Join("\n", new[] { statusText, artifactText }.Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        return null;
    }
}
