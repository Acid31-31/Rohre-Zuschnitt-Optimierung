using System.Text.Json.Serialization;

namespace RohreZuschnittOptimierung.Models;

public sealed class WarehouseSnapshotDto
{
  [JsonPropertyName("version")]
  public long Version { get; set; }

  [JsonPropertyName("items")]
  public List<WarehouseStockDto> Items { get; set; } = [];
}

public sealed class WarehouseStockDto
{
  [JsonPropertyName("profileId")]
  public string ProfileId { get; set; } = string.Empty;

  [JsonPropertyName("material")]
  public string Material { get; set; } = PipeMaterialTypes.Steel;

  [JsonPropertyName("lengthMm")]
  public double LengthMm { get; set; }

  [JsonPropertyName("quantity")]
  public int Quantity { get; set; }

  [JsonPropertyName("reservedQuantity")]
  public int ReservedQuantity { get; set; }
}
