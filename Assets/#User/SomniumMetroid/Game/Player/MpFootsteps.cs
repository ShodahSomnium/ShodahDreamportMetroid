using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// Samus's footsteps, jump and landing sounds on the player's own avatar (CPlayer::UpdateFootstepSounds, SetMoveState(Jump),
    /// AcceptScriptMsg(OnFloor)). Walking: a step every 0.85 s at a crawl down to 0.375 s at full speed, left and right in turn, the
    /// sound by the floor's material under the feet (skLeftStepSounds / skRightStepSounds), volume (89 + 38 x speed) / 127. A jump
    /// plays SFXsam_b_jump_01; landing after 0.3 s in the air plays the material's soft landing (the hard one past sqrt(2 x 35 x 30)
    /// units/s of fall), volume (95 + 1.6 x fall speed) / 127. The avatar is measured as it moves (the client moves it).
    /// </summary>
    public static class MpFootsteps
    {
        // EMaterialTypes 0..23 (the collision material bits): the game's tables, 0xFFFF = none
        private static readonly int[] LeftStep = { -1, 1465, 1495, 1570, 1619, -1, 1493, 1553, 1497, 2183, 1689, 1594, 1585, -1, 1577, 1495, 1495, 1497, -1, -1, -1, -1, 1529, 1577 };
        private static readonly int[] RightStep = { -1, 1466, 1496, 1571, 1620, -1, 1494, 1554, 1499, 2184, 1690, 1595, 1586, -1, 1578, 1496, 1496, 1499, -1, -1, -1, -1, 1530, 1578 };
        private static readonly int[] LandSoft = { -1, 1508, 1490, 1569, 1624, -1, 1507, 1542, 1472, 2190, 1684, 1592, 1579, -1, 1569, 1490, 1490, 1472, -1, -1, -1, -1, 1531, 1573 };
        private static readonly int[] LandHard = { -1, 1617, 1611, 1607, 1626, -1, 1608, 1614, 1615, 2263, 1686, 1616, 1612, -1, 1607, 1611, 1611, 1615, -1, -1, -1, -1, 1618, 1613 };
        public const int Jump = 1471;                 // SFXsam_b_jump_01 (the first jump; _00 is the space jump's second)
        public const int DefaultStep = 1495;          // metal: the frigate's floors when no material is found

        private static Vector3 _prev;
        private static bool _havePrev, _air, _left = true;
        private static float _timer, _airT, _fallSpeed;
        public static int Steps, Jumps, Landings;     // for the labs and the log

        /// <summary>Every frame of play (held: morphed, warping, a lab stand-in - nothing is played).</summary>
        public static void Tick(MpGame g, bool held)
        {
            Transform root = MpPlayer.Root;
            float dt = Time.deltaTime;
            if (g == null || root == null || held || dt <= 0f) { _havePrev = false; _air = false; _timer = 0f; return; }
            Vector3 p = root.position;
            if (!_havePrev) { _prev = p; _havePrev = true; return; }
            Vector3 v = (p - _prev) / dt;
            _prev = p;
            if (v.magnitude > 40f) { _air = false; _timer = 0f; return; }     // a teleport, the world sliding
            RaycastHit floor;
            bool grounded = Floor(p, out floor);
            float unitsPerM = 1f / MpMovement.UnitM;

            if (!grounded)
            {
                if (!_air)
                {
                    _air = true;
                    _airT = 0f;
                    if (v.y > 1f) { g.PlaySfxAt(Jump, MpSpace.ToGame(p), 1f, false, 30f); Jumps++; }
                }

                _airT += dt;
                _fallSpeed = Mathf.Max(0f, -v.y * unitsPerM);
                _timer = 0f;
                return;
            }

            if (_air)
            {
                _air = false;
                if (_airT > 0.3f)
                {
                    float hard = Mathf.Sqrt(35f * 2f * 30f);
                    int id = Pick(_fallSpeed < hard ? LandSoft : LandHard, floor, -1);
                    float vol = Mathf.Clamp(_fallSpeed * 1.6f + 95f, 95f, 127f) / 127f;
                    if (id >= 0) { g.PlaySfxAt(id, MpSpace.ToGame(p), vol, false, 30f); Landings++; }
                }
            }

            // walking: the step rate follows the speed against Samus's top speed
            float speed = new Vector2(v.x, v.z).magnitude;
            float frac = Mathf.Min(speed / Mathf.Max(0.5f, MpMovement.TargetSpeed * 0.75f), 1f);
            if (frac <= 0.05f) { _timer = 0f; _left = true; return; }
            _timer += dt;
            float delay = (0.375f - 0.85f) * frac + 0.85f;
            if (_timer <= delay) return;
            _timer = 0f;
            int step = Pick(_left ? LeftStep : RightStep, floor, _left ? DefaultStep : DefaultStep + 1);
            g.PlaySfxAt(step, MpSpace.ToGame(p), (frac * 38f + 89f) / 127f, false, 30f);
            _left = !_left;
            Steps++;
        }

        private static readonly RaycastHit[] Hits = new RaycastHit[8];

        /// <summary>The game floor just under the feet (an area's shot collider carries the per-triangle material).</summary>
        private static bool Floor(Vector3 feet, out RaycastHit best)
        {
            best = default(RaycastHit);
            int n = Physics.RaycastNonAlloc(feet + Vector3.up * 0.2f, Vector3.down, Hits, 0.45f, ~0, QueryTriggerInteraction.Ignore);
            bool any = false, game = false;
            float bd = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                bool isGame = MpAreaView.MaterialAt(Hits[i]) != 0;
                // prefer a hit with a game material; otherwise any floor (the morph ball stand, a lift)
                if ((isGame && !game) || ((isGame == game) && Hits[i].distance < bd))
                {
                    best = Hits[i];
                    bd = Hits[i].distance;
                    game |= isGame;
                    any = true;
                }
            }

            return any;
        }

        /// <summary>CPlayer::SfxIdFromMaterial: the last material bit (of 24) with a sound wins.</summary>
        private static int Pick(int[] table, RaycastHit floor, int def)
        {
            uint m = MpAreaView.MaterialAt(floor);
            int id = def;
            for (int i = 0; i < table.Length; i++) if ((m & (1u << i)) != 0 && table[i] >= 0) id = table[i];
            return id;
        }
    }
}
