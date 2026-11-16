using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using EndlessCombat.Combat;
using EndlessSurvival.Inventory;

namespace EndlessSurvival.Survival
{
    /// <summary>
    /// Karakter tab: the character model in the middle, every body slot next to its body part with a leader line.
    ///   Left column (the character's right side): head, back = long gun, right hip = pistol, right hand = melee, feet
    ///   Right column (the character's left side): torso, arms, left hand = shield, legs, quick item
    ///   Right panel: the selected item, one / two hand grip, matching backpack items, weight and ammo per kind
    /// Gamepad: D-pad / left stick choose a slot, A opens the matching backpack items (A again equips), X takes it off,
    /// Y switches the grip, right stick turns the character, B goes back / closes. Mouse: click, drag the character.
    /// The model is the real player rendered by a small camera into a texture (only the player, moved for that one
    /// render to an unused layer).
    /// </summary>
    public partial class SurvivalPanel
    {
        private const int PreviewLayer = 31;
        private const EquipSlot QuickSlot = (EquipSlot)(-1);

        private struct DollSlot
        {
            public string title;
            public EquipSlot slot;
            public Color color;
            public int icon;
            public HumanBodyBones bone;
            public string pad;
        }

        private static readonly Color LongGunColor = new Color(0.95f, 0.71f, 0.19f);
        private static readonly Color PistolColor = new Color(0.37f, 0.71f, 0.94f);
        private static readonly Color MeleeColor = new Color(0.9f, 0.42f, 0.35f);
        private static readonly Color ShieldColor = new Color(0.56f, 0.82f, 0.42f);
        private static readonly Color ApparelColor = new Color(0.73f, 0.65f, 0.91f);
        private static readonly Color QuickColor = new Color(0.68f, 0.71f, 0.75f);
        private static readonly Color StageColor = new Color(0.075f, 0.085f, 0.105f, 1f);
        private static readonly Color Gold = new Color(1f, 0.8f, 0.35f);

        // Index 0-4 left column, 5-9 right column (same rows)
        private static readonly DollSlot[] DollSlots =
        {
            new DollSlot { title = "KAFA", slot = EquipSlot.Head, color = ApparelColor, icon = 0, bone = HumanBodyBones.Head },
            new DollSlot { title = "SIRT · UZUN NAMLU", slot = EquipSlot.LongGun, color = LongGunColor, icon = 1, bone = HumanBodyBones.Chest, pad = "↑" },
            new DollSlot { title = "SAĞ BEL · TABANCA", slot = EquipSlot.Pistol, color = PistolColor, icon = 2, bone = HumanBodyBones.RightUpperLeg, pad = "←" },
            new DollSlot { title = "SAĞ EL · YAKIN DÖVÜŞ", slot = EquipSlot.Melee, color = MeleeColor, icon = 3, bone = HumanBodyBones.RightHand, pad = "→" },
            new DollSlot { title = "AYAKLAR", slot = EquipSlot.Feet, color = ApparelColor, icon = 4, bone = HumanBodyBones.RightFoot },
            new DollSlot { title = "GÖVDE", slot = EquipSlot.Torso, color = ApparelColor, icon = 5, bone = HumanBodyBones.Chest },
            new DollSlot { title = "KOLLAR", slot = EquipSlot.Arms, color = ApparelColor, icon = 6, bone = HumanBodyBones.LeftLowerArm },
            new DollSlot { title = "SOL EL · KALKAN", slot = EquipSlot.Shield, color = ShieldColor, icon = 7, bone = HumanBodyBones.LeftHand },
            new DollSlot { title = "BACAKLAR", slot = EquipSlot.Legs, color = ApparelColor, icon = 8, bone = HumanBodyBones.LeftLowerLeg },
            new DollSlot { title = "HIZLI EŞYA", slot = QuickSlot, color = QuickColor, icon = 9, bone = HumanBodyBones.LastBone, pad = "↓" },
        };

        private int _dollSel = 3;
        private bool _dollList;
        private int _dollListIndex;
        private float _dollYaw;
        private float _dollNavAt;
        private int _backConsumedFrame = -1;
        private readonly List<ItemDefinition> _dollCandidates = new List<ItemDefinition>();

        private Camera _pvCam;
        private RenderTexture _pvRT;
        private Rect _pvRect;
        private bool _pvHooked;
        private readonly List<KeyValuePair<GameObject, int>> _pvLayers = new List<KeyValuePair<GameObject, int>>();
        private static Texture2D[] _icons;
        private static Texture2D _stageTex;
        private GUIStyle _dTitle, _dTitleR, _dName, _dNameR, _dSub, _dSubR, _dPad, _dHead, _dSmall, _dRow;

        /// <summary>True when gamepad B was used by this tab (back out of the item list), so the screen stays open.</summary>
        public static bool PadBackConsumed =>
            Instance != null && ((Showing(HubTab.Character) && Instance._dollList) || (Showing(HubTab.Inventory) && Instance._bagMenu)
                                 || Instance._backConsumedFrame == Time.frameCount);

        // ------------------------------------------------------------------ player parts

        private static GameObject DollPlayer => SurvivalDirector.Player;
        private static PlayerEquipment DollEquipment => DollPlayer != null ? DollPlayer.GetComponent<PlayerEquipment>() : null;
        private static PlayerLoadout DollLoadout => DollPlayer != null ? DollPlayer.GetComponent<PlayerLoadout>() : null;
        private static PlayerInventory DollInventory => DollPlayer != null ? DollPlayer.GetComponent<PlayerInventory>() : null;

