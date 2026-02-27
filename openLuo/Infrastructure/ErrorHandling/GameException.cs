namespace openLuo.Infrastructure.ErrorHandling;

public class GameException : Exception
{
    public GameException(string message) : base(message) { }
    public GameException(string message, Exception inner) : base(message, inner) { }
}

public class ValidationException : GameException
{
    public ValidationException(string message) : base(message) { }
}

public class PluginException : GameException
{
    public PluginException(string message, Exception inner) : base(message, inner) { }
}
