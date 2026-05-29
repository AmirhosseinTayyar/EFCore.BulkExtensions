using System;
using Microsoft.Data.SqlClient;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.Oracle;
using Testcontainers.PostgreSql;

namespace EFCore.BulkExtensions.Tests.Infrastructure;

/// <summary>
/// Starts database containers for integration tests on demand.
/// Disable with environment variable: EFCORE_BULKEXTENSIONS_USE_TESTCONTAINERS=false
/// </summary>
internal static class TestDatabaseContainers
{
    private const string SqlServerKey = "SqlServer";
    private const string PostgreSqlKey = "PostgreSql";
    private const string OracleKey = "Oracle";

    private static readonly Lazy<string> SqlServerTemplate = new(StartSqlServer, isThreadSafe: true);
    private static readonly Lazy<string> PostgreSqlTemplate = new(StartPostgreSql, isThreadSafe: true);
    private static readonly Lazy<string> OracleTemplate = new(StartOracle, isThreadSafe: true);

    internal static bool IsEnabled => ShouldUseTestContainers();

    internal static string? TryGetConnectionTemplate(string name)
    {
        if (!IsEnabled)
        {
            return null;
        }

        return name switch
        {
            SqlServerKey => SqlServerTemplate.Value,
            PostgreSqlKey => PostgreSqlTemplate.Value,
            OracleKey => OracleTemplate.Value,
            _ => null,
        };
    }

    private static bool ShouldUseTestContainers()
    {
        string? flag = Environment.GetEnvironmentVariable("EFCORE_BULKEXTENSIONS_USE_TESTCONTAINERS");
        return !string.Equals(flag, "false", StringComparison.OrdinalIgnoreCase)
               && flag != "0";
    }

    private static string StartSqlServer()
    {
        MsSqlContainer container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
            .WithPassword("SuperSecret42!")
            .Build();

        return StartContainer(container, BuildSqlServerTemplate);
    }

    private static string StartPostgreSql()
    {
        PostgreSqlContainer container = new PostgreSqlBuilder("postgis/postgis:16-3.4")
            .WithUsername("postgres")
            .WithPassword("Postgres22")
            .WithDatabase("postgres")
            .Build();

        return StartContainer(container, BuildPostgreSqlTemplate);
    }

    private static string StartOracle()
    {
        OracleContainer container = new OracleBuilder("gvenzl/oracle-xe")
            .WithPassword("Oracle")
            .Build();

        return StartContainer(container, BuildOracleTemplate);
    }

    private static string StartContainer<TContainer>(TContainer container, Func<string, string> buildTemplate)
        where TContainer : IAsyncDisposable
    {
        try
        {
            switch (container)
            {
                case MsSqlContainer msSql:
                    msSql.StartAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                    RegisterDisposeOnExit(msSql);
                    return buildTemplate(msSql.GetConnectionString());
                case PostgreSqlContainer postgreSql:
                    postgreSql.StartAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                    RegisterDisposeOnExit(postgreSql);
                    return buildTemplate(postgreSql.GetConnectionString());
                case OracleContainer oracle:
                    oracle.StartAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                    RegisterDisposeOnExit(oracle);
                    return buildTemplate(oracle.GetConnectionString());
                default:
                    throw new NotSupportedException($"Unsupported container type: {container.GetType().Name}");
            }
        }
        catch
        {
            container.DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
            throw;
        }
    }

    private static void RegisterDisposeOnExit(IAsyncDisposable container)
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                container.DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch
            {
                // Best-effort cleanup for test infrastructure.
            }
        };
    }

    private static string BuildSqlServerTemplate(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "{databaseName}",
            MultipleActiveResultSets = true,
            TrustServerCertificate = true,
            Encrypt = true,
        };

        return builder.ConnectionString;
    }

    private static string BuildPostgreSqlTemplate(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "{databaseName}",
        };

        return builder.ConnectionString;
    }

    private static string BuildOracleTemplate(string connectionString)
    {
        return $"{connectionString};Pooling=True;Min Pool Size=5;Max Pool Size=30;Connection Lifetime=900;Connection Timeout=15";
    }
}