        private static ItemDefinition SlotItem(EquipSlot slot)
        {
            if (slot == QuickSlot)
            {
                var l = DollLoadout;
                return l != null ? l.QuickItem : null;
            }
            var eq = DollEquipment;
            return eq != null ? eq.Get(slot) : null;
        }

        private static bool Fits(ItemDefinition item, EquipSlot slot)
        {
            if (item == null) return false;
            if (slot == QuickSlot) return item.IsConsumable;
            if (slot == EquipSlot.LongGun || slot == EquipSlot.Pistol || slot == EquipSlot.Melee)
                return item.category == ItemCategory.Weapon && item.weaponPrefab != null && item.CarrySlot == slot;
            if (slot == EquipSlot.Shield) return item.IsShield;
            return item.category == ItemCategory.Apparel && !item.IsShield && item.equipSlot == slot;
        }

        private void RefreshCandidates()
        {
            _dollCandidates.Clear();
            var inv = DollInventory;
            if (inv == null) return;
            EquipSlot slot = DollSlots[_dollSel].slot;
            foreach (var pair in inv.Storage.Items)
                if (pair.Value > 0 && Fits(pair.Key, slot)) _dollCandidates.Add(pair.Key);
            _dollCandidates.Sort((a, b) => string.CompareOrdinal(a.DisplayName, b.DisplayName));
            if (_dollListIndex >= _dollCandidates.Count) _dollListIndex = Mathf.Max(0, _dollCandidates.Count - 1);
        }

        // ------------------------------------------------------------------ actions

        private void DollEquip(ItemDefinition item)
        {
            EquipSlot slot = DollSlots[_dollSel].slot;
            if (slot == QuickSlot)
            {
                var l = DollLoadout;
                if (l != null) l.QuickItem = item;
                _message = $"Hızlı eşya: {item.DisplayName}";
                return;
            }
            var eq = DollEquipment;
            if (eq == null) return;
            _message = eq.Equip(item);
        }

        private void DollUnequip()
        {
            EquipSlot slot = DollSlots[_dollSel].slot;
            if (slot == QuickSlot)
            {
                var l = DollLoadout;
                if (l != null) l.QuickItem = null;
                return;
            }
            var eq = DollEquipment;
            if (eq != null && eq.Get(slot) != null) _message = eq.Unequip(slot);
        }

        private static PlayerLoadout.Slot WeaponSlotOf(EquipSlot slot) => PlayerLoadout.SlotOf(slot);

        private void DollToggleGrip()
        {
            var l = DollLoadout;
            var ws = WeaponSlotOf(DollSlots[_dollSel].slot);
            if (l == null || ws == PlayerLoadout.Slot.None || !l.GripChangeable(ws)) return;
            l.SetTwoHanded(ws, !l.TwoHanded(ws));
        }

        // ------------------------------------------------------------------ update (gamepad)

        private void UpdateCharacterTab()
        {
            RefreshCandidates();
            var pad = Gamepad.current;
            if (pad == null) return;

            Vector2 stick = pad.leftStick.ReadValue();
            int dx = pad.dpad.left.wasPressedThisFrame ? -1 : pad.dpad.right.wasPressedThisFrame ? 1 : 0;
            int dy = pad.dpad.up.wasPressedThisFrame ? -1 : pad.dpad.down.wasPressedThisFrame ? 1 : 0;
            if (dx == 0 && dy == 0 && stick.magnitude > 0.6f && Time.unscaledTime >= _dollNavAt)
            {
                if (Mathf.Abs(stick.x) > Mathf.Abs(stick.y)) dx = stick.x > 0 ? 1 : -1;
                else dy = stick.y > 0 ? -1 : 1;
                _dollNavAt = Time.unscaledTime + 0.22f;
            }
            if (stick.magnitude < 0.3f) _dollNavAt = 0f;

            if (_dollList)
            {
                if (dy != 0 && _dollCandidates.Count > 0)
                    _dollListIndex = (_dollListIndex + dy + _dollCandidates.Count) % _dollCandidates.Count;
                if (pad.buttonSouth.wasPressedThisFrame && _dollListIndex < _dollCandidates.Count)
                {
                    DollEquip(_dollCandidates[_dollListIndex]);
                    _dollList = false;
                }
                if (pad.buttonEast.wasPressedThisFrame)
                {
                    _dollList = false;
                    _backConsumedFrame = Time.frameCount;
                }
            }
            else
            {
                int col = _dollSel / 5, row = _dollSel % 5;
                if (dx != 0) col = Mathf.Clamp(col + dx, 0, 1);
                if (dy != 0) row = (row + dy + 5) % 5;
                int next = col * 5 + row;
                if (next != _dollSel)
                {
                    _dollSel = next;
                    _dollListIndex = 0;
                    _message = null;
                }
                if (pad.buttonSouth.wasPressedThisFrame)
                {
                    RefreshCandidates();
                    if (_dollCandidates.Count > 0)
                    {
                        _dollList = true;
                        _dollListIndex = 0;
                    }
                    else _message = "Çantada bu yuvaya uyan eşya yok";
                }
                if (pad.buttonWest.wasPressedThisFrame) DollUnequip();
                if (pad.buttonNorth.wasPressedThisFrame) DollToggleGrip();
            }

            float rs = pad.rightStick.ReadValue().x;
            if (Mathf.Abs(rs) > 0.15f) _dollYaw += rs * 160f * Time.unscaledDeltaTime;
        }

        // ------------------------------------------------------------------ preview camera

