using System;

namespace SomniumSpace.Worlds.SomniumMetroid
{
    /// <summary>
    /// Logging for code that must also run outside Unity (the offline probe in DevTools/probe). Game code sets the sinks to
    /// Debug.Log once the whitelist lets the player in; the probe sets Console.WriteLine. Every line carries the
    /// [SomniumMetroid] tag so Player.log greps work.
    /// </summary>
    public static class MpLog
    {
        public const string Tag = "[SomniumMetroid] ";
        public static Action<string> Sink;
        public static Action<string> WarnSink;
        public static Action<string> ErrorSink;

        public static void Info(string message) { var s = Sink; if (s != null) s(Tag + message); }
        public static void Warn(string message) { var s = WarnSink ?? Sink; if (s != null) s(Tag + message); }
        public static void Error(string message) { var s = ErrorSink ?? WarnSink ?? Sink; if (s != null) s(Tag + message); }
    }
}
