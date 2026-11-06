using System.Collections.Generic;
using UnityEngine;
using EndlessCombat.Combat;
using EndlessSurvival.Inventory;
using EndlessSurvival.World;

namespace EndlessSurvival.Survival
{
    public enum AnimalKind
    {
        Deer,
        Rabbit,
        Wolf,
        Bear
    }

    /// <summary>
    /// 3.1-3.3: simple animal AI with primitive bodies. Prey (deer, rabbit) flee when they see or hear the
    /// player; predators (wolf packs, bear) hunt and bite. Moves along the ground (terrain height), no NavMesh
    /// so it works on every chunk. Dead animals become carcasses that can be butchered with [E].
    /// </summary>
    public class AnimalController : MonoBehaviour, IDamageable
    {
        public static readonly List<AnimalController> All = new List<AnimalController>();

        public AnimalKind kind;
        public int packId;

        public bool IsDead { get; private set; }
        public bool IsPredator => kind == AnimalKind.Wolf || kind == AnimalKind.Bear;
        public bool IsHunting => _state == State.Hunt;

        private enum State
        {
            Wander,
            Flee,
            Hunt
        }

        private State _state = State.Wander;
        private float _health;
        private float _walkSpeed, _runSpeed, _sight, _hearing, _damage, _attackRange, _attackCooldown;
        private float _stateTimer, _nextAttack, _groundCheck, _waterCheck;
        private Vector3 _wanderTarget, _home;
        private Vector3 _fleeDir;
        private Chunk _chunk;
        private float _bob;
        private Transform _body;

        private void OnEnable()
        {
            All.Add(this);
            CombatHooks.NoiseMade += OnNoise;
        }

        private void OnDisable()
        {
            All.Remove(this);
            CombatHooks.NoiseMade -= OnNoise;
        }

        // ---------------- creation ----------------

        public static AnimalController Spawn(AnimalKind kind, Vector3 position, Transform parent, int packId, float yaw)
        {
            var root = new GameObject("Animal_" + kind);
            root.transform.SetParent(parent, true);
            root.transform.position = position;
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var a = root.AddComponent<AnimalController>();
            a.kind = kind;
            a.packId = packId;
            a.Configure();
            a.BuildBody();
            return a;
        }

        private void Configure()
        {
            switch (kind)
            {
                case AnimalKind.Deer: _health = 60f; _walkSpeed = 1.6f; _runSpeed = 9f; _sight = 28f; _hearing = 1.2f; break;
                case AnimalKind.Rabbit: _health = 12f; _walkSpeed = 1.2f; _runSpeed = 7.5f; _sight = 12f; _hearing = 1f; break;
                case AnimalKind.Wolf: _health = 55f; _walkSpeed = 2f; _runSpeed = 7.2f; _sight = 32f; _hearing = 1.3f; _damage = 9f; _attackRange = 1.9f; _attackCooldown = 1.3f; break;
                case AnimalKind.Bear: _health = 220f; _walkSpeed = 1.4f; _runSpeed = 6.3f; _sight = 22f; _hearing = 1f; _damage = 26f; _attackRange = 2.6f; _attackCooldown = 2.2f; break;
            }
            _home = transform.position;
            PickWanderTarget();
        }

