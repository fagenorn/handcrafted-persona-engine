namespace PersonaEngine.Lib.Bootstrapper.Verification;

public sealed class IntegrityException : Exception
{
    public IntegrityException(string message)
        : base(message) { }

    public IntegrityException(string message, Exception inner)
        : base(message, inner) { }
}
