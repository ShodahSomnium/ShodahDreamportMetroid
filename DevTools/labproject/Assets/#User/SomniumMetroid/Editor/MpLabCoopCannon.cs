using System.Collections;
using SomniumSpace.Worlds.SomniumMetroid.Game;
using UnityEditor;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Editor
{
    /// <summary>
    /// Lab scenario "coopcannon" (DevTools/coop.sh [port] coopcannon): the other player's arm cannon on their avatar. The helper joins
    /// the master's frigate; the master's head pose goes over the lab link (in the client, Somnium syncs the avatar) and the helper
    /// stands the SDK's RPM template avatar there. The master then works its own cannon - idle, a full charge and the charged
    /// shot, each beam (the change and a shot), a missile and its reload, and the cannon held in the hand (as in VR) - and the helper
    /// photographs its copy on the stand-in avatar each time: coopcannon_*.png. Checks: the cannon shows, follows the beam, charges,
    /// replays each press and missile, and the stand-in's right hand is hidden in hand mode.
    /// </summary>
    public sealed partial class MpLabRunner
    {
        private bool _headOut;
        private Transform _remoteHead, _labHand;
        private Animator _standIn;
        private bool _ccFire, _ccMissile;

        partial void ExtraScenarioCoopCannon(string scenario, ref IEnumerator run)
        {
            if (scenario != "coopcannon") return;
            run = MpLabBoot.Arg("-mpcoop", "master") == "helper" ? CannonHelper() : CannonMaster();
        }

        private void LateUpdate()
        {
            if (_link == null) return;
            if (_headOut && _cam != null) _link.SendHead(_cam.transform.position, _cam.transform.rotation);
            if (_labHand != null && G != null && G.Cannon != null) _labHand.SetPositionAndRotation(G.Cannon.Root.position, G.Cannon.Root.rotation);
            if (_remoteHead != null && _link.PeerPoseSeen)
            {
                Vector3 p;
                Quaternion q;
                _link.PeerHead(out p, out q);
                _remoteHead.SetPositionAndRotation(p, q);
                if (_standIn != null)
                {
                    // the stand-in avatar under its head: turned to the head's heading, its head bone on the head
                    Transform root = _standIn.transform;
                    Vector3 f = Vector3.ProjectOnPlane(q * Vector3.forward, Vector3.up);
                    if (f.sqrMagnitude > 1e-4f) root.rotation = Quaternion.LookRotation(f.normalized, Vector3.up);
                    Transform hb = _standIn.GetBoneTransform(HumanBodyBones.Head);
                    if (hb != null) root.position += p - hb.position;
                }
            }
        }

        // ------------------------------------------------------------------ the master: works its cannon

        private IEnumerator Step(string note, string ok)
        {
            _link.Note(note);
            yield return Note(ok, 30f);
        }

        private string MpCoopLabSlot() { return G.LabOwnSlot != null ? (G.LabOwnSlot[MpNetWire.PlayerSlot.AtPresses] & 0xFFFF).ToString() : "-"; }

        private IEnumerator CannonMaster()
        {
            G.God = true;
            _cam.enabled = true;
            _cam.tag = "MainCamera";
            MpGame.LabFocus = G.PlayerGame;
            yield return SkipCinema();
            yield return SettleRooms();
            Aim(G.PlayerGame + new Vector3(0f, 0f, MpSpace.SamusEyeHeight), 0f, -35f);      // up, at the hangar roof: no lock or switch is shot (a cutscene would hold the input)
            MpGame.LabAnchor = _cam.transform;
            _headOut = true;
            MpGame.LabFire = g => _ccFire;
            MpGame.LabMissile = g => { bool m = _ccMissile; _ccMissile = false; return m; };
            Log("coopcannon master: waiting for the helper");
            yield return Wait(() => PeerJoinedIn(G.CurrentPak), 600f, "the helper to join");
            if (_failed) yield break;
            yield return Seconds(4f);
            yield return Step("idle", "idle-ok");

            // a full charge, then the charged shot (a hangar cutscene starting meanwhile is skipped: it would hold the input)
            yield return SkipCinema();
            _ccFire = true;
            float held = 0f;
            while (held < 2.2f)
            {
                if (MpCinematics.Active) { MpCinematics.Skip(); _ccFire = false; yield return Frames(2); held = 0f; _ccFire = true; }
                held += Time.deltaTime;
                yield return null;
            }
            Log("coopcannon master: charge " + G.Cannon.Charge.ToString("0.00") + ", shots fired " + G.Shots.Fired + ", input off " + MpCinematics.InputDisabled + ", cinema " + MpCinematics.Active + ", tablet " + MpTabletApp.IsOpen + ", energy " + G.State.Energy + ", morphed " + G.Ball.Morphed + ", visor " + G.State.Visor + ", holstered " + G.Cannon.Holstered + ", changing " + G.Cannon.Changing + ", charge beam " + G.State.HasCharge + ", vr " + MpXr.Present);
            yield return Step("charging", "charging-ok");
            _ccFire = false;
            yield return Frames(2);
            yield return Step("charged", "charged-ok");
            yield return Seconds(1f);

            // every beam: the change, then a shot
            foreach (int b in new[] { MpPlayerState.BeamWave, MpPlayerState.BeamIce, MpPlayerState.BeamPlasma, MpPlayerState.BeamPower })
            {
                G.Cannon.ChangeBeam(b);
                yield return Frames(6);
                yield return Step("changing " + b, "changing-ok");
                yield return Seconds(1.2f);
                yield return Step("beam " + b, "beam-ok");
                if (MpCinematics.Active) { Log("coopcannon master: a cutscene runs (input off " + MpCinematics.InputDisabled + ") - skipped"); yield return SkipCinema(); }
                _ccFire = true;
                yield return Frames(2);
                _ccFire = false;
                Log("coopcannon master: fired beam " + b + " - shots fired " + G.Shots.Fired + ", my slot presses " + (_link != null ? "" : "") + MpCoopLabSlot());
                yield return Step("fired " + b, "fired-ok");
                yield return Seconds(0.8f);
            }

            // a missile: the hatch, the shot, the reload
            if (MpCinematics.Active) yield return SkipCinema();
            _ccMissile = true;
            yield return Frames(3);
            yield return Step("missile", "missile-ok");
            yield return Seconds(0.9f);
            yield return Step("reload", "reload-ok");
            yield return Seconds(1f);

            // the cannon in the hand, as in VR
            _labHand = new GameObject("Lab master hand").transform;
            MpGame.LabAnchor = _labHand;
            MpGame.LabHandMode = true;
            yield return Seconds(1f);
            yield return Step("hand", "hand-ok");
            _link.Note("bye");
            yield return Seconds(2f);
        }

        // ------------------------------------------------------------------ the helper: photographs the copy

        private Camera _pic;

        private void FrameRemote(float side)
        {
            if (_pic == null)
            {
                _pic = new GameObject("Lab picture camera").AddComponent<Camera>();
                _pic.CopyFrom(_cam);
                _pic.enabled = false;
                _pic.tag = "Untagged";
            }

            Vector3 p = _remoteHead.position;
            Quaternion q = _remoteHead.rotation;
            Vector3 fwd = Vector3.ProjectOnPlane(q * Vector3.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.5f) fwd = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 look = p + fwd * 0.45f + right * 0.15f - Vector3.up * 0.35f;
            _pic.transform.position = p + fwd * 1.5f + right * side - Vector3.up * 0.1f;
            _pic.transform.rotation = Quaternion.LookRotation(look - _pic.transform.position, Vector3.up);
        }

        private void Picture(string name, float side = 1.1f)
        {
            FrameRemote(side);
            if (G.Hud != null) G.Hud.SetVisible(false);
            MpShaderGlobals.Apply(_pic);
            Shoot(_pic, "coopcannon_" + name + ".png", 960, 720);
        }

        private IEnumerator CannonHelper()
        {
            G.God = true;
            _cam.enabled = true;
            _cam.tag = "MainCamera";
            MpGame.LabFocus = G.PlayerGame;
            yield return SkipCinema();
            Log("coopcannon helper: waiting to join");
            yield return Wait(() => MpCoop.Helper && MpScripts.Follower && !G.Warping, 600f, "the join");
            if (_failed) yield break;
            yield return SettleRooms();

            // this helper's own cannon (by its camera) out of the pictures
            _cam.transform.position += Vector3.up * 25f;

            // the master's avatar, as Somnium would show it: the SDK's template avatar under the master's head
            _remoteHead = new GameObject("Lab remote head").transform;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SomniumSpaceTemplates/ItemsTemplate/Models/Avatars/RPM Men.fbx");
            if (prefab != null)
            {
                GameObject man = Object.Instantiate(prefab);
                man.name = "Lab remote avatar";
                _standIn = man.GetComponentInChildren<Animator>();
                if (_standIn != null && !_standIn.isHuman) _standIn = null;
            }

            Log("coopcannon helper: stand-in avatar " + (_standIn != null ? "'" + _standIn.name + "' (humanoid)" : "missing - the cannon floats at the head"));
            MpRemoteCannons.LabAnchorOf = (r, onHead) => onHead || _standIn == null ? _remoteHead : _standIn.GetBoneTransform(HumanBodyBones.RightHand);
            MpRemoteCannons.LabForceHide = true;

            yield return Note("idle", 120f);
            yield return Seconds(0.5f);
            Check("cannon-shown", MpRemoteCannons.LabShown > 0 && MpRemoteCannons.LabLast != null, "remote cannons " + MpRemoteCannons.LabShown);
            if (MpRemoteCannons.LabLast == null) yield break;
            MpArmCannon c = MpRemoteCannons.LabLast;
            float d = (c.Root.position - _remoteHead.position).magnitude;
            Check("cannon-at-avatar", d < 0.9f, "cannon " + d.ToString("0.00") + " m from the avatar's head");
            Picture("idle");
            Picture("idle_side", -1.2f);
            _link.Note("idle-ok");

            yield return Note("charging", 60f);
            yield return Seconds(0.3f);
            Check("cannon-charging", c.Charge > 0.5f, "charge " + c.Charge.ToString("0.00"));
            Picture("charging");
            _link.Note("charging-ok");
            yield return Note("charged", 60f);
            yield return Seconds(0.18f);
            Picture("charged_shot");
            Check("cannon-charge-released", c.Charge < 0.5f, "charge " + c.Charge.ToString("0.00"));
            _link.Note("charged-ok");

            for (int i = 0; i < 4; i++)
            {
                yield return Note("changing", 60f);
                yield return Seconds(0.25f);
                Picture("change_" + i);
                _link.Note("changing-ok");
                yield return Note("beam", 60f);
                int b = int.Parse(_lastNote.Substring(5));
                yield return Seconds(0.3f);
                Check("cannon-beam-" + b, c.State.Beam == b && !c.Changing, "shows beam " + c.State.Beam);
                Picture("beam_" + b);
                _link.Note("beam-ok");
                int presses = MpRemoteCannons.LabPresses;
                yield return Note("fired", 60f);
                yield return Wait(() => MpRemoteCannons.LabPresses > presses, 3f, "the press replayed");
                Log("coopcannon helper: presses " + MpRemoteCannons.LabPresses + " (was " + presses + "), the master's slot presses " + (Peer() != null ? (Peer().Ints[MpNetWire.PlayerSlot.AtPresses] & 0xFFFF).ToString() : "-"));
                yield return Frames(4);
                Picture("fired_" + b);
                Check("cannon-fired-" + b, MpRemoteCannons.LabPresses > presses, null);
                _link.Note("fired-ok");
            }

            int ms = MpRemoteCannons.LabMissiles;
            yield return Note("missile", 60f);
            yield return Wait(() => MpRemoteCannons.LabMissiles > ms, 3f, "the missile replayed");
            yield return Frames(6);
            Picture("missile");
            Check("cannon-missile", MpRemoteCannons.LabMissiles > ms, null);
            _link.Note("missile-ok");
            yield return Note("reload", 60f);
            Picture("missile_reload");
            _link.Note("reload-ok");

            yield return Note("hand", 60f);
            yield return Seconds(1.2f);
            Transform hand = _standIn != null ? _standIn.GetBoneTransform(HumanBodyBones.RightHand) : null;
            if (hand != null)
            {
                float dh = (c.Root.position - hand.position).magnitude;
                Check("cannon-in-hand", dh < 0.05f, dh.ToString("0.000") + " m from the stand-in's right hand");
            }

            Picture("hand");
            Picture("hand_side", -1.2f);
            _link.Note("hand-ok");
            yield return Note("bye", 60f);
            Log("coopcannon helper: presses replayed " + MpRemoteCannons.LabPresses + ", missiles " + MpRemoteCannons.LabMissiles + "; checks " + _checks + ", failed " + _checkFails);
        }
    }
}
