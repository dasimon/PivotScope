using Microsoft.Data.Sqlite;

namespace PivotScope.Core.Calculations;

/// <summary>A calculation stored in the library, with its scope and date.</summary>
public sealed record StoredCalculation(
    int Id, CalculationDefinition Definition, string? Cube, DateTime SavedUtc);

/// <summary>
/// Library of reusable calculations, in SQLite.
///
/// Database owned by PivotScope: CubeScope's is not shared, because two
/// processes writing the same file is a problem there is no reason to create
/// for ourselves. An explicit import will come if the need is confirmed.
/// </summary>
public sealed class CalculationLibrary : IDisposable
{
    private const int SchemaVersion = 1;

    private readonly SqliteConnection _connection;

    public CalculationLibrary(string? dbPath = null)
    {
        var path = dbPath ?? DefaultDbPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        _connection = new SqliteConnection($"Data Source={path}");
        _connection.Open();
        Migrate();
    }

    public static string DefaultDbPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PivotScope", "calculations.db");

    private void Migrate()
    {
        // Same pattern as CubeScope's StateStore: user_version holds the
        // schema number, so that future migrations are trivial.
        using var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Calculation (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                Name            TEXT    NOT NULL,
                Expression      TEXT    NOT NULL,
                Kind            INTEGER NOT NULL,
                DisplayFolder   TEXT    NULL,
                NumberFormat    TEXT    NULL,
                ParentHierarchy TEXT    NULL,
                SolveOrder      INTEGER NOT NULL DEFAULT 0,
                Cube            TEXT    NULL,
                SavedUtc        TEXT    NOT NULL
            );

            -- The same name can exist for two different cubes, but not twice
            -- for the same one: saving again updates it.
            CREATE UNIQUE INDEX IF NOT EXISTS UX_Calculation_Name_Cube
                ON Calculation (Name, IFNULL(Cube, ''));
            """;
        command.ExecuteNonQuery();

        using var version = _connection.CreateCommand();
        version.CommandText = $"PRAGMA user_version = {SchemaVersion};";
        version.ExecuteNonQuery();
    }

    public async Task<int> SaveAsync(
        CalculationDefinition definition, string? cube, CancellationToken ct = default)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Calculation
                (Name, Expression, Kind, DisplayFolder, NumberFormat,
                 ParentHierarchy, SolveOrder, Cube, SavedUtc)
            VALUES
                ($name, $expression, $kind, $folder, $format,
                 $parent, $solveOrder, $cube, $savedUtc)
            ON CONFLICT (Name, IFNULL(Cube, '')) DO UPDATE SET
                Expression      = excluded.Expression,
                Kind            = excluded.Kind,
                DisplayFolder   = excluded.DisplayFolder,
                NumberFormat    = excluded.NumberFormat,
                ParentHierarchy = excluded.ParentHierarchy,
                SolveOrder      = excluded.SolveOrder,
                SavedUtc        = excluded.SavedUtc
            RETURNING Id;
            """;

        command.Parameters.AddWithValue("$name", definition.Name.Trim());
        command.Parameters.AddWithValue("$expression", definition.Expression);
        command.Parameters.AddWithValue("$kind", (int)definition.Kind);
        command.Parameters.AddWithValue("$folder", (object?)definition.DisplayFolder ?? DBNull.Value);
        command.Parameters.AddWithValue("$format", (object?)definition.NumberFormat ?? DBNull.Value);
        command.Parameters.AddWithValue("$parent", (object?)definition.ParentHierarchy ?? DBNull.Value);
        command.Parameters.AddWithValue("$solveOrder", definition.SolveOrder);
        command.Parameters.AddWithValue("$cube", (object?)cube ?? DBNull.Value);
        command.Parameters.AddWithValue("$savedUtc", DateTime.UtcNow.ToString("O"));

        var id = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt32(id);
    }

    public async Task<IReadOnlyList<StoredCalculation>> ListAsync(CancellationToken ct = default)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Expression, Kind, DisplayFolder, NumberFormat,
                   ParentHierarchy, SolveOrder, Cube, SavedUtc
            FROM Calculation
            ORDER BY SavedUtc DESC, Id DESC;
            """;

        var list = new List<StoredCalculation>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var definition = new CalculationDefinition(
                reader.GetString(1),
                reader.GetString(2),
                (CalculationKind)reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetInt32(7));

            list.Add(new StoredCalculation(
                reader.GetInt32(0),
                definition,
                reader.IsDBNull(8) ? null : reader.GetString(8),
                DateTime.Parse(reader.GetString(9), null,
                    System.Globalization.DateTimeStyles.RoundtripKind)));
        }

        return list;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM Calculation WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _connection.Dispose();
        // Without this, the file stays locked after Dispose and a test cannot
        // clean up its temporary database.
        SqliteConnection.ClearPool(_connection);
    }
}