        private void LateUpdate()
        {
            if (!(IsHubOpen && _tab == HubTab.Character)) return;
            var player = DollPlayer;
            if (player == null || _pvRect.width < 20f) return;
            EnsurePreview(Mathf.RoundToInt(_pvRect.width), Mathf.RoundToInt(_pvRect.height));

            Transform p = player.transform;
            Vector3 center = p.position + Vector3.up * 0.93f;
            float fov = _pvCam.fieldOfView;
            float dist = 1.08f / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            Quaternion look = Quaternion.Euler(0f, p.eulerAngles.y + _dollYaw, 0f);
            _pvCam.transform.position = center + look * Vector3.forward * dist;
            _pvCam.transform.rotation = Quaternion.LookRotation(center - _pvCam.transform.position, Vector3.up);
            _pvCam.enabled = true;
        }

        private void EnsurePreview(int w, int h)
        {
            w = Mathf.Clamp(w, 64, 2048);
            h = Mathf.Clamp(h, 64, 2048);
            if (_pvRT == null || Mathf.Abs(_pvRT.width - w) > 8 || Mathf.Abs(_pvRT.height - h) > 8)
            {
                if (_pvRT != null) { _pvRT.Release(); Destroy(_pvRT); }
                _pvRT = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "CharacterPreview", antiAliasing = 4, hideFlags = HideFlags.DontSave };
                if (_pvCam != null) _pvCam.targetTexture = _pvRT;
            }
            if (_pvCam == null)
            {
                var go = new GameObject("CharacterPreviewCamera") { hideFlags = HideFlags.DontSave };
                _pvCam = go.AddComponent<Camera>();
                _pvCam.enabled = false;
                _pvCam.clearFlags = CameraClearFlags.SolidColor;
                _pvCam.backgroundColor = StageColor;
                _pvCam.cullingMask = 1 << PreviewLayer;
                _pvCam.fieldOfView = 26f;
                _pvCam.nearClipPlane = 0.1f;
                _pvCam.farClipPlane = 30f;
                _pvCam.depth = -50f;
                _pvCam.targetTexture = _pvRT;
                var data = _pvCam.GetUniversalAdditionalCameraData();
                if (data != null)
                {
                    data.renderPostProcessing = false;
                    data.renderShadows = false;
                    data.antialiasing = AntialiasingMode.None;
                }
            }
            if (!_pvHooked)
            {
                RenderPipelineManager.beginCameraRendering += OnBeginPreviewRender;
                RenderPipelineManager.endCameraRendering += OnEndPreviewRender;
                _pvHooked = true;
            }
        }