        private void BuildBody()
        {
            var body = new GameObject("Body").transform;
            body.SetParent(transform, false);
            _body = body;
            Color c; Vector3 size; float legH;
            switch (kind)
            {
                case AnimalKind.Deer: c = new Color(0.55f, 0.38f, 0.22f); size = new Vector3(0.45f, 0.55f, 1.3f); legH = 0.75f; break;
                case AnimalKind.Rabbit: c = new Color(0.62f, 0.55f, 0.45f); size = new Vector3(0.22f, 0.22f, 0.35f); legH = 0.06f; break;
                case AnimalKind.Wolf: c = new Color(0.42f, 0.42f, 0.44f); size = new Vector3(0.35f, 0.4f, 1.0f); legH = 0.45f; break;
                default: c = new Color(0.3f, 0.2f, 0.12f); size = new Vector3(0.95f, 0.95f, 1.8f); legH = 0.5f; break;
            }
            var torso = SurvivalPrimitives.Part(PrimitiveType.Cube, body, new Vector3(0f, legH + size.y * 0.5f, 0f), size, c, false);
            torso.name = "Torso";
            var head = SurvivalPrimitives.Part(PrimitiveType.Cube, body, new Vector3(0f, legH + size.y * 0.85f, size.z * 0.6f), new Vector3(size.x * 0.7f, size.y * 0.55f, size.x * 0.9f), c, false);
            head.name = "Head";
            if (kind == AnimalKind.Deer)
            {
                SurvivalPrimitives.Part(PrimitiveType.Cube, body, new Vector3(0.12f, legH + size.y * 1.25f, size.z * 0.6f), new Vector3(0.04f, 0.35f, 0.04f), new Color(0.8f, 0.75f, 0.6f), false);
                SurvivalPrimitives.Part(PrimitiveType.Cube, body, new Vector3(-0.12f, legH + size.y * 1.25f, size.z * 0.6f), new Vector3(0.04f, 0.35f, 0.04f), new Color(0.8f, 0.75f, 0.6f), false);
            }
            if (kind == AnimalKind.Rabbit)
            {
                SurvivalPrimitives.Part(PrimitiveType.Cube, body, new Vector3(0.04f, legH + 0.32f, 0.16f), new Vector3(0.03f, 0.14f, 0.03f), c, false);
                SurvivalPrimitives.Part(PrimitiveType.Cube, body, new Vector3(-0.04f, legH + 0.32f, 0.16f), new Vector3(0.03f, 0.14f, 0.03f), c, false);
            }
            if (legH > 0.1f)
            {
                float lx = size.x * 0.35f, lz = size.z * 0.38f, lw = Mathf.Max(0.06f, size.x * 0.2f);
                foreach (var p in new[] { new Vector3(lx, 0, lz), new Vector3(-lx, 0, lz), new Vector3(lx, 0, -lz), new Vector3(-lx, 0, -lz) })
                    SurvivalPrimitives.Part(PrimitiveType.Cube, body, p + Vector3.up * legH * 0.5f, new Vector3(lw, legH, lw), c * 0.85f, false);
            }

            var col = gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, legH + size.y * 0.5f, size.z * 0.1f);
            col.size = new Vector3(size.x * 1.1f, size.y + legH * 0.5f, size.z * 1.3f);
        }

        // ---------------- AI ----------------

        private void Update()
        {
            if (IsDead) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 target = SurvivalDirector.PlayerPosition;
            bool playerOnFoot = SurvivalDirector.Player != null && SurvivalDirector.Player.activeInHierarchy;
            float dist = Vector3.Distance(transform.position, target);
            bool night = DayNightCycle.Instance != null && DayNightCycle.Instance.IsNight;
            float sight = _sight * (night ? (IsPredator ? 1.3f : 0.7f) : 1f);
            var stealth = PlayerStealthController.Instance;
            if (stealth != null && stealth.IsCrouching) sight *= 0.5f;

            _stateTimer -= dt;
            switch (_state)
            {
                case State.Wander:
                    if (playerOnFoot && dist < sight)
                    {
                        if (IsPredator) StartHunt();
                        else StartFlee(target);
                        break;
                    }
                    MoveTowards(_wanderTarget, _walkSpeed, dt);
                    if ((Flat(_wanderTarget) - Flat(transform.position)).sqrMagnitude < 1f || _stateTimer <= 0f) PickWanderTarget();
                    break;

                case State.Flee:
                    MoveDirection(_fleeDir, _runSpeed, dt);
                    if (_stateTimer <= 0f)
                    {
                        if (playerOnFoot && dist < sight * 0.8f) StartFlee(target);
                        else { _state = State.Wander; _home = transform.position; PickWanderTarget(); }
                    }
                    break;

                case State.Hunt:
                    if (!playerOnFoot || dist > sight * 2.2f)
                    {
                        // Player drove away or got far: give up
                        _state = State.Wander;
                        PickWanderTarget();
                        break;
                    }
                    if (kind == AnimalKind.Wolf && _health < 15f)
                    {
                        StartFlee(target);
                        break;
                    }
                    if (dist > _attackRange * 0.85f) MoveTowards(target, _runSpeed, dt);
                    else Face(target, dt);
                    if (dist <= _attackRange && Time.time >= _nextAttack) Bite();
                    break;
            }

            // little body bob while moving
            if (_body != null) _body.localPosition = new Vector3(0f, Mathf.Abs(Mathf.Sin(_bob)) * 0.05f, 0f);
        }

