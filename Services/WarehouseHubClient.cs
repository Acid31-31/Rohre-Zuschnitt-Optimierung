using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

internal static class WarehouseHubClient
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true
  };

  private static readonly HttpClient Http = new()
  {
    Timeout = TimeSpan.FromSeconds(8)
  };

  public static string NormalizeBaseUrl(string? url)
  {
    var trimmed = (url ?? string.Empty).Trim().TrimEnd('/');
    if (string.IsNullOrWhiteSpace(trimmed))
      return string.Empty;
    if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        && !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
      trimmed = "http://" + trimmed;
    return trimmed.TrimEnd('/');
  }

  public static async Task<(bool Ok, string Message)> ProbeAsync(string baseUrl, CancellationToken cancellationToken = default)
  {
    try
    {
      var url = NormalizeBaseUrl(baseUrl) + "/api/health";
      using var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
      if (!response.IsSuccessStatusCode)
        return (false, $"Lager-Zentrale antwortet nicht ({(int)response.StatusCode}).");
      return (true, "Verbindung zur Lager-Zentrale OK.");
    }
    catch (Exception ex)
    {
      return (false, "Keine Verbindung zur Lager-Zentrale: " + ex.Message);
    }
  }

  public static (long Version, List<PipeWarehouseStockItem> Items) Load(string baseUrl)
  {
    var url = NormalizeBaseUrl(baseUrl) + "/api/warehouse";
    using var response = Http.GetAsync(url).GetAwaiter().GetResult();
    response.EnsureSuccessStatusCode();
    var snapshot = response.Content.ReadFromJsonAsync<WarehouseSnapshotDto>(JsonOptions).GetAwaiter().GetResult()
                   ?? throw new InvalidOperationException("Leere Antwort von der Lager-Zentrale.");
    var items = snapshot.Items.Select(ToItem).ToList();
    PipeWarehouseStore.RefreshDisplayNames(items);
    return (snapshot.Version, items);
  }

  public static long Save(string baseUrl, IEnumerable<PipeWarehouseStockItem> items, long expectedVersion)
  {
    var url = NormalizeBaseUrl(baseUrl) + "/api/warehouse";
    var payload = new WarehouseSnapshotDto
    {
      Version = expectedVersion,
      Items = items.Select(ToDto).ToList()
    };

    using var response = Http.PutAsJsonAsync(url, payload, JsonOptions).GetAwaiter().GetResult();
    if ((int)response.StatusCode == 409)
    {
      var conflict = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
      throw new InvalidOperationException(
        "Lager wurde gleichzeitig auf einem anderen PC geändert. Bitte Fenster neu öffnen / erneut speichern."
        + (string.IsNullOrWhiteSpace(conflict) ? string.Empty : " " + conflict));
    }

    response.EnsureSuccessStatusCode();
    var snapshot = response.Content.ReadFromJsonAsync<WarehouseSnapshotDto>(JsonOptions).GetAwaiter().GetResult()
                   ?? throw new InvalidOperationException("Leere Antwort nach Speichern.");
    return snapshot.Version;
  }

  private static WarehouseStockDto ToDto(PipeWarehouseStockItem item) =>
    new()
    {
      ProfileId = item.ProfileId,
      Material = item.Material,
      LengthMm = item.LengthMm,
      Quantity = item.Quantity,
      ReservedQuantity = item.ReservedQuantity
    };

  private static PipeWarehouseStockItem ToItem(WarehouseStockDto item) =>
    new()
    {
      ProfileId = item.ProfileId,
      Material = item.Material,
      LengthMm = item.LengthMm,
      Quantity = item.Quantity,
      ReservedQuantity = item.ReservedQuantity
    };
}
