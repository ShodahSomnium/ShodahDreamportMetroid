using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// The player's own options (the "METROID PRIME" tablet app edits them, the game reads them every frame): the helmet HUD's depth,
    /// size, height and opacity, the arm cannon's fit on the hand, sound volumes, controller ticks, damage taken. Kept per machine in
    /// options.txt next to settings.txt (key=value lines, invariant culture), written whenever <see cref="Save"/> is called.
    /// <see cref="Version"/> counts changes, so readers can re-apply only when something moved.
    /// </summary>
    public static class MpSettings
    {
        // the helmet HUD
        /// <summary>Metres from the eye to the HUD (the game's angular layout is kept: closer is the same picture, nearer the eyes).</summary>
        public static float HudDistance = 1.5f;
        /// <summary>The HUD's size against the game's own 65-degree layout (1 = the game's).</summary>
        public static float HudSize = 0.9f;
        /// <summary>Metres the HUD sits above (+) or below (-) the eye line.</summary>
        public static float HudHeight = 0f;
        public static float HudOpacity = 1f;
        public static bool HudShown = true;
        /// <summary>The helmet's rim and glare (FRME_Helmet).</summary>
        public static bool HelmetShown = true;

        // the arm cannon (VR: on the right controller)
        /// <summary>Centimetres the cannon moves along the aim (+ forward), up and right from where the hand holds it.</summary>
        public static float CannonForward, CannonUp, CannonRight;
        /// <summary>Degrees the cannon tilts up (+) from the controller's pointing direction.</summary>
        public static float CannonPitch;
        public static float CannonScale = 1f;
        /// <summary>Samus's suit forearm behind the cannon (the game's PowerArm / VariaArm model at the cannon's elbow).</summary>
        public static bool SuitArm = true;
        /// <summary>Monitor: the game's gun sway and idle motions (CGunMotion).</summary>
        public static bool GunMotion = true;
        /// <summary>VR: the cannon follows the right FOREARM (elbow to wrist) instead of the controller's pointing, and the avatar's own right
        /// hand and forearm are hidden inside it (MpAvatarArm).</summary>
        public static bool CannonOnForearm;
        /// <summary>The morph ball seen from behind with your own eyes (the world slides under your hidden avatar), else on the cinema screen.</summary>
        public static bool BallThirdPerson = true;
        /// <summary>Samus's top speed, jump and gravity on your avatar (MpMovement), else the client's own.</summary>
        public static bool SamusMovement = true;
        /// <summary>Cutscenes: you are the camera (the world moves around you, look anywhere); off: on the cinema screen.</summary>
        public static bool CinemaFirstPerson = true;
        /// <summary>Cutscenes: the instance master's avatar plays Samus.</summary>
        public static bool CinemaAvatar = true;
        /// <summary>A new game starts past the frigate's opening cutscene (skipped the moment it begins).</summary>
        public static bool SkipIntro = true;
        /// <summary>The client's own movement as MpMovement measured it (m/s, m/s^2; 0 = not measured yet).</summary>
        public static float BaseWalkSpeed, BaseSprintSpeed, BaseLocoSpeed, BaseJumpDesk, BaseJumpXr, BaseGravityDesk, BaseGravityXr;

        // sound, controls, play
        public static float MusicVolume = 1f, SfxVolume = 1f;
        public static float Haptics = 1f;
        /// <summary>Damage Samus takes, x (0.5 easy, 1 the game's, 2 hard).</summary>
        public static float DamageTaken = 1f;
        /// <summary>Every beam and visor from the start (testing; the game gives them as it goes).</summary>
        public static bool UnlockAll = true;

        public static int Version { get; private set; }
        public static event Action Changed;

        public static string File
        {
            get { return MpDiscFinder.SettingsFolder == null ? null : Path.Combine(MpDiscFinder.SettingsFolder, "options.txt"); }
        }

        /// <summary>Something was changed: readers re-apply; the file is written.</summary>
        public static void Touch(bool save = true)
        {
            Version++;
            MpPlayerState.UnlockAll = UnlockAll;
            Action c = Changed;
            if (c != null)
            {
                try { c(); }
                catch (Exception e) { MpLog.Warn("Options: a listener failed: " + e.Message); }
            }

            if (save) Save();
        }

        private static IEnumerable<KeyValuePair<string, string>> Pairs()
        {
            yield return P("hud_distance", HudDistance);
            yield return P("hud_size", HudSize);
            yield return P("hud_height", HudHeight);
            yield return P("hud_opacity", HudOpacity);
            yield return P("hud_shown", HudShown);
            yield return P("helmet_shown", HelmetShown);
            yield return P("cannon_forward", CannonForward);
            yield return P("cannon_up", CannonUp);
            yield return P("cannon_right", CannonRight);
            yield return P("cannon_pitch", CannonPitch);
            yield return P("cannon_scale", CannonScale);
            yield return P("suit_arm", SuitArm);
            yield return P("gun_motion", GunMotion);
            yield return P("cannon_forearm", CannonOnForearm);
            yield return P("ball_third_person", BallThirdPerson);
            yield return P("samus_movement", SamusMovement);
            yield return P("cinema_first_person", CinemaFirstPerson);
            yield return P("cinema_avatar", CinemaAvatar);
            yield return P("skip_intro", SkipIntro);
            yield return P("base_walk", BaseWalkSpeed);
            yield return P("base_sprint", BaseSprintSpeed);
            yield return P("base_loco", BaseLocoSpeed);
            yield return P("base_jump_desk", BaseJumpDesk);
            yield return P("base_jump_xr", BaseJumpXr);
            yield return P("base_gravity_desk", BaseGravityDesk);
            yield return P("base_gravity_xr", BaseGravityXr);
            yield return P("music_volume", MusicVolume);
            yield return P("sfx_volume", SfxVolume);
            yield return P("haptics", Haptics);
            yield return P("damage_taken", DamageTaken);
            yield return P("unlock_all", UnlockAll);
        }

        private static KeyValuePair<string, string> P(string k, float v) { return new KeyValuePair<string, string>(k, v.ToString("0.###", CultureInfo.InvariantCulture)); }
        private static KeyValuePair<string, string> P(string k, bool v) { return new KeyValuePair<string, string>(k, v ? "1" : "0"); }

        public static void Load()
        {
            string f = File;
            try
            {
                if (f == null || !System.IO.File.Exists(f)) { Touch(false); return; }
                foreach (string line in System.IO.File.ReadAllLines(f))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    Set(line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim());
                }

                MpLog.Info("Options: read " + f);
            }
            catch (Exception e) { MpLog.Warn("Options: " + f + " not read: " + e.Message); }

            Touch(false);
        }

        private static void Set(string k, string v)
        {
            float x;
            bool num = float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out x);
            bool b = v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
            switch (k)
            {
                case "hud_distance": if (num) HudDistance = Clamp(x, 0.3f, 4f); break;
                case "hud_size": if (num) HudSize = Clamp(x, 0.5f, 1.5f); break;
                case "hud_height": if (num) HudHeight = Clamp(x, -0.5f, 0.5f); break;
                case "hud_opacity": if (num) HudOpacity = Clamp(x, 0.1f, 1f); break;
                case "hud_shown": HudShown = b; break;
                case "helmet_shown": HelmetShown = b; break;
                case "cannon_forward": if (num) CannonForward = Clamp(x, -30f, 30f); break;
                case "cannon_up": if (num) CannonUp = Clamp(x, -30f, 30f); break;
                case "cannon_right": if (num) CannonRight = Clamp(x, -30f, 30f); break;
                case "cannon_pitch": if (num) CannonPitch = Clamp(x, -45f, 45f); break;
                case "cannon_scale": if (num) CannonScale = Clamp(x, 0.5f, 1.6f); break;
                case "suit_arm": SuitArm = b; break;
                case "gun_motion": GunMotion = b; break;
                case "cannon_forearm": CannonOnForearm = b; break;
                case "ball_third_person": BallThirdPerson = b; break;
                case "samus_movement": SamusMovement = b; break;
                case "cinema_first_person": CinemaFirstPerson = b; break;
                case "cinema_avatar": CinemaAvatar = b; break;
                case "skip_intro": SkipIntro = b; break;
                case "base_walk": if (num) BaseWalkSpeed = Clamp(x, 0f, 50f); break;
                case "base_sprint": if (num) BaseSprintSpeed = Clamp(x, 0f, 50f); break;
                case "base_loco": if (num) BaseLocoSpeed = Clamp(x, 0f, 50f); break;
                case "base_jump_desk": if (num) BaseJumpDesk = Clamp(x, 0f, 50f); break;
                case "base_jump_xr": if (num) BaseJumpXr = Clamp(x, 0f, 50f); break;
                case "base_gravity_desk": if (num) BaseGravityDesk = Clamp(x, 0f, 200f); break;
                case "base_gravity_xr": if (num) BaseGravityXr = Clamp(x, 0f, 200f); break;
                case "music_volume": if (num) MusicVolume = Clamp(x, 0f, 1f); break;
                case "sfx_volume": if (num) SfxVolume = Clamp(x, 0f, 1f); break;
                case "haptics": if (num) Haptics = Clamp(x, 0f, 1f); break;
                case "damage_taken": if (num) DamageTaken = Clamp(x, 0f, 4f); break;
                case "unlock_all": UnlockAll = b; break;
            }
        }

        private static float Clamp(float v, float a, float b) { return v < a ? a : v > b ? b : v; }

        public static void Save()
        {
            string f = File;
            if (f == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                var lines = new List<string>();
                foreach (var kv in Pairs()) lines.Add(kv.Key + "=" + kv.Value);
                System.IO.File.WriteAllLines(f, lines.ToArray());
            }
            catch (Exception e) { MpLog.Warn("Options: " + f + " not written: " + e.Message); }
        }

        /// <summary>Back to the defaults (the tablet's "Reset" button).</summary>
        public static void Defaults()
        {
            HudDistance = 1.5f; HudSize = 0.9f; HudHeight = 0f; HudOpacity = 1f; HudShown = true; HelmetShown = true;
            CannonForward = CannonUp = CannonRight = CannonPitch = 0f; CannonScale = 1f; SuitArm = true; GunMotion = true; CannonOnForearm = false; BallThirdPerson = true; SamusMovement = true; CinemaFirstPerson = true; CinemaAvatar = true; SkipIntro = true;
            MusicVolume = 1f; SfxVolume = 1f; Haptics = 1f; DamageTaken = 1f; UnlockAll = true;
            Touch();
        }
    }

    /// <summary>
    /// What the tablet app can ask of the running game (filled in by MpGame; null members = not available yet). Every call returns a
    /// short line for the app's status bar.
    /// </summary>
    public static class MpGameActions
    {
        /// <summary>Writes the save file now (where the player stands, the inventory, the doors and switches done).</summary>
        public static Func<string> SaveGame;
        /// <summary>Back to the last save.</summary>
        public static Func<string> LoadGame;
        /// <summary>Back to the last save point / room entrance with full energy (stuck or lost).</summary>
        public static Func<string> Respawn;
        /// <summary>True when a save file exists, and its one-line description.</summary>
        public static Func<string> SaveInfo;
        /// <summary>The rooms of the current world by index (for the room list).</summary>
        public static Func<string[]> Rooms;
        /// <summary>Moves Samus to a room's entrance (testing).</summary>
        public static Func<int, string> WarpTo;
        /// <summary>One line: room, energy, missiles, beam, visor, time played.</summary>
        public static Func<string> Status;
        /// <summary>The disc found and the iteration running.</summary>
        public static Func<string> About;
    }
}
