using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Editor
{
    /// <summary>
    /// Lab scenario "tablet" (-mplab tablet): the "METROID PRIME" tablet app against a stand-in of the client's tablet. The scenario itself is
    /// LabStubs/MpLabTabletRig.cs, in the SomniumMetroid.LabStubs assembly next to the stand-in client types (it needs Unity UI, TextMeshPro and
    /// XRI, which this editor assembly does not reference); it is reached here by name, so this assembly needs no reference to it.
    /// Its checks are logged as "LAB: CHECK ok|FAIL"; a failure ends the scenario with an exception, which the runner turns into exit code 1.
    /// </summary>
    public static class MpLabTablet
    {
        private const string RigType = "SomniumSpace.Worlds.SomniumMetroid.LabStubs.MpLabTabletRig";

        public static IEnumerator Run(MpLabRunner runner)
        {
            Type rig = null;

            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                rig = a.GetType(RigType, false);
                if (rig != null) break;
            }

            MethodInfo scenario = rig != null ? rig.GetMethod("Scenario", BindingFlags.Public | BindingFlags.Static) : null;

            if (scenario == null)
            {
                throw new InvalidOperationException(RigType + ".Scenario not found: is the SomniumMetroid.LabStubs assembly (Editor/LabStubs) compiled?");
            }

            Action<string> log = MpLabRunner.Log;
            Action<Camera, string> shoot = (cam, file) => runner.Shoot(cam, file, 1280, 720);
            return (IEnumerator)scenario.Invoke(null, new object[] { log, shoot });
        }
    }
}
