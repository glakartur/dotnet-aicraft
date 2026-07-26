using Sample.Domain.Entities;

namespace Sample.Domain.Services;

public interface IClock
{
    DateTime UtcNow { get; }
}

public interface IOrderNotifier
{
    void NotifySubmitted(Order order);
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
