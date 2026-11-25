namespace Somnium.MigrationTool
{
    /// <summary>
    ///     Fixed identifiers the migration relies on. GUIDs are stable across every Community Modules and Photon release
    ///     we know of, so they are safer than names or paths, which creators may change.
    /// </summary>
    internal static class MigrationConstants
    {
        public const string ToolVersion = "1.0.0";
        public const string TargetFusionVersion = "2.1.3";
        public const string TargetCommunityModulesVersion = "4.0";

        // Community Modules
        public const string CmScriptsAsmdefGuid = "0eac857c3f7d3f145947f82658c83ed9";
        public const string CmNetworkAsmdefGuid = "58de1d31e4aad8848b2afdda4b94c61b"; // SomniumSpace.Network
        public const string NetworkGrabbableGuid = "f8f14f1198a49cb418d604114edc06a7"; // Changed in 4.0: marks a finished import
        public const string CmCanonicalRoot = "Assets/#User/Community Modules";
        public const string CmDefaultAsmdefName = "CommunityModules";
        public const string ScriptsBackupFolderName = "#Scripts Backup"; // Written by the Namespace Helper

        // Fusion
        public const string OldNetworkRigidbody3DGuid = "0a591d221a634417e9827eb58e17de84";
        public const string FusionRuntimeDllGuid = "e725a070cec140c4caffb81624c8c787";
        public const string NetworkTransformFileId = "158639473";
        public const string NetworkProjectConfigGuid = "321799b8b9ebbb443aae0b8935040810";
        public const string PhotonAppSettingsGuid = "6e1cb6b655480254b980470bddc2240f";
        public const string PhotonRoot = "Assets/Photon";

        /// <summary>
        ///     Photon folders shipped by the old SDK packages. Everything else under Assets/Photon is left alone.
        ///     Photon Voice is removed and not installed again: Somnium worlds and Community Modules 4.0 do not use it.
        /// </summary>
        public static readonly string[] PhotonFoldersToDelete =
        {
            "Fusion", "FusionAddons", "FusionMenu", "FusionDemos", "PhotonLibs", "PhotonRealtime",
            "PhotonUnityNetworking", "PhotonVoice", "PhotonChat",
        };

        /// <summary>Loose files the old SDK packages put directly in Assets/Photon.</summary>
        public static readonly string[] PhotonLooseFilesToDelete =
        {
            "PhotonVoice-Documentation.chm", "PhotonVoice-Documentation.pdf",
        };

        /// <summary>Creator settings that live inside the Photon folders and must survive the delete.</summary>
        public static readonly string[] PhotonSettingsToKeep =
        {
            "Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion",
            "Assets/Photon/Fusion/Resources/PhotonAppSettings.asset",
        };

        /// <summary>Scripting defines Fusion 2.1.3 and Photon Realtime 5 add on their first editor load.</summary>
        public static readonly string[] Fusion21Defines =
        {
            "FUSION_WEAVER", "FUSION2", "FUSION_2", "FUSION_2_1", "FUSION_2_1_3",
            "FUSION_2_OR_NEWER", "FUSION_2_0_OR_NEWER", "FUSION_2_1_OR_NEWER", "PHOTON_REALTIME_5_OR_NEWER",
        };

        /// <summary>Assembly names that no longer exist after the Photon update (asmdef references by name).</summary>
        public static readonly string[] ObsoleteAssemblyNames =
        {
            "Fusion.Addons.Physics", "Fusion.Addons.Physics.Editor", "PhotonUnityNetworking", "PhotonUnityNetworking.Editor",
            "PhotonUnityNetworking.Utilities", "PhotonUnityNetworking.Utilities.Culling.Editor",
            "PhotonUnityNetworking.Utilities.PhotonPlayer.Editor", "PhotonVoice.PUN", "PhotonVoice.PUN.Editor", "PhotonChat",
            // Photon Voice is no longer installed
            "PhotonVoice", "PhotonVoice.API", "PhotonVoice.Fusion", "PhotonVoice.Fusion.Editor", "PhotonVoice.Unity",
            "PhotonVoice.Unity.Editor", "PhotonVoice.Editor", "PhotonVoice.Utilities.Editor", "PhotonVoice.FMOD", "PhotonVoiceApi.FMOD",
            "Voice.FMOD.Editor", "PhotonVoice.PermissionsRequester", "MicAmplifierEditor.Editor",
        };

        /// <summary>Assembly definition GUIDs that no longer exist after the Photon update (references by GUID).</summary>
        public static readonly string[] ObsoleteAssemblyGuids =
        {
            "ee448cb1442054024b3c982e54100a6a", // Fusion.Addons.Physics
            "1a714651de5a3064b976d937ff6309c6", // Fusion.Addons.Physics.Editor
            "57c32fc907df0f54e8e6e8f0d2488336", // PhotonUnityNetworking
            "4025d00f1ce60da4ea2d0830acf5ebfb", // PhotonUnityNetworking.Editor
            "a738238285a7a8246af046cbf977522f", // PhotonUnityNetworking.Utilities
            "06dafbbe6b7b3a84f84213f47aabe7f0", // PhotonUnityNetworking.Utilities.Culling.Editor
            "7024f760fc566cf45a16d2c838e22b2d", // PhotonUnityNetworking.Utilities.PhotonPlayer.Editor
            "d62e4537d6f8ca64c8d2affe565a5cd1", // PhotonVoice.PUN
            "bc45562f375e105438d27eded7d73e12", // PhotonVoice.PUN.Editor
            "08a110bd598f7604f9519c2d7e1fb3cc", // PhotonChat
            // Photon Voice is no longer installed (same GUIDs in the Pro SDK 2.0 and 2.2 Voice and in Voice 2.64)
            "b59fae9c7ebc1574e8690f2570b29d6f", // PhotonVoice
            "8e3c55029ca641543bf030857af4d525", // PhotonVoice.API
            "415d649162855d948bfd44d8c5f1e7a2", // PhotonVoice.Fusion
            "34512bc876713634eaa8074c1282686e", // PhotonVoice.Fusion.Editor
            "a0bf7528664349e458c6a16cdad56924", // PhotonVoice.Unity
            "85b9977c5d26ef6458c096b1676616ed", // PhotonVoice.Unity.Editor
            "ae8024269c736ee49ba1179cb00214e5", // PhotonVoice.Editor
            "a4110ddd96589404bb35c918cc2fe96b", // PhotonVoice.Utilities.Editor
            "f72075cbb8556954582070dc932785e1", // PhotonVoice.FMOD
            "48dcc5f57bcf244408f4c5a73f4ddb89", // PhotonVoiceApi.FMOD
            "4f7021285a23e9c4ab652c94802fafe8", // Voice.FMOD.Editor
            "feff221067f801b4fb824a8cd5f86d37", // PhotonVoice.PermissionsRequester
            "67a01810713926e45b690b7832da5847", // MicAmplifierEditor.Editor
        };

        /// <summary>Renamed assemblies: old name, new name (same GUID).</summary>
        public static readonly string[,] RenamedAssemblies =
        {
            { "PhotonRealtime", "Photon.Realtime" },
        };

        // Tool files (GUIDs of the .meta files shipped with the tool)
        public const string KnownCommunityModulesDataGuid = "5c1e0a7d2b8f4e61a9d3c7b2e4f60a11";
        public const string KnownPhotonDataGuid = "5c1e0a7d2b8f4e61a9d3c7b2e4f60a12";
        public const string PhotonPackageGuid = "5c1e0a7d2b8f4e61a9d3c7b2e4f60a21";
        public const string CommunityModulesPackageGuid = "5c1e0a7d2b8f4e61a9d3c7b2e4f60a22";
        public const string ToolRootFolderGuid = "5c1e0a7d2b8f4e61a9d3c7b2e4f60a01";
        public const string GuideGuid = "5c1e0a7d2b8f4e61a9d3c7b2e4f60a31"; // Fusion_2.1_Migration_Guide.md (see mktool.sh)

        // Working folder, outside Assets so Unity never imports it
        public const string WorkFolderName = "MigrationBackups";
    }
}
