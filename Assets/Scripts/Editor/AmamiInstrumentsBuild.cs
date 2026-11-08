using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using Object = UnityEngine.Object;

[assembly: ExportsPlugin(typeof(Yamaneko.AmamiInstruments.Editor.AmamiInstrumentsBuild))]

namespace Yamaneko.AmamiInstruments.Editor
{
    // The authoring format consists ONLY of stock MA components. No custom MonoBehaviour is used.
    internal static class InstrumentProtocol
    {
        internal const string Prefix = "Yamaneko/AmamiInstruments/";
        internal const string Root = Prefix + "v1/System";
        internal const string Entry = Prefix + "v1/Entry/";
        internal const string FixedEntry = Prefix + "v2/Entry/";
        internal const string Selection = "Instruments";
        internal const string Visible = Prefix + "Visible";
        internal const string Front = Prefix + "Front";
        internal const string Catalog = Prefix + "v1/Catalog";

        internal static bool IsEntry(ModularAvatarMenuItem item) =>
            item.Control?.parameter?.name?.StartsWith(Entry, StringComparison.Ordinal) == true ||
            item.Control?.parameter?.name?.StartsWith(FixedEntry, StringComparison.Ordinal) == true;

        internal static string Parameter(ModularAvatarMenuItem item) => item.Control?.parameter?.name;

        internal static string[] Identity(ModularAvatarMenuItem item)
        {
            var fixedSlot = Parameter(item).StartsWith(FixedEntry, StringComparison.Ordinal);
            var parts = Parameter(item).Substring(fixedSlot ? FixedEntry.Length : Entry.Length).Split('|');
            if (parts.Length != (fixedSlot ? 3 : 2) || !Guid.TryParseExact(parts[0], "N", out _))
                throw new InvalidOperationException("Amami Instruments: 楽器の登録情報が不正です: " + item.name);
            return new[] { parts[0], Uri.UnescapeDataString(parts[1]) };
        }

        internal static int? RequestedSlot(ModularAvatarMenuItem item)
        {
            if (!Parameter(item).StartsWith(FixedEntry, StringComparison.Ordinal)) return null;
            Identity(item);
            if (!int.TryParse(Parameter(item).Split('|')[2], out var slot) || slot < 0 || slot > 255)
                throw new InvalidOperationException("Instruments の値は0～255で指定してください。");
            return slot;
        }

        internal static int[] ResolveSlots(ModularAvatarMenuItem[] entries)
        {
            var reserved = new HashSet<int>();
            foreach (var item in entries)
            {
                var slot = RequestedSlot(item);
                if (slot.HasValue && !reserved.Add(slot.Value))
                    throw new InvalidOperationException("Instruments の値が重複しています: " + slot.Value);
            }
            return entries.Select(item =>
            {
                var slot = RequestedSlot(item);
                if (slot.HasValue) return slot.Value;
                var next = Enumerable.Range(0, 256).First(x => !reserved.Contains(x));
                reserved.Add(next);
                return next;
            }).ToArray();
        }

        internal static ModularAvatarMenuItem Menu(Transform parent, string label,
            VRCExpressionsMenu.Control.ControlType type, string parameter = null)
        {
            var go = new GameObject(label);
            go.transform.SetParent(parent, false);
            var menu = go.AddComponent<ModularAvatarMenuItem>();
            menu.label = label;
            menu.Control = new VRCExpressionsMenu.Control
            {
                name = label, type = type, value = 1,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = parameter ?? "" }
            };
            menu.MenuSource = SubmenuSource.Children;
            menu.automaticValue = false;
            menu.isSaved = false;
            return menu;
        }

        internal static ModularAvatarObjectToggle Toggle(ModularAvatarMenuItem item, GameObject target)
        {
            var toggle = item.gameObject.AddComponent<ModularAvatarObjectToggle>();
            var reference = new AvatarObjectReference(target);
            // MA checks for an empty path before consulting targetObject. Prefab Mode has no avatar root.
            // Keep the direct serialized reference authoritative and prevent a null path from discarding it.
            if (string.IsNullOrEmpty(reference.referencePath)) reference.referencePath = "__AmamiPrefabReference__/" + Guid.NewGuid().ToString("N");
            toggle.Objects.Add(new ToggledObject { Object = reference, Active = true });
            return toggle;
        }

