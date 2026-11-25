namespace SomniumSpace.Worlds.SomniumMetroid
{
    /// <summary>
    /// Build stamp: logged at boot, so a tester can tell which upload they are in (Player.log line "Metroid Prime Dreamport ...").
    /// Bump Iteration on every upload and add a row to the README "Iterations" table.
    /// </summary>
    public static class MpVersion
    {
        public const string Iteration = "1.8.2";
        public const string Date = "2026-11-10";
        public const string Summary = "Iteration 1.8.2: the morph ball and elevators move your AVATAR, never the world (co-op: everyone sees the same world). 1.8.1: everyone sees the same world (after a ride or a morph-ball roll the world goes back to its shared place and the avatar moves with it). 1.8 (co-op, the Annihilator way): the world carries its own SceneNetworking (Community Modules 4.0, as the Annihilator world does) so the client registers its network objects when you join. 1.7 (co-op live fix): this world registers its own network objects (an uploaded world has no Community Modules networking, so nobody could join), the intro is skipped by default. 1.6 (network upgrade): Photon Fusion 2.1.3 and Community Modules 4.0, as Somnium now runs (2.0 worlds no longer sync); co-op unchanged otherwise.";

        public static string Label { get { return "Iteration " + Iteration + " (" + Date + ")"; } }
    }
}
