using Agora.Domain.Access;

namespace Agora.Application.Abstractions;

public interface IUserRepository
{
    Task AddAsync(User user, CancellationToken cancellationToken);

    Task<User?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<User?> FindByEmailAsync(EmailAddress email, CancellationToken cancellationToken);
}
