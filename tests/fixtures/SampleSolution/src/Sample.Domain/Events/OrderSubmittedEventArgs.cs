namespace Sample.Domain.Events;

public sealed class OrderSubmittedEventArgs : EventArgs
{
    public OrderSubmittedEventArgs(Guid orderId, decimal total)
    {
        OrderId = orderId;
        Total = total;
    }

    public Guid OrderId { get; }

    public decimal Total { get; }
}
