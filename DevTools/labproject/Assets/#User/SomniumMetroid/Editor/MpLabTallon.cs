using System.Collections;
using System.Collections.Generic;
using System.Text;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using SomniumSpace.Worlds.SomniumMetroid.Game;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Editor
{
    /// <summary>
    /// Lab scenario "tallon": the frigate's end (its world teleporter) to the Tallon Overworld Landing Site with the upgrades gone,
    /// the rooms to the Chozo Ruins elevator (Canyon Cavern, Tallon Canyon, Transport Tunnel A), the elevator's teleporter (Play),
    /// then the Chozo Ruins route to the Hive Totem (Ruins Entrance, Main Plaza, Nursery Access, Eyon Tunnel, Ruined Nursery,
    /// North Atrium, Ruined Gallery, Totem Access) and the Hive Totem fight (MpLabTotem). Each room: the script types left as
    /// plain entities (not implemented), the enemies, a picture. -mplabfrom chozo / totem starts later on the way.
    /// </summary>
    public sealed partial class MpLabRunner
    {
        private static readonly int[] TallonRoute = { 0, 2, 6, 9, 14 };
        private static readonly int[] ChozoRoute = { 0, 1, 2, 5, 10, 16, 23, 28, 33, 36 };

        partial void ExtraScenarioTallon(string scenario, ref IEnumerator run)
        {
            if (scenario == "tallon") run = TallonScenario();
        }

        private IEnumerator TallonScenario()
        {
            G.God = true;
            _cam.enabled = true;
            _cam.tag = "MainCamera";
            string from = MpLabBoot.Arg("-mplabfrom", "frigate");
            yield return Settle();

            if (from == "frigate")
            {
                Log("frigate: items owned " + ItemsOwned() + ", energy " + G.State.Energy + ", missiles " + G.State.Missiles + "/" + G.State.MissileCapacity);
                // the frigate's teleporter (area 0, escape layer) leads to the Landing Site
                G.OnWorldTeleport(0x39F2DE28u, 0xB2701146u);
                yield return Wait(() => G.CurrentPak == MpGame.TallonPak && !G.SwitchingWorld, 180f, "Tallon Overworld");
                if (_failed) yield break;
                yield return Settle();
                Log("tallon: arrived in '" + G.World.Areas[G.WorldView.CurrentArea].Name + "' - items owned " + ItemsOwned() + ", energy " + G.State.Energy + ", missiles " + G.State.Missiles + "/" + G.State.MissileCapacity);
                Log("tallon: morph ball item " + G.State.Items[MpPlayerState.ItemMorphBall] + ", ice " + G.State.Items[MpPlayerState.ItemIce] + ", charge " + G.State.Items[MpPlayerState.ItemCharge] + " (UnlockAll " + MpPlayerState.UnlockAll + ")");
            }

            if (from == "frigate" || from == "tallon")
            {
                if (G.CurrentPak != MpGame.TallonPak) { yield return G.SwitchWorld(MpGame.TallonPak, 0, 0, true, null); yield return Settle(); }
                foreach (int a in TallonRoute) yield return Room(a, "tallon");
                MpWorldTeleporterEntity tp = null;
                foreach (MpEntity e in G.Scripts.All) if (e is MpWorldTeleporterEntity && e.Area == 14) tp = (MpWorldTeleporterEntity)e;
                Log("elevator: teleporter " + (tp != null ? "'" + tp.Name + "' active " + tp.Active + " to world " + tp.WorldId.ToString("X8") + " area " + tp.AreaId.ToString("X8") : "MISSING"));
                if (tp == null) { _failed = true; yield break; }
                tp.Active = true;
                tp.OnMessage(MpScriptMsg.Play, null);
                yield return Wait(() => G.CurrentPak == "Metroid2.pak" && !G.SwitchingWorld, 180f, "Chozo Ruins");
                if (_failed) yield break;
                yield return Settle();
                Log("chozo: arrived in '" + G.World.Areas[G.WorldView.CurrentArea].Name + "', items owned " + ItemsOwned());
            }

            if (from != "totem")
            {
                if (G.CurrentPak != "Metroid2.pak") { yield return G.SwitchWorld("Metroid2.pak", 0, 0, false, null); yield return Settle(); }
                foreach (int a in ChozoRoute) yield return Room(a, "chozo");
            }
            else
            {
                if (G.CurrentPak != "Metroid2.pak") { yield return G.SwitchWorld("Metroid2.pak", 0, 36, false, null); yield return Settle(); }
                yield return Room(36, "chozo");
            }

            yield return TotemFight();
        }

        private string ItemsOwned()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < G.State.Items.Length; i++) if (G.State.Items[i] > 0) sb.Append(i).Append(G.State.Items[i] > 1 ? "x" + G.State.Items[i] : "").Append(' ');
            return sb.ToString().Trim();
        }

        private IEnumerator Settle()
        {
            float t0 = Time.realtimeSinceStartup;
            while ((G.WorldView.IsBusy || G.Warping) && Time.realtimeSinceStartup - t0 < 120f) yield return null;
            for (int k = 0; k < 10 && MpCinematics.Active; k++) { MpCinematics.Skip(); yield return Frames(2); }
            yield return Frames(10);
        }

        /// <summary>A room on the way: warped to, its scripts counted (types not implemented, enemies), a picture from the entrance.</summary>
        private IEnumerator Room(int area, string tag)
        {
            if (G.WorldView.CurrentArea != area || !G.WorldView.IsLoaded(area))
            {
                MpGameActions.WarpTo(area);
                yield return Frames(2);
            }

            yield return Settle();
            MpGame.LabFocus = G.PlayerGame;
            var plain = new SortedDictionary<int, int>();
            var enemies = new SortedDictionary<string, int>();
            int all = 0;
            foreach (MpEntity e in G.Scripts.All)
            {
                if (e.Area != area) continue;
                all++;
                if (e.GetType() == typeof(MpEntity)) { int n; plain.TryGetValue(e.Type, out n); plain[e.Type] = n + 1; }
                var en = e as MpEnemy;
                if (en != null) { string k = en.GetType().Name + (en.Active ? "" : "(off)"); int n; enemies.TryGetValue(k, out n); enemies[k] = n + 1; }
            }

            var ps = new StringBuilder();
            foreach (var kv in plain) ps.Append(kv.Key.ToString("X2")).Append('x').Append(kv.Value).Append(' ');
            var es = new StringBuilder();
            foreach (var kv in enemies) es.Append(kv.Key).Append('x').Append(kv.Value).Append(' ');
            Log(tag + " room " + area + " '" + G.World.Areas[area].Name + "': " + all + " entities; plain (not implemented) " + ps + "; enemies " + es + "; player at " + G.PlayerGame);
            float yaw;
            string src;
            if (!MpPlayer.TryFacingYaw(out yaw, out src)) yaw = 0f;
            Aim(G.PlayerGame + new Vector3(0, 0, MpSpace.SamusEyeHeight), -yaw, 5f);
            yield return Frames(3);
            MpShaderGlobals.Apply(_cam);
            Shoot(_cam, tag + "_" + area.ToString("00") + ".png", 960, 540);
        }
    }
}
