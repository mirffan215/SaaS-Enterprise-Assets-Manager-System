using System.Data;
using System.Data.Common;
using AMGH.ITInventory.Shared.Abstractions;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AMGH.ITInventory.Persistence;

/// <summary>
/// Hands the active company to SQL Server on every connection EF Core opens (queries, saves, transactions and
/// stored procedures), as READ-ONLY session context: no later statement on that connection can change it.
/// SQL Server Row-Level Security (sec.CompanyIsolation, script 10) filters and blocks rows with it.
/// Pooled connections are reset by SQL Server when reused, so a value never leaks to the next request.
/// </summary>
internal sealed class CompanySessionInterceptor : DbConnectionInterceptor
{
    private readonly ICompanyContext _company;

    public CompanySessionInterceptor(ICompanyContext company) => _company = company;

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = BuildCommand(connection);
        command?.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var command = BuildCommand(connection);
        if (command is not null) await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbCommand? BuildCommand(DbConnection connection)
    {
        string key;
        int value;
        if (_company.CompanyId is { } companyId) { key = "CompanyId"; value = companyId; }
        else if (_company.IsPlatform) { key = "Platform"; value = 1; }
        else return null; // no company: company-owned rows stay invisible

        var command = connection.CreateCommand();
        command.CommandText = "EXEC sys.sp_set_session_context @key = @key, @value = @value, @read_only = 1;";
        var k = command.CreateParameter();
        k.ParameterName = "@key";
        k.DbType = DbType.String;
        k.Value = key;
        var v = command.CreateParameter();
        v.ParameterName = "@value";
        v.DbType = DbType.Int32;
        v.Value = value;
        command.Parameters.Add(k);
        command.Parameters.Add(v);
        return command;
    }
}
