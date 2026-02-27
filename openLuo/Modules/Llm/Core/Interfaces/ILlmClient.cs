using openLuo.Modules.Llm.Core.Models;
using LocalChatMessage = openLuo.Modules.Llm.Core.Models.ChatMessage;

namespace openLuo.Modules.Llm.Core.Interfaces;

/// <summary>
/// Provides plain chat capabilities.
/// </summary>
public interface ILlmClient
{
    /// <summary>
    /// Sends chat messages and returns the full reply text.
    /// </summary>
    Task<string> CompleteAsync(
        IEnumerable<LocalChatMessage> messages,
        LlmOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Sends chat messages and streams reply text chunks.
    /// </summary>
    Task<string> StreamAsync(
        IEnumerable<LocalChatMessage> messages,
        Action<string> onChunk,
        LlmOptions? options = null,
        CancellationToken ct = default);
}
