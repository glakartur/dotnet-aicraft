namespace Sample.Domain.Entities;

public sealed class Customer : Entity<Guid>
{
    public Customer(Guid id, string name, string email)
        : base(id)
    {
        Name = name;
        Email = email;
    }

    public string Name { get; }

    public string Email { get; }
}

public readonly record struct OrderLine(string Sku, int Quantity, decimal UnitPrice)
{
    public decimal Total => Quantity * UnitPrice;
}

public sealed record Money(decimal Amount, string Currency);

public enum OrderStatus
{
    Draft,
    Submitted,
    Cancelled
}

public interface IAuditable
{
    string AuditLabel();
}