        private void Bite()
        {
            _nextAttack = Time.time + _attackCooldown;
            var player = SurvivalDirector.Player;
            if (player == null) return;
            var health = player.GetComponent<PlayerHealth>();
            if (health == null || health.IsDead) return;
            health.TakeDamage(_damage, player.transform.position + Vector3.up, (player.transform.position - transform.position).normalized);
            PlayerVitals.Instance?.AddStress(SurvivalSettings.Instance.stressPredatorAttack * 0.5f);
        }

        private void StartFlee(Vector3 from)
        {
            _state = State.Flee;
            _fleeDir = Flat(transform.position - from).normalized;
            if (_fleeDir.sqrMagnitude < 0.01f) _fleeDir = transform.forward;
            _fleeDir = Quaternion.Euler(0f, Random.Range(-30f, 30f), 0f) * _fleeDir;
            _stateTimer = Random.Range(5f, 9f);
        }

        private void StartHunt()
        {
            if (_state == State.Hunt) return;
            _state = State.Hunt;
            if (packId != 0)
            {
                foreach (var a in All)
                    if (a != this && a.packId == packId && !a.IsDead) a.StartHunt();
            }
            if (Vector3.Distance(transform.position, SurvivalDirector.PlayerPosition) < 40f)
            {
                PlayerVitals.Instance?.AddStress(SurvivalSettings.Instance.stressPredatorAttack);
            }
        }

        private void OnNoise(Vector3 origin, float radius)
        {
            if (IsDead) return;
            float heard = radius * _hearing;
            if ((origin - transform.position).sqrMagnitude > heard * heard) return;
            if (IsPredator) StartHunt();
            else StartFlee(origin);
        }

        private void PickWanderTarget()
        {
            Vector2 r = Random.insideUnitCircle * (kind == AnimalKind.Rabbit ? 6f : 16f);
            _wanderTarget = _home + new Vector3(r.x, 0f, r.y);
            _stateTimer = Random.Range(6f, 14f);
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private void MoveTowards(Vector3 target, float speed, float dt)
        {
            Vector3 dir = Flat(target - transform.position);
            if (dir.sqrMagnitude < 0.01f) return;
            MoveDirection(dir.normalized, speed, dt);
        }

        private void MoveDirection(Vector3 dir, float speed, float dt)
        {
            Vector3 next = transform.position + dir * speed * dt;

            // Do not walk into the sea / rivers: turn around
            _waterCheck -= dt;
            if (_waterCheck <= 0f)
            {
                _waterCheck = 0.4f;
                if (WaterBodies.Query(next + dir * 2f, out _, out _))
                {
                    _fleeDir = -_fleeDir;
                    PickWanderTarget();
                    return;
                }
            }

            next.y = GroundHeight(next, transform.position.y);
            transform.position = next;
            Face(transform.position + dir, dt);
            _bob += dt * speed * 3f;
        }

        private void Face(Vector3 target, float dt)
        {
            Vector3 d = Flat(target - transform.position);
            if (d.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d), dt * 8f);
        }

