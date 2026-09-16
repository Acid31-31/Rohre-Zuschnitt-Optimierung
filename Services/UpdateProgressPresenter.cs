using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

internal sealed class UpdateProgressPresenter
{
  private readonly Action<int, string, string> _updateUi;
  private readonly DateTime _startedAt = DateTime.UtcNow;

  public UpdateProgressPresenter(Action<int, string, string> updateUi) =>
    _updateUi = updateUi;

  public void Report(UpdateProgressInfo info) =>
    _updateUi(info.Percent, info.Message, FormatRemaining(info.Percent));

  private string FormatRemaining(int percent)
  {
    if (percent <= 0)
      return "Restlaufzeit wird berechnet…";

    if (percent >= 100)
      return "Fast fertig…";

    var elapsed = DateTime.UtcNow - _startedAt;
    if (elapsed.TotalSeconds < 1.5)
      return "Restlaufzeit wird berechnet…";

    var totalSeconds = elapsed.TotalSeconds * 100.0 / percent;
    var remaining = TimeSpan.FromSeconds(Math.Max(1, totalSeconds - elapsed.TotalSeconds));

    if (remaining.TotalHours >= 1)
      return $"Restlaufzeit: ca. {(int)remaining.TotalHours} Std. {remaining.Minutes} Min.";

    if (remaining.TotalMinutes >= 1)
      return $"Restlaufzeit: ca. {(int)remaining.TotalMinutes} Min. {remaining.Seconds} Sek.";

    return $"Restlaufzeit: ca. {Math.Max(1, (int)remaining.TotalSeconds)} Sek.";
  }
}
