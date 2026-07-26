using Sample.Domain.Entities;
using Sample.Domain.Services;
using Sample.Infrastructure.Notifications;
using Sample.Infrastructure.Persistence;

namespace Sample.Tests;

public static class OrderServiceSmokeTests
{
    public static Order SubmittedOrder()
    {
        // Arrange
        var repository = new InMemoryOrderRepository();
        var service = new OrderService(repository, new ConsoleOrderNotifier());
        var customer = new Customer(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "Grace Hopper", "grace@example.test");
        var order = service.CreateOrder(customer, [new OrderLine("COMPILER", 1, 100m)]);

        // Act
        service.SubmitOrder(order.Id);

        // Assert
        return repository.Find(order.Id)!;
    }
}
