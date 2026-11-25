using SomniumSpace.Worlds.SomniumMetroid.Data;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// Samus's state (CPlayerState): energy (99 + 100 per energy tank), missiles, the items the start spawn point gives (its item
    /// counts, EItemType order: 0 power, 1 ice, 2 wave, 3 plasma beams, 4 missiles, 5 scan visor, 9 thermal, 10 charge beam, 13 x-ray,
    /// 17 combat visor, 24 energy tanks ...), the equipped beam (EBeamId 0 power, 1 ice, 2 wave, 3 plasma) and visor (EPlayerVisor
    /// 0 combat, 1 x-ray, 2 scan, 3 thermal).
    /// </summary>
    public sealed class MpPlayerState
    {
        public const int BeamPower = 0, BeamIce = 1, BeamWave = 2, BeamPlasma = 3;
        public const int VisorCombat = 0, VisorXRay = 1, VisorScan = 2, VisorThermal = 3;
        public const int ItemPower = 0, ItemIce = 1, ItemWave = 2, ItemPlasma = 3, ItemMissiles = 4, ItemScan = 5, ItemThermal = 9, ItemCharge = 10, ItemXRay = 13, ItemCombat = 17, ItemTanks = 24;

        /// <summary>Iteration 1: every beam and visor is unlocked for testing (the frigate itself only gives Power Beam, Combat and Scan).</summary>
        public static bool UnlockAll = true;

        public readonly int[] Items = new int[41];
        public float Energy = 99f;
        public int Missiles, MissileCapacity;
        public int Beam = BeamPower;
        public int Visor = VisorCombat;

        public const int ItemHealthRefill = 26, ItemMorphBall = 16, ItemSpaceJump = 15, ItemGrapple = 12, ItemBombs = 6, ItemGravity = 21, ItemVaria = 22, ItemPhazon = 23;
        public const int SuitPower = 0, SuitGravity = 1, SuitVaria = 2, SuitPhazon = 3;

        /// <summary>CPlayerState::GetCurrentSuit: the best suit owned (EPlayerSuit order) - the cannon's forearm model follows it.</summary>
        public int Suit
        {
            get
            {
                if (Items[ItemPhazon] > 0) return SuitPhazon;
                if (Items[ItemGravity] > 0) return SuitGravity;
                if (Items[ItemVaria] > 0) return SuitVaria;
                return SuitPower;
            }
        }

        public int EnergyTanks { get { return Items[ItemTanks]; } }
        public float MaxEnergy { get { return 99f + 100f * EnergyTanks; } }

        public void Give(int[] spawnItems)
        {
            for (int i = 0; i < Items.Length; i++) Items[i] = spawnItems != null && i < spawnItems.Length ? spawnItems[i] : 0;

            UnlockForTesting();
            Items[ItemPower] = 1;
            Items[ItemCombat] = 1;
            MissileCapacity = Items[ItemMissiles];
            Missiles = MissileCapacity;
            Energy = MaxEnergy;
        }

        /// <summary>Testing (tablet "All beams & visors"): every beam, visor and the charge beam.</summary>
        public void UnlockForTesting()
        {
            if (!UnlockAll) return;
            Items[ItemPower] = Items[ItemIce] = Items[ItemWave] = Items[ItemPlasma] = 1;
            Items[ItemScan] = Items[ItemThermal] = Items[ItemXRay] = Items[ItemCombat] = Items[ItemCharge] = 1;
        }

        public bool HasBeam(int beam)
        {
            switch (beam)
            {
                case BeamPower: return Items[ItemPower] > 0;
                case BeamIce: return Items[ItemIce] > 0;
                case BeamWave: return Items[ItemWave] > 0;
                case BeamPlasma: return Items[ItemPlasma] > 0;
            }

            return false;
        }

        public bool HasVisor(int visor)
        {
            switch (visor)
            {
                case VisorCombat: return Items[ItemCombat] > 0;
                case VisorScan: return Items[ItemScan] > 0;
                case VisorThermal: return Items[ItemThermal] > 0;
                case VisorXRay: return Items[ItemXRay] > 0;
            }

            return false;
        }

        public bool HasCharge { get { return Items[ItemCharge] > 0; } }

        /// <summary>The weapon type of a beam (EWeaponType: power 0, ice 1, wave 2, plasma 3).</summary>
        public static int WeaponOf(int beam) { return beam; }
    }
}
