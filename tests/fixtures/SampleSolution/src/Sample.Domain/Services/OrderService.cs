using Sample.Domain.Entities;
using Sample.Domain.Repositories;

namespace Sample.Domain.Services;

public class OrderService
{
    private readonly IOrderRepository _orders;
    private readonly IOrderNotifier _notifier;

    public OrderService(IOrderRepository orders, IOrderNotifier notifier)
    {
        _orders = orders;
        _notifier = notifier;
    }

    public Order CreateOrder(Customer customer, IEnumerable<OrderLine> lines)
    {
        var order = new Order(Guid.NewGuid(), customer);
        foreach (var line in lines)
            order.AddLine(line.Sku, line.Quantity, line.UnitPrice);

        _orders.Save(order);
        return order;
    }

    public void SubmitOrder(Guid id)
    {
        var order = _orders.Find(id)
            ?? throw new InvalidOperationException($"Order '{id}' was not found.");

        order.Submit();
        _orders.Save(order);
        _notifier.NotifySubmitted(order);
    }

    public string FormatAudit(Order order) => order.AuditLabel();

    public string FormatAudit(Customer customer) => $"Customer:{customer.Id:N}:{customer.Email}";
}
