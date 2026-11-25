namespace SomniumSpace.Worlds.SomniumMetroid.Data
{
    /// <summary>
    /// The Targeting tweak (CTWK "Targeting", decomp CTweakTargeting.cpp, read field by field in its order): the combat reticle's
    /// scales, colours (4 floats each), screen clamps and the per-beam angles of the outer beam squares and the charge gauge.
    /// Only the fields up to the charge gauge pulse are kept; angles are in radians as the game converts them.
    /// </summary>
    public sealed class MpTweakTargeting
    {
        public float LockConfirmScale, SeekerScale, SeekerAngleSpeed, XrayRetAngleSpeed;
        public float TargetFlowerScale;
        public float[] TargetFlowerColor;
        public float LockonDuration, InnerBeamScale;
        public float[][] InnerBeamColor = new float[4][];
        public float ChargeGaugeOvershootOffset, ChargeGaugeOvershootDuration, OuterBeamSquaresScale;
        public float[] OuterBeamSquareColor;
        public float[][] OuterBeamSquareAngles;     // [beam][square 0..8]
        public float[] ChargeGaugeAngles;            // [beam]
        public float ChargeGaugeScale;
        public float[] ChargeGaugeNonFullColor;
        public int ChargeTickCount;
        public float ChargeTickAnglePitch;           // radians, negated as the game does
        public float[] LockConfirmColor, SeekerColor;
        public float LockConfirmClampMin, LockConfirmClampMax, TargetFlowerClampMin, TargetFlowerClampMax, SeekerClampMin, SeekerClampMax;
        public float MissileBracketClampMin, MissileBracketClampMax, InnerBeamClampMin, InnerBeamClampMax, ChargeGaugeClampMin, ChargeGaugeClampMax;
        public float[] ChargeGaugePulseColorHigh, ChargeGaugePulseColorLow, CrosshairsColor;
        public float FullChargeFadeDuration, CrosshairsScaleDur, ChargeGaugePulsePeriod;

        private static float[] Color(MpReader r) { return new[] { r.F32(), r.F32(), r.F32(), r.F32() }; }
        private static void Floats(MpReader r, int n) { r.Skip(4 * n); }
        private static float Rad(float deg) { return deg * 0.017453292f; }

        public static MpTweakTargeting Read(byte[] b)
        {
            var r = new MpReader(b);
            var t = new MpTweakTargeting();
            r.S32();                                   // target radius mode
            Floats(r, 3);                              // curr lock-on exit / enter / switch
            t.LockConfirmScale = r.F32();
            Floats(r, 3);                              // next lock-on enter / exit / switch
            t.SeekerScale = r.F32();
            t.SeekerAngleSpeed = r.F32();
            t.XrayRetAngleSpeed = r.F32();
            Floats(r, 6);                              // x30, x3c (vectors)
            Floats(r, 6);                              // x48, x4c, orbit point z offset / in / out, x5c
            Floats(r, 12);                             // x60, x6c, x78, x84 (vectors)
            Floats(r, 8);                              // x90 .. xac
            Color(r);                                  // thermal reticule colour
            t.TargetFlowerScale = r.F32();
            t.TargetFlowerColor = Color(r);
            Floats(r, 4);                              // missile bracket duration, scale start / end / duration
            Color(r);                                  // missile bracket colour
            t.LockonDuration = r.F32();
            t.InnerBeamScale = r.F32();
            for (int i = 0; i < 4; i++) t.InnerBeamColor[i] = Color(r);
            t.ChargeGaugeOvershootOffset = r.F32();
            t.ChargeGaugeOvershootDuration = r.F32();
            t.OuterBeamSquaresScale = r.F32();
            t.OuterBeamSquareColor = Color(r);
            int beams = r.S32();
            t.OuterBeamSquareAngles = new float[beams][];
            for (int i = 0; i < beams; i++)
            {
                int n = r.S32();
                t.OuterBeamSquareAngles[i] = new float[n];
                for (int j = 0; j < n; j++) t.OuterBeamSquareAngles[i][j] = Rad(r.F32());
            }

            int g = r.S32();
            t.ChargeGaugeAngles = new float[g];
            for (int i = 0; i < g; i++) t.ChargeGaugeAngles[i] = Rad(r.F32());
            t.ChargeGaugeScale = r.F32();
            t.ChargeGaugeNonFullColor = Color(r);
            t.ChargeTickCount = r.S32();
            t.ChargeTickAnglePitch = -Rad(r.F32());
            Floats(r, 2);                              // lock fire scale / duration
            Color(r);                                  // lock fire colour
            Floats(r, 2);                              // lock dagger scale start / end
            Color(r);                                  // lock dagger colour
            Floats(r, 3);                              // lock dagger angles
            t.LockConfirmColor = Color(r);
            t.SeekerColor = Color(r);
            t.LockConfirmClampMin = r.F32(); t.LockConfirmClampMax = r.F32();
            t.TargetFlowerClampMin = r.F32(); t.TargetFlowerClampMax = r.F32();
            t.SeekerClampMin = r.F32(); t.SeekerClampMax = r.F32();
            t.MissileBracketClampMin = r.F32(); t.MissileBracketClampMax = r.F32();
            t.InnerBeamClampMin = r.F32(); t.InnerBeamClampMax = r.F32();
            t.ChargeGaugeClampMin = r.F32(); t.ChargeGaugeClampMax = r.F32();
            Floats(r, 4);                              // lock fire / lock dagger clamps
            Floats(r, 4);                              // grapple select scale, scale, clamp min / max
            Color(r); Color(r); Color(r);              // grapple point colours
            r.F32();                                   // grapple min clamp scale
            t.ChargeGaugePulseColorHigh = Color(r);
            t.FullChargeFadeDuration = r.F32();
            Color(r);                                  // orbit point colour
            t.CrosshairsColor = Color(r);
            t.CrosshairsScaleDur = r.F32();
            r.Bool();                                  // draw orbit point
            t.ChargeGaugePulseColorLow = Color(r);
            t.ChargeGaugePulsePeriod = r.F32();
            return t;
        }
    }
}
