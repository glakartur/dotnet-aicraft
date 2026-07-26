using Sample.Domain.Entities;

namespace Sample.Domain.Processing;

public interface IProcessor<in T>
{
    void Process(T item);
}

public abstract class ProcessorBase<T> : IProcessor<T>
{
    public void Process(T item)
    {
        BeforeProcess(item);
        ProcessCore(item);
    }

    protected virtual void BeforeProcess(T item)
    {
    }

    protected abstract void ProcessCore(T item);
}

public sealed class OrderProcessor : ProcessorBase<Order>
{
    protected override void ProcessCore(Order item)
    {
        _ = item.CreateSnapshot();
    }
}
