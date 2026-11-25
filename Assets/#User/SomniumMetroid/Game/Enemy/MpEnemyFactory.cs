using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// The frigate's enemies by script type (CStateManager's loader table): Parasite (0x3D), Space Pirate (0x24), Gun Turret (0x64),
    /// Wall Crawler Swarm (0x5A), the Parasite Queen (NewIntroBoss, 0x0E); on Tallon IV and in the Chozo Ruins (MpRuinsEnemies) Beetle
    /// (0x16), War Wasp (0x21), Fire Flea (0x36), Eyeball (0x67), Geemer (0x70), Ambient AI (0x75). <see cref="Collect"/> runs on the loader thread while an
    /// area is prepared (characters, animations, effects and weapons read and decoded); <see cref="Make"/> builds the entity on
    /// the main thread when the area's scripts are made. Each enemy class keeps its own reading of its script object.
    /// </summary>
    public static class MpEnemyFactory
    {
        public const int Parasite = 0x3D, SpacePirate = 0x24, GunTurret = 0x64, WallCrawlerSwarm = 0x5A, NewIntroBoss = 0x0E,
            Beetle = 0x16, WarWasp = 0x21, FireFlea = 0x36, EyeBall = 0x67, Geemer = 0x70, AmbientAI = 0x75;

        public static bool IsEnemy(int type)
        {
            return type == Parasite || type == SpacePirate || type == GunTurret || type == WallCrawlerSwarm || type == NewIntroBoss
                || type == Beetle || type == WarWasp || type == FireFlea || type == EyeBall || type == Geemer || type == AmbientAI;
        }

        /// <summary>Loader thread: everything the enemy will need; textures to decode go into <paramref name="tex"/>.</summary>
        public static void Collect(MpAssets assets, MpScriptObject o, HashSet<uint> tex)
        {
            try
            {
                switch (o.Type)
                {
                    case Parasite: MpParasite.Collect(assets, o, tex); break;
                    case SpacePirate: MpSpacePirate.Collect(assets, o, tex); break;
                    case GunTurret: MpGunTurret.Collect(assets, o, tex); break;
                    case WallCrawlerSwarm: MpWallCrawlerSwarm.Collect(assets, o, tex); break;
                    case NewIntroBoss: MpParasiteQueen.Collect(assets, o, tex); break;
                    case Beetle: MpBeetle.Collect(assets, o, tex); break;
                    case WarWasp: MpWarWasp.Collect(assets, o, tex); break;
                    case FireFlea: MpFireFlea.Collect(assets, o, tex); break;
                    case EyeBall: MpEyeBall.Collect(assets, o, tex); break;
                    case Geemer: MpGeemer.Collect(assets, o, tex); break;
                    case AmbientAI: MpAmbientAI.Collect(assets, o, tex); break;
                }
            }
            catch (System.Exception e) { MpLog.Warn("Enemies: " + o.Type.ToString("X2") + " " + o.Id.ToString("X8") + " '" + o.Name + "' not prepared: " + e.Message); }
        }

        /// <summary>Main thread: the enemy entity (null = not an enemy, or unreadable: a plain entity takes its place).</summary>
        public static MpEntity Make(MpScriptObject o, MpAreaView view)
        {
            try
            {
                switch (o.Type)
                {
                    case Parasite: return MpParasite.Make(o, view);
                    case SpacePirate: return MpSpacePirate.Make(o, view);
                    case GunTurret: return MpGunTurret.Make(o, view);
                    case WallCrawlerSwarm: return MpWallCrawlerSwarm.Make(o, view);
                    case NewIntroBoss: return MpParasiteQueen.Make(o, view);
                    case Beetle: return MpBeetle.Make(o, view);
                    case WarWasp: return MpWarWasp.Make(o, view);
                    case FireFlea: return MpFireFlea.Make(o, view);
                    case EyeBall: return MpEyeBall.Make(o, view);
                    case Geemer: return MpGeemer.Make(o, view);
                    case AmbientAI: return MpAmbientAI.Make(o, view);
                }
            }
            catch (System.Exception e) { MpLog.Warn("Enemies: " + o.Type.ToString("X2") + " " + o.Id.ToString("X8") + " '" + o.Name + "' not made: " + e.Message); }
            return null;
        }

        /// <summary>
        /// The common start of a patterned enemy's script object (ScriptLoader: name, head with scale, CPatternedInfo, CActorParameters);
        /// the reader is left after the actor parameters for the type's own fields.
        /// </summary>
        public static MpReader ReadPatterned(MpScriptObject o, out MpScriptRead.Head head, out MpPatternedInfo info, out MpActorParams ap)
        {
            var r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
            head = MpScriptRead.ReadHead(r, true);
            info = MpPatternedInfo.Read(r);
            ap = MpScriptRead.ReadActorParams(r);
            return r;
        }

        /// <summary>Loader thread: a character (model, animations) and its textures, plus a patterned enemy's death effects.</summary>
        public static MpCharacter CollectCharacter(MpAssets assets, uint ancs, int character, HashSet<uint> tex)
        {
            MpCharacter c = MpCharacter.Load(assets, ancs, character);
            if (c != null) c.TexturesInto(tex);
            if (c != null && c.Info != null)
                foreach (uint p in c.Info.Particles) MpEnemyShots.PreparePart(assets, p);
            return c;
        }

        public static void CollectPatterned(MpAssets assets, MpPatternedInfo info, HashSet<uint> tex)
        {
            if (info == null) return;
            CollectCharacter(assets, info.Anim.Ancs, info.Anim.Character, tex);
            MpEnemyShots.PreparePart(assets, info.Particle1);
            MpEnemyShots.PreparePart(assets, info.Particle2);
        }
    }
}
