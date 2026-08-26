using System.Globalization;
using System.IO;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

/// <summary>
/// Lokale SQLite-Lagerdatenbank (nur auf dem Host-PC bzw. im Lokalmodus).
/// Schreibzugriffe sind serialisiert – kein gemeinsames Datei-Chaos.
/// </summary>
internal static class WarehouseSqliteStore
{
  private static readonly object Gate = new();
  private const string DbFileName = "pipe-warehouse.db";
  private const string LegacyXmlFileName = "pipe-warehouse.xml";

  public static string DatabasePath =>
    Path.Combine(AppInfo.UserDataDirectory, DbFileName);

  public static void EnsureInitialized()
  {
    lock (Gate)
    {
      Directory.CreateDirectory(AppInfo.UserDataDirectory);
      using var connection = Open();
      Execute(connection, """
        CREATE TABLE IF NOT EXISTS meta (
          key TEXT PRIMARY KEY NOT NULL,
          value TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS stock (
          profile_id TEXT NOT NULL,
          material TEXT NOT NULL,
          length_mm REAL NOT NULL,
          quantity INTEGER NOT NULL,
          reserved_quantity INTEGER NOT NULL,
          PRIMARY KEY (profile_id, material, length_mm)
        );
        """);

      if (GetVersion(connection) <= 0)
        SetVersion(connection, 1);

      var count = ScalarLong(connection, "SELECT COUNT(*) FROM stock;");
      if (count == 0)
        TryImportLegacyXml(connection);

      if (ScalarLong(connection, "SELECT COUNT(*) FROM stock;") == 0)
        SeedCatalog(connection);
    }
  }

  public static (long Version, List<PipeWarehouseStockItem> Items) Load()
  {
    lock (Gate)
    {
      EnsureInitializedUnlocked();
      using var connection = Open();
      var version = GetVersion(connection);
      var items = ReadItems(connection);
      PipeWarehouseStore.RefreshDisplayNames(items);
      return (version, items);
    }
  }

