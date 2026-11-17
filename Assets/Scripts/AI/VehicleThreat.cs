using UnityEngine;
using EndlessCombat.Combat;
using EndlessSurvival.Survival;
using EndlessSurvival.Vehicle;

namespace EndlessCombat.AI
{
    /// <summary>
    /// The player's vehicle as seen by the enemy AI (Enemy_AI_Deep_Plan.md 18): lamps, a vehicle coming at an
    /// enemy, the aim point (window line or a tyre), the engine and parked vehicle stimuli, the vehicle health floor
    /// against enemy damage (18.2) and bullets that go through a window into the driver (18.3).
    /// </summary>
    public static class VehicleThreat
    {
        public static float ChargeSpeed = 5f;        // m/s
        public static float ChargeLookAhead = 2.5f;  // seconds
        public static float ChargeHalfWidth = 2.6f;  // m
        public static float EngineRadius = 60f;
        public static float ParkedRadius = 45f;
        public static float HeadlightRadius = 150f;
        public static float DriverDamageShare = 0.7f;
        public static float MaxGrenadeVehicleDamage = 35f;

        private static VehicleController Vehicle => SurvivalDirector.Vehicle;

        public static bool LampsLit
        {
            get
            {
                var parts = VehicleParts.Instance;
                return parts != null && parts.HeadlightsOn && parts.Headlights >= 20f && (parts.EngineRunning || parts.BatteryCharge > 3f);
            }
        }

        /// <summary>The driven vehicle comes at this enemy fast: away = sideways out of its path.</summary>
        public static bool Charging(EnemyController e, out Vector3 away)
        {
            away = Vector3.zero;
            var v = Vehicle;
            if (e == null || v == null || !v.isDriven || v.Rigidbody == null) return false;
            Vector3 vel = v.Rigidbody.linearVelocity;
            vel.y = 0f;
            float speed = vel.magnitude;
            if (speed < ChargeSpeed) return false;
            Vector3 dir = vel / speed;
            Vector3 rel = e.transform.position - v.transform.position;
            rel.y = 0f;
            float along = Vector3.Dot(rel, dir);
            if (along < 0f || along > speed * ChargeLookAhead + 3f) return false;
            Vector3 side = rel - dir * along;
            if (side.magnitude > ChargeHalfWidth) return false;
            Vector3 perp = Vector3.Cross(Vector3.up, dir);
            away = Vector3.Dot(side, perp) >= 0f ? perp : -perp;
            return true;
        }

        public static bool ChargingAt(EnemySquad s)
        {
            if (s == null) return false;
            foreach (var m in s.Members)
                if (m != null && !m.IsDead && Charging(m, out _)) return true;
            return false;
        }

        /// <summary>18.1: window line; on Hard and above a tyre half of the time when the vehicle is slow or close.</summary>
        public static Vector3 AimPoint(EnemyController e, Vector3 aim)
        {
            var v = Vehicle;
            if (v == null) return aim;
            Vector3 window = v.transform.position + Vector3.up * 1.3f;
            var p = AIDifficulty.For(e.Squad);
            if (p.vehicleDeny >= 2 && (v.CurrentSpeedKmh < 25f || (v.transform.position - e.transform.position).sqrMagnitude < 625f)
                && ((Time.frameCount / 30) + (e.GetHashCode() & 1)) % 2 == 0)
            {
                float best = float.MaxValue;
                Vector3 tyre = window;
                for (int i = 0; i < v.WheelCount; i++)
                {
                    Vector3 w = v.WheelPosition(i);
                    float d = (w - e.transform.position).sqrMagnitude;
                    if (d < best) { best = d; tyre = w; }
                }
                return tyre;
            }
            return window + (aim - v.transform.position - Vector3.up * 1f) * 0.1f;
        }

        // ------------------------------------------------------------------ stimuli (18.4), run by AISystem

        private static int s_parked;
        private static int s_lamps;
        private static float s_nextEngine;
        private static bool s_wasRunning;
        private static Vector3 s_lastEnginePos;
        private static float s_slowSince = -1f;

