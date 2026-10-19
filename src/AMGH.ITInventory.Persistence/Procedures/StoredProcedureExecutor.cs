using System.Data;
using AMGH.ITInventory.Persistence.Errors;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AMGH.ITInventory.Persistence.Procedures;

/// <summary>
/// Executes stored procedures on the DbContext's connection, enlisting in the current EF
/// transaction, with all values passed as typed parameters (never concatenated).
/// </summary>
internal sealed class StoredProcedureExecutor
{
    private readonly AMGHDbContext _db;

    public StoredProcedureExecutor(AMGHDbContext db) => _db = db;

    public static SqlParameter In(string name, SqlDbType type, object? value, int size = 0) =>
        new(name, type, size) { Value = value ?? DBNull.Value, Direction = ParameterDirection.Input };

    public static SqlParameter Out(string name, SqlDbType type, int size = 0) =>
        new(name, type, size) { Direction = ParameterDirection.Output };

    /// <summary>Runs a procedure that returns no rows; output parameters are populated afterwards.</summary>
    public async Task ExecuteAsync(string procedure, IReadOnlyList<SqlParameter> parameters, CancellationToken cancellationToken)
    {
        var assignments = string.Join(", ", parameters.Select(p =>
            p.Direction == ParameterDirection.Output ? $"{p.ParameterName} = {p.ParameterName} OUTPUT" : $"{p.ParameterName} = {p.ParameterName}"));
        var sql = $"EXEC {procedure} {assignments}";
        try
        {
            await _db.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);
        }
        catch (Exception ex) when (DatabaseExceptionTranslator.Translate(ex) is { } translated)
        {
            throw translated;
        }
    }

    /// <summary>Runs a procedure and returns the first column of the first row.</summary>
    public async Task<object?> ExecuteScalarAsync(string procedure, IReadOnlyList<SqlParameter> parameters, CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await _db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = procedure;
            command.CommandType = CommandType.StoredProcedure;
            command.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
            foreach (var p in parameters) command.Parameters.Add(p);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return value is DBNull ? null : value;
        }
        catch (Exception ex) when (DatabaseExceptionTranslator.Translate(ex) is { } translated)
        {
            throw translated;
        }
        finally
        {
            if (openedHere) await _db.Database.CloseConnectionAsync();
        }
    }
}
