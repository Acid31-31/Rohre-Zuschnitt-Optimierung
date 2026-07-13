namespace RohreZuschnittOptimierung.Models;

public static class PipeMaterialTypes
{
  public const string Steel = "Stahl";
  public const string Stainless = "Edelstahl";
  public const string Aluminum = "Aluminium";

  public static IReadOnlyList<string> All { get; } = [Steel, Stainless, Aluminum];
}
