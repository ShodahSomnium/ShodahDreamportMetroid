using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// Game space (Metroid Prime: X right, Y forward, Z up, right-handed, game units) to Unity space. Every mesh is built in game
    /// space and sits under one root whose transform does the conversion: rotation (-90, 0, 0) with scale (s, -s, s) maps game
    /// (x, y, z) to Unity (s x, s z, s y) - a reflection, which Unity handles by flipping the winding. The shaders therefore see game
    /// space as object space (MpTev.hlsl). s makes Samus's eye height (Player tweak: height 2.7, eye offset 0.2 -> 2.5 units) the
    /// player's own: 1.65 m / 2.5 = 0.66 m per game unit.
    /// </summary>
    public static class MpSpace
    {
        public const float SamusEyeHeight = 2.5f;      // CTweakPlayer mPlayerHeight 2.7 - mEyeOffset 0.2 (probe "tweak Player CTweakPlayer")
        public const float PlayerEyeHeight = 1.65f;
        public static float Scale = PlayerEyeHeight / SamusEyeHeight;

        /// <summary>Where the game world sits in the Somnium world (high above the plain spawn floor everyone sees).</summary>
        public static readonly Vector3 Home = new Vector3(0f, 1000f, 0f);
        /// <summary>Where the game world sits now: Home, except while this client rides / rolls / watches a cutscene (MpGame.SettleWorld
        /// brings it back - every client's world must sit at Home for the players to see each other in the same place).</summary>
        public static Vector3 Origin = Home;

        /// <summary>Slides the whole game world by a Unity offset (riding a moving platform: the world moves, the player never does).</summary>
        public static void Shift(Vector3 unityDelta)
        {
            Origin += unityDelta;
            if (Root != null) Root.position = Origin;
        }

        /// <summary>
        /// The world turned about its origin (identity except in first-person cutscenes: MpCinemaView turns it so each shot's exact
        /// view, pitch and roll included, is straight ahead of the player). Every conversion below goes through it.
        /// </summary>
        public static Quaternion Turn { get; private set; } = Quaternion.identity;
        private static Quaternion _unturn = Quaternion.identity;
        public static bool Turned { get; private set; }

        public static void SetTurn(Quaternion q)
        {
            Turn = q;
            _unturn = Quaternion.Inverse(q);
            Turned = Quaternion.Angle(q, Quaternion.identity) > 1e-4f;
            if (Root != null) Root.rotation = q * Quaternion.Euler(-90f, 0f, 0f);
        }

        public static Transform Root;

        public static Transform MakeRoot(Transform parent)
        {
            var go = new GameObject("Metroid World");
            go.transform.SetParent(parent, false);
            go.transform.position = Origin;
            go.transform.rotation = Turn * Quaternion.Euler(-90f, 0f, 0f);
            go.transform.localScale = new Vector3(Scale, -Scale, Scale);
            Root = go.transform;
            return Root;
        }

        public static Vector3 ToUnity(float x, float y, float z)
        {
            Vector3 d = new Vector3(x * Scale, z * Scale, y * Scale);
            return Origin + (Turned ? Turn * d : d);
        }

        public static Vector3 ToUnity(Vector3 game) { return ToUnity(game.x, game.y, game.z); }

        public static Vector3 ToGame(Vector3 unity)
        {
            Vector3 d = (unity - Origin) / Scale;
            if (Turned) d = _unturn * d;
            return new Vector3(d.x, d.z, d.y);
        }

        /// <summary>A game-space direction in Unity (no scale).</summary>
        public static Vector3 DirToUnity(Vector3 game) { Vector3 d = new Vector3(game.x, game.z, game.y); return Turned ? Turn * d : d; }

        public static Vector3 DirToGame(Vector3 unity) { if (Turned) unity = _unturn * unity; return new Vector3(unity.x, unity.z, unity.y); }

        /// <summary>Unity world -> game space as a matrix (the shaders' _MpUnityToRetro).</summary>
        public static Matrix4x4 UnityToGame()
        {
            if (Root != null) return Root.worldToLocalMatrix;
            return Matrix4x4.TRS(Origin, Turn * Quaternion.Euler(-90f, 0f, 0f), new Vector3(Scale, -Scale, Scale)).inverse;
        }

        /// <summary>
        /// A game transform (3x4 rows: the editor's position + Z*Y*X euler degrees, ScriptLoader ConvertEditorEulerToTransform4f)
        /// as a local transform under the root: localPosition = game position, localRotation = the game rotation (the root does the
        /// axis swap and scale, so it is applied in game space directly).
        /// </summary>
        public static void Place(Transform t, Vector3 gamePos, Vector3 gameEulerDeg, Vector3 scale)
        {
            t.SetParent(Root, false);
            t.localPosition = gamePos;
            t.localRotation = GameRotation(gameEulerDeg);
            t.localScale = scale;
        }

        /// <summary>The game's editor rotation (Rz * Ry * Rx of the euler degrees) as a quaternion in game space.</summary>
        public static Quaternion GameRotation(Vector3 eulerDeg)
        {
            return Quaternion.AngleAxis(eulerDeg.z, Vector3.forward) * Quaternion.AngleAxis(eulerDeg.y, Vector3.up) * Quaternion.AngleAxis(eulerDeg.x, Vector3.right);
        }
    }
}
