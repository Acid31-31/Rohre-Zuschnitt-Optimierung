using System.IO;
using System.Media;

namespace RohreZuschnittOptimierung.Services;

/// <summary>
/// Eigene weiche Keygen-Melodie nur im KEY-Tool. Startet leise (Einblendung).
/// </summary>
internal static class KeyToolChiptunePlayer
{
  private const int SampleRate = 22050;
  private const int Bpm = 120;
  private const double FadeInSeconds = 3.5;
  private const int LoopRepeats = 28;
  private static readonly object Gate = new();
  private static SoundPlayer? _player;
  private static MemoryStream? _stream;

  public static bool IsPlaying { get; private set; }

  public static void Start()
  {
    lock (Gate)
    {
      StopUnlocked();
      try
      {
        var wav = BuildWav(RenderSession());
        _stream = new MemoryStream(wav, writable: false);
        _player = new SoundPlayer(_stream);
        _player.Load();
        _player.Play();
        IsPlaying = true;
      }
      catch
      {
        StopUnlocked();
      }
    }
  }

  public static void Stop()
  {
    lock (Gate)
      StopUnlocked();
  }

  public static bool Toggle()
  {
    lock (Gate)
    {
      if (IsPlaying)
      {
        StopUnlocked();
        return false;
      }
    }

    Start();
    return true;
  }

  private static void StopUnlocked()
  {
    try { _player?.Stop(); } catch { }
    _player?.Dispose();
    _player = null;
    _stream?.Dispose();
    _stream = null;
    IsPlaying = false;
  }

  private static short[] RenderSession()
  {
    var loop = RenderLoop();
    var fadeSamples = Math.Max(1, (int)Math.Round(SampleRate * FadeInSeconds));
    var total = loop.Length * LoopRepeats;
    var pcm = new short[total];

    for (var r = 0; r < LoopRepeats; r++)
      Array.Copy(loop, 0, pcm, r * loop.Length, loop.Length);

    var fadeLen = Math.Min(fadeSamples, pcm.Length);
    for (var i = 0; i < fadeLen; i++)
    {
      var gain = 0.5 - 0.5 * Math.Cos(Math.PI * i / fadeLen);
      pcm[i] = (short)Math.Round(pcm[i] * gain);
    }

    return pcm;
  }

  private static short[] RenderLoop()
  {
    var stepSamples = (int)Math.Round(SampleRate * 60.0 / Bpm / 4.0);
    const int steps = 128;
    var length = stepSamples * steps;
    var mix = new double[length];

    int[] bass =
    [
      48, 48, 48, 55, 48, 48, 55, 48,
      53, 53, 53, 60, 55, 55, 50, 48,
      45, 45, 45, 52, 45, 45, 52, 45,
      53, 53, 53, 50, 55, 55, 52, 48
    ];

    int[] pads =
    [
      64, 64, 64, 64, 71, 71, 71, 71,
      69, 69, 69, 69, 71, 71, 67, 64
    ];

    int[] lead =
    [
      0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
      67, 67, 0, 71, 72, 0, 71, 67, 64, 0, 67, 71, 72, 71, 67, 0,
      71, 71, 0, 74, 76, 0, 74, 71, 67, 0, 71, 74, 76, 74, 71, 0,
      67, 0, 64, 67, 71, 72, 71, 67, 69, 72, 76, 72, 71, 67, 64, 0,
      72, 0, 71, 67, 71, 0, 72, 76, 72, 71, 67, 71, 72, 0, 71, 0,
      69, 69, 0, 72, 76, 0, 72, 69, 71, 67, 64, 67, 71, 0, 72, 0,
      67, 71, 72, 76, 72, 71, 67, 64, 66, 67, 71, 0, 67, 64, 60, 0,
      64, 67, 71, 72, 76, 72, 71, 67, 69, 72, 71, 67, 64, 60, 64, 67
    ];

    var bassPhase = 0.0;
    var padPhase = 0.0;
    var leadPhase = 0.0;
    var bassLp = 0.0;
    var padLp = 0.0;
    var leadLp = 0.0;

    for (var step = 0; step < steps; step++)
    {
      var bassNote = bass[step % bass.Length];
      var padNote = pads[step % pads.Length];
      var leadNote = lead[step % lead.Length];

      for (var i = 0; i < stepSamples; i++)
      {
        var idx = step * stepSamples + i;
        var t = i / (double)stepSamples;
        var env = 1.0 - t * 0.10;

        bassPhase += MidiHz(bassNote) / SampleRate;
        if (bassPhase >= 1) bassPhase -= 1;
        var rawBass = Sine(bassPhase) * 0.85 + Tri(bassPhase) * 0.15;
        bassLp += 0.08 * (rawBass - bassLp);
        mix[idx] += bassLp * 0.14 * env;

        padPhase += MidiHz(padNote) / SampleRate;
        if (padPhase >= 1) padPhase -= 1;
        padLp += 0.06 * (Sine(padPhase) - padLp);
        mix[idx] += padLp * 0.06 * env;

        if (leadNote > 0)
        {
          leadPhase += MidiHz(leadNote) / SampleRate;
          if (leadPhase >= 1) leadPhase -= 1;
          var leadEnv = Math.Min(1, t / 0.04) * (1.0 - t * 0.28);
          var rawLead = Sine(leadPhase) * 0.65 + Tri(leadPhase) * 0.35;
          leadLp += 0.12 * (rawLead - leadLp);
          mix[idx] += leadLp * 0.26 * leadEnv;
        }
      }
    }

    var peak = 1e-9;
    for (var i = 0; i < length; i++)
    {
      var a = Math.Abs(mix[i]);
      if (a > peak)
        peak = a;
    }

    var scale = 15000.0 / peak;
    var pcm = new short[length];
    for (var i = 0; i < length; i++)
      pcm[i] = (short)Math.Clamp(Math.Round(mix[i] * scale), -32767, 32767);

    return pcm;
  }

  private static double MidiHz(int midi) =>
    440.0 * Math.Pow(2.0, (midi - 69) / 12.0);

  private static double Sine(double phase) =>
    Math.Sin(2 * Math.PI * phase);

  private static double Tri(double phase) =>
    phase < 0.5 ? phase * 4 - 1 : 3 - phase * 4;

  private static byte[] BuildWav(short[] pcm)
  {
    var dataBytes = pcm.Length * 2;
    using var ms = new MemoryStream(44 + dataBytes);
    using var w = new BinaryWriter(ms);
    w.Write("RIFF"u8.ToArray());
    w.Write(36 + dataBytes);
    w.Write("WAVE"u8.ToArray());
    w.Write("fmt "u8.ToArray());
    w.Write(16);
    w.Write((short)1);
    w.Write((short)1);
    w.Write(SampleRate);
    w.Write(SampleRate * 2);
    w.Write((short)2);
    w.Write((short)16);
    w.Write("data"u8.ToArray());
    w.Write(dataBytes);
    var bytes = new byte[dataBytes];
    Buffer.BlockCopy(pcm, 0, bytes, 0, dataBytes);
    w.Write(bytes);
    return ms.ToArray();
  }
}
