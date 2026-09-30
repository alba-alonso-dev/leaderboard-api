using Leaderboard.Application.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Leaderboard.Infrastructure.Persistence;

internal sealed class UnitOfWork(AppDbContext context) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("The entity was modified by another request.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            // Detach the rejected inserts so the context stays usable for follow-up reads in the same request.
            foreach (var entry in ex.Entries)
            {
                entry.State = EntityState.Detached;
            }

            throw new UniqueConstraintException(pg.ConstraintName ?? "unknown", ex);
        }
    }

    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        // With EnableRetryOnFailure, user transactions must run inside the execution strategy so the whole block is retried.
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            (context, operation),
            async (_, state, ct) =>
            {
                await using var transaction = await state.context.Database.BeginTransactionAsync(ct);
                var result = await state.operation(ct);
                await transaction.CommitAsync(ct);
                return result;
            },
            verifySucceeded: null,
            cancellationToken);
    }
}
