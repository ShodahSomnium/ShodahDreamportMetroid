using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// The monitor's aiming reticle: the game's combat reticle group (decomp CCompoundTargetReticle::DrawCurrLockOnGroup) - lock
    /// confirm, target flower, the nine outer beam squares turned to the current beam's angles, the charge gauge (turned per beam,
    /// pulsing when fully charged) and its ticks filling with the charge - drawn at the centre of the view. In the GameCube game this
    /// group appears on a lock-on; lock-on is not in this Dreamport yet, so it stands on what the view's centre is on (as a lock on a
    /// 1-unit target there would) as the aiming point.
    /// Models, colours, scales and screen clamps come from the disc (CMDL_*, the Targeting tweak); the size on screen follows
    /// CalculateClampedScale (projected size clamped between the tweak's min / max on a 640-wide view). Additive, no depth test.
    /// </summary>
    public sealed class MpReticle
    {
        public sealed class Payload
        {
            public MpTweakTargeting Tweak;
            public readonly Dictionary<string, MpModel> Models = new Dictionary<string, MpModel>();
            public readonly List<KeyValuePair<uint, MpTexture>> Textures = new List<KeyValuePair<uint, MpTexture>>();
        }

        private static readonly string[] Names =
        {
            "CMDL_LockConfirm", "CMDL_TargetFlower", "CMDL_ChargeGauge", "CMDL_ChargeTickFirst",
            "CMDL_BeamSquare0", "CMDL_BeamSquare1", "CMDL_BeamSquare2", "CMDL_BeamSquare3", "CMDL_BeamSquare4",
            "CMDL_BeamSquare5", "CMDL_BeamSquare6", "CMDL_BeamSquare7", "CMDL_BeamSquare8",
        };

        /// <summary>Loader thread: the tweak, the models and their textures.</summary>
        public static Payload Prepare(MpAssets assets)
        {
            var p = new Payload();
            MpPak.Entry tw = assets.Named("Targeting");
            if (tw != null) p.Tweak = MpTweakTargeting.Read(tw.Pak.Read(tw));
            var tex = new HashSet<uint>();
            foreach (string n in Names)
            {
                MpPak.Entry e = assets.Named(n);
                if (e == null) continue;
                MpModel m = MpModels.Load(assets, e.Id);
                if (m == null) continue;
                p.Models[n] = m;
                MpMaterials.TexturesOf(m.Set, tex);
            }

            foreach (uint id in tex)
            {
                if (MpTextures.Has(id)) continue;
                try { p.Textures.Add(new KeyValuePair<uint, MpTexture>(id, MpTexture.Read(assets.Read(id)))); }
                catch (Exception) { }
            }

            return p;
        }

        private sealed class Part
        {
            public Transform T;
            public Renderer[] Renderers;
        }

        private readonly Payload _p;
        private readonly Transform _anchor, _frame;
        private Part _confirm, _flower, _gauge;
        private readonly Part[] _squares = new Part[9];
        private readonly List<Part> _ticks = new List<Part>();
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        private static readonly int IdMod = Shader.PropertyToID("_ModColor");
        private readonly float[] _squareAng = new float[9];
        private float _gaugeAng, _fullT;
        private int _beam = -1;
        public bool Built;
        private readonly HashSet<string> _blends = new HashSet<string>();

        /// <summary>The nearest it stands (Unity metres) - it stands on what the view's centre is on, like a lock on a 1-unit target there.</summary>
        public const float MinDistance = 0.5f;
        public const int Queue = 3900;      // after the world and its effects, before the helmet HUD (4000+)

        public MpReticle(Payload p, Transform parent)
        {
            _p = p;
            _anchor = new GameObject("Samus reticle").transform;
            _anchor.SetParent(parent, false);
            _frame = new GameObject("game space").transform;
            _frame.SetParent(_anchor, false);
            _frame.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        }

        public void Build()
        {
            if (Built || _p == null || _p.Tweak == null) return;
            Built = true;
            foreach (var kv in _p.Textures) MpTextures.Upload(kv.Key, kv.Value);
            _confirm = Make("CMDL_LockConfirm");
            _flower = Make("CMDL_TargetFlower");
            _gauge = Make("CMDL_ChargeGauge");
            for (int i = 0; i < 9; i++) _squares[i] = Make("CMDL_BeamSquare" + i);
            for (int i = 0; i < Mathf.Max(0, _p.Tweak.ChargeTickCount); i++)
            {
                Part t = Make("CMDL_ChargeTickFirst");
                if (t == null) break;
                _ticks.Add(t);
            }

            MpLog.Info("Reticle: the game's combat reticle (" + _p.Models.Count + " models, " + _ticks.Count + " charge ticks, blends " + string.Join(", ", new List<string>(_blends).ToArray()) + ") for the monitor view");
        }

        private Part Make(string name)
        {
            MpModel m;
            if (!_p.Models.TryGetValue(name, out m) || m == null) return null;
            m.Build(false);
            GameObject go = m.Instantiate(name, _frame);
            var rs = go.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in rs)
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                Material[] src = r.sharedMaterials;
                var mats = new Material[src.Length];
                for (int k = 0; k < src.Length; k++)
                {
                    if (src[k] == null) continue;
                    // CModelFlags::Additive(colour).DepthCompareUpdate(false, false), no depth. CCubeMaterial::SetupBlendMode keeps the
                    // material's own blend and only turns a (one, zero) one into (source alpha, one); HandleTransparency's last stage
                    // multiplies the colour and the alpha by the flags' colour (the _ModColor tint)
                    var mat = new Material(src[k]) { name = src[k].name + " (reticle)" };
                    mat.SetFloat("_Lit", 2f);
                    mat.SetColor("_Ambient", Color.white);
                    var sb = (UnityEngine.Rendering.BlendMode)(int)src[k].GetFloat("_SrcBlend");
                    var db = (UnityEngine.Rendering.BlendMode)(int)src[k].GetFloat("_DstBlend");
                    if (sb == UnityEngine.Rendering.BlendMode.One)
                    {
                        sb = UnityEngine.Rendering.BlendMode.SrcAlpha;
                        if (db == UnityEngine.Rendering.BlendMode.Zero) db = UnityEngine.Rendering.BlendMode.One;
                    }

                    _blends.Add(sb + "+" + db);
                    mat.SetFloat("_SrcBlend", (float)sb);
                    mat.SetFloat("_DstBlend", (float)db);
                    mat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
                    mat.SetFloat("_ZWrite", 0f);
                    mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
                    mat.SetFloat("_AlphaTest", 0f);
                    mat.SetShaderPassEnabled("DepthOnly", false);
                    mat.SetShaderPassEnabled("DepthNormals", false);
                    mat.renderQueue = Queue;
                    mats[k] = mat;
                }

                r.sharedMaterials = mats;
            }

            return new Part { T = go.transform, Renderers = rs };
        }

        private static Color C(float[] c) { return c != null && c.Length >= 4 ? new Color(c[0], c[1], c[2], c[3]) : Color.white; }

        private void Tint(Part p, Color c)
        {
            if (p == null) return;
            // the flags' colour multiplies the model's colour and alpha (the blend then applies the alpha)
            Color m = c;
            foreach (Renderer r in p.Renderers)
            {
                r.GetPropertyBlock(_block);
                _block.SetColor(IdMod, m);
                r.SetPropertyBlock(_block);
            }
        }

        private static void Place(Part p, float angle, float scale)
        {
            if (p == null) return;
            p.T.localPosition = Vector3.zero;
            p.T.localRotation = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, Vector3.up);
            p.T.localScale = Vector3.one * scale;
        }

        /// <summary>CalculateClampedScale: the world scale whose projected width (on a 640-wide view) lies within [min, max].</summary>
        private static float Clamped(Camera cam, Vector3 pos, float radiusGame, float min, float max)
        {
            Vector3 a = cam.WorldToViewportPoint(pos), b = cam.WorldToViewportPoint(pos + cam.transform.right * radiusGame * MpSpace.Scale);
            float pixel = (b.x - a.x) * 2f * 640f;
            if (pixel <= 1e-4f) return radiusGame;
            return radiusGame * (Mathf.Clamp(pixel, min, max) / pixel);
        }

        public void SetVisible(bool on)
        {
            if (_anchor.gameObject.activeSelf != on) _anchor.gameObject.SetActive(on);
        }

        /// <summary>Each frame, just before the camera draws.</summary>
        public void Tick(Camera cam, int beam, float charge, float distance, float dt)
        {
            if (!Built || cam == null || _p.Tweak == null) return;
            MpTweakTargeting t = _p.Tweak;
            // (past a few metres the size sits on the minimum clamp, so the distance is capped well inside the far plane)
            Vector3 pos = cam.transform.position + cam.transform.forward * Mathf.Clamp(distance, MinDistance, Mathf.Min(20f, cam.farClipPlane * 0.5f));
            _anchor.SetPositionAndRotation(pos, cam.transform.rotation);
            _frame.localScale = new Vector3(MpSpace.Scale, -MpSpace.Scale, MpSpace.Scale);

            // the beam's angles; a beam change turns the squares and the gauge over to the new ones
            beam = Mathf.Clamp(beam, 0, 3);
            float[] target = t.OuterBeamSquareAngles != null && beam < t.OuterBeamSquareAngles.Length ? t.OuterBeamSquareAngles[beam] : null;
            float gaugeTarget = t.ChargeGaugeAngles != null && beam < t.ChargeGaugeAngles.Length ? t.ChargeGaugeAngles[beam] : 0f;
            float k = _beam < 0 ? 1f : Mathf.Clamp01(dt / 0.12f);
            _beam = beam;
            for (int i = 0; i < 9; i++)
            {
                float goal = target != null && i < target.Length ? target[i] : 0f;
                _squareAng[i] += Mathf.DeltaAngle(_squareAng[i] * Mathf.Rad2Deg, goal * Mathf.Rad2Deg) * Mathf.Deg2Rad * k;
            }

            _gaugeAng += Mathf.DeltaAngle(_gaugeAng * Mathf.Rad2Deg, gaugeTarget * Mathf.Rad2Deg) * Mathf.Deg2Rad * k;

            float confirmScale = Clamped(cam, pos, 1f, t.LockConfirmClampMin, t.LockConfirmClampMax) * t.LockConfirmScale;
            float flowerScale = Clamped(cam, pos, 1f, t.TargetFlowerClampMin, t.TargetFlowerClampMax) * t.TargetFlowerScale;
            float gaugeBase = Clamped(cam, pos, 1f, t.ChargeGaugeClampMin, t.ChargeGaugeClampMax);
            float squareScale = t.OuterBeamSquaresScale * gaugeBase;
            float gaugeScale = gaugeBase * t.ChargeGaugeScale;

            Place(_confirm, 0f, confirmScale);
            Tint(_confirm, C(t.LockConfirmColor));
            Place(_flower, 0f, flowerScale);
            Tint(_flower, C(t.TargetFlowerColor));
            for (int i = 0; i < 9; i++)
            {
                Place(_squares[i], _squareAng[i], squareScale);
                Tint(_squares[i], C(t.OuterBeamSquareColor));
            }

            // the gauge: the non-full colour, fading to a pulse between the high / low colours once fully charged
            _fullT = charge >= 1f ? Mathf.Min(t.FullChargeFadeDuration, _fullT + dt) : 0f;
            float fade = t.FullChargeFadeDuration > 0f ? _fullT / t.FullChargeFadeDuration : 0f;
            float period = Mathf.Max(0.01f, t.ChargeGaugePulsePeriod), pt = Mathf.Repeat(Time.time, period), half = period * 0.5f;
            float ratio = pt < half ? pt / half : (period - pt) / half;
            Color pulse = Color.Lerp(C(t.ChargeGaugePulseColorHigh), C(t.ChargeGaugePulseColorLow), ratio);
            Color gaugeColor = Color.Lerp(C(t.ChargeGaugeNonFullColor), pulse, fade);
            Place(_gauge, _gaugeAng, gaugeScale);
            Tint(_gauge, gaugeColor);
            int ticks = (int)(t.ChargeTickCount * Mathf.Clamp01(charge));
            for (int i = 0; i < _ticks.Count; i++)
            {
                bool on = i < ticks;
                if (_ticks[i].T.gameObject.activeSelf != on) _ticks[i].T.gameObject.SetActive(on);
                if (!on) continue;
                Place(_ticks[i], _gaugeAng + i * t.ChargeTickAnglePitch, gaugeScale);
                Tint(_ticks[i], gaugeColor);
            }
        }

        /// <summary>For the lab: puts the reticle on a layer (a picture of it alone).</summary>
        public void SetLayer(int layer)
        {
            foreach (Transform t in _anchor.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        /// <summary>For the lab: where each part lands on this camera's screen (centre and size in pixels), and its colour.</summary>
        public string Report(Camera cam)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("reticle ").Append(_anchor.gameObject.activeInHierarchy ? "shown" : "hidden");
            Action<string, Part> one = (n, p) =>
            {
                if (p == null) { sb.Append("; ").Append(n).Append(" missing"); return; }
                Bounds b = new Bounds();
                bool any = false;
                foreach (Renderer r in p.Renderers)
                {
                    if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }

                if (!any) { sb.Append("; ").Append(n).Append(" off"); return; }
                Vector3 c = cam.WorldToScreenPoint(b.center), e = cam.WorldToScreenPoint(b.center + cam.transform.right * b.extents.magnitude);
                p.Renderers[0].GetPropertyBlock(_block);
                Color m = _block.GetColor(IdMod);
                sb.Append("; ").Append(n).Append(" at ").Append(c.x.ToString("0")).Append(",").Append(c.y.ToString("0"))
                  .Append(" r ").Append((e - c).magnitude.ToString("0")).Append(" px col ").Append(m.r.ToString("0.00")).Append(",").Append(m.g.ToString("0.00")).Append(",").Append(m.b.ToString("0.00"));
            };
            one("confirm", _confirm);
            one("flower", _flower);
            one("gauge", _gauge);
            one("square0", _squares[0]);
            int on = 0;
            foreach (Part t in _ticks) if (t.T.gameObject.activeSelf) on++;
            sb.Append("; ticks on ").Append(on);
            return sb.ToString();
        }

        public void Destroy()
        {
            if (_anchor != null) UnityEngine.Object.Destroy(_anchor.gameObject);
        }
    }
}
