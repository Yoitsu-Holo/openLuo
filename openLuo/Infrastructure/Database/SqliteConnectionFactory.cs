using Microsoft.Data.Sqlite;

namespace openLuo.Infrastructure.Database;

public sealed class SqliteConnectionFactory : IDatabaseConnectionFactory
{
    private readonly string _connectionString;
    private readonly string _baseDir;
    private readonly string _sqliteVecExtensionPath;

    public SqliteConnectionFactory(string connectionString, string? baseDir = null, string sqliteVecExtensionPath = "")
    {
        _connectionString = connectionString;
        _baseDir = string.IsNullOrWhiteSpace(baseDir) ? AppContext.BaseDirectory : baseDir;
        _sqliteVecExtensionPath = sqliteVecExtensionPath;
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken ct = default)
    {
        var conn = new SqliteConnection(_connectionString);
        try
        {
            await conn.OpenAsync(ct);
            return conn;
        }
        catch
        {
            await conn.DisposeAsync();
            throw;
        }
    }

    public async Task<SqliteConnection> OpenVecAsync(CancellationToken ct = default)
    {
        var conn = await OpenAsync(ct);
        try
        {
            var extensionPath = SqliteVecExtensionLoader.ResolveExtensionPath(_baseDir, _sqliteVecExtensionPath);
            SqliteVecExtensionLoader.Load(conn, extensionPath);
            return conn;
        }
        catch
        {
            await conn.DisposeAsync();
            throw;
        }
    }
}
