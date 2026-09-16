using System.IO;
using RohreZuschnittOptimierung.Models;
using RohreZuschnittOptimierung.Services;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var outDir = Path.Combine(root, "tools");
var outPdf = Path.Combine(outDir, "logo-preview.pdf");
Directory.CreateDirectory(outDir);

var assetsDir = Path.Combine(root, "Assets");
foreach (var file in new[] { "DrawingLogo.png", "AppIcon.png" })
{
  var source = Path.Combine(assetsDir, file);
  if (File.Exists(source))
    File.Copy(source, Path.Combine(AppContext.BaseDirectory, file), true);
}

var profile = new PipeProfileDefinition
{
  Id = "sq-30",
  Kind = PipeProfileKind.Square,
  DisplayName = "30x30x3",
  Dimensions = "30 x 30 x 3",
  Material = "Stahl"
};

TechnicalTubeDrawingRenderer.Render(
  outPdf,
  "preview_001",
  profile,
  "Stahl",
  1200,
  1,
  0,
  45,
  "test");

Console.WriteLine(outPdf);
