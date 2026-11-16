using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using EndlessSurvival.Inventory;

namespace EndlessCombat.Combat.Editor
{
    /// <summary>
    /// Design preview for the weapon slot revision (long gun / pistol / melee / shield, one hand vs two hand grip).
    /// Carry spots that are not built yet (pistol on the hip, melee on the left hip, heavy weapon crossed on the back)
    /// are mocked with mesh-only copies of the weapon prefabs. Gameplay code is not changed.
    /// Writes PoseShots/slot_*.png + slot_log.txt, then quits.
    /// Run the editor (not batchmode): -executeMethod EndlessCombat.Combat.Editor.WeaponSlotScenarioCapture.Run
    /// </summary>
    [InitializeOnLoad]
    public static class WeaponSlotScenarioCapture
    {
        private const string Flag = "EndlessCombat.WeaponSlotScenarioCapture.Active";
        private const string ScenePath = "Assets/Scenes/Game.unity";
        private const int W = 420, H = 620;

        private static double _t0 = -1, _stepAt;
        private static int _step;
        private static Camera _cam;
        private static RenderTexture _rt;
        private static GameObject _player;
        private static PlayerShooter _shooter;
        private static Animator _animator;
        private static CombatBody _actor;
        private static ShieldHolder _holder;
        private static readonly List<GameObject> Props = new List<GameObject>();
        private static readonly StringWriter Log = new StringWriter();
        private static readonly List<(System.Action act, double wait)> Plan = new List<(System.Action, double)>();

        static WeaponSlotScenarioCapture()
        {
            if (!EditorPrefs.GetBool(Flag, false)) return;
            EditorApplication.update += Tick;
        }

        [MenuItem("Tools/Combat/Debug/Weapon Slot Scenario Capture")]
        public static void Run()
        {
            EditorPrefs.SetBool(Flag, true);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        private const string DollFlag = "EndlessCombat.WeaponSlotScenarioCapture.Doll";
        private static bool DollMode => EditorPrefs.GetBool(DollFlag, false);

        /// <summary>Inventory paperdoll: transparent full body renders + bone screen points (PoseShots/doll_*.png, doll_points.txt).</summary>
        [MenuItem("Tools/Combat/Debug/Inventory Paperdoll Capture")]
        public static void RunDoll()
        {
            EditorPrefs.SetBool(DollFlag, true);
            Run();
        }

        private static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "PoseShots"));

        private static void Finish()
        {
            EditorPrefs.DeleteKey(Flag);
            EditorPrefs.DeleteKey(DollFlag);
            EditorApplication.update -= Tick;
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(Path.Combine(OutDir, "slot_log.txt"), Log.ToString());
            if (System.Environment.GetCommandLineArgs().Contains("-executeMethod")) EditorApplication.Exit(0);
            else EditorApplication.ExitPlaymode();
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            if (_step > 0 && Time.timeScale < 0.5f) Time.timeScale = 1f;
            double now = EditorApplication.timeSinceStartup;
            if (_t0 < 0)
            {
                _t0 = now;
                _stepAt = now + 6.0;
                Directory.CreateDirectory(OutDir);
                if (!DollMode) foreach (var f in Directory.GetFiles(OutDir, "slot_*.png")) File.Delete(f);
                return;
            }
            if (now - _t0 > 240) { Log.WriteLine("timeout"); Finish(); return; }
            if (now < _stepAt) return;
            try
            {
                if (_step == 0) { if (!Setup()) { Finish(); return; } _stepAt = now + 0.5; }
                else
                {
                    int i = _step - 1;
                    if (i >= Plan.Count) { Finish(); return; }
                    Plan[i].act();
                    _stepAt = now + Plan[i].wait;
                }
            }
            catch (System.Exception e)
            {
                Log.WriteLine("step " + _step + " failed: " + e);
                Finish();
                return;
            }
            _step++;
        }

