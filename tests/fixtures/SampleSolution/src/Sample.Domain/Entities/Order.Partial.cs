namespace Sample.Domain.Entities;

public partial class Order
{
    public Snapshot CreateSnapshot() => new(Id, Total);
}
