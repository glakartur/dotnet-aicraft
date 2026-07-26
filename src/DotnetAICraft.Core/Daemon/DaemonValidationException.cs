using DotnetAICraft.Models;

namespace DotnetAICraft.Daemon;

public sealed class DaemonValidationException : Exception
{
    public ErrorInfo Error { get; }

    public DaemonValidationException(ErrorInfo error)
        : base(error.Message)
    {
        Error = error;
    }
}
