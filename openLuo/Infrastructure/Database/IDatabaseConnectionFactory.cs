using Microsoft.Data.Sqlite;

namespace openLuo.Infrastructure.Database;

public interface IDatabaseConnectionFactory
{
    Task<SqliteConnection> OpenAsync(CancellationToken ct = default);
    Task<SqliteConnection> OpenVecAsync(CancellationToken ct = default);
}
