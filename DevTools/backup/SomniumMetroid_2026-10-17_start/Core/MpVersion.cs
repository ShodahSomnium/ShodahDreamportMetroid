namespace SomniumSpace.Worlds.SomniumMetroid
{
    /// <summary>
    /// Build stamp: logged at boot, so a tester can tell which upload they are in (Player.log line "Metroid Prime Dreamport ...").
    /// Bump Iteration on every upload and add a row to the README "Iterations" table.
    /// </summary>
    public static class MpVersion
    {
        public const string Iteration = "1.1";
        public const string Date = "2026-10-16";
        public const string Summary = "Iteration 1.1 (gun polish): the game's own gun effects (each beam's shots, trails, impacts with their sounds, muzzle flash, the charge glow and its aux muzzle, timed as on the GameCube), the game's combat reticle on the monitor, shots pass closed doors the way the game lets them (the hangar's upper lock buttons can be shot).";

        public static string Label { get { return "Iteration " + Iteration + " (" + Date + ")"; } }
    }
}
