using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// Everything the arm cannon needs, found the game's way: the GunRes tweak (decomp CTweakGunRes: named resources in order - gun
    /// motion, grapple arm, right hand, the beam ANCS power / ice / wave / plasma / phazon, holo transition, bomb set / explosion /
    /// power bomb explosion, then per beam the normal and charged WPSC, the muzzle, charge and aux-muzzle PARTs) and the PlayerGun
    /// tweak (cool-downs, damage). Prepared on the loader thread; <see cref="Upload"/> makes the Unity side.
    /// </summary>
    public sealed class MpGunAssets
    {
        public sealed class Beam
        {
            public string Name;
            public uint Ancs, Cmdl, HoloCmdl;
            public MpModel Model;
            public MpCharacter Char;        // the beam ANCS character 0: the cannon skinned, with the beam's own animations
            public MpWeaponDesc Normal, Charged;
            public uint NormalTex = 0xFFFFFFFF, ChargedTex = 0xFFFFFFFF, MuzzleTex = 0xFFFFFFFF, ChargeTex = 0xFFFFFFFF, TrailTex = 0xFFFFFFFF;
            public Material NormalMat, ChargedMat, MuzzleMat, ChargeMat, TrailMat;
        }

        public readonly Beam[] Beams = new Beam[4];

        // the game's own effects (Data/Particles): per beam the normal / charged weapons (WPSC), the muzzle, charge and aux-muzzle PARTs
        public readonly Data.Particles.MpWeaponDescFull[] NormalWeapon = new Data.Particles.MpWeaponDescFull[4], ChargedWeapon = new Data.Particles.MpWeaponDescFull[4];
        public Data.Particles.MpWeaponDescFull MissileWeapon;
        public readonly uint[] MuzzlePart = new uint[4], ChargePart = new uint[4], AuxMuzzlePart = new uint[4];
        public string EffectsReport = "";
        public MpWeaponDesc Missile;
        public uint MissileTex = 0xFFFFFFFF, MissileSmokeTex = 0xFFFFFFFF;
        public Material MissileMat, SmokeMat, ImpactMat;
        public MpTweakGun Tweak;
        public readonly List<KeyValuePair<uint, MpTexture>> Textures = new List<KeyValuePair<uint, MpTexture>>();
        public string Report = "";

        private static readonly string[] BeamNames = { "Power", "Ice", "Wave", "Plasma" };

        /// <summary>CGunWeapon::skSuitArmNames (EPlayerSuit order: Power, Gravity, Varia, Phazon).</summary>
        public static readonly string[] SuitArmNames = { "PowerArm", "GravityArm", "VariaArm", "PhazonArm" };
        public readonly MpModel[] SuitArms = new MpModel[4];
        public MpCharacter Motion;
        public MpCharacter Ball;          // the morph ball (SamusBallANCS)
        public uint BallAncs;
        public uint HoloPart = 0xFFFFFFFF;
        /// <summary>GunRes 9 / 10: the bomb burning (CTweakGunRes mBombSet) and its explosion (mBombExplode).</summary>
        public uint BombSetPart = 0xFFFFFFFF, BombExplodePart = 0xFFFFFFFF;
        /// <summary>The beams' own secondary effects by name (CPowerBeam / CIceBeam / CWaveBeam / CPlasmaBeam): the charge effect per beam
        /// (power, ice, wave, plasma; the wave's is an ELSC), the ice beam's release effect, its muzzle smoke, the wave's cancel ELSC.</summary>
        public readonly uint[] SecondPart = { 0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF };
        public uint IceEndPart = 0xFFFFFFFF, IceSmokePart = 0xFFFFFFFF, WaveChargeElsc = 0xFFFFFFFF, WaveCancelElsc = 0xFFFFFFFF;

        public static MpGunAssets Prepare(MpAssets assets)
        {
            var g = new MpGunAssets();
            var names = new List<string>();
            MpPak.Entry gunRes = assets.Named("GunRes");
            if (gunRes != null)
            {
                var r = new MpReader(gunRes.Pak.Read(gunRes));
                while (r.Remaining > 0)
                {
                    string s = r.CString();
                    if (s.Length == 0 || s[0] < 32 || s[0] > 126) break;
                    names.Add(s);
                }
            }

            g.Report = "GunRes names " + names.Count;
            Func<int, uint> idOf = i =>
            {
                if (i < 0 || i >= names.Count) return 0xFFFFFFFF;
                MpPak.Entry e = assets.Named(names[i]);
                return e != null ? e.Id : 0xFFFFFFFF;
            };

            MpPak.Entry gunTweak = assets.Named("PlayerGun");
            if (gunTweak != null) g.Tweak = MpTweakGun.Read(gunTweak.Pak.Read(gunTweak));
            var tex = new HashSet<uint>();
            for (int b = 0; b < 4; b++)
            {
                var beam = new Beam { Name = BeamNames[b] };
                beam.Ancs = idOf(3 + b);
                MpAncs ancs = MpModels.Ancs(assets, beam.Ancs);
                if (ancs != null && ancs.Characters.Count > 0)
                {
                    beam.Cmdl = ancs.Characters[0].Cmdl;
                    if (ancs.Characters.Count > 1) beam.HoloCmdl = ancs.Characters[1].Cmdl;
                }

                beam.Char = MpCharacter.Load(assets, beam.Ancs, 0);
                beam.Model = beam.Char != null ? beam.Char.Model : MpModels.Load(assets, beam.Cmdl);
                MpModels.TexturesOf(beam.Model, tex);
                uint normalId = idOf(12 + b * 2), chargedId = idOf(13 + b * 2);
                beam.Normal = ReadWpsc(assets, normalId);
                beam.Charged = ReadWpsc(assets, chargedId);
                beam.NormalTex = PartTexture(assets, beam.Normal.Particle, tex);
                beam.TrailTex = PartTexture(assets, beam.Normal.Particle2, tex);
                beam.ChargedTex = PartTexture(assets, beam.Charged.Particle, tex);
                beam.MuzzleTex = PartTexture(assets, idOf(22 + b), tex);
                beam.ChargeTex = PartTexture(assets, idOf(27 + b), tex);
                g.Beams[b] = beam;
                g.Report += "; " + beam.Name + " CMDL " + beam.Cmdl.ToString("X8") + " shot " + beam.Normal.Speed.ToString("0") + " u/s" + (beam.Char != null ? " (" + beam.Char.Report + ")" : " (no animations)");
            }

            // Samus's forearm drawn at the cannon's elbow locator (CGunWeapon::skSuitArmNames by EPlayerSuit), the gun's own
            // motion (CGunMotion, GunRes 0: GBSE_SDK carries the sway) and the beam change hologram (GunRes 8)
            for (int s = 0; s < SuitArmNames.Length; s++)
            {
                MpPak.Entry arm = assets.Named(SuitArmNames[s]);
                if (arm == null) continue;
                g.SuitArms[s] = MpModels.Load(assets, arm.Id);
                MpModels.TexturesOf(g.SuitArms[s], tex);
            }

            g.Motion = MpCharacter.Load(assets, idOf(0), 0);
            MpPak.Entry ballAncs = assets.Named("SamusBallANCS");
            if (ballAncs != null)
            {
                g.BallAncs = ballAncs.Id;
                g.Ball = MpCharacter.Load(assets, ballAncs.Id, 0);
                if (g.Ball != null) g.Ball.TexturesInto(tex);
            }
            g.HoloPart = idOf(8);

            MpPak.Entry missile = assets.Named("Missile");
            g.Missile = missile != null && missile.Type == "WPSC" ? MpWeaponDesc.Read(missile.Pak.Read(missile)) : new MpWeaponDesc();
            g.MissileTex = PartTexture(assets, g.Missile.Particle, tex);
            MpPak.Entry smoke = assets.Named("ShotSmoke");
            g.MissileSmokeTex = smoke != null ? PartTexture(assets, smoke.Id, tex) : 0xFFFFFFFF;

            // the game's effects: weapons, their projectile systems and swooshes, impacts (CRSC), muzzle / charge / aux muzzle
            try
            {
                if (MpEffects.Lib == null) MpEffects.Lib = new Data.Particles.MpPartLibrary(assets);
                var lib = MpEffects.Lib;
                var parts = new List<uint>();
                var swooshes = new List<uint>();
                Action<Data.Particles.MpWeaponDescFull> addWeapon = w =>
                {
                    if (w == null) return;
                    parts.Add(w.APSM); parts.Add(w.APS2);
                    swooshes.Add(w.ASW1); swooshes.Add(w.ASW2); swooshes.Add(w.ASW3);
                    Data.Particles.MpCollisionResponse cr = lib.Response(w.COLR);
                    if (cr != null) foreach (uint p in cr.Particles) if (MpAssets.Valid(p)) parts.Add(p);
                };
                for (int b = 0; b < 4; b++)
                {
                    g.NormalWeapon[b] = lib.Weapon(idOf(12 + b * 2));
                    g.ChargedWeapon[b] = lib.Weapon(idOf(13 + b * 2));
                    addWeapon(g.NormalWeapon[b]);
                    addWeapon(g.ChargedWeapon[b]);
                    g.MuzzlePart[b] = idOf(22 + b);
                    g.ChargePart[b] = idOf(27 + b);
                    g.AuxMuzzlePart[b] = idOf(32 + b);
                    parts.Add(g.MuzzlePart[b]); parts.Add(g.ChargePart[b]); parts.Add(g.AuxMuzzlePart[b]);
                }

                parts.Add(idOf(8));
                g.BombSetPart = idOf(9);
                g.BombExplodePart = idOf(10);
                parts.Add(g.BombSetPart); parts.Add(g.BombExplodePart);
                Func<string, uint> named = n => { MpPak.Entry e = assets.Named(n); return e != null ? e.Id : 0xFFFFFFFFu; };
                g.SecondPart[0] = named("Power2nd_1");
                g.SecondPart[1] = named("Ice2nd_1");
                g.SecondPart[3] = named("Plasma2nd_1");
                g.IceEndPart = named("Ice2nd_2");
                g.IceSmokePart = named("IceSmoke");
                g.WaveChargeElsc = named("Wave2nd_1");
                g.WaveCancelElsc = named("Wave2nd_2");
                foreach (uint p in new[] { g.SecondPart[0], g.SecondPart[1], g.SecondPart[3], g.IceEndPart, g.IceSmokePart }) if (MpAssets.Valid(p)) parts.Add(p);

                MpPak.Entry mw = assets.Named("Missile");
                if (mw != null) { g.MissileWeapon = lib.Weapon(mw.Id); addWeapon(g.MissileWeapon); }
                MpEffects.Prepare(assets, parts, swooshes);
                g.EffectsReport = "effects: " + lib.Textures.Count + " textures, " + lib.Models.Count + " models" + (lib.Problems.Count > 0 ? ", problems: " + string.Join("; ", lib.Problems.ToArray()) : "");
            }
            catch (Exception e) { g.EffectsReport = "effects not prepared: " + e.Message; }

            foreach (uint id in tex)
            {
                if (MpTextures.Has(id)) continue;
                try
                {
                    byte[] b = assets.Read(id);
                    if (b != null) g.Textures.Add(new KeyValuePair<uint, MpTexture>(id, MpTexture.Read(b)));
                }
                catch (Exception e) { MpLog.Warn("Gun: TXTR " + id.ToString("X8") + " not decoded: " + e.Message); }
            }

            return g;
        }

        private static MpWeaponDesc ReadWpsc(MpAssets assets, uint id)
        {
            try
            {
                if (assets.TypeOf(id) == "WPSC") return MpWeaponDesc.Read(assets.Read(id));
            }
            catch (Exception) { }

            return new MpWeaponDesc();
        }

        private static uint PartTexture(MpAssets assets, uint part, HashSet<uint> tex)
        {
            if (!MpAssets.Valid(part) || assets.TypeOf(part) != "PART") return 0xFFFFFFFF;
            uint t = MpSprites.TextureOfPart(assets.Read(part));
            if (MpAssets.Valid(t) && assets.TypeOf(t) == "TXTR") { tex.Add(t); return t; }
            return 0xFFFFFFFF;
        }

        /// <summary>Main thread: textures, the beam models' meshes and materials, the sprite materials.</summary>
        public void Upload()
        {
            foreach (var kv in Textures) MpTextures.Upload(kv.Key, kv.Value);
            MpEffects.Upload();
            foreach (MpModel arm in SuitArms) if (arm != null) arm.Build(true);
            if (Motion != null) Motion.Upload(true);
            if (Ball != null) Ball.Upload(true);
            foreach (Beam b in Beams)
            {
                if (b == null) continue;
                if (b.Model != null) b.Model.Build(true);
                b.NormalMat = MpSprites.MaterialFor(b.NormalTex, true);
                b.ChargedMat = MpSprites.MaterialFor(b.ChargedTex, true);
                b.MuzzleMat = MpSprites.MaterialFor(b.MuzzleTex, true);
                b.ChargeMat = MpSprites.MaterialFor(b.ChargeTex, true);
                b.TrailMat = MpSprites.MaterialFor(b.TrailTex, true);
            }

            MissileMat = MpSprites.MaterialFor(MissileTex, true);
            SmokeMat = MpSprites.MaterialFor(MissileSmokeTex, false);
            ImpactMat = MpSprites.MaterialFor(null, true);
        }
    }
}