        private static void EngineStopped(Vector3 at)
        {
            foreach (var s in EnemySquad.Squads)
            {
                if (s.Phase != AlertPhase.Calm && s.Phase != AlertPhase.Guarded) continue;
                if (s.Alive == 0 || (s.home - at).sqrMagnitude > 90f * 90f) continue;
                // Only squads that could hear it
                bool heard = false;
                foreach (var m in s.Members)
                    if (m != null && !m.IsDead && (m.transform.position - at).sqrMagnitude < EngineRadius * EngineRadius) { heard = true; break; }
                if (!heard) continue;
                s.Raise(AlertPhase.Suspicious, at);
                AIDebug.Log(s, "motor sesi kesildi, bakmaya gidiyorlar");
            }
        }

        public static void Tick()
        {
            var v = Vehicle;
            if (v == null)
            {
                ClearPersistent();
                return;
            }
            var vt = AITarget.PlayerVehicle;
            bool running = v.IsEngineRunning;
            Vector3 pos = v.transform.position;

            // Engine: a heard stimulus every second while running (those who can not see the car, e.g. in the forest,
            // still come to the sound; seeing the driven car is enough on its own, see EnemyController.OnSeen)
            if (running && Time.time >= s_nextEngine)
            {
                s_nextEngine = Time.time + 1f;
                float r = EngineRadius * Mathf.Lerp(0.6f, 1.3f, Mathf.Clamp01(v.Throttle));
                var w = WeatherSystem.Instance;
                if (w != null) r *= 1f - 0.3f * w.RainIntensity;
                AIStimuli.Raise(new Stimulus
                {
                    kind = StimulusKind.VehicleEngine, position = pos, radius = r, source = vt != null ? vt : AITarget.Player,
                    certainty = v.isDriven ? 0.65f : 0.5f, time = Time.time
                });
                s_lastEnginePos = pos;
            }
            // Engine stopped (or crawling for 3 s) inside someone's hearing: they come to look (18.4)
            bool slow = running && v.CurrentSpeedKmh < 3f;
            if (slow) { if (s_slowSince < 0f) s_slowSince = Time.time; }
            else s_slowSince = -1f;
            if ((s_wasRunning && !running) || (slow && s_slowSince > 0f && Time.time - s_slowSince > 3f && Time.time - s_slowSince < 3.2f))
                EngineStopped(s_lastEnginePos);
            s_wasRunning = running;

            // Parked (nobody inside): persistent, needs sight
            bool parked = !v.isDriven;
            if (parked && s_parked == 0)
                s_parked = AIStimuli.RegisterPersistent(new Stimulus
                {
                    kind = StimulusKind.ParkedVehicle, position = pos, radius = ParkedRadius, needsSight = true, certainty = 0.3f,
                    source = AITarget.Player, owner = v
                });
            else if (!parked && s_parked != 0)
            {
                AIStimuli.RemovePersistent(s_parked);
                s_parked = 0;
            }
            else if (parked) AIStimuli.MovePersistent(s_parked, pos);

            // Headlights at night: a light seen from far away
            var cycle = EndlessSurvival.World.DayNightCycle.Instance;
            bool lamps = LampsLit && cycle != null && cycle.IsNight;
            if (lamps && s_lamps == 0)
                s_lamps = AIStimuli.RegisterPersistent(new Stimulus
                {
                    kind = StimulusKind.Light, position = pos, radius = HeadlightRadius, needsSight = true, certainty = 0.4f,
                    source = v.isDriven ? AITarget.Player : null, owner = v
                });
            else if (!lamps && s_lamps != 0)
            {
                AIStimuli.RemovePersistent(s_lamps);
                s_lamps = 0;
            }
            else if (lamps) AIStimuli.MovePersistent(s_lamps, pos + v.transform.forward * 2f);
        }

        public static void ClearPersistent()
        {
            if (s_parked != 0) AIStimuli.RemovePersistent(s_parked);
            if (s_lamps != 0) AIStimuli.RemovePersistent(s_lamps);
            s_parked = s_lamps = 0;
        }

        // ------------------------------------------------------------------ car assault on foot (user bug report 16 Nov:
        // out of ammo they only stood around the car; nobody hit it or tried to drag him out)

