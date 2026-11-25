namespace Somnium.MigrationTool
{
    /// <summary>Short fixes for the compile errors creator scripts usually get after the Fusion 2.1 update.</summary>
    internal static class CompileHints
    {
        static readonly string[,] Hints =
        {
            { "CharacterTrigger", "CharacterTrigger was renamed LocalPlayerTrigger (Community Modules 3.0). Its events are now OnPlayerEnter / OnPlayerExit." },
            { "OnCharacterEnter", "LocalPlayerTrigger events are OnPlayerEnter / OnPlayerExit (they were OnCharacterEnter / OnCharacterExit)." },
            { "OnCharacterExit", "LocalPlayerTrigger events are OnPlayerEnter / OnPlayerExit (they were OnCharacterEnter / OnCharacterExit)." },
            { "NetworkFloat", "NetworkFloat was renamed NetworkEventFloat (Community Modules 3.0). SetValue(float) is unchanged; it now uses a NetworkBridgeData component." },
            { "NetworkRigidbody3D","NetworkRigidbody3D no longer exists. Use NetworkTransform (using Fusion;) on the object with the Rigidbody. " +
                "Teleport: networkTransform.Teleport(position, rotation). Velocity: GetComponent<Rigidbody>().linearVelocity." },
            { "Fusion.Addons.Physics", "Remove 'using Fusion.Addons.Physics;' and use 'using Fusion;' with NetworkTransform." },
            { "RunnerSimulatePhysics", "RunnerSimulatePhysics3D is not used anymore: remove it. NetworkTransform handles Rigidbodies itself." },
            { "ArraySegment<byte>", "OnReliableDataReceived now receives ReadOnlySpan<byte> data. Use data.ToArray() if you need to keep the bytes." },
            { "OnReliableDataReceived", "OnReliableDataReceived now receives ReadOnlySpan<byte> data. Use data.ToArray() if you need to keep the bytes." },
            { "ExitGames", "Replace 'using ExitGames.Client.Photon;' with 'using Photon.Client;'." },
            { "Fusion.Photon.Realtime", "Replace 'using Fusion.Photon.Realtime;' with 'using Photon.Realtime;'." },
            { "DebugLevel", "DebugLevel was renamed LogLevel (namespace Photon.Client)." },
            { "MasterClient", "PlayerRef.MasterClient was renamed. Use Runner.IsSharedModeMasterClient or Runner.GetMasterClient()." },
            { "SendPriority", "NetworkObject.SendPriority is obsolete. Remove it, or use NetworkObject.SetPriority(player, priority)." },
            { "FUSION_2_0", "Version defines changed: use FUSION_2_1_OR_NEWER instead of FUSION_2_0_*." },
            { "PhotonRealtime", "The PhotonRealtime assembly is now called Photon.Realtime. Update your assembly definition references." },
            { "CS0246", "A type was not found. If it was a Community Modules script, it may have been renamed or removed in this version." },
        };

        public static string For(string error)
        {
            for (int i = 0; i < Hints.GetLength(0); i++)
            {
                if (error.Contains(Hints[i, 0]))
                    return Hints[i, 1];
            }
            return null;
        }
    }
}
