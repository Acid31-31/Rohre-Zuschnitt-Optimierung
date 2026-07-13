using System.Windows;
using RohreZuschnittOptimierung.Services;

namespace RohreZuschnittOptimierung;

public partial class App : Application
{
  protected override void OnStartup(StartupEventArgs e)
  {
    if (UpdateApplyRunner.TryParseApplyUpdateArgs(
          e.Args ?? [],
          out var stagedRoot,
          out var targetRoot,
          out var parentProcessId))
    {
      var applyWindow = new UpdateApplyProgressWindow(stagedRoot, targetRoot, parentProcessId);
      applyWindow.ShowDialog();
      Shutdown();
      return;
    }

    AppSecurityService.Initialize();
    PdfFontBootstrap.EnsureInitialized();
    ThemeService.Initialize(this);
    PipeWarehouseStore.EnsureInitialized();
    base.OnStartup(e);
  }
}
