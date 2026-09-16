using System.Collections.Concurrent;
using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

/// <summary>In-Memory-Präsenz der verbundenen PCs (nur auf der Lager-Zentrale).</summary>
internal static class WarehousePresenceRegistry
{
  private static readonly ConcurrentDictionary<string, WarehousePresenceDto> Peers = new(StringComparer.OrdinalIgnoreCase);
  private static readonly TimeSpan OfflineAfter = TimeSpan.FromSeconds(10);

  public static void Heartbeat(WarehousePresenceDto peer)
  {
    if (peer is null || string.IsNullOrWhiteSpace(peer.ClientId))
      return;

    peer.LastSeenUtc = DateTimeOffset.UtcNow;
    peer.DisplayName = string.IsNullOrWhiteSpace(peer.DisplayName)
      ? BuildDisplayName(peer.UserName, peer.MachineName)
      : peer.DisplayName.Trim();
    Peers[peer.ClientId.Trim()] = peer;
  }

  public static IReadOnlyList<WarehousePresenceDto> GetActive()
  {
    var cutoff = DateTimeOffset.UtcNow - OfflineAfter;
    var active = new List<WarehousePresenceDto>();
    foreach (var pair in Peers)
    {
      if (pair.Value.LastSeenUtc < cutoff)
      {
        Peers.TryRemove(pair.Key, out _);
        continue;
      }

      active.Add(pair.Value);
    }

    return active
      .OrderBy(p => p.Role == "host" ? 0 : 1)
      .ThenBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
      .ToList();
  }

  public static WarehousePresenceDto CreateLocalPeer(string role)
  {
    var user = Environment.UserName ?? string.Empty;
    var machine = Environment.MachineName ?? string.Empty;
    return new WarehousePresenceDto
    {
      ClientId = MachineFingerprintService.GetMachineFingerprint(),
      UserName = user,
      MachineName = machine,
      DisplayName = BuildDisplayName(user, machine),
      Role = role,
      LastSeenUtc = DateTimeOffset.UtcNow
    };
  }

  public static string BuildDisplayName(string? userName, string? machineName)
  {
    var user = (userName ?? string.Empty).Trim();
    var machine = (machineName ?? string.Empty).Trim();
    if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(machine))
      return $"{user} @ {machine}";
    if (!string.IsNullOrWhiteSpace(machine))
      return machine;
    if (!string.IsNullOrWhiteSpace(user))
      return user;
    return "Unbekannt";
  }
}
