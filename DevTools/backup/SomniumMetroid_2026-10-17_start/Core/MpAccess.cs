using System;

namespace SomniumSpace.Worlds.SomniumMetroid
{
    /// <summary>
    /// The private-testing gate (the BO2 Dreamport's Bo2Access, ported). While the Dreamport is shown only to the
    /// team, the world starts only for the Somnium accounts listed in <see cref="Whitelist"/>. Everyone else sees
    /// NOTHING of it: just the plain floor, no room, no panel, no text, no music; the disc image is not looked for or read and no
    /// setting is touched (MpGame starts nothing before the verdict). (Hiding other players from each other - the BO2 isolation -
    /// is not ported yet: this Dreamport has no networking, every player runs the game on their own.)
    ///
    /// The check (Core/Unity/MpAccessCheck.cs, the Unity/Bridge half of this class) reads the local player's
    /// <c>Properties.NickName</c> (the username on the name tag) and <c>Properties.Id</c> from the Bridge (proven members,
    /// fusionapi/known_good.txt). The comparison ignores letter case and surrounding spaces. The Bridge fills the local
    /// player a moment after the world starts, so the check waits for it, with a warning in the log after
    /// <see cref="NameTimeout"/> seconds. Nothing starts while it waits, so the gate fails closed. The editor (no client)
    /// is always let in. Player.log gets one line with the verdict, the username and the id, so an id can be copied into
    /// the list as well. This half is pure C# (the offline harness runs <see cref="Matches(string, string, string[])"/>).
    /// </summary>
    public static partial class MpAccess
    {
        /// <summary>
        /// Who may play. Add Somnium usernames (as on the name tag; any letter case) or Somnium user ids (Player.log shows
        /// each player's at start: "Access: ... (id ...)"). Leave the list empty to open the world to everyone.
        /// </summary>
        public static readonly string[] Whitelist =
        {
            "Shodah",
            "Wildy",
            "Meloz",
            "ꨄ Rage Quit ꨄ",
            "denevraut",   // 2026-10-05: added at the user's request   // 2026-10-04: added at the user's request (exact name-tag text; if it does not match, her Player.log "Access:" line gives the exact name and id)
        };

        /// <summary>After this many seconds without a username from the client, the log says the check is still waiting.</summary>
        public const float NameTimeout = 30f;

        public enum Verdict
        {
            Pending,
            Allowed,
            Denied,
        }

        public static Verdict State { get; private set; }

        /// <summary>The local player's username and Somnium id as the client reported them (null before, or in the editor).</summary>
        public static string Nick { get; private set; }

        public static string UserId { get; private set; }

        public static bool Allowed
        {
            get { return State == Verdict.Allowed; }
        }

        /// <summary>True when the username or the id is on <see cref="Whitelist"/> (letter case and spaces ignored).</summary>
        public static bool Matches(string nick, string id)
        {
            return Matches(nick, id, Whitelist);
        }

        /// <summary>The rule of <see cref="Matches(string, string)"/> against any list (blank entries never match).</summary>
        public static bool Matches(string nick, string id, string[] list)
        {
            if (list == null)
            {
                return false;
            }

            foreach (string entry in list)
            {
                string e = entry != null ? entry.Trim() : "";
                if (e.Length == 0)
                {
                    continue;
                }

                if ((!string.IsNullOrEmpty(nick) && string.Equals(e, nick.Trim(), StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(id) && string.Equals(e, id.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }

        public static void Reset()
        {
            State = Verdict.Pending;
            Nick = null;
            UserId = null;
        }

        /// <summary>The verdict once the username is known, with its one Player.log line.</summary>
        private static void Decide()
        {
            string who = Nick + " (id " + (string.IsNullOrEmpty(UserId) ? "unknown" : UserId) + ")";
            State = Matches(Nick, UserId) ? Verdict.Allowed : Verdict.Denied;
            MpLog.Info("Access: " + (State == Verdict.Allowed
                ? who + " is on the whitelist: starting the Dreamport"
                : who + " is not on the whitelist: nothing of the Dreamport is loaded or shown (other players are not hidden by this Dreamport yet)"));
        }
    }
}
