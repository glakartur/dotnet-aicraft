using Sample.Domain.Events;

namespace Sample.Domain.Entities;

/// <summary>
/// Aggregate root for sample orders.
/// </summary>
public partial class Order : Entity<Guid>, IAuditable
{
    private readonly List<OrderLine> _lines = [];

    public Order(Guid id, Customer customer)
        : base(id)
    {
        Customer = customer;
        Status = OrderStatus.Draft;
    }

    public Customer Customer { get; }

    public OrderStatus Status { get; private set; }

    public IReadOnlyList<OrderLine> Lines => _lines;

    public event EventHandler<OrderSubmittedEventArgs>? Submitted;

    public decimal Total => _lines.Sum(line => line.Total);

    public void AddLine(string sku, int quantity, decimal unitPrice)
    {
        _lines.Add(new OrderLine(sku, quantity, unitPrice));
    }

    public void Submit()
    {
        if (_lines.Count == 0)
            throw new InvalidOperationException("Cannot submit an empty order.");

        Status = OrderStatus.Submitted;
        Submitted?.Invoke(this, new OrderSubmittedEventArgs(Id, Total));
    }

    public string AuditLabel() => $"Order:{Id:N}:{Status}";

    public sealed class Snapshot
    {
        public Snapshot(Guid id, decimal total)
        {
            Id = id;
            Total = total;
        }

        public Guid Id { get; }

        public decimal Total { get; }
    }
}
