using Microsoft.Extensions.Logging;

namespace openLuo.Infrastructure.ErrorHandling;

public static class ErrorHandler
{
    public static void LogError(ILogger logger, Exception ex, string context)
    {
        logger.LogError(ex, "{Context} failed: {Message}", context, ex.Message);
    }

    public static T HandleRepositoryError<T>(ILogger logger, Func<T> operation, string context)
    {
        try
        {
            return operation();
        }
        catch (Exception ex)
        {
            LogError(logger, ex, context);
            throw new GameException($"{context} failed", ex);
        }
    }

    public static async Task<T> HandleRepositoryErrorAsync<T>(ILogger logger, Func<Task<T>> operation, string context)
    {
        try
        {
            return await operation();
        }
        catch (Exception ex)
        {
            LogError(logger, ex, context);
            throw new GameException($"{context} failed", ex);
        }
    }
}
