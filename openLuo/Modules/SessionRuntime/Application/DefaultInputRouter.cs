using openLuo.Modules.SessionRuntime.Core.Interfaces;
using openLuo.Modules.SessionRuntime.Core.Models;

namespace openLuo.Modules.SessionRuntime.Application;

public sealed class DefaultInputRouter : IInputRouter
{
    public Task<SessionExecutionRequest?> RouteAsync(
        SessionInput input,
        IReadOnlyList<SessionAttachmentReference> attachments,
        CancellationToken ct = default)
    {
        var rawInput = ResolveTextInput(input);
        if (string.IsNullOrWhiteSpace(rawInput))
            return Task.FromResult<SessionExecutionRequest?>(null);

        input.Metadata.TryGetValue("gameId", out var gameId);

        return Task.FromResult<SessionExecutionRequest?>(new SessionExecutionRequest
        {
            RawInput = rawInput,
            Context = new SessionExecutionContext
            {
                SessionId = input.SessionId,
                GameId = string.IsNullOrWhiteSpace(gameId) ? null : gameId,
                SourceId = input.SourceId,
                ChannelId = input.ChannelId,
                ActorId = input.ActorId,
                Attachments = attachments,
                InputKind = input.Kind,
                Origin = input.Origin,
                PresentationProfile = input.PresentationProfile,
                Metadata = new Dictionary<string, string>(input.Metadata, StringComparer.OrdinalIgnoreCase)
            }
        });
    }

    private static string? ResolveTextInput(SessionInput input)
    {
        if (input.Kind == SessionInputKind.Command && input.Command is not null)
            return BuildCommandText(input.Command);

        if (input.Kind == SessionInputKind.Chat)
        {
            var chatText = !string.IsNullOrWhiteSpace(input.Text)
                ? input.Text
                : input.Parts
                    .Where(p => p.Kind == SessionContentKind.Text && !string.IsNullOrWhiteSpace(p.Text))
                    .Select(p => p.Text)
                    .FirstOrDefault();

            var hasBinaryParts = input.Parts.Any(p => p.Kind != SessionContentKind.Text);
            if (string.IsNullOrWhiteSpace(chatText) && hasBinaryParts)
                chatText = "[图片]";

            return string.IsNullOrWhiteSpace(chatText) ? null : $"/chat {chatText!.Trim()}";
        }

        if (!string.IsNullOrWhiteSpace(input.Text))
            return input.Text;

        return input.Parts
            .Where(p => p.Kind == SessionContentKind.Text && !string.IsNullOrWhiteSpace(p.Text))
            .Select(p => p.Text)
            .FirstOrDefault();
    }

    private static string BuildCommandText(SessionCommandInvocation command)
    {
        var parts = new List<string> { $"{command.Prefix}{command.Name}" };
        if (command.Args.Count > 0)
            parts.AddRange(command.Args.Where(static x => !string.IsNullOrWhiteSpace(x)));

        foreach (var pair in command.Options)
        {
            parts.Add($"--{pair.Key}");
            if (!string.IsNullOrWhiteSpace(pair.Value))
                parts.Add(pair.Value);
        }

        return string.Join(" ", parts);
    }
}
