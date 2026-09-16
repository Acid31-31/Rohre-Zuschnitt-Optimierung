namespace RohreZuschnittOptimierung.Models;

public sealed class WarehousePresenceDto
{
  public string ClientId { get; set; } = string.Empty;
  public string DisplayName { get; set; } = string.Empty;
  public string MachineName { get; set; } = string.Empty;
  public string UserName { get; set; } = string.Empty;
  public string Role { get; set; } = "client"; // host | client
  public DateTimeOffset LastSeenUtc { get; set; }
}

public sealed class WarehousePresenceSnapshotDto
{
  public List<WarehousePresenceDto> Peers { get; set; } = [];
}