        /// <summary>Faster than this the car is dodged, not attacked.</summary>
        public static float AssaultMaxKmh = 8f;
        /// <summary>Melee men farther than this do not run to the car.</summary>
        public static float AssaultRange = 35f;
        /// <summary>Seconds at the open driver window before he is dragged out (stopped car only).</summary>
        public static float PullOutSeconds = 2.5f;
        public static float PullOutMaxKmh = 3f;
        public static float MeleeVehicleShare = 0.5f;
        public static float MeleeDriverShare = 0.6f;
        public static float MeleeWindowChance = 0.35f;
        /// <summary>Gap between the body box and an attacker's spot (capsule radius 0.28 + room for the swing).</summary>
        public static float AssaultGap = 0.7f;
        private static float s_lastReachWarn = -999f;

        public static bool CanAssault()
        {
            var v = Vehicle;
            return v != null && v.isDriven && v.CurrentSpeedKmh < AssaultMaxKmh;
        }

        /// <summary>
        /// Where the n-th attacker stands: driver door first, then the other door, the windshield, the rear opening;
        /// more men pair up along the doors.
        /// </summary>
        public static Vector3 AssaultSpot(int rank, out int slot)
        {
            var v = Vehicle;
            slot = (int)WindowSlot.Left;
            if (v == null) return Vector3.zero;
            // 17 Nov: from the real body box plus a gap (capsule radius + a hand's width), never inside the body.
            // Fallback: the pickup's numbers.
            float halfW = 0.83f, halfL = 1.45f, cz = 0.04f;
            var body = v.GetComponent<BoxCollider>();
            if (body != null)
            {
                Vector3 s = v.transform.lossyScale;
                halfW = body.size.x * 0.5f * Mathf.Abs(s.x);
                halfL = body.size.z * 0.5f * Mathf.Abs(s.z);
                cz = body.center.z * Mathf.Abs(s.z);
            }
            float side = halfW + AssaultGap, front = cz + halfL + AssaultGap + 0.2f, rear = cz - halfL - AssaultGap - 0.2f;
            Vector3 local;
            switch (rank % 4)
            {
                case 0: slot = (int)WindowSlot.Left; local = new Vector3(-side, 0f, cz + 0.2f); break;
                case 1: slot = (int)WindowSlot.Right; local = new Vector3(side, 0f, cz + 0.2f); break;
                case 2: slot = (int)WindowSlot.Front; local = new Vector3(0f, 0f, front); break;
                default: slot = (int)WindowSlot.Rear; local = new Vector3(0f, 0f, rear); break;
            }
            // More men: along the doors, or beside each other at the ends (never pushed into the body)
            if (rank >= 4)
            {
                float k = rank % 2 == 0 ? -1f : 1f;
                if (rank % 4 < 2) local.z += k * Mathf.Min(1f, halfL * 0.7f);
                else local.x += k * Mathf.Min(0.8f, halfW);
            }
            // Local position is unscaled: TransformPoint would scale it again
            Vector3 p = v.transform.position + v.transform.rotation * local;
            p.y = v.transform.position.y;
            return p;
        }

        /// <summary>The opening lets a hand into the cabin (open or broken, no cage).</summary>
        public static bool SlotOpen(int slot)
        {
            var parts = VehicleParts.Instance;
            if (parts == null) return true; // no parts system: plain side windows are open
            if (parts.Caged) return false;
            var s = parts.Windows[slot];
            return s == WindowState.Open || s == WindowState.Broken;
        }

        /// <summary>One melee blow on the car at a window: the body takes some, a closed window may give way, an open one lets the blow reach the driver.</summary>
        public static void MeleeHitCar(EnemyController e, int slot, float damage)
        {
            var v = Vehicle;
            if (e == null || v == null || !v.isDriven) return;
            Vector3 dir = (v.transform.position - e.transform.position);
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.001f ? dir.normalized : e.transform.forward;
            Vector3 point = e.transform.position + dir * 0.8f + Vector3.up * 1.2f;
            var info = new DamageInfo(damage * MeleeVehicleShare, DamageType.Melee, point, -dir) { Source = e.gameObject, SourceDirection = -dir };
            v.ApplyDamage(ClampEnemyDamage(v, info, info.Amount));

            if (!SlotOpen(slot))
            {
                VehicleParts.Instance?.MeleeHitWindow(slot, MeleeWindowChance);
                return;
            }
            if (Random.value > 0.6f) return;
            var player = SurvivalDirector.Player;
            var health = player != null ? player.GetComponent<EndlessSurvival.Inventory.PlayerHealth>() : null;
            if (health == null || health.IsDead) return;
            health.TakeDamage(new DamageInfo(damage * MeleeDriverShare, DamageType.Melee, point, -dir) { Source = e.gameObject, SourceDirection = -dir });
            if (Time.time - s_lastReachWarn > 4f)
            {
                s_lastReachWarn = Time.time;
                SurvivalNotify.Warn($"{VehicleParts.WindowNames[slot]} açık: içeri uzanıp vuruyorlar!");
            }
        }

