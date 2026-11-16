using UnityEngine;
using EndlessCombat.Combat;

namespace EndlessSurvival.Inventory
{
    /// <summary>
    /// The weapons on the body and which one is in the hand (Combat_Action_Plan.md, 2026-11-16).
    ///   Long gun  : on the back (holster socket).                LB held + Y / key 1
    ///   Pistol    : right hip.                                    LB held + X / key 2
    ///   Melee     : left hip, heavy ones crossed on the back.     LB held + RB / key 3 (empty slot = fists)
    ///   Shield    : left arm or the back.                         LB held + A
    ///   Rock / grenade: in the right hand like a weapon.          LB held + RT / T, G
    ///   Quick item: key 4 only (not on the gamepad any more)
    /// LB tap (R key): the weapon in the hand goes to its holster (holster time); nothing in the hand: the last one
    /// comes out (draw time). LT with the weapon in its holster draws the last firearm and aims; releasing LT keeps it.
    /// Drop rule: a pick that needs a hand still holding a weapon (not holstered first) drops that weapon to the
    /// ground with its magazine; X picks it up again.
    /// Grips are fixed since Core_Feel_Plan stage 2 (2026-11-14, no grip switch any more):
    ///   Long gun : always two hands (the shield goes to the back).
    ///   Pistol   : two hands, one hand when a shield is worn (shield on the left arm).
    ///   Melee    : two hands only for two handed weapons (bat...), the rest one hand (shield stays on the arm).
    /// The one hand long gun / two hand melee fields below are no longer used by the fixed rules.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerLoadout : MonoBehaviour
    {
        public enum Slot { None = -1, LongGun = 0, Pistol = 1, Melee = 2 }

        [Header("Grip effects")]
        [Tooltip("Extra spread (degrees) of a long gun held with one hand")]
        public float oneHandLongGunSpread = 4f;
        [Tooltip("Reload time multiplier of a long gun held with one hand")]
        public float oneHandLongGunReload = 1.6f;
        [Tooltip("Extra spread (degrees) of a pistol held with one hand")]
        public float oneHandPistolSpread = 1.2f;
        [Tooltip("Melee weapon held with both hands (one handed weapons only): damage, stagger and stamina multipliers")]
        public float twoHandDamage = 1.35f;
        public float twoHandStagger = 1.5f;
        public float twoHandStamina = 1.2f;

        [Header("Body spots (character space: x right, y up, z forward)")]
        public Vector3 pistolOffset = new Vector3(0.19f, -0.06f, -0.02f);
        public Vector3 meleeHipOffset = new Vector3(-0.2f, -0.2f, 0f);
        public Vector3 meleeHipDir = new Vector3(-0.08f, -1f, 0.18f);
        public Vector3 meleeBackOffset = new Vector3(0f, -0.05f, -0.2f);
        public Vector3 meleeBackDir = new Vector3(-0.7f, 1f, 0f);

        private PlayerShooter _shooter;
        private PlayerInventory _inventory;
        private ShieldHolder _shield;
        private TorchHolder _torch;

        /// <summary>A lit torch is in the left hand (Combat_Action_Plan.md 8).</summary>
        public bool TorchHeld => _torch != null && _torch.Held;
        private Animator _animator;

        private readonly WeaponController[] _weapons = new WeaponController[3];
        private readonly ItemDefinition[] _items = new ItemDefinition[3];
        private readonly bool[] _twoHand = { true, false, false };
        private Transform _pistolSpot, _meleeHipSpot, _meleeBackSpot;

        public Slot Active { get; private set; } = Slot.None;

        /// <summary>Last active slot, drawn again by "draw" (R hold / Y hold after stowing).</summary>
        public Slot LastActive { get; private set; } = Slot.None;

        /// <summary>Item used with key 4 (bandage, food; not on the gamepad since 2026-11-16). Chosen in the character tab.</summary>
        public ItemDefinition QuickItem { get; set; }

        public event System.Action Changed;

        public static PlayerLoadout GetOrAdd(GameObject owner)
        {
            var l = owner.GetComponent<PlayerLoadout>();
            return l != null ? l : owner.AddComponent<PlayerLoadout>();
        }

        public WeaponController Weapon(Slot slot) => slot == Slot.None ? null : _weapons[(int)slot];
        public ItemDefinition Item(Slot slot) => slot == Slot.None ? null : _items[(int)slot];

        /// <summary>Item of the weapon in the hand (null for fists or when stowed).</summary>
        public ItemDefinition ActiveItem => Active == Slot.None || IsStowed ? null : _items[(int)Active];

        public bool IsStowed => _shooter == null || _shooter.IsHolstered || Active == Slot.None;

        public static Slot SlotOf(EquipSlot slot)
        {
            switch (slot)
            {
                case EquipSlot.LongGun: return Slot.LongGun;
                case EquipSlot.Pistol: return Slot.Pistol;
                case EquipSlot.Melee: return Slot.Melee;
                default: return Slot.None;
            }
        }

        // ------------------------------------------------------------------ Grip

        /// <summary>
        /// True when the slot is gripped with both hands. Fixed since Core_Feel_Plan stage 2 (no grip switch):
        /// long gun two hands, two handed melee weapon (bat) two hands, pistol two hands unless a shield is worn,
        /// knife / one handed melee / fists one hand. A two handed weapon in the hand sends the shield to the back.
        /// </summary>
        public bool TwoHanded(Slot slot)
        {
            if (slot == Slot.None) return false;
            return DefaultTwoHand(slot, _weapons[(int)slot]);
        }

        /// <summary>Grips are fixed: nothing can be switched.</summary>
        public bool GripChangeable(Slot slot) => false;

        /// <summary>Kept for old saves and callers; the grip is fixed, so this only stores the flag.</summary>
        public void SetTwoHanded(Slot slot, bool twoHand)
        {
            if (slot == Slot.None) return;
            _twoHand[(int)slot] = twoHand;
        }

        /// <summary>The fixed grip of a weapon kind.</summary>
        private bool DefaultTwoHand(Slot slot, WeaponController prefabOrWeapon)
        {
            if (slot == Slot.Pistol) return !((_shield != null && _shield.HasShield && _shieldDrawn) || TorchHeld); // the left hand carries the shield / torch
            return TwoHandedWeapon(slot, prefabOrWeapon);
        }

        /// <summary>The weapon needs the left hand too: long gun, two handed melee weapon (bat). A pistol does not.</summary>
        private static bool TwoHandedWeapon(Slot slot, WeaponController prefabOrWeapon)
        {
            if (slot == Slot.LongGun) return true;
            if (slot == Slot.Melee && prefabOrWeapon != null)
            {
                var melee = prefabOrWeapon.GetComponent<MeleeWeapon>();
                return melee != null && melee.stats.ResolveCategory(false) == MeleeCategory.TwoHand;
            }
            return false;
        }

        /// <summary>
        /// A shield was put on or taken off: the fixed grips are applied again (pistol one hand with a shield,
        /// two hands without; rifle and two handed melee always two hands, so the shield goes to the back).
        /// </summary>
        public void OnShieldChanged()
        {
            if (_shield == null || !_shield.HasShield) _shieldDrawn = true; // the next shield comes onto the arm
            for (int i = 0; i < 3; i++) _twoHand[i] = DefaultTwoHand((Slot)i, _weapons[i]);
            ApplyGrip();
            Changed?.Invoke();
        }

        /// <summary>Grip flag of a slot as stored (for the save file).</summary>
        public bool GripFlag(Slot slot) => slot != Slot.None && _twoHand[(int)slot];

        /// <summary>Loading a save: which slot is active and whether it is in the hand.</summary>
        public void RestoreActive(Slot slot, bool stowed)
        {
            if (_shooter == null) return;
            if (slot == Slot.None) slot = Slot.Melee;
            if (_weapons[(int)slot] == null && slot != Slot.Melee) slot = Slot.Melee;
            Active = slot;
            LastActive = slot;
            _shooter.SetActiveWeapon(_weapons[(int)slot], !stowed);
            ApplyGrip();
            Changed?.Invoke();
        }

        /// <summary>Re-applies the grip effects and the shield arm / back rule.</summary>
        public void Refresh()
        {
            ApplyGrip();
            Changed?.Invoke();
        }

        /// <summary>Shield on the left arm: worn, not put on the back (A on the map), no two handed weapon in the right hand.</summary>
        public bool ShieldOnArm => _shield != null && _shield.HasShield && _shieldDrawn && !TorchHeld
                                   && !(RightDrawn && TwoHandedWeapon(Active, _weapons[(int)Active]));

        private void ApplyGrip()
        {
            if (_shooter == null) return;
            bool drawn = !IsStowed;
            _shooter.OneHandLongGun = Active == Slot.LongGun && !TwoHanded(Slot.LongGun);
            _shooter.PistolTwoHand = Active == Slot.Pistol && TwoHanded(Slot.Pistol);
            float spread = 0f;
            if (drawn && Active == Slot.LongGun && !TwoHanded(Slot.LongGun)) spread = oneHandLongGunSpread;
            if (drawn && Active == Slot.Pistol && !TwoHanded(Slot.Pistol)) spread = oneHandPistolSpread;
            _shooter.ExtraSpreadDegrees = spread;

            var gun = _weapons[(int)Slot.LongGun];
            if (gun != null) gun.ReloadTimeMultiplier = TwoHanded(Slot.LongGun) ? 1f : oneHandLongGunReload;

            if (_shield != null && _shield.HasShield) _shield.SetOnBack(!ShieldOnArm);
        }

        // ------------------------------------------------------------------ Setup

        private void Awake()
        {
            _shooter = GetComponent<PlayerShooter>();
            _inventory = GetComponent<PlayerInventory>();
            _shield = ShieldHolder.GetOrAdd(gameObject);
            _torch = TorchHolder.GetOrAdd(gameObject);
            _animator = GetComponent<Animator>();
            if (_shooter != null) _shooter.ParkWeapon = Park;
        }

        /// <summary>The weapon already in the hand at the start of the game becomes the slot's weapon.</summary>
        public void AdoptExisting(Slot slot, ItemDefinition item, WeaponController weapon)
        {
            if (slot == Slot.None || weapon == null || _shooter == null) return;
            _items[(int)slot] = item;
            _weapons[(int)slot] = weapon;
            _shooter.RegisterHeldWeapon(weapon);
            PreparePlayerWeapon(weapon);
            Active = slot;
            LastActive = slot;
            ApplyGrip();
            Changed?.Invoke();
        }

        /// <summary>Puts a weapon item into its slot (null empties the slot). The old one is destroyed.</summary>
        public void SetSlot(Slot slot, ItemDefinition item)
        {
            if (slot == Slot.None || _shooter == null) return;
            int i = (int)slot;
            bool wasActive = Active == slot;
            bool wasDrawn = wasActive && !_shooter.IsHolstered;

            WeaponController old = _weapons[i];
            if (old != null)
            {
                if (_inventory != null) _inventory.Add(old.AmmoKind, old.TakeAllAmmo()); // magazine back to the pool
                _shooter.DestroyWeapon(old);
            }
            _weapons[i] = null;
            _items[i] = item;
            _twoHand[i] = DefaultTwoHand(slot, item != null ? item.weaponPrefab : null); // fresh weapon: default grip

            if (item != null && item.weaponPrefab != null)
            {
                WeaponController w = _shooter.SpawnWeapon(item.weaponPrefab);
                _weapons[i] = w;
                PreparePlayerWeapon(w);
            }

            if (wasActive && _weapons[i] == null && slot != Slot.Melee)
            {
                // Gun taken off while in the hand: the melee slot (or the fists) takes over
                Active = Slot.Melee;
                LastActive = Slot.Melee;
                _shooter.SetActiveWeapon(_weapons[(int)Slot.Melee], wasDrawn);
            }
            else if (wasActive)
            {
                // The new weapon comes to the hand when the old one was in the hand
                _shooter.SetActiveWeapon(_weapons[i], wasDrawn);
            }
            else if (Active == Slot.None && _weapons[i] != null)
            {
                Active = slot;
                LastActive = slot;
                _shooter.SetActiveWeapon(_weapons[i], false);
            }
            ApplyGrip();
            Changed?.Invoke();
        }

        private void PreparePlayerWeapon(WeaponController w)
        {
            if (w == null || !w.IsFirearm || _inventory == null) return;
            // Its own reserve goes to the shared pool; from now on it reloads from the pool
            int own = w.ReserveAmmo;
            w.AmmoPool = null;
            int all = w.TakeAllAmmo();
            int mag = all - own;
            _inventory.Add(w.AmmoKind, own);
            w.AmmoPool = _inventory;
            w.RestoreMagazine(mag);
        }

        // ------------------------------------------------------------------ Picking, holstering, dropping
        // Combat_Action_Plan.md 3-5. Pick codes: the selection map (LB held) and the keyboard keys send them.

        public const int PickLongGun = 0, PickPistol = 1, PickMelee = 2, PickShield = 3, PickBow = 4,
                         PickRock = 5, PickGrenade = 6, PickThrowable = 7, PickTorch = 8;

        private PlayerThrower _thrower;
        private PlayerEquipment _equipment;
        private bool _shieldDrawn = true;
        private float _holsterAt = -1f;
        private bool _ltWas;

        /// <summary>The player wants the shield on the left arm (A on the map toggles it). It still goes to the back
        /// while a two handed weapon is in the hand.</summary>
        public bool ShieldDrawn => _shieldDrawn;

        /// <summary>A weapon (not the fists) is in the right hand.</summary>
        public bool RightDrawn => _shooter != null && !_shooter.IsHolstered && Active != Slot.None && _weapons[(int)Active] != null;

        /// <summary>The right hand is being emptied (LB tap): the weapon goes to its holster when the time is up.</summary>
        public bool Holstering => _holsterAt > 0f;

        /// <summary>
        /// One pick from the selection map / a keyboard key (Combat_Action_Plan.md 3, 5). The pick comes to the hand
        /// at once (its draw time follows). A weapon still in the hand it needs is not put away: it falls to the ground.
        /// </summary>
        public void Pick(int code)
        {
            if (_shooter == null) return;
            if (_thrower == null) _thrower = GetComponent<PlayerThrower>();
            CancelPendingHolster();
            switch (code)
            {
                case PickShield: PickShieldArm(); return;
                case PickTorch: PickTorchHand(); return;
                case PickBow: EndlessSurvival.Survival.SurvivalNotify.Show("Yay yok (yay henüz oyunda değil)", 1.5f); return;
                case PickRock: TakeThrowable(PlayerThrower.Kind.Rock); return;
                case PickGrenade: TakeThrowable(PlayerThrower.Kind.Grenade); return;
                case PickThrowable: TakeThrowable(_thrower != null ? _thrower.NextKind() : PlayerThrower.Kind.None); return;
            }
            if (code < PickLongGun || code > PickMelee) return;

            Slot slot = (Slot)code;
            WeaponController w = _weapons[code];
            if (w == null && slot != Slot.Melee)
            {
                EndlessSurvival.Survival.SurvivalNotify.Show(slot == Slot.LongGun ? "Uzun namlu yuvası boş" : "Tabanca yuvası boş", 1.5f);
                return;
            }
            bool throwableInHand = _thrower != null && _thrower.HasItem;
            if (Active == slot && !_shooter.IsHolstered && !throwableInHand) return; // already in the hand

            // Left hand: a two handed pick drops the shield from the arm (read before the right hand changes)
            bool twoHand = slot == Slot.LongGun || (slot == Slot.Melee && TwoHandedWeapon(Slot.Melee, w));
            bool dropShield = twoHand && ShieldOnArm;
            bool dropTorch = twoHand && TorchHeld;

            // Right hand: whatever is in it falls
            if (throwableInHand) _thrower.DropHeld();
            else if (RightDrawn) DropRight();
            if (dropShield) DropShield();
            if (dropTorch) DropTorch();

            w = _weapons[code];
            Active = slot;
            LastActive = slot;
            _shooter.SetActiveWeapon(w, true); // starts its draw time (empty melee slot: the fists, no time)
            ApplyGrip();
            Changed?.Invoke();
        }

        /// <summary>Old entry (inventory, tests): same as a pick.</summary>
        public void Select(Slot slot)
        {
            if (slot != Slot.None) Pick((int)slot);
        }

        private void TakeThrowable(PlayerThrower.Kind kind)
        {
            if (_thrower == null) return;
            if (kind == PlayerThrower.Kind.None)
            {
                EndlessSurvival.Survival.SurvivalNotify.Show("Fırlatılacak taş / el bombası yok", 1.5f);
                return;
            }
            if (_thrower.HasItem)
            {
                // Switching between rock and grenade: the other one goes back to the bag (not a drop)
                if (_thrower.Held != kind) _thrower.Hold(kind);
                return;
            }
            if (_thrower.Count(kind) <= 0)
            {
                _thrower.Hold(kind); // shows "none" and does nothing
                return;
            }
            if (RightDrawn) DropRight();
            // The right hand must be empty: a weapon still in it goes to its holster at once
            if (!_shooter.IsHolstered) _shooter.SetHolstered(true);
            _thrower.Hold(kind);
            ApplyGrip();
            Changed?.Invoke();
        }

        private void PickShieldArm()
        {
            if (_shield == null || !_shield.HasShield)
            {
                EndlessSurvival.Survival.SurvivalNotify.Show("Kalkan yok", 1.5f);
                return;
            }
            if (_shieldDrawn && ShieldOnArm)
            {
                // On the arm: back to the back
                _shieldDrawn = false;
            }
            else
            {
                // A two handed weapon in the hand falls (the left hand takes the shield); the right hand stays empty
                if (RightDrawn && TwoHandedWeapon(Active, _weapons[(int)Active]))
                {
                    DropRight();
                    Active = Slot.Melee;
                    _shooter.SetActiveWeapon(_weapons[(int)Slot.Melee], false);
                    PlayerCombatState.Of(gameObject)?.ClearDraw();
                }
                if (TorchHeld) DropTorch(); // the left hand takes the shield: the torch falls
                _shieldDrawn = true;
                PlayerCombatState.Of(gameObject)?.NoteDraw(WeaponDefinition.DrawShield);
            }
            ApplyGrip();
            Changed?.Invoke();
        }

        /// <summary>
        /// Torch pick (Combat_Action_Plan.md 5, 8): a lit torch comes to the left hand from the bag; picked again it is
        /// put out (stays in the bag with its fuel). A shield on the arm falls (one left hand); a shield on the back
        /// stays there. A two handed weapon in the right hand falls.
        /// </summary>
        private void PickTorchHand()
        {
            if (_torch == null) return;
            if (_torch.Held)
            {
                _torch.PutOut();
                PlayerCombatState.Of(gameObject)?.NoteDraw(WeaponDefinition.DrawShield * WeaponDefinition.HolsterFactor);
            }
            else
            {
                if (_torch.TorchInBag() == null)
                {
                    EndlessSurvival.Survival.SurvivalNotify.Show("Meşale yok (Üretim: odun + bez)", 1.5f);
                    return;
                }
                if (ShieldOnArm) DropShield();
                _shieldDrawn = false; // a shield still worn stays on the back
                if (RightDrawn && TwoHandedWeapon(Active, _weapons[(int)Active]))
                {
                    DropRight();
                    Active = Slot.Melee;
                    _shooter.SetActiveWeapon(_weapons[(int)Slot.Melee], false);
                    PlayerCombatState.Of(gameObject)?.ClearDraw();
                }
                _torch.Light();
                PlayerCombatState.Of(gameObject)?.NoteDraw(WeaponDefinition.DrawShield);
            }
            ApplyGrip();
            Changed?.Invoke();
        }

        /// <summary>The torch in the left hand falls to the ground (another pick needs the left hand).</summary>
        public void DropTorch()
        {
            if (_torch == null || !_torch.Held) return;
            string name = _torch.Item != null ? _torch.Item.DisplayName : "Meşale";
            _torch.Drop(DropPoint(-0.35f), _inventory != null ? _inventory.dropPickupPrefab : null);
            EndlessSurvival.Survival.SurvivalNotify.Show($"{name} yere düştü", 1.5f);
        }

        /// <summary>
        /// The weapon in the right hand falls to the ground (Combat_Action_Plan.md 5): it leaves its slot, its
        /// magazine stays in it, X picks it up again (WorldPickup).
        /// </summary>
        public void DropRight()
        {
            if (!RightDrawn) return;
            Slot s = Active;
            WeaponController w = _weapons[(int)s];
            ItemDefinition item = _items[(int)s];
            if (_equipment == null) _equipment = GetComponent<PlayerEquipment>();
            if (w == null || item == null || _equipment == null || _inventory == null || _inventory.dropPickupPrefab == null)
            {
                StowAll(); // nowhere to drop it: it goes to its holster instead
                return;
            }

            int magazine = w.IsFirearm ? w.CurrentAmmo : -1;
            if (w.IsFirearm) w.TakeAllAmmo(); // the rounds stay with the dropped gun, not in the bag

            _equipment.DropCarried(EquipSlotOf(s));
            WorldPickup p = WorldPickup.Spawn(_inventory.dropPickupPrefab, item, 1, DropPoint(0.35f), null);
            if (p != null) { p.fromHand = true; p.magazineRounds = magazine; }
            EndlessSurvival.Survival.SurvivalNotify.Show($"{item.DisplayName} yere düştü", 1.5f);
        }

        /// <summary>The shield on the arm falls to the ground (another pick needs the left hand).</summary>
        public void DropShield()
        {
            if (_shield == null || !_shield.HasShield) return;
            if (_equipment == null) _equipment = GetComponent<PlayerEquipment>();
            ItemDefinition item = _shield.Item;
            if (item == null || _equipment == null || _inventory == null || _inventory.dropPickupPrefab == null)
            {
                _shieldDrawn = false;
                return;
            }
            _equipment.DropCarried(EquipSlot.Shield);
            WorldPickup sp = WorldPickup.Spawn(_inventory.dropPickupPrefab, item, 1, DropPoint(-0.35f), null);
            if (sp != null) sp.fromHand = true;
            EndlessSurvival.Survival.SurvivalNotify.Show($"{item.DisplayName} yere düştü", 1.5f);
        }

        /// <summary>In front of the character, a bit to the side (a pickup must not land on the character).</summary>
        private Vector3 DropPoint(float side) => transform.position + transform.forward * 0.7f + transform.right * side + Vector3.up * 0.9f;

        private static EquipSlot EquipSlotOf(Slot s) =>
            s == Slot.LongGun ? EquipSlot.LongGun : s == Slot.Pistol ? EquipSlot.Pistol : EquipSlot.Melee;

        /// <summary>
        /// LB tap / R key (Combat_Action_Plan.md 4): a weapon in the hand goes back to its holster (takes its holster
        /// time, a hit does not stop it); nothing in the hand: the last weapon comes out. A rock / grenade goes back
        /// to the bag. A second tap while it is going back keeps it in the hand.
        /// </summary>
        public void HolsterToggle()
        {
            if (_shooter == null) return;
            if (_thrower == null) _thrower = GetComponent<PlayerThrower>();
            if (_thrower != null && _thrower.HasItem)
            {
                _thrower.Stow();
                return;
            }
            if (_holsterAt > 0f)
            {
                CancelPendingHolster();
                PlayerCombatState.Of(gameObject)?.ClearDraw();
                return;
            }
            if (RightDrawn)
            {
                WeaponController w = _weapons[(int)Active];
                float t = WeaponDefinition.HolsterTimeOf(w);
                if (w.IsReloading) w.CancelReload();
                _holsterAt = Time.time + Mathf.Max(0.01f, t);
                PlayerCombatState.Of(gameObject)?.NoteDraw(t);
                return;
            }
            DrawLast();
        }

        private void CancelPendingHolster() => _holsterAt = -1f;

        /// <summary>Everything to its spot on the body at once (no time; hands busy, vehicles, save loading).</summary>
        public void StowAll()
        {
            if (_shooter == null) return;
            CancelPendingHolster();
            if (!_shooter.IsHolstered) _shooter.SetHolstered(true);
            ApplyGrip();
            Changed?.Invoke();
        }

        /// <summary>The last weapon comes to the hand (its draw time follows). Its slot emptied: the next weapon there is.</summary>
        public void DrawLast()
        {
            if (_shooter == null) return;
            CancelPendingHolster();
            Slot slot = LastSlotToDraw();
            Active = slot;
            LastActive = slot;
            _shooter.SetActiveWeapon(_weapons[(int)slot], true);
            ApplyGrip();
            Changed?.Invoke();
        }

        private Slot LastSlotToDraw()
        {
            Slot slot = LastActive != Slot.None ? LastActive : Slot.Melee;
            if (_weapons[(int)slot] == null && slot != Slot.Melee)
                slot = _weapons[(int)Slot.Pistol] != null ? Slot.Pistol : _weapons[(int)Slot.LongGun] != null ? Slot.LongGun : Slot.Melee;
            return slot;
        }

        private bool LastIsFirearm
        {
            get
            {
                WeaponController w = _weapons[(int)LastSlotToDraw()];
                return w != null && w.IsFirearm;
            }
        }

        private void UseQuickItem()
        {
            if (QuickItem == null || _inventory == null)
            {
                EndlessSurvival.Survival.SurvivalNotify.Show("Hızlı eşya seçilmedi (Envanter > Karakter)", 2f);
                return;
            }
            if (_inventory.Storage.GetCount(QuickItem) <= 0)
            {
                EndlessSurvival.Survival.SurvivalNotify.Show($"{QuickItem.DisplayName} kalmadı", 2f);
                return;
            }
            string msg = _inventory.UseItem(QuickItem);
            if (!string.IsNullOrEmpty(msg)) EndlessSurvival.Survival.SurvivalNotify.Show(msg, 2f);
        }

        private bool _wasHolstered;

        private void Update()
        {
            if (_shooter == null) return;
            if (_thrower == null) _thrower = GetComponent<PlayerThrower>();

            // LB tap: the holster time is up, the weapon goes to its spot on the body
            if (_holsterAt > 0f && Time.time >= _holsterAt) StowAll();

            // Hands busy / save loading can stow and draw through PlayerShooter: keep the grip and the shield in step
            if (_wasHolstered != _shooter.IsHolstered)
            {
                _wasHolstered = _shooter.IsHolstered;
                ApplyGrip();
                Changed?.Invoke();
            }
            else if (_shield != null && _shield.HasShield) _shield.SetOnBack(!ShieldOnArm);

            // A pistol or melee weapon put away before the skeleton was ready (the starting pistol) ended up on the
            // long gun's back socket: move it to its own spot on the hip as soon as that spot exists.
            for (int i = (int)Slot.Pistol; i <= (int)Slot.Melee; i++)
            {
                WeaponController w = _weapons[i];
                if (w == null || (Active == (Slot)i && !_shooter.IsHolstered)) continue;
                Transform parent = w.transform.parent;
                if (parent == null || (parent != _pistolSpot && parent != _meleeHipSpot && parent != _meleeBackSpot)) Park(w);
            }

            // Picks and the holster button go through the combat state: input owner, input buffer, rules table
            var state = PlayerCombatState.Of(gameObject);
            if (state == null || state.InputBlocked)
            {
                _ltWas = false;
                return;
            }
            if (EndlessSurvival.Survival.HandsBusyHolster.HandsBusy) return;

            // LT with the weapon in its holster: it comes out, the aim follows when it is in the hand
            // (Combat_Action_Plan.md 1). LT during the LB holster time keeps it in the hand. Releasing LT leaves it in the hand.
            bool lt = GameInput.AimHeld;
            if (lt && !_ltWas && (_thrower == null || !_thrower.HasItem))
            {
                if (_holsterAt > 0f)
                {
                    CancelPendingHolster();
                    state.ClearDraw();
                }
                else if (!RightDrawn && LastIsFirearm && state.Allows(CombatIntent.Swap)) DrawLast();
            }
            _ltWas = lt;

            if (state.TryConsume(CombatIntent.Swap, out int code)) Pick(code);
            if (state.TryConsume(CombatIntent.Holster)) HolsterToggle();

            // Key 4: the quick item (not on the gamepad any more; not during a strike or a hit reaction)
            if (GameInput.QuickItemPressed && !CombatBody.PlayerLocked) UseQuickItem();
        }

        // ------------------------------------------------------------------ Body spots

        /// <summary>PlayerShooter callback: puts a weapon that is not in the hand on its spot. False = default socket.</summary>
        private bool Park(WeaponController weapon)
        {
            if (weapon == null) return true;
            Slot slot = Slot.None;
            for (int i = 0; i < 3; i++)
                if (_weapons[i] == weapon) slot = (Slot)i;
            if (slot == Slot.None || slot == Slot.LongGun) return false; // long guns: the back holster socket

            Transform spot;
            if (slot == Slot.Pistol) spot = PistolSpot();
            else
            {
                var melee = MeleeOf(weapon);
                bool heavy = melee != null && melee.stats.ResolveCategory(false) == MeleeCategory.TwoHand;
                spot = heavy ? MeleeBackSpot() : MeleeHipSpot();
            }
            if (spot == null) return false;

            // The pivot is the grip: grip -> model center is the long axis (works for models tilted in the prefab)
            Bounds b = LocalBounds(weapon);
            Vector3 s = b.size;
            Vector3 modelLong = b.center.magnitude > 0.05f ? b.center.normalized
                : (s.z >= s.x && s.z >= s.y ? Vector3.forward : (s.y >= s.x ? Vector3.up : Vector3.right));
            Vector3 modelUp = Mathf.Abs(Vector3.Dot(modelLong, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up;
            modelUp = Vector3.ProjectOnPlane(modelUp, modelLong).normalized;
            Quaternion rot = Quaternion.Inverse(Quaternion.LookRotation(modelLong, modelUp));

            weapon.transform.SetParent(spot, false);
            weapon.transform.localRotation = rot;
            weapon.transform.localPosition = -(rot * b.center);
            return true;
        }

        private Transform PistolSpot() =>
            _pistolSpot != null ? _pistolSpot : (_pistolSpot = MakeSpot("PistolHipSpot", HumanBodyBones.Hips, pistolOffset, Vector3.down, Vector3.back));

        private Transform MeleeHipSpot() =>
            _meleeHipSpot != null ? _meleeHipSpot : (_meleeHipSpot = MakeSpot("MeleeHipSpot", HumanBodyBones.Hips, meleeHipOffset, meleeHipDir, Vector3.left));

        private Transform MeleeBackSpot() =>
            _meleeBackSpot != null ? _meleeBackSpot : (_meleeBackSpot = MakeSpot("MeleeBackSpot", HumanBodyBones.Chest, meleeBackOffset, meleeBackDir, Vector3.back));

        /// <summary>
        /// A spot on a bone whose forward is the weapon's long axis and up its up, in the character's space.
        /// The character's facing (not the animated bone) gives the directions, so it is right in any pose.
        /// </summary>
        private Transform MakeSpot(string spotName, HumanBodyBones boneId, Vector3 offset, Vector3 longDir, Vector3 upDir)
        {
            if (_animator == null) _animator = GetComponentInChildren<Animator>();
            Transform bone = _animator != null && _animator.isHuman ? _animator.GetBoneTransform(boneId) : null;
            if (bone == null && boneId == HumanBodyBones.Chest && _animator != null) bone = _animator.GetBoneTransform(HumanBodyBones.Spine);
            if (bone == null) return null;

            Transform c = transform;
            var go = new GameObject(spotName);
            go.transform.SetParent(bone, false);
            go.transform.SetPositionAndRotation(bone.position + c.TransformDirection(offset),
                Quaternion.LookRotation(c.TransformDirection(longDir.normalized), c.TransformDirection(upDir.normalized)));
            return go.transform;
        }

        private static Bounds LocalBounds(WeaponController weapon)
        {
            Matrix4x4 toRoot = weapon.transform.worldToLocalMatrix;
            bool any = false;
            Bounds b = default;
            foreach (var mf in weapon.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Matrix4x4 m = toRoot * mf.transform.localToWorldMatrix;
                Bounds mb = mf.sharedMesh.bounds;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 corner = mb.center + Vector3.Scale(mb.extents,
                        new Vector3((k & 1) == 0 ? -1f : 1f, (k & 2) == 0 ? -1f : 1f, (k & 4) == 0 ? -1f : 1f));
                    Vector3 p = m.MultiplyPoint3x4(corner);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            return b;
        }

        private static MeleeWeapon MeleeOf(WeaponController w) => w != null ? w.GetComponent<MeleeWeapon>() : null;
    }
}
