using Sample.Domain.Entities;

namespace Sample.Domain.Services;

public static class OrderExtensions
{
    public static bool IsLarge(this Order order, decimal threshold = 1_000m)
        => order.Total >= threshold;
}