        /// <summary>Drags the driver out through the open driver door (stopped car). True when he is out.</summary>
        public static bool PullOut(EnemyController e)
        {
            var v = Vehicle;
            if (e == null || v == null || !v.isDriven || v.CurrentSpeedKmh > PullOutMaxKmh) return false;
            var vi = v.GetComponentInChildren<VehicleInteraction>();
            if (vi == null) vi = v.GetComponentInParent<VehicleInteraction>();
            if (vi == null) return false;
            vi.ExitVehicle();
            if (v.isDriven) return false;
            Vector3 dir = SurvivalDirector.Player != null ? SurvivalDirector.Player.transform.position - e.transform.position : -e.transform.forward;
            dir.y = 0f;
            CombatBody.Player?.Stagger(0.8f, dir.normalized * 2f);
            SurvivalNotify.Warn("Seni araçtan çekip indirdiler!");
            return true;
        }

        // ------------------------------------------------------------------ damage (18.2, 18.3)

        /// <summary>The hit that is being applied to the vehicle (VehicleController.TakeDamage fills it).</summary>
        public static DamageInfo LastBullet;
        public static bool HasLastBullet;

        public static bool FromEnemy(DamageInfo info)
        {
            if (info.Source == null) return false;
            var t = info.Source.GetComponentInParent<AITarget>();
            return t != null && t.team == CombatTeam.Enemy;
        }

        /// <summary>18.2: enemy damage can not take the vehicle below the floor; one enemy grenade at most 35.</summary>
        public static float ClampEnemyDamage(VehicleController v, DamageInfo info, float amount)
        {
            if (v == null || !FromEnemy(info)) return amount;
            if (info.Type == DamageType.Explosion) amount = Mathf.Min(amount, MaxGrenadeVehicleDamage);
            float floor = v.maxHealth * AIDifficulty.P.vehicleFloor;
            float room = Mathf.Max(0f, v.currentHealth - floor);
            return Mathf.Min(amount, room);
        }

        /// <summary>
        /// 18.3: a bullet at window height; state = the window before this hit, broke = this hit broke the glass.
        /// slot: 0 front, 1 rear, 2 left, 3 right, 4 roof.
        /// </summary>
        public static void CabinHit(VehicleController v, int slot, WindowState before, bool broke, bool cage, float amount, Vector3 point, Vector3 normal)
        {
            if (v == null || !v.isDriven || slot == (int)WindowSlot.Roof) return;
            float through;
            switch (before)
            {
                case WindowState.Open:
                case WindowState.Broken: through = 1f; break;
                case WindowState.Plastic: through = 0.9f; break;
                case WindowState.Intact: through = broke ? 0.5f : 0f; break;
                case WindowState.Plank: through = 0.35f; break;
                default: through = 0f; break;
            }
            if (cage) through *= 0.5f;
            if (Random.value >= through) return;
            float hitChance = slot == (int)WindowSlot.Front ? 0.45f : slot == (int)WindowSlot.Left ? 0.45f : 0.25f;
            if (Random.value >= hitChance) return;
            var player = SurvivalDirector.Player;
            var health = player != null ? player.GetComponent<EndlessSurvival.Inventory.PlayerHealth>() : null;
            if (health == null || health.IsDead) return;
            float raw = amount / Mathf.Max(0.01f, v.bulletDamageMultiplier);
            var info = new DamageInfo(raw * DriverDamageShare, DamageType.Bullet, point, normal);
            if (HasLastBullet)
            {
                info.Source = LastBullet.Source;
                info.SourceDirection = LastBullet.SourceDirection;
            }
            health.TakeDamage(info);
            SurvivalNotify.Warn("Camdan giren mermi!");
        }
    }
}