        private static bool Setup()
        {
            _player = GameObject.FindGameObjectWithTag("Player");
            if (_player == null) { Log.WriteLine("no player"); return false; }
            _shooter = _player.GetComponent<PlayerShooter>();
            _animator = _player.GetComponent<Animator>();
            _actor = _player.GetComponent<CombatBody>();
            _holder = ShieldHolder.GetOrAdd(_player);
            var melee = _player.GetComponent<PlayerMelee>();
            if (melee != null) melee.enabled = false; // shield side / guard are set by the plan
            Time.timeScale = 1f;

            var cam = new GameObject("SlotCaptureCam");
            _cam = cam.AddComponent<Camera>();
            _cam.fieldOfView = 38f;
            _cam.nearClipPlane = 0.05f;
            _cam.enabled = false;
            _rt = new RenderTexture(W, H, 24);
            _cam.targetTexture = _rt;

            foreach (var p in new[] { "Weapon_AssaultRifle", "Weapon_Pistol", "Weapon_WoodenClub", "Weapon_BaseballBat", "Weapon_Knife" })
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Weapons/{p}.prefab");
                if (go == null) { Log.WriteLine("missing " + p); continue; }
                Bounds b = LocalBounds(go);
                Log.WriteLine($"{p}: rootLocalPos={go.transform.localPosition} rootEuler={go.transform.localEulerAngles} bounds c={b.center} size={b.size}");
            }

