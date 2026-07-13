namespace RohreZuschnittOptimierung.Models;

public enum PipeOrderStatus
{
  Reserved,
  Ordered,
  MaterialReceived,
  Completed
}

public static class PipeOrderStatusLabels
{
  public static string ToLabel(PipeOrderStatus status) => status switch
  {
    PipeOrderStatus.Reserved => "Reserviert (Material im Lager)",
    PipeOrderStatus.Ordered => "Bestellt (Material fehlt)",
    PipeOrderStatus.MaterialReceived => "Material da",
    PipeOrderStatus.Completed => "Abgeschlossen",
    _ => status.ToString()
  };
}
