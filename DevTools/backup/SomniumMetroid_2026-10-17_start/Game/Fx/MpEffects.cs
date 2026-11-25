using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using SomniumSpace.Worlds.SomniumMetroid.Data.Particles;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// The game's own effects (PART / SWHC from the disc, run by the ports in Data/Particles) placed and drawn in Unity: each live
    /// effect is a system tree updated at the game's 60 Hz, drawn every frame as one mesh per system the way CElementGen::Render /
    /// CParticleSwoosh::Render build their geometry - camera-facing quads (rotation, animated texture sheets, motion blur copies),
    /// velocity-oriented quads (ORNT), GX lines (as camera-facing strips of the game's pixel width), model particles (PMDL, per
    /// particle model matrices) and 3-sided / 2-sided swooshes - with the game's blends (additive for AAPH / AALP, else alpha with
    /// depth writes from ZBUF). Systems that carry a light (LTYP) light the world through MpShaderGlobals.
    /// </summary>
    public static class MpEffects
    {
        public static MpPartLibrary Lib;
        private static List<KeyValuePair<uint, MpTexture>> _pendingTextures;
        private static readonly List<MpModel> PendingModels = new List<MpModel>();

        public sealed class Effect
        {
            public IPartGen Gen;
            public bool Dead;
            public bool Persistent;          // the owner removes it (a projectile's systems); else gone when the system is done
            public C4 Modulation = C4.White;
            public bool Hidden;              // kept but not drawn (the gun's muzzle systems between shots)
            public bool OwnerUpdates;        // the owner steps it (the gun updates its muzzle systems only while they are shown)

            /// <summary>The owner's step for an <see cref="OwnerUpdates"/> system.</summary>
            public void Update(float dt)
            {
                if (Gen == null || Dead) return;
                try { Gen.Update(dt); }
                catch (Exception ex) { MpLog.Warn("Effects: a system failed and was dropped: " + ex.Message); Dead = true; }
            }

            public void SetPlace(Vector3 gamePos, M3 orientation)
            {
                if (Gen == null) return;
                Gen.SetOrientation(orientation);
                Gen.SetTranslation(new V3(gamePos.x, gamePos.y, gamePos.z));
            }

            /// <summary>Gun-attached systems (CGunWeapon::UpdateMuzzleFx): the system rides a frame - its particles stay in that frame.</summary>
            public void SetGlobal(Vector3 gamePos, M3 orientation, float scale)
            {
                if (Gen == null) return;
                Gen.SetGlobalTranslation(new V3(gamePos.x, gamePos.y, gamePos.z));
                Gen.SetGlobalOrientation(orientation);
                Gen.SetGlobalScale(new V3(scale, scale, scale));
            }

            /// <summary>Stops emitting and lets the live particles finish (CElementGen::EndLifetime / SWHC emission off).</summary>
            public void Finish()
            {
                Persistent = false;
                if (Gen != null) Gen.EndLifetime();
            }

            public void Kill()
            {
                Dead = true;
                if (Gen != null) Gen.DestroyParticles();
            }
        }

        private static readonly List<Effect> Live = new List<Effect>();

        /// <summary>Shots in flight: their systems are updated by the shot (MpProjectiles), drawn and lit here.</summary>
        public static readonly List<MpProjectileWeapon> Projectiles = new List<MpProjectileWeapon>();
        public static int LiveCount { get { return Live.Count; } }

        // ------------------------------------------------------------------ loading

        /// <summary>Loader thread: the descriptions of these effects and everything they spawn, their textures decoded, their models read.</summary>
        public static void Prepare(MpAssets assets, IEnumerable<uint> parts, IEnumerable<uint> swooshes)
        {
            if (Lib == null) Lib = new MpPartLibrary(assets);
            foreach (uint p in parts) Lib.Prefetch(p);
            foreach (uint s in swooshes) Lib.PrefetchSwoosh(s);
            var list = new List<KeyValuePair<uint, MpTexture>>();
            foreach (uint t in Lib.Textures)
            {
                if (MpTextures.Has(t)) continue;
                try { list.Add(new KeyValuePair<uint, MpTexture>(t, MpTexture.Read(assets.Read(t)))); }
                catch (Exception e) { MpLog.Warn("Effects: TXTR " + t.ToString("X8") + " not decoded: " + e.Message); }
            }

            var models = new List<MpModel>();
            var modelTex = new HashSet<uint>();
            foreach (uint m in Lib.Models)
            {
                MpModel model = MpModels.Load(assets, m);
                if (model == null) continue;
                models.Add(model);
                MpMaterials.TexturesOf(model.Set, modelTex);
            }

            foreach (uint t in modelTex)
            {
                if (MpTextures.Has(t)) continue;
                try { list.Add(new KeyValuePair<uint, MpTexture>(t, MpTexture.Read(assets.Read(t)))); }
                catch (Exception) { }
            }

            lock (PendingModels)
            {
                _pendingTextures = list;
                PendingModels.AddRange(models);
            }
        }

        /// <summary>Main thread: what <see cref="Prepare"/> decoded goes to Unity.</summary>
        public static void Upload()
        {
            List<KeyValuePair<uint, MpTexture>> tex;
            MpModel[] models;
            lock (PendingModels)
            {
                tex = _pendingTextures;
                _pendingTextures = null;
                models = PendingModels.ToArray();
                PendingModels.Clear();
            }

            if (tex != null) foreach (var kv in tex) MpTextures.Upload(kv.Key, kv.Value);
            foreach (MpModel m in models) m.Build(true);
        }

        // ------------------------------------------------------------------ spawning and updating

        /// <summary>Starts a PART at a game-space place (null when the id is not a readable PART).</summary>
        public static Effect Spawn(uint part, Vector3 gamePos, M3 orientation, bool persistent = false)
        {
            if (Lib == null || !MpAssets.Valid(part)) return null;
            MpGenDesc d = Lib.Part(part);
            if (d == null) return null;
            var e = new Effect { Gen = new MpElementGen(d, Lib), Persistent = persistent };
            e.SetPlace(gamePos, orientation);
            Live.Add(e);
            return e;
        }

        public static Effect SpawnSwoosh(uint swoosh, Vector3 gamePos, M3 orientation, bool persistent = true)
        {
            if (Lib == null || !MpAssets.Valid(swoosh)) return null;
            MpSwooshDesc d = Lib.Swoosh(swoosh);
            if (d == null) return null;
            var e = new Effect { Gen = new MpSwooshGen(d, 0), Persistent = persistent };
            e.SetPlace(gamePos, orientation);
            Live.Add(e);
            return e;
        }

        /// <summary>A game orientation (CTransform4f basis: right, forward, up) from a Unity rotation.</summary>
        public static M3 GameOrientation(Quaternion unityRot)
        {
            Vector3 r = MpSpace.DirToGame(unityRot * Vector3.right), f = MpSpace.DirToGame(unityRot * Vector3.forward), u = MpSpace.DirToGame(unityRot * Vector3.up);
            return new M3 { C0 = new V3(r.x, r.y, r.z), C1 = new V3(f.x, f.y, f.z), C2 = new V3(u.x, u.y, u.z) };
        }

        /// <summary>Every frame (before the shader globals): the 60 Hz updates, finished systems dropped, the systems' lights out.</summary>
        public static void Tick(float dt)
        {
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                Effect e = Live[i];
                if (e.Dead) { Live.RemoveAt(i); continue; }
                if (e.OwnerUpdates && e.Persistent) { if (!e.Hidden) AddLights(e.Gen); continue; }
                try { e.Gen.Update(dt); }
                catch (Exception ex)
                {
                    MpLog.Warn("Effects: a system failed and was dropped: " + ex.Message);
                    e.Dead = true;
                    Live.RemoveAt(i);
                    continue;
                }

                if (!e.Persistent && e.Gen.IsSystemDeletable) { e.Dead = true; Live.RemoveAt(i); continue; }
                if (!e.Hidden) AddLights(e.Gen);
            }

            foreach (MpProjectileWeapon w in Projectiles)
            {
                if (w.APSM != null) AddLights(w.APSM);
                if (w.APS2 != null) AddLights(w.APS2);
            }
        }

        private static readonly List<IPartGen> Systems = new List<IPartGen>();

        private static void AddLights(IPartGen root)
        {
            var g = root as MpElementGen;
            if (g == null) return;
            V3 p;
            C4 c;
            float k0, k1, k2;
            if (g.LightType != 0 && g.TryGetLight(out p, out c, out k0, out k1, out k2) && (c.R + c.G + c.B) > 0.01f)
            {
                MpShaderGlobals.AddLight(new MpShaderGlobals.Light
                {
                    GamePos = new Vector3(p.X, p.Y, p.Z), Color = new Color(c.R, c.G, c.B, 1f),
                    K0 = k0, K1 = k1, K2 = k2, Radius = Radius(k0, k1, k2),
                });
            }

            foreach (IPartGen ch in g.Children) AddLights(ch);
        }

        /// <summary>Where the attenuation drops the light below 1/32 (the game's light radius test, CLight::CalculateRadius).</summary>
        private static float Radius(float k0, float k1, float k2)
        {
            const float limit = 32f;
            if (k2 > 0f) return Mathf.Sqrt(limit / k2);
            if (k1 > 0f) return limit / k1;
            return 3000f;
        }

        public static void Clear()
        {
            foreach (Effect e in Live) e.Dead = true;
            Projectiles.Clear();
            Live.Clear();
        }

        // ------------------------------------------------------------------ drawing

        private sealed class Builder
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Color32> C = new List<Color32>();
            public readonly List<Vector2> UV = new List<Vector2>();
            public readonly List<int> T = new List<int>();
            public void Clear() { V.Clear(); C.Clear(); UV.Clear(); T.Clear(); }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 ca, Color32 cb, Color32 cc, Color32 cd, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                int n = V.Count;
                V.Add(a); V.Add(b); V.Add(c); V.Add(d);
                C.Add(ca); C.Add(cb); C.Add(cc); C.Add(cd);
                UV.Add(ua); UV.Add(ub); UV.Add(uc); UV.Add(ud);
                T.Add(n); T.Add(n + 1); T.Add(n + 2); T.Add(n); T.Add(n + 2); T.Add(n + 3);
            }
        }

        private static readonly Builder B = new Builder();
        private static readonly List<Mesh> MeshPool = new List<Mesh>();
        private static int _meshUsed;
        private static readonly Dictionary<long, Material> Materials = new Dictionary<long, Material>();
        private static readonly Dictionary<Material, Material[]> ModelVariants = new Dictionary<Material, Material[]>();
        // (Unity objects are made on first use: the loader thread starts this class)
        private static MaterialPropertyBlock _block;
        private static MaterialPropertyBlock Block { get { return _block ?? (_block = new MaterialPropertyBlock()); } }
        private static int _idMod = -1;
        private static int IdMod { get { if (_idMod < 0) _idMod = Shader.PropertyToID("_ModColor"); return _idMod; } }
        public const int Queue = 3000;

        private static Color32 C32(C4 c, C4 mod)
        {
            return new Color32((byte)(Mathf.Clamp01(c.R * mod.R) * 255f), (byte)(Mathf.Clamp01(c.G * mod.G) * 255f), (byte)(Mathf.Clamp01(c.B * mod.B) * 255f), (byte)(Mathf.Clamp01(c.A * mod.A) * 255f));
        }

        private static Vector3 U(V3 game) { return MpSpace.ToUnity(game.X, game.Y, game.Z); }

        /// <summary>The particle material for a texture and the game's blend: 0 alpha (depth write), 1 alpha (no depth write), 2 additive; 3 = no depth test.</summary>
        private static Material Mat(uint texture, int blend, bool zTest)
        {
            long key = ((long)texture << 8) | ((long)blend << 1) | (zTest ? 1L : 0L);
            Material m;
            if (Materials.TryGetValue(key, out m) && m != null) return m;
            Material carrier = MpGame.FxMaterial;
            if (carrier == null) return null;
            Texture tex = MpAssets.Valid(texture) ? (Texture)MpTextures.Get(texture) : null;
            m = new Material(carrier) { name = "Mp effect " + texture.ToString("X8") + " " + blend };
            m.SetTexture("_MainTex", tex != null ? tex : Texture2D.whiteTexture);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(blend == 2 ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZWrite", blend == 0 ? 1f : 0f);
            m.SetFloat("_ZTest", (float)(zTest ? UnityEngine.Rendering.CompareFunction.LessEqual : UnityEngine.Rendering.CompareFunction.Always));
            m.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            m.SetFloat("_AlphaClip", 0.002f);       // GX alpha compare: greater than 0
            m.SetFloat("_Billboard", 0f);
            m.renderQueue = Queue + (blend == 2 ? 10 : 0);
            Materials[key] = m;
            return m;
        }

        private static Mesh NextMesh()
        {
            if (_meshUsed >= MeshPool.Count) { var m = new Mesh { name = "Mp effect" }; m.MarkDynamic(); MeshPool.Add(m); }
            return MeshPool[_meshUsed++];
        }

        private static void Flush(Material mat)
        {
            if (B.V.Count == 0 || mat == null) { B.Clear(); return; }
            Mesh mesh = NextMesh();
            mesh.Clear();
            if (B.V.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(B.V);
            mesh.SetColors(B.C);
            mesh.SetUVs(0, B.UV);
            mesh.SetTriangles(B.T, 0, false);
            mesh.RecalculateBounds();
            Graphics.DrawMesh(mesh, Matrix4x4.identity, mat, 0, null, 0, null, false, false, false);
            B.Clear();
        }

        /// <summary>Late in the frame (after the camera moved): every live system drawn for this camera.</summary>
        public static void Render(Camera cam)
        {
            _meshUsed = 0;
            if (cam == null) return;
            foreach (Effect e in Live)
            {
                if (e.Dead || e.Hidden) continue;
                Systems.Clear();
                var g = e.Gen as MpElementGen;
                if (g != null) g.Collect(Systems);
                else Systems.Add(e.Gen);
                foreach (IPartGen s in Systems)
                {
                    try
                    {
                        var pg = s as MpElementGen;
                        if (pg != null) RenderPart(pg, cam);
                        var sw = s as MpSwooshGen;
                        if (sw != null) RenderSwoosh(sw, cam);
                    }
                    catch (Exception ex) { MpLog.Warn("Effects: a system did not draw: " + ex.Message); B.Clear(); }
                }
            }

            foreach (MpProjectileWeapon w in Projectiles)
            {
                Systems.Clear();
                if (w.APSM != null) w.APSM.Collect(Systems);
                if (w.APS2 != null) w.APS2.Collect(Systems);
                if (w.Swoosh1 != null) Systems.Add(w.Swoosh1);
                if (w.Swoosh2 != null) Systems.Add(w.Swoosh2);
                if (w.Swoosh3 != null) Systems.Add(w.Swoosh3);
                foreach (IPartGen s in Systems)
                {
                    try
                    {
                        var pg = s as MpElementGen;
                        if (pg != null) RenderPart(pg, cam);
                        var sw = s as MpSwooshGen;
                        if (sw != null) RenderSwoosh(sw, cam);
                    }
                    catch (Exception ex) { MpLog.Warn("Effects: a shot's system did not draw: " + ex.Message); B.Clear(); }
                }
            }
        }

        private static void RenderPart(MpElementGen g, Camera cam)
        {
            if (g.Particles.Count == 0) return;
            MpGenDesc d = g.Desc;
            C4 mod = g.ModulationColor;
            if (MpAssets.Valid(d.PMDL) || d.PMUS) RenderModels(g, cam, mod);
            if (g.LINE) { RenderLines(g, cam, mod); return; }

            // a constant size of 0 draws nothing (CElementGen::RenderParticles)
            if (d.SIZE != null && d.SIZE.IsConstant)
            {
                float sz = 1f;
                d.SIZE.Get(0, ref sz);
                if (sz == 0f) { sz = 1f; d.SIZE.Get(1, ref sz); if (sz == 0f) return; }
            }

            uint texId = d.TEXR != null ? d.TEXR.Texture : 0;
            Texture2D tex = MpAssets.Valid(texId) ? MpTextures.Get(texId) : null;
            if (d.TEXR != null && tex != null) d.TEXR.SetTextureSize(tex.width, tex.height);
            bool constUV = d.TEXR == null || d.TEXR.HasConstantUV;
            var uv = new UVSet { XMin = 0, YMin = 0, XMax = 1, YMax = 1 };
            int blend = g.AAPH ? 2 : (g.ZBUF ? 0 : 1);
            Material mat = Mat(texId, blend, g.ZTest);
            float scale = g.ScaleFactor * MpSpace.Scale;
            Vector3 right = cam.transform.right, up = cam.transform.up;
            Vector3 camFwdGame = MpSpace.DirToGame(cam.transform.forward);
            M3 inv = g.GlobalOrientation.Transposed;
            V3 fwdSys = inv.Mul(new V3(camFwdGame.x, camFwdGame.y, camFwdGame.z));
            Vector3 camGame = MpSpace.ToGame(cam.transform.position);
            float tds = g.TimeDeltaScale;
            int mbsp = Math.Max(1, g.MBSP);

            foreach (MpParticle p in g.Particles)
            {
                if (!constUV)
                {
                    int pf = g.CurFrame - p.StartFrame - 1;
                    MpPartGlobals.SetParticleLifetime(p.EndFrame - p.StartFrame);
                    MpPartGlobals.UpdateParticleLifetimeTweenValues(pf);
                    d.TEXR.GetUV(pf, ref uv);
                }

                Vector2 u00 = new Vector2(uv.XMin, uv.YMin), u10 = new Vector2(uv.XMax, uv.YMin), u01 = new Vector2(uv.XMin, uv.YMax), u11 = new Vector2(uv.XMax, uv.YMax);
                V3 delta = p.Pos - p.PrevPos;
                V3 pos = delta * tds + p.PrevPos;

                if (g.ORNT)
                {
                    // velocity-oriented quads (the ORNT branch), built in the system's space
                    float rota = d.ROTA == null ? 1f : p.LineWidthOrRota;
                    V3 dir;
                    if (p.Vel.MagSquared > 1.1920929e-7f) dir = p.Vel.Normalized;
                    else if (delta.MagSquared > 1.1920929e-7f) dir = delta.Normalized;
                    else dir = new V3(0, 0, 1);
                    V3 fore = dir * p.LineLengthOrSize;
                    V3 rgt;
                    if (d.RSOP)
                    {
                        rgt = V3.Cross(dir, fwdSys);
                        if (rgt.MagSquared > 1.1920929e-7f) rgt = rgt.Normalized * (p.LineLengthOrSize * rota);
                        else
                        {
                            V3 camLocal = inv.Mul(new V3(camGame.x, camGame.y, camGame.z) - g.GlobalTranslation);
                            rgt = V3.Cross(dir, (camLocal - p.Pos).Normalized);
                            if (rgt.MagSquared > 1.1920929e-7f) rgt = rgt.Normalized * (p.LineLengthOrSize * rota);
                        }
                    }
                    else rgt = V3.Cross(fore, fwdSys) * rota;

                    V3 a = pos + rgt * 0.5f + fore * 0.5f, b2 = a - fore, c2 = b2 - rgt, d2 = c2 + fore;
                    Color32 col = C32(p.Color, mod);
                    B.Quad(U(g.WorldOf(a)), U(g.WorldOf(b2)), U(g.WorldOf(c2)), U(g.WorldOf(d2)), col, col, col, col, u11, u01, u00, u10);
                    continue;
                }

                float half = 0.5f * p.LineLengthOrSize * scale;
                float theta = p.LineWidthOrRota * Mathf.Deg2Rad;
                float s = Mathf.Sin(theta) * half, c = Mathf.Cos(theta) * half;
                Vector3 o0 = right * (s + c) + up * (c - s), o1 = right * (s - c) + up * (s + c), o2 = -o0, o3 = -o1;
                if (!g.MBLR)
                {
                    Vector3 ctr = U(g.WorldOf(pos));
                    Color32 col = C32(p.Color, mod);
                    B.Quad(ctr + o0, ctr + o1, ctr + o2, ctr + o3, col, col, col, col, u11, u01, u00, u10);
                }
                else
                {
                    // motion blur: MBSP copies along the last step, each with 1/MBSP of the alpha
                    float k = 1f / mbsp;
                    C4 pc = p.Color;
                    pc.A *= k;
                    Color32 col = C32(pc, mod);
                    V3 step = delta * k;
                    V3 vec = pos;
                    for (int j = 0; j < mbsp; j++)
                    {
                        vec = vec + step;
                        Vector3 ctr = U(g.WorldOf(vec));
                        B.Quad(ctr + o0, ctr + o1, ctr + o2, ctr + o3, col, col, col, col, u11, u01, u00, u10);
                    }
                }
            }

            Flush(mat);
        }

        private static void RenderLines(MpElementGen g, Camera cam, C4 mod)
        {
            MpGenDesc d = g.Desc;
            uint texId = d.TEXR != null ? d.TEXR.Texture : 0;
            Texture2D tex = MpAssets.Valid(texId) ? MpTextures.Get(texId) : null;
            if (d.TEXR != null && tex != null) d.TEXR.SetTextureSize(tex.width, tex.height);
            var uv = new UVSet { XMin = 0, YMin = 0, XMax = 1, YMax = 1 };
            if (d.TEXR != null && g.Particles.Count > 0) d.TEXR.GetUV(g.CurFrame - g.Particles[0].StartFrame, ref uv);
            bool constUV = d.TEXR == null || d.TEXR.HasConstantUV;
            Material mat = Mat(texId, g.AAPH ? 2 : 0, true);
            bool widthConst = d.WIDT != null && d.WIDT.IsConstant;
            float constWidth = 1f;
            if (widthConst) d.WIDT.Get(0, ref constWidth);
            float pixelToWorld = 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / 480f;
            foreach (MpParticle p in g.Particles)
            {
                if (!constUV) d.TEXR.GetUV(g.CurFrame - p.StartFrame, ref uv);
                V3 dv = p.Pos - p.PrevPos;
                if (d.FXLL) { float m = dv.Magnitude; if (m > 0f) dv = dv * (1f / m); }
                Vector3 a = U(g.WorldOf(p.Pos)), b = U(g.WorldOf(p.Pos + dv * p.LineLengthOrSize));
                float w = Mathf.Clamp(widthConst ? constWidth : p.LineWidthOrRota, 0f, 42.5f);
                Vector3 mid = (a + b) * 0.5f;
                float dist = Mathf.Max(0.05f, (mid - cam.transform.position).magnitude);
                Vector3 along = b - a;
                Vector3 side = Vector3.Cross(along, cam.transform.position - mid);
                if (side.sqrMagnitude < 1e-12f) continue;
                side = side.normalized * (0.5f * w * pixelToWorld * dist);
                Color32 col = C32(p.Color, mod);
                B.Quad(a + side, b + side, b - side, a - side, col, col, col, col, new Vector2(uv.XMin, uv.YMin), new Vector2(uv.XMax, uv.YMax), new Vector2(uv.XMax, uv.YMax), new Vector2(uv.XMin, uv.YMin));
            }

            Flush(mat);
        }

        private static Matrix4x4 GameMatrix(M3 r, V3 t, V3 s)
        {
            var m = new Matrix4x4();
            m.SetColumn(0, new Vector4(r.C0.X * s.X, r.C0.Y * s.X, r.C0.Z * s.X, 0));
            m.SetColumn(1, new Vector4(r.C1.X * s.Y, r.C1.Y * s.Y, r.C1.Z * s.Y, 0));
            m.SetColumn(2, new Vector4(r.C2.X * s.Z, r.C2.Y * s.Z, r.C2.Z * s.Z, 0));
            m.SetColumn(3, new Vector4(t.X, t.Y, t.Z, 1));
            return m;
        }

        private static M3 Mul(M3 a, M3 b) { return new M3 { C0 = a.Mul(b.C0), C1 = a.Mul(b.C1), C2 = a.Mul(b.C2) }; }

        private static M3 Euler(V3 deg)
        {
            // CTransform4f::RotateZ(z) then RotateLocalY(y), RotateLocalX(x)
            float x = deg.X * Mathf.Deg2Rad, y = deg.Y * Mathf.Deg2Rad, z = deg.Z * Mathf.Deg2Rad;
            M3 rz = new M3 { C0 = new V3(Mathf.Cos(z), Mathf.Sin(z), 0), C1 = new V3(-Mathf.Sin(z), Mathf.Cos(z), 0), C2 = new V3(0, 0, 1) };
            M3 ry = new M3 { C0 = new V3(Mathf.Cos(y), 0, -Mathf.Sin(y)), C1 = new V3(0, 1, 0), C2 = new V3(Mathf.Sin(y), 0, Mathf.Cos(y)) };
            M3 rx = new M3 { C0 = new V3(1, 0, 0), C1 = new V3(0, Mathf.Cos(x), Mathf.Sin(x)), C2 = new V3(0, -Mathf.Sin(x), Mathf.Cos(x)) };
            return Mul(Mul(rz, ry), rx);
        }

        private static void RenderModels(MpElementGen g, Camera cam, C4 mod)
        {
            MpGenDesc d = g.Desc;
            MpModel model = MpAssets.Valid(d.PMDL) ? MpModels.Get(d.PMDL) : null;
            if (!d.PMUS && (model == null || !model.Built || model.Meshes == null)) return;
            Transform root = MpSpace.Root;
            if (root == null) return;
            Matrix4x4 toUnity = root.localToWorldMatrix;
            M3 orient = d.PMOO ? M3.Identity : g.Orientation;
            orient = Mul(orient, g.GlobalOrientation);
            V3 trans = g.GlobalTranslation;
            C4 col = mod;
            foreach (MpParticle p in g.Particles)
            {
                if (p.EndFrame == -1) continue;
                int pf = g.CurFrame - p.StartFrame - 1;
                MpPartGlobals.SetParticleLifetime(p.EndFrame - p.StartFrame);
                MpPartGlobals.UpdateParticleLifetimeTweenValues(pf);
                MpPartGlobals.CurrentParticle = p;
                V3 pmop = V3.Zero;
                if (d.PMOP != null) d.PMOP.Get(pf, ref pmop);
                V3 at = trans + (p.Pos - p.PrevPos) * g.TimeDeltaScale + p.PrevPos + orient.Mul(pmop);
                M3 rot = orient;
                if (d.PMRT != null) { V3 r = V3.Zero; d.PMRT.Get(pf, ref r); rot = Mul(orient, Euler(r)); }
                V3 sc = new V3(1, 1, 1);
                if (d.PMSC != null) d.PMSC.Get(pf, ref sc);
                if (d.PMCL != null) { d.PMCL.Get(pf, ref col); col = new C4(col.R * mod.R, col.G * mod.G, col.B * mod.B, col.A * mod.A); }
                float gs = g.ScaleFactor;
                Matrix4x4 m = toUnity * GameMatrix(rot, at, new V3(sc.X * gs, sc.Y * gs, sc.Z * gs));
                if (d.PMUS)
                {
                    // a textured quad in the model's XZ plane instead of a model
                    uint texId = d.TEXR != null ? d.TEXR.Texture : 0;
                    Material qm = Mat(texId, d.PMAB ? 2 : 0, true);
                    Color32 c32 = C32(col, C4.White);
                    B.Quad(m.MultiplyPoint3x4(new Vector3(0.5f, 0, 0.5f)), m.MultiplyPoint3x4(new Vector3(-0.5f, 0, 0.5f)), m.MultiplyPoint3x4(new Vector3(-0.5f, 0, -0.5f)), m.MultiplyPoint3x4(new Vector3(0.5f, 0, -0.5f)),
                        c32, c32, c32, c32, new Vector2(1, 1), new Vector2(0, 1), new Vector2(0, 0), new Vector2(1, 0));
                    Flush(qm);
                    continue;
                }

                int mode = d.PMAB ? 1 : (col.A >= 1f ? 0 : 2);
                for (int i = 0; i < model.Meshes.Length; i++)
                {
                    if (model.Meshes[i] == null) continue;
                    Material[] mats = model.Materials[i];
                    for (int sub = 0; sub < mats.Length && sub < model.Meshes[i].subMeshCount; sub++)
                    {
                        Material src = mats[sub];
                        if (src == null) continue;
                        Material use = Variant(src, mode);
                        Block.Clear();
                        Block.SetColor(IdMod, mode == 1 ? new Color(col.R, col.G, col.B, col.A) : mode == 2 ? new Color(1, 1, 1, col.A) : Color.white);
                        Graphics.DrawMesh(model.Meshes[i], m, use, 0, null, sub, Block, false, false, false);
                    }
                }
            }
        }

        /// <summary>A model material as the particle draws it: 0 normal, 1 additive (CModelFlags::Additive), 2 alpha blended.</summary>
        private static Material Variant(Material src, int mode)
        {
            if (mode == 0) return src;
            Material[] v;
            if (!ModelVariants.TryGetValue(src, out v)) { v = new Material[3]; ModelVariants[src] = v; }
            if (v[mode] != null) return v[mode];
            var m = new Material(src) { name = src.name + (mode == 1 ? " (additive)" : " (alpha)") };
            m.SetColor("_Ambient", Color.white);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(mode == 1 ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZWrite", 0f);
            m.SetShaderPassEnabled("DepthOnly", false);
            m.SetShaderPassEnabled("DepthNormals", false);
            m.renderQueue = Queue + 5;
            v[mode] = m;
            return m;
        }

        // ------------------------------------------------------------------ swooshes

        private static void RenderSwoosh(MpSwooshGen s, Camera cam)
        {
            if (s.LENG < 2 || s.ParticleCount <= 1 || s.Points.Length < 2) return;
            MpSwooshDesc d = s.Desc;
            uint texId = d.TEXR != null ? d.TEXR.Texture : 0;
            Texture2D tex = MpAssets.Valid(texId) ? MpTextures.Get(texId) : null;
            if (d.TEXR != null && tex != null) d.TEXR.SetTextureSize(tex.width, tex.height);
            var uvs = new UVSet { XMin = 0, YMin = 0, XMax = 1, YMax = 1 };
            if (d.TEXR != null) d.TEXR.GetUV(s.CurFrame, ref uvs);
            int tspn = 0;
            if (d.TSPN != null) d.TSPN.Get(s.CurFrame, ref tspn);
            if (tspn <= 0) tspn = s.Points.Length - 1;
            float uvSpan = tspn > 0 ? 1f / tspn : 1f;
            Material mat = Mat(texId, s.AALP ? 2 : (d.ZBUF ? 0 : 1), true);
            if (s.SIDE == 2) Ribbon(s, uvs, uvSpan);
            else Tube3(s, uvs, uvSpan);
            Flush(mat);
        }

        private static float Angle(MpSwooshGen.Point p)
        {
            float a = Mathf.PI * (p.InitialRot + p.Rotm) / 180f;
            if (Mathf.Abs(a) > Mathf.PI) a = Mathf.Repeat(a + Mathf.PI, 2f * Mathf.PI) - Mathf.PI;
            return a;
        }

        /// <summary>Render3SidedSolidNoSplineNoGaps: a triangular tube from the newest point back (the first quad fades in from black).</summary>
        private static void Tube3(MpSwooshGen s, UVSet uvs, float uvSpan)
        {
            var p0 = new Vector3[2];
            var p1 = new Vector3[2];
            var p2 = new Vector3[2];
            int cur = 0, prev = 1, idx = s.CurParticle;
            Color32 color = new Color32(0, 0, 0, 0), prevColor;
            bool lastActive = false;
            float uv = -uvSpan;
            for (int i = 0; i < s.Points.Length; i++)
            {
                MpSwooshGen.Point sw = s.Points[idx];
                if (--idx < 0) idx = s.Points.Length - 1;
                float angle = Angle(sw), r = sw.LeftRad;
                V3 a = new V3(r * Mathf.Cos(angle), 0, r * Mathf.Sin(angle));
                angle += 2.0943952f; if (angle > Mathf.PI) angle -= 2f * Mathf.PI;
                V3 b = new V3(r * Mathf.Cos(angle), 0, r * Mathf.Sin(angle));
                angle += 2.0943952f; if (angle > Mathf.PI) angle -= 2f * Mathf.PI;
                V3 c = new V3(r * Mathf.Cos(angle), 0, r * Mathf.Sin(angle));
                V3 off = sw.Translation + sw.UseOffset;
                p0[cur] = U(s.WorldOf(sw.Orientation.Mul(a) + off));
                p1[cur] = U(s.WorldOf(sw.Orientation.Mul(b) + off));
                p2[cur] = U(s.WorldOf(sw.Orientation.Mul(c) + off));
                prevColor = color;
                if (!sw.Active) lastActive = false;
                else if (!lastActive) lastActive = true;
                else
                {
                    color = C32(sw.Color, s.ModulationColor);
                    float prevUv = uv;
                    uv += uvSpan;
                    B.Quad(p0[cur], p1[cur], p1[prev], p0[prev], color, color, prevColor, prevColor, new Vector2(uv, uvs.YMin), new Vector2(uv, uvs.YMax), new Vector2(prevUv, uvs.YMax), new Vector2(prevUv, uvs.YMin));
                    B.Quad(p1[cur], p2[cur], p2[prev], p1[prev], color, color, prevColor, prevColor, new Vector2(uv, uvs.YMin), new Vector2(uv, uvs.YMax), new Vector2(prevUv, uvs.YMax), new Vector2(prevUv, uvs.YMin));
                    B.Quad(p2[cur], p0[cur], p0[prev], p2[prev], color, color, prevColor, prevColor, new Vector2(uv, uvs.YMin), new Vector2(uv, uvs.YMax), new Vector2(prevUv, uvs.YMax), new Vector2(prevUv, uvs.YMin));
                }

                cur = (cur + 1) & 1;
                prev = (prev + 1) & 1;
            }
        }

        /// <summary>Render2SidedNoSplineNoGaps (not oriented): a ribbon from the newest point back.</summary>
        private static void Ribbon(MpSwooshGen s, UVSet uvs, float uvSpan)
        {
            int idx = s.CurParticle;
            float uv = 0f;
            bool have = false;
            Vector3 l0 = Vector3.zero, r0 = Vector3.zero;
            Color32 c0 = default(Color32);
            float u0 = 0f;
            for (int i = 0; i < s.Points.Length; i++)
            {
                MpSwooshGen.Point sw = s.Points[idx];
                if (--idx < 0) idx = s.Points.Length - 1;
                if (!sw.Active) { have = false; continue; }
                float angle = Angle(sw), cs = Mathf.Cos(angle), sn = Mathf.Sin(angle);
                V3 off = sw.Translation + sw.UseOffset;
                Vector3 l = U(s.WorldOf(sw.Orientation.Mul(new V3(sw.LeftRad * cs, 0, sw.LeftRad * sn)) + off));
                Vector3 r = U(s.WorldOf(sw.Orientation.Mul(new V3(sw.RightRad * -cs, 0, sw.RightRad * -sn)) + off));
                Color32 c = C32(sw.Color, s.ModulationColor);
                if (have) B.Quad(l0, r0, r, l, c0, c0, c, c, new Vector2(u0, uvs.YMin), new Vector2(u0, uvs.YMax), new Vector2(uv, uvs.YMax), new Vector2(uv, uvs.YMin));
                l0 = l; r0 = r; c0 = c; u0 = uv; have = true;
                uv += uvSpan;
            }
        }
    }
}
