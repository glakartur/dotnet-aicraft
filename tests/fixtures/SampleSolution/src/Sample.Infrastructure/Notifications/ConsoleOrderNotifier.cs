using Sample.Domain.Entities;
using Sample.Domain.Services;

namespace Sample.Infrastructure.Notifications;

public sealed class ConsoleOrderNotifier : IOrderNotifier
{
    public void NotifySubmitted(Order order)
    {
        Console.WriteLine($"Submitted {order.Id:N} total={order.Total}");
    }
}
