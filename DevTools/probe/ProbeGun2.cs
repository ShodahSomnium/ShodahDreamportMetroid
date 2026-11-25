using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumMetroid.Data;

static partial class Probe
{
    static partial void ExtraImpl9(string cmd, string[] args, ref int result)
    {
        switch (cmd)
        {
            case "gunTweak":
            {
                var t = MpTweakGun.Read(Assets.Read(ParseId("PlayerGun")));
                Console.WriteLine("look up " + t.UpLookAngle + " down " + t.DownLookAngle + " transform " + t.GunTransformTime + " holster " + t.GunHolsterTime + " notFiring " + t.GunNotFiringTime + " extend " + t.GunExtendDistance + " gunPos " + t.GunPosX + "," + t.GunPosY + "," + t.GunPosZ);
                Console.WriteLine("missile " + D(t.Missile) + " bomb " + D(t.Bomb) + " pb " + D(t.PowerBomb));
                string[] names = { "power", "ice", "wave", "plasma", "phazon" };
                for (int i = 0; i < 5; i++) Console.WriteLine(names[i] + ": cooldown " + t.CoolDown[i] + " normal " + D(t.Normal[i]) + " charged " + D(t.Charged[i]) + " combo " + D(t.Combo[i]));
                result = 0; break;
            }
            default: ExtraImpl10(cmd, args, ref result); break;
        }
    }
    static string D(MpDamage d) { return "[type " + d.Weapon + " dmg " + d.Damage + " rdmg " + d.RadiusDamage + " r " + d.Radius + " kb " + d.Knockback + "]"; }
    static partial void ExtraImpl10(string cmd, string[] args, ref int result);
}