  public static long Save(IEnumerable<PipeWarehouseStockItem> items, long? expectedVersion = null)
  {
    lock (Gate)
    {
      EnsureInitializedUnlocked();
      using var connection = Open();
      using var tx = connection.BeginTransaction();

      var current = GetVersion(connection);
      if (expectedVersion is not null && expectedVersion.Value != current)
        throw new InvalidOperationException(
          $"Lager wurde zwischenzeitlich geändert (Version {current}, erwartet {expectedVersion.Value}). Bitte neu laden.");

      Execute(connection, "DELETE FROM stock;", tx);
      foreach (var item in items.Where(i => !string.IsNullOrWhiteSpace(i.ProfileId) && i.LengthMm > 0))
      {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
          INSERT INTO stock (profile_id, material, length_mm, quantity, reserved_quantity)
          VALUES ($p, $m, $l, $q, $r);
          """;
        cmd.Parameters.AddWithValue("$p", item.ProfileId);
        cmd.Parameters.AddWithValue("$m", item.Material ?? PipeMaterialTypes.Steel);
        cmd.Parameters.AddWithValue("$l", item.LengthMm);
        cmd.Parameters.AddWithValue("$q", Math.Max(0, item.Quantity));
        cmd.Parameters.AddWithValue("$r", Math.Max(0, item.ReservedQuantity));
        cmd.ExecuteNonQuery();
      }

      var next = current + 1;
      SetVersion(connection, next, tx);
      tx.Commit();
      return next;
    }
  }

  public static long GetCurrentVersion()
  {
    lock (Gate)
    {
      EnsureInitializedUnlocked();
      using var connection = Open();
      return GetVersion(connection);
    }
  }

  private static void EnsureInitializedUnlocked()
  {
    Directory.CreateDirectory(AppInfo.UserDataDirectory);
    using var connection = Open();
    Execute(connection, """
      CREATE TABLE IF NOT EXISTS meta (
        key TEXT PRIMARY KEY NOT NULL,
        value TEXT NOT NULL
      );
      CREATE TABLE IF NOT EXISTS stock (
        profile_id TEXT NOT NULL,
        material TEXT NOT NULL,
        length_mm REAL NOT NULL,
        quantity INTEGER NOT NULL,
        reserved_quantity INTEGER NOT NULL,
        PRIMARY KEY (profile_id, material, length_mm)
      );
      """);
    if (GetVersion(connection) <= 0)
      SetVersion(connection, 1);
  }

  private static SqliteConnection Open()
  {
    var connection = new SqliteConnection($"Data Source={DatabasePath}");
    connection.Open();
    using var cmd = connection.CreateCommand();
    cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
    cmd.ExecuteNonQuery();
    return connection;
  }

  private static List<PipeWarehouseStockItem> ReadItems(SqliteConnection connection)
  {
    var items = new List<PipeWarehouseStockItem>();
    using var cmd = connection.CreateCommand();
    cmd.CommandText = """
      SELECT profile_id, material, length_mm, quantity, reserved_quantity
      FROM stock
      ORDER BY profile_id, length_mm;
      """;
    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
      items.Add(new PipeWarehouseStockItem
      {
        ProfileId = reader.GetString(0),
        Material = reader.IsDBNull(1) ? PipeMaterialTypes.Steel : reader.GetString(1),
        LengthMm = reader.GetDouble(2),
        Quantity = reader.GetInt32(3),
        ReservedQuantity = reader.GetInt32(4)
      });
    }

    return items;
  }

  private static void SeedCatalog(SqliteConnection connection)
  {
    foreach (var profile in PipeStockCatalog.All)
    {
      using var cmd = connection.CreateCommand();
      cmd.CommandText = """
        INSERT OR IGNORE INTO stock (profile_id, material, length_mm, quantity, reserved_quantity)
        VALUES ($p, $m, $l, 0, 0);
        """;
      cmd.Parameters.AddWithValue("$p", profile.Id);
      cmd.Parameters.AddWithValue("$m", profile.Material);
      cmd.Parameters.AddWithValue("$l", CutOptimizationDefaults.StockLengthMm);
      cmd.ExecuteNonQuery();
    }

    SetVersion(connection, Math.Max(1, GetVersion(connection) + 1));
  }

  private static void TryImportLegacyXml(SqliteConnection connection)
  {
    var xmlPath = Path.Combine(AppInfo.UserDataDirectory, LegacyXmlFileName);
    if (!File.Exists(xmlPath))
      return;

    try
    {
      var document = XDocument.Load(xmlPath);
      var imported = 0;
      foreach (var element in document.Root?.Elements("Stock") ?? [])
      {
        var profileId = (string?)element.Attribute("profileId") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(profileId))
          continue;

        var length = double.TryParse(
          (string?)element.Attribute("lengthMm"),
          NumberStyles.Float,
          CultureInfo.InvariantCulture,
          out var parsedLength)
          ? parsedLength
          : 0;
        if (length <= 0)
          continue;

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
          INSERT OR REPLACE INTO stock (profile_id, material, length_mm, quantity, reserved_quantity)
          VALUES ($p, $m, $l, $q, $r);
          """;
        cmd.Parameters.AddWithValue("$p", profileId);
        cmd.Parameters.AddWithValue("$m", (string?)element.Attribute("material") ?? PipeMaterialTypes.Steel);
        cmd.Parameters.AddWithValue("$l", length);
        cmd.Parameters.AddWithValue("$q", int.TryParse((string?)element.Attribute("quantity"), out var q) ? q : 0);
        cmd.Parameters.AddWithValue("$r", int.TryParse((string?)element.Attribute("reservedQuantity"), out var r) ? r : 0);
        cmd.ExecuteNonQuery();
        imported++;
      }

      if (imported > 0)
        SetVersion(connection, Math.Max(2, GetVersion(connection) + 1));
    }
    catch
    {
      // Legacy-Import optional
    }
  }

  private static long GetVersion(SqliteConnection connection)
  {
    using var cmd = connection.CreateCommand();
    cmd.CommandText = "SELECT value FROM meta WHERE key = 'version' LIMIT 1;";
    var value = cmd.ExecuteScalar()?.ToString();
    return long.TryParse(value, out var version) ? version : 0;
  }

  private static void SetVersion(SqliteConnection connection, long version, SqliteTransaction? tx = null)
  {
    using var cmd = connection.CreateCommand();
    cmd.Transaction = tx;
    cmd.CommandText = """
      INSERT INTO meta(key, value) VALUES('version', $v)
      ON CONFLICT(key) DO UPDATE SET value = excluded.value;
      """;
    cmd.Parameters.AddWithValue("$v", version.ToString(CultureInfo.InvariantCulture));
    cmd.ExecuteNonQuery();
  }

  private static void Execute(SqliteConnection connection, string sql, SqliteTransaction? tx = null)
  {
    using var cmd = connection.CreateCommand();
    cmd.Transaction = tx;
    cmd.CommandText = sql;
    cmd.ExecuteNonQuery();
  }

  private static long ScalarLong(SqliteConnection connection, string sql)
  {
    using var cmd = connection.CreateCommand();
    cmd.CommandText = sql;
    var result = cmd.ExecuteScalar();
    return result is long l ? l : Convert.ToInt64(result, CultureInfo.InvariantCulture);
  }
}
