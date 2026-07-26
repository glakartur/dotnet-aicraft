using Sample.Domain.Entities;

namespace Sample.Domain.Repositories;

public interface IRepository<T, in TId>
    where T : Entity<TId>
{
    T? Find(TId id);

    void Save(T entity);
}

public interface IOrderRepository : IRepository<Order, Guid>
{
    IReadOnlyList<Order> FindByCustomer(Guid customerId);
}