        internal static void SeparateRegistration(ModularAvatarMenuItem item)
        {
            item.name = "InstrumentSelection";
            // Compile moves this leaf to the selection menu BEFORE Modular Avatar collects menus.
        }

        internal static bool FlattenRegistrationFolders(GameObject root, bool undo)
        {
            var changed = false;
            foreach (var folder in root.GetComponentsInChildren<Transform>(true).Reverse().ToArray())
            {
                if (folder.name != "__AmamiInstrumentRegistration" || folder.GetComponents<Component>().Length != 1) continue;
                foreach (Transform child in folder.Cast<Transform>().ToArray())
                {
                    var entry = child.GetComponent<ModularAvatarMenuItem>();
                    if (entry == null || !IsEntry(entry)) continue;
                    if (undo) Undo.SetTransformParent(child, folder.parent, "Flatten instrument registration");
                    else child.SetParent(folder.parent, true);
                    if (undo) PrefabUtility.RecordPrefabInstancePropertyModifications(child);
                    changed = true;
                }
                // Preserve any user-added content rather than deleting it with the obsolete folder.
                if (folder.childCount != 0) continue;
                if (undo) Undo.DestroyObjectImmediate(folder.gameObject);
                else Object.DestroyImmediate(folder.gameObject);
                changed = true;
            }
            return changed;
        }

