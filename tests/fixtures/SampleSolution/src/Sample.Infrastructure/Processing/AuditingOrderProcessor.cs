using Sample.Domain.Entities;
using Sample.Domain.Processing;
using Sample.Domain.Services;

namespace Sample.Infrastructure.Processing;

public sealed class AuditingOrderProcessor : ProcessorBase<Order>
{
    public string LastAuditLabel { get; private set; } = string.Empty;

    protected override void BeforeProcess(Order item)
    {
        LastAuditLabel = item.AuditLabel();
    }

    protected override void ProcessCore(Order item)
    {
        if (item.IsLarge())
            LastAuditLabel = $"large:{LastAuditLabel}";
    }
}
