using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// A saved game (the game's CGameState / CWorldState / CPlayerState in short, as key=value lines next to options.txt): the world
    /// and room, where Samus stands and faces, energy, missiles, the items owned, the beam and visor, every room's script layer bits
    /// (CWorldLayerState: what the scripts switched on and off), the memory relays that are set (CScriptMailbox: doors and switches
    /// done), the scans completed (CScannableObjectInfo log), the time played. Two slots: "save" (a save station, or the tablet's
    /// Save) and "checkpoint" (written by the Dreamport each time a new room is entered, for respawning after a death).
    /// </summary>
    public sealed class MpSaveGame
    {
        public string World = MpGame.StartPak;
        public int Area;
        public float X, Y, Z, Yaw;
        public float Energy;
        public int Missiles, MissileCapacity, Beam, Visor;
        public int[] Items = new int[41];
        public readonly Dictionary<int, ulong> Layers = new Dictionary<int, ulong>();
        public readonly List<uint> Mailbox = new List<uint>();
        public readonly List<uint> Scans = new List<uint>();
        public double PlaySeconds;
        public string Where = "";
        public DateTime When;

        public static string PathOf(string slot)
        {
            if (MpDiscFinder.SettingsFolder == null) return null;
            return Path.Combine(MpDiscFinder.SettingsFolder, slot + ".txt");
        }

        private static string F(float v) { return v.ToString("0.####", CultureInfo.InvariantCulture); }

        public void Write(string slot)
        {
            string p = PathOf(slot);
            if (p == null) throw new InvalidOperationException("no settings folder");
            var sb = new StringBuilder();
            sb.Append("world=").Append(World).Append('\n');
            sb.Append("area=").Append(Area).Append('\n');
            sb.Append("pos=").Append(F(X)).Append(' ').Append(F(Y)).Append(' ').Append(F(Z)).Append('\n');
            sb.Append("yaw=").Append(F(Yaw)).Append('\n');
            sb.Append("energy=").Append(F(Energy)).Append('\n');
            sb.Append("missiles=").Append(Missiles).Append(' ').Append(MissileCapacity).Append('\n');
            sb.Append("beam=").Append(Beam).Append('\n');
            sb.Append("visor=").Append(Visor).Append('\n');
            sb.Append("items=").Append(string.Join(" ", Array.ConvertAll(Items, i => i.ToString(CultureInfo.InvariantCulture)))).Append('\n');
            foreach (var kv in Layers) sb.Append("layers=").Append(kv.Key).Append(' ').Append(kv.Value.ToString("X16")).Append('\n');
            if (Mailbox.Count > 0) sb.Append("mailbox=").Append(string.Join(" ", Mailbox.ConvertAll(i => i.ToString("X8")).ToArray())).Append('\n');
            if (Scans.Count > 0) sb.Append("scans=").Append(string.Join(" ", Scans.ConvertAll(i => i.ToString("X8")).ToArray())).Append('\n');
            sb.Append("played=").Append(PlaySeconds.ToString("0.#", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("where=").Append(Where.Replace('\n', ' ')).Append('\n');
            sb.Append("when=").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append('\n');
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            string tmp = p + ".tmp";
            File.WriteAllText(tmp, sb.ToString());
            if (File.Exists(p)) File.Delete(p);
            File.Move(tmp, p);
        }

        public static bool Exists(string slot)
        {
            string p = PathOf(slot);
            return p != null && File.Exists(p);
        }

        public static MpSaveGame Read(string slot)
        {
            string p = PathOf(slot);
            if (p == null || !File.Exists(p)) return null;
            var s = new MpSaveGame();
            foreach (string line in File.ReadAllLines(p))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq), v = line.Substring(eq + 1);
                string[] w = v.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                try
                {
                    switch (k)
                    {
                        case "world": s.World = v; break;
                        case "area": s.Area = Int(v); break;
                        case "pos": s.X = Flt(w[0]); s.Y = Flt(w[1]); s.Z = Flt(w[2]); break;
                        case "yaw": s.Yaw = Flt(v); break;
                        case "energy": s.Energy = Flt(v); break;
                        case "missiles": s.Missiles = Int(w[0]); s.MissileCapacity = Int(w[1]); break;
                        case "beam": s.Beam = Int(v); break;
                        case "visor": s.Visor = Int(v); break;
                        case "items": for (int i = 0; i < w.Length && i < s.Items.Length; i++) s.Items[i] = Int(w[i]); break;
                        case "layers": s.Layers[Int(w[0])] = ulong.Parse(w[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture); break;
                        case "mailbox": foreach (string x in w) s.Mailbox.Add(uint.Parse(x, NumberStyles.HexNumber, CultureInfo.InvariantCulture)); break;
                        case "scans": foreach (string x in w) s.Scans.Add(uint.Parse(x, NumberStyles.HexNumber, CultureInfo.InvariantCulture)); break;
                        case "played": s.PlaySeconds = double.Parse(v, CultureInfo.InvariantCulture); break;
                        case "where": s.Where = v; break;
                        case "when": DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out s.When); break;
                    }
                }
                catch (Exception) { MpLog.Warn("Save: bad line in " + p + ": " + line); }
            }

            return s;
        }

        private static int Int(string s) { return int.Parse(s, CultureInfo.InvariantCulture); }
        private static float Flt(string s) { return float.Parse(s, CultureInfo.InvariantCulture); }

        public string Describe()
        {
            TimeSpan t = TimeSpan.FromSeconds(PlaySeconds);
            return Where + " - " + ((int)t.TotalHours).ToString("00") + ":" + t.Minutes.ToString("00") + " played" + (When != default(DateTime) ? " (" + When.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + ")" : "");
        }
    }
}
