namespace RohreZuschnittOptimierung.Models;

public enum WarehouseSyncMode
{
  /// <summary>Nur dieser PC – SQLite lokal neben der EXE.</summary>
  Local = 0,

  /// <summary>Dieser PC ist die Lager-Zentrale (HTTP-Server + lokale SQLite).</summary>
  Host = 1,

  /// <summary>Verbindung zu einer Lager-Zentrale im Netzwerk.</summary>
  Client = 2,

  /// <summary>Gemeinsamer Lager-Ordner (UNC/Netzwerk) – jeder PC kann zugreifen.</summary>
  SharedFolder = 3
}
