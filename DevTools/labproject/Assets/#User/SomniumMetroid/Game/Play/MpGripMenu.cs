using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// Picking a visor (left hand) or beam (right hand) the way the GameCube D-pad / C-stick does, by moving the hand: hold the grip,
    /// move the hand up / right / down / left (at least <see cref="Reach"/> metres, measured in the head's facing so it works whichever
    /// way the player turns), and the item in that direction is chosen at once with a tick on the controller; let go when done.
    /// Directions are the game's menu layout (CHudVisorBeamMenu::UpdateMenuWidgetTransform): visors up Combat, right X-Ray, down
    /// Thermal, left Scan; beams up Power, right Wave, down Ice, left Plasma.
    /// </summary>
    public sealed class MpGripSelector
    {
        public const float Reach = 0.07f, Keep = 0.045f;
        public readonly bool Right;
        public bool Open;
        public int Hover = -1;
        private Vector3 _start;

        // direction (0 up, 1 right, 2 down, 3 left) -> item
        private static readonly int[] VisorAt = { MpPlayerState.VisorCombat, MpPlayerState.VisorXRay, MpPlayerState.VisorThermal, MpPlayerState.VisorScan };
        private static readonly int[] BeamAt = { MpPlayerState.BeamPower, MpPlayerState.BeamWave, MpPlayerState.BeamIce, MpPlayerState.BeamPlasma };

        public MpGripSelector(bool right) { Right = right; }

        /// <summary>Returns the item chosen this frame, or -1.</summary>
        public int Tick(MpHand hand, Transform handT, Transform head)
        {
            if (handT == null || head == null) { Open = false; Hover = -1; return -1; }
            if (hand.GripPressed)
            {
                Open = true;
                Hover = -1;
                _start = handT.position;
                MpXr.Pulse(Right, 0.15f, 0.02f);
            }

            if (!hand.GripHeld)
            {
                Open = false;
                Hover = -1;
                return -1;
            }

            Vector3 fwd = head.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 d = handT.position - _start;
            float x = Vector3.Dot(d, right), y = d.y;
            float len = Mathf.Sqrt(x * x + y * y);
            int dir = Mathf.Abs(y) >= Mathf.Abs(x) ? (y > 0f ? 0 : 2) : (x > 0f ? 1 : 3);
            if (len < (Hover >= 0 ? Keep : Reach)) return -1;
            if (dir == Hover) return -1;
            Hover = dir;
            MpXr.Pulse(Right, 0.5f, 0.04f);
            return Right ? BeamAt[dir] : VisorAt[dir];
        }

        /// <summary>The HUD's highlighted item while the grip is held.</summary>
        public int HoverItem { get { return Hover < 0 ? -1 : Right ? BeamAt[Hover] : VisorAt[Hover]; } }
    }
}
