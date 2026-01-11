namespace GameImpact.Abstractions.Automation;

public interface IAutomationTask
{
    string Name { get; }
    Task ExecuteAsync(CancellationToken ct);
}

public interface IAutomationTask<T> : IAutomationTask
{
    new Task<T> ExecuteAsync(CancellationToken ct);
}
