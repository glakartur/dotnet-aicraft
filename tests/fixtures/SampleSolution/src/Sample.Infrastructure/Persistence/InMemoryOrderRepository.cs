using Sample.Domain.Entities;
using Sample.Domain.Repositories;

namespace Sample.Infrastructure.Persistence;

public sealed class InMemoryOrderRepository : IOrderRepository
{
    private readonly Dictionary<Guid, Order> _orders = [];

    public Order? Find(Guid id)
        => _orders.TryGetValue(id, out var order) ? order : null;

    public void Save(Order entity)
    {
        _orders[entity.Id] = entity;
    }

    public IReadOnlyList<Order> FindByCustomer(Guid customerId)
        => _orders.Values.Where(order => order.Customer.Id == customerId).ToList();
}