        // Only for the preview camera's own render: the player is moved to the preview layer and back right after
        private void OnBeginPreviewRender(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam != _pvCam) return;
            RestorePreviewLayers();
            var player = DollPlayer;
            if (player == null) return;
            foreach (var r in player.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || !r.enabled) continue;
                _pvLayers.Add(new KeyValuePair<GameObject, int>(r.gameObject, r.gameObject.layer));
                r.gameObject.layer = PreviewLayer;
            }
        }

        private void OnEndPreviewRender(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam == _pvCam) RestorePreviewLayers();
        }

        private void RestorePreviewLayers()
        {
            for (int i = _pvLayers.Count - 1; i >= 0; i--)
                if (_pvLayers[i].Key != null) _pvLayers[i].Key.layer = _pvLayers[i].Value;
            _pvLayers.Clear();
        }

        private void EndCharacterPreview()
        {
            if (_pvCam != null) _pvCam.enabled = false;
            RestorePreviewLayers();
            _dollList = false;
        }

        private void OnDestroyPreview()
        {
            if (_pvHooked)
            {
                RenderPipelineManager.beginCameraRendering -= OnBeginPreviewRender;
                RenderPipelineManager.endCameraRendering -= OnEndPreviewRender;
                _pvHooked = false;
            }
            RestorePreviewLayers();
            if (_pvCam != null) Destroy(_pvCam.gameObject);
            if (_pvRT != null) { _pvRT.Release(); Destroy(_pvRT); }
        }

        /// <summary>Screen point of a body part on the preview (falls back to a fixed spot on the stage).</summary>
        private Vector2 BodyPoint(DollSlot s, Rect stage, int index)
        {
            var player = DollPlayer;
            var anim = player != null ? player.GetComponent<Animator>() : null;
            Vector3? world = null;
            if (anim != null && anim.isHuman && s.bone != HumanBodyBones.LastBone)
            {
                Transform t = anim.GetBoneTransform(s.bone);
                if (t != null) world = t.position;
                if (s.slot == EquipSlot.Head && t != null) world = t.position + Vector3.up * 0.1f;
            }
            // Carried weapons: point at the weapon itself (back, hip)
            var l = DollLoadout;
            if (l != null)
            {
                var ws = WeaponSlotOf(s.slot);
                var w = ws != PlayerLoadout.Slot.None ? l.Weapon(ws) : null;
                if (w != null)
                {
                    var rends = w.GetComponentsInChildren<Renderer>();
                    if (rends.Length > 0)
                    {
                        Bounds b = rends[0].bounds;
                        foreach (var r in rends) b.Encapsulate(r.bounds);
                        world = b.center;
                    }
                }
            }
            if (s.slot == EquipSlot.Shield && player != null)
            {
                var sh = player.GetComponent<ShieldHolder>();
                var vis = sh != null && sh.HasShield ? sh.Visual : null;
                var rend = vis != null ? vis.GetComponentInChildren<Renderer>() : null;
                if (rend != null) world = rend.bounds.center;
            }
            if (world.HasValue && _pvCam != null)
            {
                Vector3 vp = _pvCam.WorldToViewportPoint(world.Value);
                if (vp.z > 0f) return new Vector2(stage.x + vp.x * stage.width, stage.y + (1f - vp.y) * stage.height);
            }
            float fx = index < 5 ? 0.42f : 0.58f;
            float fy = 0.18f + (index % 5) * 0.16f;
            return new Vector2(stage.x + stage.width * fx, stage.y + stage.height * fy);
        }

        // ------------------------------------------------------------------ drawing

        private void DollStyles()
        {
            if (_dTitle != null) return;
            _dTitle = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperLeft, clipping = TextClipping.Clip };
            _dTitleR = new GUIStyle(_dTitle) { alignment = TextAnchor.UpperRight };
            _dName = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperLeft, normal = { textColor = Color.white }, clipping = TextClipping.Clip };
            _dNameR = new GUIStyle(_dName) { alignment = TextAnchor.UpperRight };
            _dSub = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.UpperLeft, normal = { textColor = new Color(0.72f, 0.75f, 0.8f) }, clipping = TextClipping.Clip };
            _dSubR = new GUIStyle(_dSub) { alignment = TextAnchor.UpperRight };
            _dPad = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.1f, 0.1f, 0.12f) } };
            _dHead = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.62f, 0.66f, 0.72f) } };
            _dSmall = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, normal = { textColor = new Color(0.8f, 0.82f, 0.86f) } };
            _dRow = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white }, clipping = TextClipping.Clip };
        }

        private void DrawCharacterTab(Rect panel)
        {
            DollStyles();
            EnsureIcons();
            SurvivalGUI.Fill(panel, new Color(0.05f, 0.06f, 0.08f, 0.96f));

            var eq = DollEquipment;
            var inv = DollInventory;
            var loadout = DollLoadout;
            if (eq == null || inv == null)
            {
                GUI.Label(new Rect(panel.x + 20, panel.y + 20, 400, 30), "Karakter bulunamadı", SurvivalGUI.Header);
                return;
            }

            // Columns: [left slots][stage][right slots][info panel]
            float pad = 18f;
            float infoW = Mathf.Clamp(panel.width * 0.27f, 280f, 380f);
            float colW = Mathf.Clamp(panel.width * 0.19f, 210f, 290f);
            float top = panel.y + 34f, bottom = panel.yMax - 40f;
            float stageX = panel.x + pad + colW + 16f;
            float stageW = panel.width - infoW - 2f * colW - 2f * pad - 48f;
            if (stageW < 200f)
            {
                infoW = Mathf.Max(240f, infoW - (200f - stageW));
                stageW = Mathf.Max(200f, panel.width - infoW - 2f * colW - 2f * pad - 48f);
            }
            var stage = new Rect(stageX, top, stageW, bottom - top);
            float rightColX = stage.xMax + 16f;
            var info = new Rect(rightColX + colW + 16f, top, panel.xMax - pad - (rightColX + colW + 16f), bottom - top);

            GUI.Label(new Rect(panel.x + pad, panel.y + 8f, colW, 20f), "KARAKTERİN SAĞ TARAFI", _dHead);
            var rightHead = new GUIStyle(_dHead);
            GUI.Label(new Rect(rightColX, panel.y + 8f, colW, 20f), "KARAKTERİN SOL TARAFI", rightHead);

            // Stage: soft floor ellipse + live model
            if (_stageTex == null) _stageTex = MakeStage();
            GUI.DrawTexture(new Rect(stage.x + stage.width * 0.12f, stage.yMax - stage.height * 0.12f, stage.width * 0.76f, stage.height * 0.12f), _stageTex);
            float aspect = 0.62f;
            float modelH = stage.height, modelW = Mathf.Min(stage.width, modelH * aspect);
            _pvRect = new Rect(stage.center.x - modelW * 0.5f, stage.y, modelW, modelH);
            if (_pvRT != null) GUI.DrawTexture(_pvRect, _pvRT, ScaleMode.StretchToFill, false);

            // Mouse: drag the model to turn it
            Event e = Event.current;
            if (e.type == EventType.MouseDrag && _pvRect.Contains(e.mousePosition))
            {
                _dollYaw += e.delta.x * 0.6f;
                e.Use();
            }

            // Slots
            float rowH = (bottom - top) / 5f;
            float box = Mathf.Clamp(rowH - 34f, 54f, 84f);
            for (int i = 0; i < DollSlots.Length; i++)
            {
                bool left = i < 5;
                int row = i % 5;
                float colX = left ? panel.x + pad : rightColX;
                float y = top + row * rowH + (rowH - box) * 0.5f;
                if (!left) y += rowH * 0.18f; // right column sits a little lower, like the mockup
                var boxRect = left ? new Rect(colX + colW - box, y, box, box) : new Rect(colX, y, box, box);
                var labelRect = left ? new Rect(colX, y + 4f, colW - box - 12f, box) : new Rect(colX + box + 12f, y + 4f, colW - box - 12f, box);
                DrawDollSlot(i, boxRect, labelRect, left, stage);
            }

            DrawDollInfo(info, eq, inv, loadout);

            // Hints
            string hints = GameInput.UsingPad
                ? (_dollList ? "✥ Seç    A Kuşan    B Geri" : "✥ Yuva seç    A Değiştir    X Çıkar    Y Tek / çift el    RS Karakteri döndür    B Kapat")
                : "Tıkla: yuva seç    Sağ panelden kuşan    Karakteri sürükle: döndür    TAB Kapat";
            GUI.Label(new Rect(panel.x + pad, panel.yMax - 32f, panel.width - 2f * pad, 24f), hints, _dHead);
            if (!string.IsNullOrEmpty(_message))
                GUI.Label(new Rect(info.x, panel.yMax - 32f, info.width, 24f), _message, SurvivalGUI.Small);
        }

        private void DrawDollSlot(int i, Rect boxRect, Rect labelRect, bool left, Rect stage)
        {
            DollSlot s = DollSlots[i];
            ItemDefinition item = SlotItem(s.slot);
            bool selected = i == _dollSel;

            // Leader line: box edge -> elbow -> body point
            Vector2 body = BodyPoint(s, _pvRect.width > 10f ? _pvRect : stage, i);
            if (s.slot != QuickSlot)
            {
                Vector2 a = left ? new Vector2(boxRect.xMax, boxRect.center.y) : new Vector2(boxRect.x, boxRect.center.y);
                Vector2 elbow = new Vector2(a.x + (left ? 1f : -1f) * Mathf.Min(70f, Mathf.Abs(body.x - a.x) * 0.45f), a.y);
                Color lc = s.color;
                lc.a = selected ? 0.95f : 0.6f;
                DrawLine(a, elbow, lc, selected ? 2.2f : 1.5f);
                DrawLine(elbow, body, lc, selected ? 2.2f : 1.5f);
                DrawRound(new Rect(body.x - 5f, body.y - 5f, 10f, 10f), lc, 5f);
            }

            // Box
            if (selected)
                for (int k = 3; k >= 1; k--)
                    DrawRound(new Rect(boxRect.x - k * 3, boxRect.y - k * 3, boxRect.width + k * 6, boxRect.height + k * 6), new Color(Gold.r, Gold.g, Gold.b, 0.07f * (4 - k)), 14f + k * 3);
            DrawRound(boxRect, new Color(0.1f, 0.115f, 0.14f, 0.97f), 12f);
            Color border = selected ? Gold : s.color;
            if (item == null) border.a = 0.45f;
            GUI.DrawTexture(boxRect, SurvivalGUI.White, ScaleMode.StretchToFill, true, 0f, border, selected ? 3f : 2f, 12f);

            var old = GUI.color;
            Color ic = s.color;
            ic.a = item != null ? 1f : 0.25f;
            GUI.color = ic;
            float isz = boxRect.width * 0.66f;
            GUI.DrawTexture(new Rect(boxRect.center.x - isz * 0.5f, boxRect.center.y - isz * 0.5f, isz, isz), _icons[s.icon]);
            GUI.color = old;

            if (!string.IsNullOrEmpty(s.pad))
            {
                var pr = new Rect(boxRect.center.x - 12f, boxRect.yMax - 9f, 24f, 18f);
                DrawRound(pr, new Color(0.85f, 0.87f, 0.9f, 0.95f), 6f);
                GUI.Label(pr, s.pad, _dPad);
            }

            // Labels
            var titleStyle = left ? _dTitleR : _dTitle;
            titleStyle.normal.textColor = s.color;
            GUI.Label(new Rect(labelRect.x, labelRect.y, labelRect.width, 16f), s.title, titleStyle);
            string name = item != null ? item.DisplayName : "boş";
            var nameStyle = left ? _dNameR : _dName;
            nameStyle.normal.textColor = item != null ? Color.white : new Color(1f, 1f, 1f, 0.4f);
            nameStyle.fontStyle = item != null ? FontStyle.Bold : FontStyle.Italic;
            GUI.Label(new Rect(labelRect.x, labelRect.y + 16f, labelRect.width, 22f), name, nameStyle);
            GUI.Label(new Rect(labelRect.x, labelRect.y + 39f, labelRect.width, 18f), SlotSubtitle(s.slot, item), left ? _dSubR : _dSub);

            // Click
            var hit = new Rect(Mathf.Min(boxRect.x, labelRect.x), boxRect.y, boxRect.width + labelRect.width + 12f, boxRect.height);
            if (GUI.Button(hit, GUIContent.none, GUIStyle.none))
            {
                if (_dollSel != i) _dollListIndex = 0;
                _dollSel = i;
                _dollList = false;
                _message = null;
            }
        }

        private static string SlotSubtitle(EquipSlot slot, ItemDefinition item)
        {
            var l = DollLoadout;
            var inv = DollInventory;
            if (item == null)
            {
                switch (slot)
                {
                    case EquipSlot.Melee: return "boşken yumruk";
                    case EquipSlot.Arms: return "kol koruyucu, eldiven";
                    case QuickSlot: return "sargı, yiyecek, içecek";
                    default: return "";
                }
            }
            var ws = WeaponSlotOf(slot);
            if (ws != PlayerLoadout.Slot.None && l != null)
            {
                var w = l.Weapon(ws);
                string grip = l.TwoHanded(ws) ? "çift el" : "tek el";
                if (w != null && w.IsFirearm) return $"{w.CurrentAmmo}/{w.MagazineCapacity} · {AmmoName(w.AmmoKind)} · {grip}";
                return grip;
            }
            if (slot == EquipSlot.Shield) return l != null && l.ShieldOnArm ? "kolda" : "sırtta (çift el tutuşta)";
            if (slot == QuickSlot) return inv != null ? $"çantada x{inv.Storage.GetCount(item)}" : "";
            return ApparelLine(item);
        }

        private static string ApparelLine(ItemDefinition item)
        {
            var parts = new List<string>();
            if (item.armor > 0f) parts.Add($"+{item.armor:0.#} zırh");
            if (item.insulation > 0f) parts.Add($"+{item.insulation:0.#}°C");
            if (item.waterproof > 0f) parts.Add($"su geçirmez %{Mathf.RoundToInt(item.waterproof * 100f)}");
            return string.Join(" · ", parts);
        }

        private static string AmmoName(AmmoKind kind)
        {
            switch (kind)
            {
                case AmmoKind.Pistol: return "tabanca mermisi";
                case AmmoKind.Rifle: return "tüfek mermisi";
                case AmmoKind.Shotgun: return "av fişeği";
                default: return "";
            }
        }

        private static MeleeWeapon MeleeOfItem(ItemDefinition item) =>
            item != null && item.weaponPrefab != null ? item.weaponPrefab.GetComponent<MeleeWeapon>() : null;

        /// <summary>One number to compare items of the same slot (armor, melee damage, shield block). 0 = none.</summary>
        private static float Score(ItemDefinition item, out string unit)
        {
            unit = "";
            if (item == null) return 0f;
            var melee = MeleeOfItem(item);
            if (melee != null) { unit = "hasar"; return melee.stats.damage; }
            if (item.IsShield) { unit = "% engel"; return item.shield.absorb * 100f; }
            if (item.category == ItemCategory.Apparel) { unit = "zırh"; return item.armor; }
            if (item.IsConsumable) { unit = ""; return 0f; }
            return 0f;
        }

        private void DrawDollInfo(Rect r, PlayerEquipment eq, PlayerInventory inv, PlayerLoadout loadout)
        {
            DollSlot s = DollSlots[_dollSel];
            ItemDefinition item = SlotItem(s.slot);
            var ws = WeaponSlotOf(s.slot);
            float y = r.y;

            // --- selected item card
            float cardH = ws != PlayerLoadout.Slot.None ? 210f : 128f;
            var card = new Rect(r.x, y, r.width, cardH);
            DrawRound(card, new Color(0.1f, 0.115f, 0.14f, 0.95f), 12f);
            GUI.DrawTexture(card, SurvivalGUI.White, ScaleMode.StretchToFill, true, 0f, new Color(s.color.r, s.color.g, s.color.b, 0.5f), 1.5f, 12f);
            var t = new GUIStyle(_dTitle) { normal = { textColor = s.color } };
            GUI.Label(new Rect(card.x + 14, y + 10, card.width - 28, 16), s.title, t);
            _dName.normal.textColor = Color.white;
            _dName.fontStyle = FontStyle.Bold;
            GUI.Label(new Rect(card.x + 14, y + 27, card.width - 28, 24), item != null ? item.DisplayName : (s.slot == EquipSlot.Melee ? "Yumruk" : "Boş"), _dName);
            GUI.Label(new Rect(card.x + 14, y + 52, card.width - 28, 70), ItemDetails(s.slot, item, loadout, inv), _dSmall);

            if (ws != PlayerLoadout.Slot.None && loadout != null)
            {
                bool changeable = loadout.GripChangeable(ws);
                bool two = loadout.TwoHanded(ws);
                float gy = y + 116f, gw = (card.width - 38f) * 0.5f;
                if (GripBox(new Rect(card.x + 14, gy, gw, 34), "Tek el", !two, changeable) && changeable && two) loadout.SetTwoHanded(ws, false);
                if (GripBox(new Rect(card.x + 24 + gw, gy, gw, 34), "Çift el", two, changeable) && changeable && !two) loadout.SetTwoHanded(ws, true);
                GUI.Label(new Rect(card.x + 14, gy + 40, card.width - 28, 56), GripText(ws, loadout), _dSmall);
            }
            y += cardH + 12f;

            // --- matching backpack items
            float weightH = 128f;
            float listH = Mathf.Max(90f, r.yMax - y - weightH - 12f);
            var list = new Rect(r.x, y, r.width, listH);
            DrawRound(list, new Color(0.09f, 0.1f, 0.125f, 0.95f), 12f);
            string listTitle = s.slot == QuickSlot ? "ÇANTADA · HIZLI EŞYA OLABİLENLER" : "ÇANTADA · BU YUVAYA UYANLAR";
            GUI.Label(new Rect(list.x + 14, y + 8, list.width - 28, 18), listTitle, _dHead);
            float ry = y + 30f, rowH = 30f;
            float baseScore = Score(item, out _);
            if (_dollCandidates.Count == 0)
                GUI.Label(new Rect(list.x + 14, ry, list.width - 28, 40), "Uyan eşya yok", _dSmall);
            for (int i = 0; i < _dollCandidates.Count && ry + rowH <= list.yMax - 6f; i++)
            {
                var c = _dollCandidates[i];
                var row = new Rect(list.x + 8, ry, list.width - 16, rowH - 3f);
                bool focus = _dollList && i == _dollListIndex;
                DrawRound(row, focus ? new Color(Gold.r, Gold.g, Gold.b, 0.22f) : new Color(1f, 1f, 1f, 0.04f), 7f);
                int count = inv.Storage.GetCount(c);
                GUI.Label(new Rect(row.x + 10, row.y, row.width - 120, row.height), count > 1 ? $"{c.DisplayName}  x{count}" : c.DisplayName, _dRow);
                float sc = Score(c, out string unit);
                string cmp = "";
                if (!string.IsNullOrEmpty(unit))
                {
                    float d = sc - baseScore;
                    cmp = Mathf.Abs(d) < 0.05f ? $"= {unit}" : $"{(d > 0 ? "+" : "")}{d:0.#} {unit}";
                }
                else if (c.category == ItemCategory.Weapon && c.weaponPrefab != null && c.weaponPrefab.IsFirearm)
                    cmp = AmmoName(c.weaponPrefab.AmmoKind);
                var cs = new GUIStyle(_dSubR) { normal = { textColor = cmp.StartsWith("+") ? new Color(0.55f, 0.9f, 0.5f) : cmp.StartsWith("-") ? new Color(0.95f, 0.5f, 0.45f) : new Color(0.72f, 0.75f, 0.8f) } };
                GUI.Label(new Rect(row.xMax - 118, row.y + 7, 110, row.height), cmp, cs);
                if (GUI.Button(row, GUIContent.none, GUIStyle.none))
                {
                    DollEquip(c);
                    _dollList = false;
                }
                ry += rowH;
            }
            if (item != null)
            {
                var off = new Rect(list.xMax - 96, y + 6, 84, 22);
                if (GUI.Button(off, s.slot == QuickSlot ? "Temizle" : "Çıkar")) DollUnequip();
            }
            y = list.yMax + 12f;

            // --- weight and ammo
            var wr = new Rect(r.x, y, r.width, r.yMax - y);
            DrawRound(wr, new Color(0.09f, 0.1f, 0.125f, 0.95f), 12f);
            float total = inv.Storage.TotalWeight, max = inv.Storage.MaxWeight;
            float carried = eq.CarriedWeight, ammoW = inv.AmmoWeight;
            float bag = Mathf.Max(0f, total - carried - ammoW);
            GUI.Label(new Rect(wr.x + 14, y + 8, wr.width - 28, 18), $"YÜK  {total:0.0} / {(max <= 0f ? "sınırsız" : max.ToString("0.#") + " kg")}", _dHead);
            var bar = new Rect(wr.x + 14, y + 30, wr.width - 28, 12);
            DrawRound(bar, new Color(0f, 0f, 0f, 0.5f), 6f);
            float fx = bar.x;
            void Seg(float kg, Color col)
            {
                if (max <= 0f || kg <= 0f) return;
                float w = Mathf.Min(bar.xMax - fx, bar.width * kg / max);
                if (w <= 0.5f) return;
                SurvivalGUI.Fill(new Rect(fx, bar.y + 1, w, bar.height - 2), col);
                fx += w;
            }
            Seg(carried, LongGunColor);
            Seg(ammoW, PistolColor);
            Seg(bag, new Color(0.62f, 0.66f, 0.72f));
            GUI.Label(new Rect(wr.x + 14, y + 46, wr.width - 28, 18), $"Üstte {carried:0.0} kg · Mermi {ammoW:0.0} kg · Çanta {bag:0.0} kg", _dSub);

            float aw = (wr.width - 28f - 16f) / 3f;
            float ay = y + 70f;
            AmmoBox(new Rect(wr.x + 14, ay, aw, 44), "Tabanca", inv.Count(AmmoKind.Pistol), PistolColor);
            AmmoBox(new Rect(wr.x + 22 + aw, ay, aw, 44), "Tüfek", inv.Count(AmmoKind.Rifle), LongGunColor);
            AmmoBox(new Rect(wr.x + 30 + 2 * aw, ay, aw, 44), "Av fişeği", inv.Count(AmmoKind.Shotgun), MeleeColor);
        }

        private void AmmoBox(Rect r, string label, int count, Color c)
        {
            DrawRound(r, new Color(1f, 1f, 1f, 0.05f), 8f);
            var ls = new GUIStyle(_dTitle) { normal = { textColor = c }, alignment = TextAnchor.UpperCenter };
            GUI.Label(new Rect(r.x, r.y + 4, r.width, 16), label.ToUpperInvariant(), ls);
            var ns = new GUIStyle(_dName) { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
            ns.normal.textColor = Color.white;
            GUI.Label(new Rect(r.x, r.y + 19, r.width, 24), count.ToString(), ns);
        }

        private bool GripBox(Rect r, string label, bool on, bool enabled)
        {
            Color fill = on ? new Color(Gold.r, Gold.g, Gold.b, 0.28f) : new Color(1f, 1f, 1f, 0.05f);
            if (!enabled && !on) fill.a *= 0.5f;
            DrawRound(r, fill, 8f);
            GUI.DrawTexture(r, SurvivalGUI.White, ScaleMode.StretchToFill, true, 0f, on ? Gold : new Color(1f, 1f, 1f, 0.2f), 1.5f, 8f);
            var st = new GUIStyle(_dRow) { alignment = TextAnchor.MiddleCenter, fontStyle = on ? FontStyle.Bold : FontStyle.Normal };
            st.normal.textColor = enabled || on ? Color.white : new Color(1f, 1f, 1f, 0.35f);
            GUI.Label(r, (GameInput.UsingPad && enabled ? "Y  " : "") + label, st);
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        private static string GripText(PlayerLoadout.Slot ws, PlayerLoadout l)
        {
            // Grips are fixed since Core_Feel_Plan stage 2 (no one / two hand switch)
            bool two = l.TwoHanded(ws);
            switch (ws)
            {
                case PlayerLoadout.Slot.LongGun:
                    return "Tüfek hep iki elle tutulur. Kalkan sırtta.";
                case PlayerLoadout.Slot.Pistol:
                    return two ? "İki elle: en isabetli. Kalkan takınca tek ele geçer."
                               : $"Tek elle: +{l.oneHandPistolSpread:0.#}° sapma. Kalkan kolda; nişan alırken kalkan kalkar.";
                default:
                {
                    var w = l.Weapon(PlayerLoadout.Slot.Melee);
                    var m = w != null ? w.GetComponent<MeleeWeapon>() : null;
                    if (m == null) return "Yumruk. Kalkan kolda.";
                    var cat = m.stats.ResolveCategory(false);
                    if (cat == MeleeCategory.TwoHand) return "Ağır silah, iki elle. Kalkan sırtta.";
                    return "Tek elle. Kalkan kolda.";
                }
            }
        }

        private static string ItemDetails(EquipSlot slot, ItemDefinition item, PlayerLoadout l, PlayerInventory inv)
        {
            if (item == null)
            {
                if (slot == EquipSlot.Melee) return "Yakın dövüş yuvası boşken yumrukla dövüşür, kalkan kolda kalır.";
                if (slot == QuickSlot) return "Yolda D-pad ↓ (klavye 4) ile kullanılır.";
                return "Çantadan uyan bir eşya seç.";
            }
            var parts = new List<string>();
            var ws = WeaponSlotOf(slot);
            var w = ws != PlayerLoadout.Slot.None && l != null ? l.Weapon(ws) : null;
            if (w != null && w.IsFirearm)
            {
                parts.Add($"Şarjör {w.CurrentAmmo}/{w.MagazineCapacity} · {(w.IsAutomatic ? "otomatik" : "tek atış")}");
                parts.Add($"{AmmoName(w.AmmoKind)}: {(inv != null ? inv.Count(w.AmmoKind) : 0)} yedek");
            }
            var melee = MeleeOfItem(item);
            if (melee != null)
            {
                var st = melee.stats;
                parts.Add($"Hasar {st.damage:0} · dayanıklılık {st.staminaCost:0} · menzil {st.range:0.#} m");
            }
            if (item.IsShield) parts.Add($"Engel %{Mathf.RoundToInt(item.shield.absorb * 100f)} · mermi %{Mathf.RoundToInt(item.shield.bulletAbsorb * 100f)}");
            if (item.category == ItemCategory.Apparel && !item.IsShield)
            {
                string a = ApparelLine(item);
                if (!string.IsNullOrEmpty(a)) parts.Add(a);
            }
            if (item.IsConsumable && inv != null) parts.Add($"Çantada x{inv.Storage.GetCount(item)} · D-pad ↓ (4) ile kullan");
            if (slot != QuickSlot) parts.Add($"Ağırlık {item.weight:0.0#} kg");
            return string.Join("\n", parts);
        }

        // ------------------------------------------------------------------ primitives

        private static void DrawRound(Rect r, Color c, float radius)
        {
            GUI.DrawTexture(r, SurvivalGUI.White, ScaleMode.StretchToFill, true, 0f, c, 0f, radius);
        }

        private static void DrawLine(Vector2 a, Vector2 b, Color c, float width)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.5f) return;
            Matrix4x4 m = GUI.matrix;
            var old = GUI.color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            GUI.color = c;
            GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, len, width), SurvivalGUI.White);
            GUI.matrix = m;
            GUI.color = old;
        }

        private static Texture2D MakeStage()
        {
            const int w = 128, h = 32;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = (x + 0.5f) / w * 2f - 1f, dy = (y + 0.5f) / h * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - d) * 0.55f;
                t.SetPixel(x, y, new Color(1f, 0.85f, 0.55f, a * a));
            }
            t.Apply();
            return t;
        }

        // Slot icons: flat white silhouettes (tinted when drawn), rasterized once from simple polygons
        private static void EnsureIcons()
        {
            if (_icons != null && _icons[0] != null) return;
            _icons = new Texture2D[10];
            _icons[0] = Icon(54, 40, P(8, 30, 9, 20, 13, 12, 20, 7, 27, 6, 34, 7, 41, 12, 45, 20, 46, 30), P(2, 28, 52, 28, 52, 35, 2, 35));
            _icons[1] = Icon(66, 30, P(2, 12, 16, 12, 18, 8, 62, 8, 62, 13, 48, 13, 46, 25, 40, 25, 40, 16, 24, 16, 16, 23, 2, 23));
            _icons[2] = Icon(50, 40, P(4, 6, 46, 6, 46, 15, 26, 15, 21, 35, 10, 35, 14, 15, 4, 15));
            _icons[3] = Icon(54, 56, P(8, 50, 15, 54, 48, 10, 42, 3, 32, 6));
            _icons[4] = Icon(56, 40, P(10, 4, 26, 4, 26, 22, 50, 26, 53, 29, 52, 34, 8, 34));
            _icons[5] = Icon(56, 52, P(18, 4, 28, 10, 38, 4, 52, 12, 46, 24, 42, 22, 42, 48, 14, 48, 14, 22, 10, 24, 4, 12));
            _icons[6] = Icon(48, 48, P(14, 4, 30, 4, 34, 30, 30, 44, 18, 44, 12, 30));
            _icons[7] = Icon(44, 54, P(22, 2, 40, 9, 40, 27, 37, 37, 30, 46, 22, 52, 14, 46, 7, 37, 4, 27, 4, 9));
            _icons[8] = Icon(44, 56, P(6, 4, 38, 4, 40, 52, 27, 52, 22, 20, 17, 52, 4, 52));
            _icons[9] = Icon(34, 34, P(4, 12, 30, 12, 30, 22, 4, 22), P(12, 4, 22, 4, 22, 30, 12, 30));
        }

        private static Vector2[] P(params float[] xy)
        {
            var pts = new Vector2[xy.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = new Vector2(xy[i * 2], xy[i * 2 + 1]);
            return pts;
        }

        private static Texture2D Icon(float vw, float vh, params Vector2[][] polys)
        {
            const int size = 96;
            float scale = (size - 6f) / Mathf.Max(vw, vh);
            float ox = (size - vw * scale) * 0.5f, oy = (size - vh * scale) * 0.5f;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int hits = 0;
                for (int sy = 0; sy < 4; sy++)
                for (int sx = 0; sx < 4; sx++)
                {
                    // texture y goes up, the drawing's y goes down
                    float gx = (x + (sx + 0.5f) / 4f - ox) / scale;
                    float gy = (size - (y + (sy + 0.5f) / 4f) - oy) / scale;
                    foreach (var poly in polys)
                        if (Inside(poly, gx, gy)) { hits++; break; }
                }
                px[y * size + x] = new Color(1f, 1f, 1f, hits / 16f);
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        private static bool Inside(Vector2[] poly, float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if ((poly[i].y > y) != (poly[j].y > y) &&
                    x < (poly[j].x - poly[i].x) * (y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            }
            return inside;
        }
    }
}