        private float GroundHeight(Vector3 p, float fallback)
        {
            _groundCheck -= Time.deltaTime;
            if (_chunk == null || _groundCheck <= 0f)
            {
                _groundCheck = 1f;
                _chunk = SurvivalDirector.ChunkAt(p);
            }
            if (_chunk != null && _chunk.ChunkTerrain != null)
            {
                var t = _chunk.ChunkTerrain;
                return t.SampleHeight(p) + t.transform.position.y;
            }
            if (Physics.Raycast(p + Vector3.up * 4f, Vector3.down, out RaycastHit hit, 12f, ~0, QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(transform))
                return hit.point.y;
            return fallback;
        }

        // ---------------- damage ----------------

        public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitNormal)
        {
            if (IsDead) return;
            _health -= damage;
            if (_health <= 0f)
            {
                Die();
                return;
            }
            if (IsPredator) StartHunt();
            else StartFlee(SurvivalDirector.PlayerPosition);
        }

        private void Die()
        {
            IsDead = true;
            _state = State.Wander;
            if (_body != null)
            {
                _body.localPosition = Vector3.zero;
                transform.rotation = transform.rotation * Quaternion.Euler(0f, 0f, 90f);
                transform.position += Vector3.up * 0.25f;
            }
            var col = GetComponent<BoxCollider>();
            if (col != null) col.isTrigger = true; // butcher with [E], no longer blocks
            gameObject.AddComponent<Carcass>().kind = kind;
            if (IsPredator) PlayerVitals.Instance?.AddStressRelief(3f);
            enabled = false;
            All.Remove(this);
        }
    }

    /// <summary>3.2: dead animal, [E] to butcher for raw meat and hide (knife gives more).</summary>
    public class Carcass : MonoBehaviour, IInteractable
    {
        public AnimalKind kind;
        private bool _done;

        public Vector3 InteractPosition => transform.position;
        public bool CanInteract => !_done;

        public string GetPrompt()
        {
            string n = kind == AnimalKind.Deer ? "Geyik" : kind == AnimalKind.Rabbit ? "Tavşan" : kind == AnimalKind.Wolf ? "Kurt" : "Ayı";
            return $"{n} leşini kes (et, deri)";
        }

        public string Interact(PlayerInventory user)
        {
            var s = SurvivalSettings.Instance;
            var knife = s.Item("Item_Knife");
            bool hasKnife = knife != null && user.Storage.GetCount(knife) > 0 || HoldsKnife(user);
            int meat, hide;
            switch (kind)
            {
                case AnimalKind.Deer: meat = 4; hide = 2; break;
                case AnimalKind.Rabbit: meat = 1; hide = 1; break;
                case AnimalKind.Wolf: meat = 2; hide = 2; break;
                default: meat = 6; hide = 3; break;
            }
            if (hasKnife) meat += 1;
            else hide = Mathf.Max(0, hide - 1);
            if (Random.value < StatSettings.Instance.natureExtraYieldChance * CharacterStats.Frac(StatType.Nature)) meat += 1;

            SurvivalDirector.GiveItem(s.Item("Item_RawMeat"), meat);
            if (hide > 0) SurvivalDirector.GiveItem(s.Item("Item_RawHide"), hide);
            CharacterStats.Instance?.Train(StatType.Nature, StatSettings.Instance.naturePerGather * 1.5f);
            _done = true;
            Destroy(gameObject, 0.1f);
            return $"Kesildi: Çiğ Et x{meat}" + (hide > 0 ? $", Ham Deri x{hide}" : "") + (hasKnife ? "" : " (bıçakla daha fazla çıkar)");
        }

        private static bool HoldsKnife(PlayerInventory user)
        {
            var eq = user != null ? user.GetComponent<PlayerEquipment>() : null;
            var w = eq != null ? eq.Get(EquipSlot.Weapon) : null;
            return w != null && w.name == "Item_Knife";
        }
    }
}
