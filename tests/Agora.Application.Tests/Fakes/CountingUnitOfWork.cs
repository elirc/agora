using Agora.Application.Abstractions;

namespace Agora.Application.Tests.Fakes;

/// <summary>An IUnitOfWork that counts commits — so a test can assert "exactly one transaction".</summary>
public sealed class CountingUnitOfWork : IUnitOfWork
{
    public int Commits { get; private set; }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        Commits++;
        return Task.CompletedTask;
    }
}
