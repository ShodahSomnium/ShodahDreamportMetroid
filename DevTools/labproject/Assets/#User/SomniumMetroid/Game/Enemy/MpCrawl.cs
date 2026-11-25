using SomniumSpace.Worlds.SomniumMetroid.Data;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// Crawling on the area's surfaces for wall walkers (decomp CWallWalker::AlignToFloor / OrientToSurfaceNormal and
    /// CWallCrawlerSwarm::FindBestSurface / UpdateBoid): the game searches the collision octree for the nearest triangle under the
    /// body; here a few rays on the world's own collision do it (down along the body's up, ahead along its forward). Everything is
    /// in game space (X right, Y forward, Z up); a body's rotation maps model +Y to its forward and model +Z to its up.
    /// </summary>
    public static class MpCrawl
    {
        /// <summary>CPhysicsActor::GravityConstant (9.81 * 2.5).</summary>
        public const float Gravity = 24.525f;

        public static Vector3 Fwd(Quaternion q) { return q * new Vector3(0f, 1f, 0f); }

        public static Vector3 Up(Quaternion q) { return q * new Vector3(0f, 0f, 1f); }

        /// <summary>A rotation whose model up is <paramref name="up"/> and model forward is <paramref name="fwd"/> (made perpendicular).</summary>
        public static Quaternion Basis(Vector3 up, Vector3 fwd)
        {
            Vector3 f = fwd - Vector3.Dot(fwd, up) * up;
            if (f.sqrMagnitude < 1e-6f) f = Vector3.Cross(up, Mathf.Abs(up.x) < 0.9f ? Vector3.right : new Vector3(0f, 1f, 0f));
            return Quaternion.LookRotation(up, f.normalized);
        }

        /// <summary>CQuaternion::ShortestRotationArcClamped applied in world space: <paramref name="q"/> turned so its up goes toward <paramref name="to"/> by at most <paramref name="maxDeg"/>.</summary>
        public static Quaternion TurnUp(Quaternion q, Vector3 to, float maxDeg)
        {
            Vector3 up = Up(q);
            float dot = Vector3.Dot(up, to);
            if (dot > 0.99999f || to.sqrMagnitude < 1e-6f) return q;
            Quaternion arc = dot < -0.999f ? Quaternion.AngleAxis(180f, Fwd(q)) : Quaternion.FromToRotation(up, to);
            return Quaternion.RotateTowards(Quaternion.identity, arc, maxDeg) * q;
        }

        /// <summary>Turns the forward toward a direction about the up (at most <paramref name="maxDeg"/>).</summary>
        public static Quaternion TurnForward(Quaternion q, Vector3 dir, float maxDeg)
        {
            Vector3 up = Up(q);
            Vector3 d = dir - Vector3.Dot(dir, up) * up;
            if (d.sqrMagnitude < 1e-8f) return q;
            Vector3 f = Fwd(q);
            float ang = Vector3.SignedAngle(f, d.normalized, up);
            return Quaternion.AngleAxis(Mathf.Clamp(ang, -maxDeg, maxDeg), up) * q;
        }

        /// <summary>A ray on the world's collision in game space; the normal is turned to face the ray's start.</summary>
        public static bool Ray(Vector3 fromGame, Vector3 dirGame, float lenGame, out Vector3 pointGame, out Vector3 normalGame)
        {
            pointGame = normalGame = Vector3.zero;
            float m = dirGame.magnitude;
            if (m < 1e-6f || lenGame <= 0f) return false;
            Vector3 dir = dirGame / m;
            RaycastHit hit;
            bool backs = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;          // the game measures to a surface from either side (CWallCrawlerSwarm::FindBestSurface: |height|)
            bool any = MpEnemy.WorldRay(MpSpace.ToUnity(fromGame), MpSpace.DirToUnity(dir), lenGame * MpSpace.Scale, out hit);
            Physics.queriesHitBackfaces = backs;
            if (!any) return false;
            pointGame = MpSpace.ToGame(hit.point);
            normalGame = MpSpace.DirToGame(hit.normal).normalized;
            if (Vector3.Dot(normalGame, dir) > 0f) normalGame = -normalGame;
            return true;
        }

        /// <summary>
        /// The nearest surface around a body (CWallCrawlerSwarm::FindBestSurface, CWallWalker::AlignToFloor): under it along -up,
        /// ahead of it (a wall to climb), and ahead and down (an edge to go over). Returns the surface's point and normal and the
        /// body's height above that plane.
        /// </summary>
        public static bool FindSurface(Vector3 pos, Vector3 up, Vector3 fwd, float reach, out Vector3 point, out Vector3 normal, out float height)
        {
            point = normal = Vector3.zero;
            height = float.MaxValue;
            Vector3 p, n;
            if (Ray(pos, -up, reach, out p, out n)) Take(pos, p, n, ref point, ref normal, ref height);
            if (Ray(pos, fwd, reach, out p, out n)) Take(pos, p, n, ref point, ref normal, ref height);
            if (height != float.MaxValue) return true;
            // nothing under or ahead (past an edge, or turned away from it): the game searches every direction, so look round
            Vector3 right = Vector3.Cross(fwd, up);
            Around[0] = fwd - up; Around[1] = -fwd - up; Around[2] = -fwd; Around[3] = right; Around[4] = -right;
            Around[5] = right - up; Around[6] = -right - up; Around[7] = up;
            for (int i = 0; i < Around.Length; i++)
                if (Ray(pos, Around[i].normalized, reach * 1.4f, out p, out n)) Take(pos, p, n, ref point, ref normal, ref height);
            return height != float.MaxValue;
        }

        private static readonly Vector3[] Around = new Vector3[8];

        private static void Take(Vector3 pos, Vector3 p, Vector3 n, ref Vector3 point, ref Vector3 normal, ref float height)
        {
            float h = Vector3.Dot(pos - p, n);
            if (h < -0.05f || h >= height) return;
            height = Mathf.Max(0f, h);
            point = p;
            normal = n;
        }

        private static readonly Collider[] Near = new Collider[8];

        /// <summary>Any world collision within a radius of a game point (CParasite::CloseToWall).</summary>
        public static bool CloseToWall(Vector3 pos, float radius)
        {
            int k = Physics.OverlapSphereNonAlloc(MpSpace.ToUnity(pos), radius * MpSpace.Scale, Near, MpEnemy.WorldMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < k; i++)
                if (MpSpace.Root != null && Near[i].transform.IsChildOf(MpSpace.Root)) return true;
            return false;
        }

        /// <summary>
        /// CParasite::UpdateJumpVelocity / CWallCrawlerSwarm::LaunchBoid: the velocity of a leap along a horizontal direction at
        /// <paramref name="speed"/> (up at half of it) bent so the arc under gravity comes down on the target; <paramref name="flat"/>
        /// scales the horizontal part as the swarm does (0.6).
        /// </summary>
        public static Vector3 LeapVelocity(Vector3 from, Vector3 target, Vector3 forward, float speed, float flat = 1f)
        {
            Vector3 v = forward * speed;
            v.z = 0.5f * speed;
            float height = target.z - from.z;
            float acc = -Gravity;
            Vector3 d = new Vector3(target.x - from.x, target.y - from.y, 0f);
            float dist = d.magnitude;
            if (dist <= 1e-5f) return v;
            d /= dist;
            float along = Vector3.Dot(d, v);
            if (along <= 1e-5f) return v;
            float t = 0f;
            bool below = height < 0f;
            float pos, neg;
            if (SolveQuadratic(acc, v.z, -height, out pos, out neg)) t = below ? neg : pos;
            if (!below) t += dist / along;
            if (t <= 1e-4f || t >= 10f) return v;
            Vector3 r = dist / t * d * flat;
            r.z = -(0.5f * acc * t - height / t);
            return r;
        }

        /// <summary>CSteeringBehaviors::SolveQuadratic (a t^2 + b t + c = 0; the leap code passes the acceleration as a, as the game does).</summary>
        private static bool SolveQuadratic(float a, float b, float c, out float r0, out float r1)
        {
            r0 = r1 = 0f;
            if (a == 0f) return false;
            float disc = b * b - 4f * a * c;
            if (disc < 0f) return false;
            float s = Mathf.Sqrt(disc);
            r0 = (-b + s) / (2f * a);
            r1 = (-b - s) / (2f * a);
            return true;
        }

        /// <summary>
        /// The root bone's motion of an animation between two times (model space; a wrap past the end goes round): the walk speed the
        /// game takes from its locomotion animations (CAnimData::AdvanceAnimation offset deltas).
        /// </summary>
        public static Vector3 RootDelta(MpCharacter ch, int anim, float t0, float t1)
        {
            if (ch == null) return Vector3.zero;
            var e = ch.EntryOf(anim);
            if (e == null || e.Anims.Count == 0) return Vector3.zero;
            var a = ch.Anim(e.Anims[0]);
            if (a == null || a.Duration <= 0f) return Vector3.zero;
            int root = a.Root > 0 ? a.Root : MpRig.RootSegment;
            if (t1 >= t0) return RootAt(a, root, t1) - RootAt(a, root, t0);
            return RootAt(a, root, a.Duration) - RootAt(a, root, t0) + RootAt(a, root, t1) - RootAt(a, root, 0f);
        }

        /// <summary>The average root speed of an animation (units per second at playback rate 1).</summary>
        public static float RootSpeed(MpCharacter ch, int anim)
        {
            if (ch == null) return 0f;
            var e = ch.EntryOf(anim);
            if (e == null || e.Anims.Count == 0) return 0f;
            var a = ch.Anim(e.Anims[0]);
            if (a == null || a.Duration <= 0f) return 0f;
            return RootDelta(ch, anim, 0f, a.Duration).magnitude / a.Duration;
        }

        private static readonly float[] Xyz = new float[3];

        private static Vector3 RootAt(MpAnimation a, int root, float t)
        {
            if (!a.Offset(root, Mathf.Clamp(t, 0f, a.Duration), Xyz)) return Vector3.zero;
            return new Vector3(Xyz[0], Xyz[1], Xyz[2]);
        }
    }
}
