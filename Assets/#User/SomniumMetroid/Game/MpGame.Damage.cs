using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// Damage to Samus (CPlayer::ApplyDamage / DoSfxEffects): energy goes down by the hit (times the player's own "damage taken"
    /// option), a 0.5 s immunity follows each hit, the suit's hit sound plays with Samus's voice by the size of the hit (light up to
    /// 15, medium up to 30, heavy above; the voice at most every 3-4 s), the HUD and both controllers react, and the near-death
    /// warning sounds under 30 energy. Enemies, shots, radial damage and harmful triggers call <see cref="DamagePlayer"/>.
    /// </summary>
    public sealed partial class MpGame
    {
        private float _immuneT, _voiceT, _hurtFlashT, _nearDeathT;
        public bool God;        // lab: no damage
        public float LastDamageT = -10f;
        public Vector3 LastDamageFrom;

        /// <summary>Co-op, the master: damage for another player (their id, damage, from, ignore immunity); true when sent.</summary>
        public static System.Func<int, float, Vector3?, bool, bool> RemoteHurt;

        /// <summary>A hit on Samus; false while immune (or dead).</summary>
        public bool DamagePlayer(float damage, Vector3? fromGame, bool ignoreImmunity = false)
        {
            // co-op, the master: an enemy thinking about another player hurts that player (MpCoop sends it to them)
            if (MpEnemy.FocusPlayer >= 0) return RemoteHurt != null && RemoteHurt(MpEnemy.FocusPlayer, damage, fromGame, ignoreImmunity);
            if (damage <= 0f || State == null || State.Energy <= 0f || _warping) return false;
            if (_immuneT > 0f && !ignoreImmunity) return false;
            _immuneT = 0.5f;
            float d = God ? 0f : damage * Mathf.Max(0f, MpSettings.DamageTaken);
            State.Energy = Mathf.Max(0f, State.Energy - d);
            LastDamageT = Time.time;
            if (fromGame.HasValue) LastDamageFrom = fromGame.Value;
            PlayUiSfx(MpSfx.SuitHit);
            if (_voiceT <= 0f)
            {
                PlayUiSfx(damage > 30f ? MpSfx.HitHeavy : damage > 15f ? MpSfx.HitMed : MpSfx.HitLight);
                _voiceT = Random.Range(3f, 4f);
            }

            _hurtFlashT = 0.35f;
            if (MpXr.Present && MpSettings.Haptics > 0f)
            {
                float amp = Mathf.Clamp01(0.35f + damage / 40f) * MpSettings.Haptics;
                MpXr.Pulse(true, amp, 0.15f);
                MpXr.Pulse(false, amp, 0.15f);
            }

            if (Hud != null) Hud.Hurt(Mathf.Clamp01(damage / 30f));
            return true;
        }

        private void TickDamage(float dt)
        {
            _immuneT = Mathf.Max(0f, _immuneT - dt);
            _voiceT = Mathf.Max(0f, _voiceT - dt);
            _hurtFlashT = Mathf.Max(0f, _hurtFlashT - dt);
            if (State.Energy > 0f && State.Energy < 30f)
            {
                _nearDeathT -= dt;
                if (_nearDeathT <= 0f) { PlayUiSfx(MpSfx.NearDeath); _nearDeathT = 6f; }
            }
            else _nearDeathT = 0f;
        }
    }
}
