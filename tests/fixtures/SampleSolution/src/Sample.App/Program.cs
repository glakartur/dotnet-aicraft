using Sample.Domain.Entities;
using Sample.Domain.Repositories;
using Sample.Domain.Services;
using Sample.Infrastructure.Notifications;
using Sample.Infrastructure.Persistence;

var repository = new InMemoryOrderRepository();
var notifier = new ConsoleOrderNotifier();
var service = new OrderService(repository, notifier);

var customer = new Customer(Guid.NewGuid(), "Ada Lovelace", "ada@example.test");
var lines = new[]
{
    new OrderLine("BOOK", 2, 25m),
    new OrderLine("PEN", 4, 3.5m)
};

var order = service.CreateOrder(customer, lines);
service.SubmitOrder(order.Id);

IOrderRepository asInterface = repository;
Console.WriteLine(service.FormatAudit(asInterface.Find(order.Id)!));