        [InitializeOnLoadMethod]
        private static void MigrateOpenSceneRegistrationFolders()
        {
            if (Application.isBatchMode) return;
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                foreach (var avatar in Object.FindObjectsOfType<VRCAvatarDescriptor>(true))
                {
                    if (EditorUtility.IsPersistent(avatar) || UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(avatar.gameObject.scene)) continue;
                    if (FlattenRegistrationFolders(avatar.gameObject, true))
                    {
                        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(avatar.gameObject.scene);
                        Debug.Log("Amami Instruments: 登録フォルダーを削除し、登録情報を直下へ移動しました。シーンを保存してください（Undo可能）。", avatar);
                    }
                }
            };
        }

        internal static GameObject Target(ModularAvatarMenuItem item)
        {
            var toggle = item.GetComponent<ModularAvatarObjectToggle>();
            if (toggle == null || toggle.Objects.Count != 1 || !toggle.Objects[0].Active)
                throw new InvalidOperationException("Amami Instruments: MA Object Toggle に表示対象を1つ指定してください: " + item.name);
            var target = toggle.Objects[0].Object?.Get(toggle);
            if (target == null) throw new InvalidOperationException("Amami Instruments: 表示対象の参照が切れています: " + item.name);
            return target;
        }

        internal static Component PositionConstraint(GameObject target)
        {
            var constraints = target.GetComponents<Component>().Where(c => c is ParentConstraint ||
                c != null && c.GetType().FullName == "VRC.SDK3.Dynamics.Constraint.Components.VRCParentConstraint").ToArray();
            if (constraints.Length != 1)
                throw new InvalidOperationException("Amami Instruments: 格納先に前・後の2ソースを持つ Parent Constraint が必要です: " + target.name);
            var constraint = constraints[0];
            if (constraint is ParentConstraint unity)
            {
                if (!unity.constraintActive || unity.sourceCount != 2 ||
                    unity.GetSource(0).sourceTransform == null || unity.GetSource(1).sourceTransform == null)
                    throw new InvalidOperationException("Amami Instruments: Parent Constraint の有効化と前・後ソースを確認してください。");
            }
            else
            {
                var so = new SerializedObject(constraint);
                var sources = so.FindProperty("Sources");
                if (sources == null || sources.FindPropertyRelative("source0.SourceTransform")?.objectReferenceValue == null ||
                    sources.FindPropertyRelative("source1.SourceTransform")?.objectReferenceValue == null ||
                    sources.FindPropertyRelative("totalLength")?.intValue != 2 || so.FindProperty("IsActive")?.boolValue != true)
                    throw new InvalidOperationException("Amami Instruments: VRC Parent Constraint の前・後ソースがありません。");
            }
            return constraint;
        }
    }

    public sealed class AmamiInstrumentsBuild : Plugin<AmamiInstrumentsBuild>
    {
        public override string QualifiedName => "dev.yamaneko.amami-instruments";
        public override string DisplayName => "Amami Instruments";

        protected override void Configure()
        {
            InPhase(BuildPhase.Resolving).BeforePlugin("nadena.dev.modular-avatar")
                .Run("Collect installed instruments", ctx => Compile(ctx.AvatarRootObject));
        }

        internal sealed class Layout
        {
            internal ModularAvatarMenuItem Root, Visible, Front, Catalog;
            internal GameObject Container;
            internal Component Constraint;
            internal ModularAvatarMenuItem[] Entries;
        }

        // Validation has no side effects; the same checks are used by the management UI and builds.
        internal static Layout Inspect(GameObject avatar)
        {
            var items = avatar.GetComponentsInChildren<ModularAvatarMenuItem>(true);
            var roots = items.Where(x => InstrumentProtocol.Parameter(x) == InstrumentProtocol.Root).ToArray();
            var entries = items.Where(InstrumentProtocol.IsEntry).ToArray();
            if (roots.Length == 0 && entries.Length == 0) return null;
            if (roots.Length != 1) throw new InvalidOperationException("Amami Instruments: アバターごとに共通設定が1つ必要です。");
            var root = roots[0];
            if (!root.enabled || !root.gameObject.activeInHierarchy)
                throw new InvalidOperationException("Amami Instruments: 共通設定を有効にしてください。");
            ModularAvatarMenuItem Single(string parameter)
            {
                var matches = root.GetComponentsInChildren<ModularAvatarMenuItem>(true)
                    .Where(x => InstrumentProtocol.Parameter(x) == parameter).ToArray();
                if (matches.Length != 1) throw new InvalidOperationException("Amami Instruments: 共通メニュー設定が不足または重複しています: " + parameter);
                return matches[0];
            }
            var layout = new Layout { Root = root, Visible = Single(InstrumentProtocol.Visible),
                Front = Single(InstrumentProtocol.Front), Catalog = Single(InstrumentProtocol.Catalog) };
            layout.Container = InstrumentProtocol.Target(layout.Visible);
            if (layout.Container == avatar || root.transform.IsChildOf(layout.Container.transform))
                throw new InvalidOperationException("Amami Instruments: 共通メニューは楽器の表示対象の外に配置してください。");
            layout.Constraint = InstrumentProtocol.PositionConstraint(layout.Container);
            if (entries.Length > 256)
                throw new InvalidOperationException("Amami Instruments: 登録できる楽器は最大256個です。");
            var ids = entries.Select(InstrumentProtocol.Identity).ToArray();
            if (ids.Select(x => x[0]).Distinct(StringComparer.Ordinal).Count() != entries.Length)
                throw new InvalidOperationException("Amami Instruments: 同じ楽器IDが重複しています。複製品には管理UIから新しいIDを割り当ててください。");
            if (entries.Count(x => x.isDefault) > 1)
                throw new InvalidOperationException("Amami Instruments: 初期楽器 (Is Default) は1つだけ指定してください。");
            var targets = entries.Select(InstrumentProtocol.Target).ToArray();
            foreach (var target in targets)
            {
                if (target == layout.Container || !target.transform.IsChildOf(layout.Container.transform))
                    throw new InvalidOperationException("Amami Instruments: 楽器は共通の格納先配下に配置してください: " + target.name);
                if (targets.Count(t => target.transform.IsChildOf(t.transform)) != 1)
                    throw new InvalidOperationException("Amami Instruments: 楽器の表示対象が重複または入れ子になっています: " + target.name);
                for (var ancestor = target.transform.parent; ancestor != layout.Container.transform; ancestor = ancestor.parent)
                    if (!ancestor.gameObject.activeSelf)
                        throw new InvalidOperationException("Amami Instruments: 楽器と格納先の間の親オブジェクトを有効にしてください: " + ancestor.name);
            }
            foreach (var entry in entries)
            {
                if (!entry.enabled) throw new InvalidOperationException("Amami Instruments: 登録メニューを有効にしてください: " + entry.name);
                if (entry.transform.childCount != 0 || entry.GetComponents<Component>().Any(c =>
                    !(c is Transform) && !(c is ModularAvatarMenuItem) && !(c is ModularAvatarObjectToggle) && !(c is ModularAvatarMenuInstaller) && !(c is ModularAvatarMergeAnimator)))
                    throw new InvalidOperationException("Amami Instruments: 登録メニューは MA Menu Item / Object Toggle だけの独立した葉にしてください: " + entry.name);
                if (entry.GetComponent<ModularAvatarObjectToggle>().Inverted)
                    throw new InvalidOperationException("Amami Instruments: 登録メニューの Invert Condition は無効にしてください。");
                if (entry.menuSource_otherObjectChildren != null &&
                    !entry.menuSource_otherObjectChildren.transform.IsChildOf(avatar.transform))
                    throw new InvalidOperationException("Amami Instruments: カスタマイズ参照は同じアバター内に指定してください。");
                var category = InstrumentProtocol.Identity(entry)[1];
                if (category.Split('/').Length > 8) throw new InvalidOperationException("Amami Instruments: 分類は8階層以内にしてください。");
            }
            // Keep explicit slot values stable so saved selections retain their meaning after updates.
            layout.Entries = entries.OrderByDescending(x => x.isDefault)
                .ThenBy(x => InstrumentProtocol.Identity(x)[0], StringComparer.Ordinal).ToArray();
            InstrumentProtocol.ResolveSlots(layout.Entries);
            return layout;
        }

        internal static void Compile(GameObject avatar)
        {
            AmamiInstrumentsLegacy.UpgradeBuildClone(avatar);
            InstrumentSelectionAnimator.NormalizeSelectionTargets(avatar, false);
            AmamiInstrumentsLegacy.RepairBassColor(avatar, false);
            InstrumentProtocol.FlattenRegistrationFolders(avatar, false);
            var layout = Inspect(avatar);
            if (layout == null) return;
            var slots = InstrumentProtocol.ResolveSlots(layout.Entries);
            var parameters = layout.Root.GetComponent<ModularAvatarParameters>() ??
                layout.Root.gameObject.AddComponent<ModularAvatarParameters>();
            foreach (var p in new[] { InstrumentProtocol.Selection, InstrumentProtocol.Visible, InstrumentProtocol.Front })
                parameters.parameters.RemoveAll(x => x.nameOrPrefix == p && !x.isPrefix);
            parameters.parameters.Add(new ParameterConfig { nameOrPrefix = InstrumentProtocol.Selection,
                syncType = ParameterSyncType.Int, saved = true, hasExplicitDefaultValue = true, defaultValue = slots.Length > 0 ? slots[0] : 0 });
            parameters.parameters.Add(new ParameterConfig { nameOrPrefix = InstrumentProtocol.Visible,
                syncType = ParameterSyncType.Bool, saved = true, hasExplicitDefaultValue = true, defaultValue = 1 });
            parameters.parameters.Add(new ParameterConfig { nameOrPrefix = InstrumentProtocol.Front,
                syncType = ParameterSyncType.Bool, saved = true, hasExplicitDefaultValue = true, defaultValue = 1 });
            layout.Root.Control.parameter.name = "";
            layout.Catalog.Control.parameter.name = "";
            // Neutralize authoring defaults. MA then derives the true default and both ON/OFF states.
            layout.Container.SetActive(false);
            layout.Visible.isDefault = true;
            layout.Visible.automaticValue = false;
            layout.Visible.Control.value = 1;
            var custom = InstrumentProtocol.Menu(layout.Root.transform, "楽器別カスタマイズ",
                VRCExpressionsMenu.Control.ControlType.SubMenu);
            var categories = new Dictionary<string, Transform>(StringComparer.Ordinal) { [""] = layout.Catalog.transform };
            var customizationSources = new HashSet<GameObject>();
            for (int i = 0; i < layout.Entries.Length; i++)
            {
                var entry = layout.Entries[i];
                var identity = InstrumentProtocol.Identity(entry);
                var target = InstrumentProtocol.Target(entry);
                target.SetActive(false);
                var parent = layout.Catalog.transform;
                var key = "";
                foreach (var part in identity[1].Split('/').Where(x => !string.IsNullOrWhiteSpace(x) && x != "既存の楽器"))
                {
                    key += "/" + part;
                    if (!categories.TryGetValue(key, out var category))
                    {
                        category = InstrumentProtocol.Menu(parent, part, VRCExpressionsMenu.Control.ControlType.SubMenu).transform;
                        categories.Add(key, category);
                    }
                    parent = category;
                }
                var owner = entry.GetComponentsInParent<ModularAvatarMenuItem>(true).FirstOrDefault(x => x != entry &&
                    x.transform.IsChildOf(layout.Container.transform) && x.Control?.type == VRCExpressionsMenu.Control.ControlType.SubMenu);
                var source = entry.menuSource_otherObjectChildren;
                if (source == null && owner != null && owner.MenuSource == SubmenuSource.Children)
                    source = owner.menuSource_otherObjectChildren != null ? owner.menuSource_otherObjectChildren : owner.gameObject;
                if (source != null && HasCustomization(source))
                {
                    if (customizationSources.Add(source))
                    {
                        ExposeNestedCustomization(source);
                        var settings = InstrumentProtocol.Menu(custom.transform,
                            owner != null ? (string.IsNullOrEmpty(owner.label) ? owner.name : owner.label) :
                                (string.IsNullOrEmpty(entry.label) ? entry.name : entry.label),
                            VRCExpressionsMenu.Control.ControlType.SubMenu);
                        settings.menuSource_otherObjectChildren = source;
                        if (owner != null) settings.Control.icon = owner.Control.icon;
                    }
                }
                else if (owner != null && owner.MenuSource == SubmenuSource.Children &&
                    !HasCustomization(owner.menuSource_otherObjectChildren != null ? owner.menuSource_otherObjectChildren : owner.gameObject))
                    Object.DestroyImmediate(owner); // Empty settings pages have no useful radial controls.
                // Move only a dedicated menu leaf on the build clone, never an instrument/settings hierarchy.
                var registrationFolder = entry.transform.parent;
                entry.transform.SetParent(parent, false);
                if (registrationFolder != null && registrationFolder.name == "__AmamiInstrumentRegistration" &&
                    registrationFolder.childCount == 0 && registrationFolder.GetComponents<Component>().Length == 1)
                    Object.DestroyImmediate(registrationFolder.gameObject);
                entry.gameObject.SetActive(true);
                entry.Control.parameter.name = InstrumentProtocol.Selection;
                entry.Control.type = VRCExpressionsMenu.Control.ControlType.Toggle;
                entry.Control.value = slots[i];
                entry.automaticValue = false;
                entry.isDefault = i == 0;
                entry.isSynced = true;
                entry.isSaved = true;
                foreach (var installer in entry.GetComponents<ModularAvatarMenuInstaller>()) Object.DestroyImmediate(installer);
                InstrumentSelectionAnimator.Attach(entry, target, slots[i], i == 0, false);
                Object.DestroyImmediate(entry.GetComponent<ModularAvatarObjectToggle>());
            }
            if (custom.transform.childCount == 0) Object.DestroyImmediate(custom.gameObject);
            CreatePositionAnimator(layout);
        }

        internal static bool HasCustomization(GameObject source)
        {
            var seenObjects = new HashSet<GameObject>();
            var seenAssets = new HashSet<VRCExpressionsMenu>();
            bool AssetHasControls(VRCExpressionsMenu menu)
            {
                return menu != null && seenAssets.Add(menu) && menu.controls.Any(c =>
                    c.type != VRCExpressionsMenu.Control.ControlType.SubMenu || AssetHasControls(c.subMenu));
            }
            bool ChildrenHaveControls(GameObject root)
            {
                if (root == null || !seenObjects.Add(root)) return false;
                foreach (Transform child in root.transform)
                {
                    var menu = child.GetComponent<ModularAvatarMenuItem>();
                    if (menu != null)
                    {
                        if (!menu.enabled || menu.Control == null || InstrumentProtocol.IsEntry(menu)) continue;
                        if (menu.Control.type != VRCExpressionsMenu.Control.ControlType.SubMenu) return true;
                        if (menu.MenuSource == SubmenuSource.MenuAsset ? AssetHasControls(menu.Control.subMenu) :
                            ChildrenHaveControls(menu.menuSource_otherObjectChildren != null ? menu.menuSource_otherObjectChildren : menu.gameObject)) return true;
                    }
                    else
                    {
                        var group = child.GetComponent<ModularAvatarMenuGroup>();
                        if (ChildrenHaveControls(group != null && group.targetObject != null ? group.targetObject : child.gameObject)) return true;
                    }
                }
                return false;
            }
            return ChildrenHaveControls(source);
        }

        // A model/wrapper is not a menu boundary. Add MA traversal markers only on the build clone;
        // do not move customization objects or change the source scene's hierarchy.
        private static void ExposeNestedCustomization(GameObject source)
        {
            var visited = new HashSet<GameObject>();
            void Visit(GameObject root)
            {
                if (!visited.Add(root)) return;
                foreach (Transform child in root.transform)
                {
                    var menu = child.GetComponent<ModularAvatarMenuItem>();
                    if (menu != null)
                    {
                        if (!menu.enabled || InstrumentProtocol.IsEntry(menu)) continue;
                        if (menu.Control?.type == VRCExpressionsMenu.Control.ControlType.SubMenu && menu.MenuSource == SubmenuSource.Children)
                            Visit(menu.menuSource_otherObjectChildren != null ? menu.menuSource_otherObjectChildren : child.gameObject);
                        continue;
                    }
                    var group = child.GetComponent<ModularAvatarMenuGroup>();
                    var content = group != null && group.targetObject != null ? group.targetObject : child.gameObject;
                    if (!HasCustomization(content)) continue;
                    if (group == null) child.gameObject.AddComponent<ModularAvatarMenuGroup>();
                    Visit(content);
                }
            }
            Visit(source);
        }

        private static void CreatePositionAnimator(Layout layout)
        {
            var controller = new AnimatorController { name = "Amami Instruments - Position (build)" };
            controller.AddParameter(InstrumentProtocol.Front, AnimatorControllerParameterType.Bool);
            controller.AddParameter("AFK", AnimatorControllerParameterType.Bool);
            var machine = new AnimatorStateMachine { name = "Amami Instruments Position" };
            controller.AddLayer(new AnimatorControllerLayer { name = machine.name, defaultWeight = 1, stateMachine = machine });
            AnimatorState State(bool front)
            {
                var clip = new AnimationClip { name = front ? "Front" : "Rear" };
                for (int i = 0; i < 2; i++)
                {
                    var property = layout.Constraint is ParentConstraint ? "m_Sources.Array.data[" + i + "].weight" : "Sources.source" + i + ".Weight";
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", layout.Constraint.GetType(), property),
                        AnimationCurve.Constant(0, 0, (i == 0) == front ? 1 : 0));
                }
                var state = machine.AddState(clip.name);
                state.motion = clip;
                state.writeDefaultValues = false;
                return state;
            }
            var frontState = State(true);
            var rearState = State(false);
            machine.defaultState = frontState;
            AnimatorStateTransition Transition(AnimatorState from, AnimatorState to, AnimatorConditionMode condition, string parameter)
            {
                var transition = from.AddTransition(to);
                transition.hasExitTime = false;
                transition.duration = 0;
                transition.AddCondition(condition, 0, parameter);
                return transition;
            }
            // AFK overrides position without overwriting the user's saved Front parameter.
            Transition(frontState, rearState, AnimatorConditionMode.IfNot, InstrumentProtocol.Front)
                .AddCondition(AnimatorConditionMode.IfNot, 0, "AFK");
            Transition(rearState, frontState, AnimatorConditionMode.If, InstrumentProtocol.Front);
            Transition(rearState, frontState, AnimatorConditionMode.If, "AFK");
            var merge = layout.Root.gameObject.AddComponent<ModularAvatarMergeAnimator>();
            merge.animator = controller;
            merge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
            merge.pathMode = MergeAnimatorPathMode.Relative;
            merge.relativePathRoot = new AvatarObjectReference(layout.Container);
            merge.matchAvatarWriteDefaults = true;
            merge.deleteAttachedAnimator = false;
        }
    }
}