            BuildPlan();
            return true;
        }

        // ------------------------------------------------------------------ scenario helpers

        private static WeaponController Prefab(string name)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Weapons/{name}.prefab");
            return go != null ? go.GetComponent<WeaponController>() : null;
        }

        private static ItemDefinition Item(string name) => AssetDatabase.LoadAssetAtPath<ItemDefinition>($"Assets/Data/Items/{name}.asset");

        private static void Reset()
        {
            foreach (var p in Props) if (p != null) Object.Destroy(p);
            Props.Clear();
            _actor.SetGuard(false);
            _holder.GunSide = false;
            _animator.SetBool("IsAiming", false);
            if (_shooter.IsHolstered) _shooter.SetHolstered(false);
            _shooter.ReplaceWeapon(null);
            _holder.Set(null);
            SetRaise(false);
        }

        private static void Hand(string weapon)
        {
            _shooter.ReplaceWeapon(Prefab(weapon));
            FaceAim();
        }

        /// <summary>Turns the character to the main camera's direction (aiming follows the camera).</summary>
        private static void FaceAim()
        {
            var main = Camera.main;
            if (main == null) return;
            Vector3 f = Vector3.ProjectOnPlane(main.transform.forward, Vector3.up);
            if (f.sqrMagnitude > 0.01f) _player.transform.rotation = Quaternion.LookRotation(f.normalized, Vector3.up);
        }

        private static void Shield(string item, bool onBack)
        {
            _holder.Set(Item(item));
            _holder.SetOnBack(onBack);
            _actor.RefreshShield();
        }

        private static void SetRaise(bool raised)
        {
            var f = typeof(PlayerShooter).GetField("pistolRaiseUntil", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (f != null) f.SetValue(_shooter, raised ? Time.time + 100f : -1f);
        }

        /// <summary>Mesh-only copy of a weapon prefab (no scripts, no colliders).</summary>
        private static GameObject Visual(string weapon)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Weapons/{weapon}.prefab");
            var root = new GameObject("Mock_" + weapon);
            if (prefab == null) return root;
            Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
            foreach (var mr in prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var part = new GameObject(mr.name);
                part.transform.SetParent(root.transform, false);
                Matrix4x4 m = toRoot * mr.transform.localToWorldMatrix;
                part.transform.localPosition = m.GetColumn(3);
                part.transform.localRotation = m.rotation;
                part.transform.localScale = m.lossyScale;
                part.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                part.AddComponent<MeshRenderer>().sharedMaterials = mr.sharedMaterials;
            }
            Props.Add(root);
            return root;
        }

        private static Bounds LocalBounds(GameObject prefab)
        {
            Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
            bool any = false;
            Bounds b = default;
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Matrix4x4 m = toRoot * mf.transform.localToWorldMatrix;
                Bounds mb = mf.sharedMesh.bounds;
                foreach (var sx in new[] { -1f, 1f })
                foreach (var sy in new[] { -1f, 1f })
                foreach (var sz in new[] { -1f, 1f })
                {
                    Vector3 p = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3(sx, sy, sz)));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            return b;
        }

        /// <summary>Same spot and angle the real holster uses (rifle on the back).</summary>
        private static void MockOnHolsterSocket(string weapon)
        {
            var v = Visual(weapon);
            Transform socket = _shooter.HolsterSocket;
            if (socket == null) { _shooter.SetHolstered(true); _shooter.SetHolstered(false); socket = _shooter.HolsterSocket; }
            v.transform.SetParent(socket, false);
            v.transform.localPosition = Vector3.zero;
            v.transform.localRotation = Quaternion.identity;
        }

        /// <summary>
        /// Places a mock weapon with its long axis along a body direction.
        /// offset / longDir / upDir are in the character's space (x right, y up, z forward).
        /// flip turns the model end for end (grip vs. muzzle / head).
        /// </summary>
        private static void MockOnBody(string weapon, HumanBodyBones bone, Vector3 offset, Vector3 longDir, Vector3 upDir, bool flip)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Weapons/{weapon}.prefab");
            var v = Visual(weapon);
            if (prefab == null) return;
            Bounds b = LocalBounds(prefab);
            Vector3 s = b.size;
            // The pivot is the grip: grip -> model center is the long axis (works for models tilted in the prefab)
            Vector3 modelLong = b.center.magnitude > 0.05f ? b.center.normalized
                : (s.z >= s.x && s.z >= s.y ? Vector3.forward : (s.y >= s.x ? Vector3.up : Vector3.right));
            Vector3 modelUp = Mathf.Abs(Vector3.Dot(modelLong, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up;
            modelUp = Vector3.ProjectOnPlane(modelUp, modelLong).normalized;
            if (flip) modelLong = -modelLong;

            Transform c = _player.transform;
            Vector3 wLong = c.TransformDirection(longDir.normalized);
            Vector3 wUp = c.TransformDirection(upDir.normalized);
            Quaternion rot = Quaternion.LookRotation(wLong, wUp) * Quaternion.Inverse(Quaternion.LookRotation(modelLong, modelUp));

            Transform anchor = _animator.GetBoneTransform(bone);
            Vector3 target = anchor.position + c.TransformDirection(offset);
            v.transform.SetPositionAndRotation(target - rot * b.center, rot);
            v.transform.SetParent(anchor, true);
        }

        private static void PistolOnHip() =>
            MockOnBody("Weapon_Pistol", HumanBodyBones.Hips, new Vector3(0.19f, -0.06f, -0.02f), Vector3.down, Vector3.back, false);

        private static void ClubOnHip(string weapon = "Weapon_WoodenClub") =>
            MockOnBody(weapon, HumanBodyBones.Hips, new Vector3(-0.2f, -0.2f, 0f), new Vector3(-0.08f, -1f, 0.18f), Vector3.left, false);

        private static void BatCrossedOnBack() =>
            MockOnBody("Weapon_BaseballBat", HumanBodyBones.Chest, new Vector3(0f, -0.05f, -0.2f), new Vector3(-0.7f, 1f, 0f), Vector3.back, false);

        // ------------------------------------------------------------------ plan

        private static void BuildPlan()
        {
            if (DollMode) { BuildDollPlan(); return; }
            void Add(System.Action a, double w) => Plan.Add((a, w));
            void Shoot(string label) => Add(() => { Shot("slot_" + label); Dump(label); }, 0.2);

            // 01 Everything stowed: rifle + shield on the back, pistol right hip, club left hip
            Add(() =>
            {
                Reset();
                Shield("Item_WoodenShield", true);
                MockOnHolsterSocket("Weapon_AssaultRifle");
                PistolOnHip();
                ClubOnHip();
            }, 1.2);
            Shoot("01_hepsi_yerinde");

            // 02 Long gun, two hands, aiming: shield on the back
            Add(() =>
            {
                Reset();
                Hand("Weapon_AssaultRifle");
                Shield("Item_WoodenShield", true);
                PistolOnHip();
                ClubOnHip();
                _animator.SetBool("IsAiming", true);
            }, 1.2);
            Shoot("02_uzun_namlu_cift_el");

            // 03 Long gun one hand + riot shield (mock: pistol pose, rifle model in the hand)
            Add(() =>
            {
                Reset();
                Hand("Weapon_Pistol");
                foreach (var r in _shooter.CurrentWeapon.GetComponentsInChildren<Renderer>()) r.enabled = false;
                var rifle = Visual("Weapon_AssaultRifle");
                var rp = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Weapons/Weapon_AssaultRifle.prefab");
                rifle.transform.SetParent(_shooter.CurrentWeapon.transform.parent, false);
                rifle.transform.localPosition = rp.transform.localPosition;
                rifle.transform.localRotation = rp.transform.localRotation;
                Shield("Item_RiotShield", false);
                _actor.SetGuard(true);
                _holder.GunSide = true;
                _animator.SetBool("IsAiming", true);
                SetRaise(true);
                PistolOnHip();
                ClubOnHip();
            }, 1.2);
            Shoot("03_uzun_namlu_tek_el_kalkan");

            // 04 Pistol two hands, aiming: shield + rifle on the back
            Add(() =>
            {
                Reset();
                Hand("Weapon_Pistol");
                Shield("Item_WoodenShield", true);
                MockOnHolsterSocket("Weapon_AssaultRifle");
                ClubOnHip();
                _animator.SetBool("IsAiming", true);
                SetRaise(true);
            }, 1.2);
            Shoot("04_tabanca_cift_el");

            // 05 Pistol one hand + riot shield raised
            Add(() =>
            {
                Reset();
                Hand("Weapon_Pistol");
                Shield("Item_RiotShield", false);
                _actor.SetGuard(true);
                _holder.GunSide = true;
                _animator.SetBool("IsAiming", true);
                SetRaise(true);
                MockOnHolsterSocket("Weapon_AssaultRifle");
                ClubOnHip();
            }, 1.2);
            Shoot("05_tabanca_tek_el_kalkan");

            // 06 Club one hand + wooden shield, guard up
            Add(() =>
            {
                Reset();
                Hand("Weapon_WoodenClub");
                Shield("Item_WoodenShield", false);
                _actor.SetGuard(true);
                MockOnHolsterSocket("Weapon_AssaultRifle");
                PistolOnHip();
            }, 1.2);
            Shoot("06_sopa_tek_el_kalkan");

            // 07 Club one hand + shield: mid swing
            Add(() =>
            {
                _actor.SetGuard(false);
                _actor.ExternalSpend = c => true;
                var mw = _shooter.CurrentWeapon != null ? _shooter.CurrentWeapon.GetComponent<MeleeWeapon>() : null;
                _actor.TryAttack(mw != null ? mw.stats : MeleeStats.Default, MeleeCategory.OneHand);
            }, 0.32);
            Shoot("07_sopa_tek_el_kalkan_vurus");

            // 08 Bat two hands: shield on the back, mid swing
            Add(() =>
            {
                Reset();
                Hand("Weapon_BaseballBat");
                Shield("Item_WoodenShield", true);
                MockOnHolsterSocket("Weapon_AssaultRifle");
                PistolOnHip();
            }, 1.2);
            Shoot("08_sopa_cift_el");
            Add(() =>
            {
                _actor.ExternalSpend = c => true;
                var mw = _shooter.CurrentWeapon != null ? _shooter.CurrentWeapon.GetComponent<MeleeWeapon>() : null;
                _actor.TryAttack(mw != null ? mw.stats : MeleeStats.Default, MeleeCategory.TwoHand);
            }, 0.4);
            Shoot("09_sopa_cift_el_vurus");

            // 10 Knife + shield
            Add(() =>
            {
                Reset();
                Hand("Weapon_Knife");
                Shield("Item_WoodenShield", false);
                _actor.SetGuard(true);
                MockOnHolsterSocket("Weapon_AssaultRifle");
                PistolOnHip();
            }, 1.2);
            Shoot("10_bicak_kalkan");

            // 11 Fists + shield
            Add(() =>
            {
                Reset();
                Shield("Item_WoodenShield", false);
                _actor.SetGuard(true);
                MockOnHolsterSocket("Weapon_AssaultRifle");
                PistolOnHip();
                ClubOnHip();
            }, 1.2);
            Shoot("11_yumruk_kalkan");

            // 12 Heavy weapon stowed: bat crossed with the rifle on the back (shield left off so the X is visible)
            Add(() =>
            {
                Reset();
                MockOnHolsterSocket("Weapon_AssaultRifle");
                BatCrossedOnBack();
                PistolOnHip();
            }, 1.2);
            Shoot("12_agir_silah_sirtta_x");
        }

        // ------------------------------------------------------------------ paperdoll

        private const int DW = 700, DH = 1000;

        private static void BuildDollPlan()
        {
            // Everything carried and visible: rifle on the back, pistol right hip, club left hip, shield hanging on the left arm
            Plan.Add((() =>
            {
                Reset();
                Shield("Item_WoodenShield", false);
                MockOnHolsterSocket("Weapon_AssaultRifle");
                PistolOnHip();
                ClubOnHip();
            }, 1.5));
            foreach (var view in new[] { "front", "back", "front34", "back34" })
            {
                string v = view;
                Plan.Add((() => DollShot(v), 0.3));
            }
        }

        private static void DollShot(string view)
        {
            Transform c = _player.transform;
            Vector3 dir = view == "front" ? c.forward
                : view == "back" ? -c.forward
                : view == "front34" ? (c.forward - c.right * 0.7f).normalized
                : (-c.forward - c.right * 0.7f).normalized;

            int layer = _player.layer;
            foreach (var p in Props) foreach (var t in p.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            var shieldVis = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).FirstOrDefault(t => t.name.StartsWith("Shield_"));
            if (shieldVis != null) foreach (var t in shieldVis.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

            var rt = new RenderTexture(DW, DH, 24, RenderTextureFormat.ARGB32);
            _cam.targetTexture = rt;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0, 0, 0, 0);
            _cam.cullingMask = 1 << layer;
            _cam.fieldOfView = 30f;
            _cam.transform.position = c.position + dir * 4.3f + Vector3.up * 1.0f;
            _cam.transform.LookAt(c.position + Vector3.up * 0.92f);
            _cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(DW, DH, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, DW, DH), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(OutDir, "doll_" + view + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            // Screen points (top-left origin) of the body parts and carried items
            var sb = new System.Text.StringBuilder();
            void P(string name, Vector3 world)
            {
                Vector3 s = _cam.WorldToScreenPoint(world);
                sb.AppendLine($"{view} {name} {Mathf.RoundToInt(s.x)} {Mathf.RoundToInt(DH - s.y)}");
            }
            void B(string name, HumanBodyBones bone) { var t = _animator.GetBoneTransform(bone); if (t != null) P(name, t.position); }
            Transform head = _animator.GetBoneTransform(HumanBodyBones.Head);
            if (head != null) P("head", head.position + Vector3.up * 0.1f);
            B("chest", HumanBodyBones.Chest);
            B("leftArm", HumanBodyBones.LeftLowerArm);
            B("rightArm", HumanBodyBones.RightLowerArm);
            B("leftLeg", HumanBodyBones.LeftLowerLeg);
            B("rightLeg", HumanBodyBones.RightUpperLeg);
            B("leftFoot", HumanBodyBones.LeftFoot);
            B("rightFoot", HumanBodyBones.RightFoot);
            foreach (var p in Props)
            {
                var rs = p.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) continue;
                Bounds b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                P(p.name, b.center);
            }
            if (shieldVis != null)
            {
                var rs = shieldVis.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0) { Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); P("shield", b.center); }
            }
            File.AppendAllText(Path.Combine(OutDir, "doll_points.txt"), sb.ToString());
            Log.WriteLine("doll " + view + " ok");
        }

        private static void Dump(string label)
        {
            Log.WriteLine($"{label}: hand={_shooter.HandType} holstered={_shooter.IsHolstered} shieldUsable={_holder.Usable} " +
                          $"guarding={_actor.IsGuarding} state={_actor.State} props={Props.Count}");
        }

        // ------------------------------------------------------------------ capture

        private static void Shot(string name)
        {
            Transform c = _player.transform;
            var a = Render(c, (c.forward + c.right * 0.55f).normalized * 2.7f + Vector3.up * 1.25f);
            var b = Render(c, (-c.forward + c.right * 0.35f).normalized * 2.7f + Vector3.up * 1.45f);
            var d = Render(c, (-c.right + c.forward * 0.15f).normalized * 2.7f + Vector3.up * 1.25f);
            var combo = new Texture2D(W * 3, H, TextureFormat.RGB24, false);
            combo.SetPixels(0, 0, W, H, a.GetPixels());
            combo.SetPixels(W, 0, W, H, b.GetPixels());
            combo.SetPixels(W * 2, 0, W, H, d.GetPixels());
            combo.Apply();
            File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), combo.EncodeToPNG());
            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
            Object.DestroyImmediate(d);
            Object.DestroyImmediate(combo);
        }

        private static Texture2D Render(Transform character, Vector3 offset)
        {
            _cam.transform.position = character.position + offset;
            _cam.transform.LookAt(character.position + Vector3.up * 1.05f);
            _cam.Render();
            RenderTexture.active = _rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            return tex;
        }
    }
}
