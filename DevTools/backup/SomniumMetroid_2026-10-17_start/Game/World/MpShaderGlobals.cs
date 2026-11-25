using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// The TEV shader's globals, set once a frame: time (seconds mod 900, CGraphics::GetSecondsMod900), the world light fade of the
    /// area the player is in (TEV register 1 = AreaAttributes world lighting level, CCubeRenderer::SetWorldLightFadeLevel), the camera
    /// in game space (UV animation mode 7), the Unity->game matrix, the dynamic lights (shots light the walls: up to 4, as
    /// CCubeRenderer::ActivateLightsForModel loads), the visor, and whether the frame buffer is linear.
    /// </summary>
    public static class MpShaderGlobals
    {
        public struct Light
        {
            public Vector3 GamePos;
            public Color Color;
            public float K0, K1, K2, Radius;
        }

        private static readonly int IdC1 = Shader.PropertyToID("_MpC1"), IdTime = Shader.PropertyToID("_MpTime"), IdCam = Shader.PropertyToID("_MpRetroCam"),
            IdU2R = Shader.PropertyToID("_MpUnityToRetro"), IdScale = Shader.PropertyToID("_MpWorldScale"), IdLinear = Shader.PropertyToID("_MpLinearOut"),
            IdVisor = Shader.PropertyToID("_MpVisor"), IdLightCount = Shader.PropertyToID("_MpLightCount"), IdLightPos = Shader.PropertyToID("_MpLightPos"),
            IdLightColor = Shader.PropertyToID("_MpLightColor"), IdLightAtten = Shader.PropertyToID("_MpLightAtten"),
            IdFogColor = Shader.PropertyToID("_MpFogColor"), IdFogRange = Shader.PropertyToID("_MpFogRange");

        private static readonly Vector4[] Pos = new Vector4[4], Col = new Vector4[4], Att = new Vector4[4];
        private static readonly Light[] Lights = new Light[16];
        private static int _lightCount;

        public static float LightFade = 1f;
        public static int Visor;
        public static Color FogColor;
        public static bool FogOn;
        public static float FogStart = 30f, FogEnd = 120f;

        private static readonly int IdThermal = Shader.PropertyToID("_MpThermalPalette"), IdXRay = Shader.PropertyToID("_MpXRayPalette"), IdHasPal = Shader.PropertyToID("_MpHasPalettes");

        /// <summary>The visor palettes (the TLUTs of TXTR_ThermoPalette / TXTR_XRayPalette, RGBA in index order) as 1-pixel-high ramps.</summary>
        public static void SetPalettes(byte[] thermal, int thermalCount, byte[] xray, int xrayCount)
        {
            Texture2D t = Ramp("Mp thermal palette", thermal, thermalCount), x = Ramp("Mp x-ray palette", xray, xrayCount);
            if (t != null) Shader.SetGlobalTexture(IdThermal, t);
            if (x != null) Shader.SetGlobalTexture(IdXRay, x);
            Shader.SetGlobalFloat(IdHasPal, t != null && x != null ? 1f : 0f);
            MpLog.Info("Visors: thermal palette " + (t != null ? t.width : 0) + " colours, x-ray palette " + (x != null ? x.width : 0) + " colours" + (t != null && x != null ? "" : " (missing: the thermal / x-ray views stay uncoloured)"));
        }

        private static Texture2D Ramp(string name, byte[] rgba, int count)
        {
            if (rgba == null || count <= 0) return null;
            // the used part only: the thermal TLUT is a 16-colour heat ramp (an I4 index) in a 256-entry table, the rest black
            while (count > 2 && rgba[(count - 1) * 4] == 0 && rgba[(count - 1) * 4 + 1] == 0 && rgba[(count - 1) * 4 + 2] == 0) count--;
            if (count * 4 != rgba.Length) { var cut = new byte[count * 4]; System.Array.Copy(rgba, cut, cut.Length); rgba = cut; }
            var tex = new Texture2D(count, 1, TextureFormat.RGBA32, false, true) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.LoadRawTextureData(rgba);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>Gameplay adds the frame's dynamic lights (beam shots, muzzle flashes, explosions) before <see cref="Apply"/>.</summary>
        public static void AddLight(Light l)
        {
            if (_lightCount < Lights.Length) Lights[_lightCount++] = l;
        }

        public static void Apply(Camera cam)
        {
            Shader.SetGlobalVector(IdC1, new Vector4(LightFade, LightFade, LightFade, 1f));
            Shader.SetGlobalFloat(IdTime, Time.time % 900f);
            Vector3 camGame = cam != null ? MpSpace.ToGame(cam.transform.position) : Vector3.zero;
            Shader.SetGlobalVector(IdCam, camGame);
            Shader.SetGlobalMatrix(IdU2R, MpSpace.UnityToGame());
            Shader.SetGlobalFloat(IdScale, MpSpace.Scale);
            Shader.SetGlobalFloat(IdLinear, QualitySettings.activeColorSpace == ColorSpace.Linear ? 1f : 0f);
            Shader.SetGlobalFloat(IdVisor, Visor);
            Shader.SetGlobalVector(IdFogColor, new Vector4(FogColor.r, FogColor.g, FogColor.b, FogOn ? 1f : 0f));
            Shader.SetGlobalVector(IdFogRange, new Vector4(FogStart, FogEnd, 0f, 0f));

            // the 4 lights nearest the camera (the game picks per model; a frame-wide pick keeps it cheap)
            int n = 0;
            for (int k = 0; k < 4 && k < _lightCount; k++)
            {
                int best = -1;
                float bestD = float.MaxValue;
                for (int i = 0; i < _lightCount; i++)
                {
                    if (Lights[i].Radius < 0) continue;
                    float d = (Lights[i].GamePos - camGame).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = i; }
                }

                if (best < 0) break;
                Light l = Lights[best];
                Pos[n] = new Vector4(l.GamePos.x, l.GamePos.y, l.GamePos.z, 1f);
                Col[n] = new Vector4(l.Color.r, l.Color.g, l.Color.b, 1f);
                Att[n] = new Vector4(l.K0, l.K1, l.K2, l.Radius);
                Lights[best].Radius = -1f;
                n++;
            }

            for (int i = n; i < 4; i++) { Pos[i] = Vector4.zero; Col[i] = Vector4.zero; Att[i] = new Vector4(1, 0, 0, 0); }
            Shader.SetGlobalFloat(IdLightCount, n);
            Shader.SetGlobalVectorArray(IdLightPos, Pos);
            Shader.SetGlobalVectorArray(IdLightColor, Col);
            Shader.SetGlobalVectorArray(IdLightAtten, Att);
            _lightCount = 0;
        }
    }
}
