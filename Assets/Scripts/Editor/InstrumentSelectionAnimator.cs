using System;
using System.IO;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using Object = UnityEngine.Object;

namespace Yamaneko.AmamiInstruments.Editor
{
    internal static class InstrumentSelectionAnimator
    {
        internal const string ControllerPrefix = "Amami Selection ";
        private static string EditVisibilityKey => "AmamiInstruments.EditVisible." + Application.dataPath;

        internal static GameObject PrefabVisualTarget(GameObject root)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(root);
            if (source == null) return null;
            var entries = source.GetComponentsInChildren<ModularAvatarMenuItem>(true).Where(InstrumentProtocol.IsEntry).ToArray();
            if (entries.Length != 1) return null;
            var target = PrefabTarget(entries[0]);
            if (target == null || !target.transform.IsChildOf(source.transform)) return null;
            // Match source objects rather than names, so renamed model objects remain supported.
            return root.GetComponentsInChildren<Transform>(true).Select(t => t.gameObject).FirstOrDefault(go =>
                PrefabUtility.GetCorrespondingObjectFromSource(go) == target);
        }

        internal static void NormalizeSelectionTargets(GameObject avatar, bool undo)
        {
            foreach (var entry in avatar.GetComponentsInChildren<ModularAvatarMenuItem>(true).Where(InstrumentProtocol.IsEntry))
            {
                var target = InstrumentProtocol.Target(entry);
                var model = PrefabVisualTarget(target);
                var settings = target.GetComponent<ModularAvatarMenuItem>();
                if (model == null && settings != null && settings.Control?.type == VRCExpressionsMenu.Control.ControlType.SubMenu)
                    model = LegacyVisualTarget(target);
                if (model == null || model == target) continue;
                // The old toggle root becomes a structural parent. Keep it enabled so the
                // newly targeted model can be activated independently, even if the old root was off.
                if (undo)
                {
                    SetEditModeVisibility(model, target.activeSelf && model.activeSelf);
                    SetEditModeVisibility(target, true);
                }
                else target.SetActive(true);
                var toggle = entry.GetComponent<ModularAvatarObjectToggle>();
                if (undo) Undo.RecordObject(toggle, "Correct instrument visual target");
                toggle.Objects[0] = new ToggledObject { Object = new AvatarObjectReference(model), Active = true };
                if (undo) PrefabUtility.RecordPrefabInstancePropertyModifications(toggle);
            }
        }

        internal static GameObject LegacyVisualTarget(GameObject root)
        {
            var prefabTarget = PrefabVisualTarget(root);
            if (prefabTarget != null) return prefabTarget;
            if (root.GetComponent<Renderer>() != null) return root;
            // NDMF clones need not retain prefab links. A legacy Settings wrapper with exactly
            // one model branch can still be resolved without any avatar/object-name assumptions.
            var models = root.transform.Cast<Transform>().Where(t => t.GetComponentsInChildren<Renderer>(true).Length > 0).ToArray();
            return models.Length == 1 ? models[0].gameObject : root;
        }

        internal static GameObject[] EditTargets(GameObject avatar)
        {
            var targets = avatar.GetComponentsInChildren<ModularAvatarMenuItem>(true).Where(InstrumentProtocol.IsEntry)
                .Select(InstrumentProtocol.Target).ToList();
            foreach (var t in avatar.GetComponentsInChildren<Transform>(true))
            {
                if (!PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)) continue;
                var target = PrefabVisualTarget(t.gameObject);
                if (target != null) targets.Add(target);
            }
            return targets.Distinct().ToArray();
        }

        internal static void SetAllEditVisibility(GameObject avatar, bool visible, bool remember = true)
        {
            NormalizeSelectionTargets(avatar, true);
            foreach (var entry in avatar.GetComponentsInChildren<ModularAvatarMenuItem>(true).Where(InstrumentProtocol.IsEntry))
            {
                // This leaf is metadata until Compile moves and enables it. Its editor preview must
                // not override the author's GameObject visibility, even when Is Default is selected.
                SetEditModeVisibility(entry.gameObject, false);
            }
            foreach (var target in EditTargets(avatar)) SetEditModeVisibility(target, visible);
            if (remember) EditorPrefs.SetBool(EditVisibilityKey, visible);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(avatar.scene);
        }

        [InitializeOnLoadMethod]
        private static void InitializeEditVisibility()
        {
            if (Application.isBatchMode) return;
            EditorApplication.delayCall += () =>
            {
                const string once = "AmamiInstruments.UnifiedEditVisibility.v1";
                if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(once, false)) return;
                SessionState.SetBool(once, true);
                foreach (var avatar in Object.FindObjectsOfType<VRCAvatarDescriptor>(true))
                {
                    if (EditorUtility.IsPersistent(avatar) || UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(avatar.gameObject.scene)) continue;
                    if (EditTargets(avatar.gameObject).Length == 0) continue;
                    SetAllEditVisibility(avatar.gameObject, EditorPrefs.GetBool(EditVisibilityKey, false), false);
                    Debug.Log("Amami Instruments: 編集時の楽器表示を統一しました。Managerで一括表示／非表示を変更できます。シーンを保存してください。", avatar);
                }
            };
        }

        [InitializeOnLoadMethod]
        private static void ScheduleEditModeRepair()
        {
            if (Application.isBatchMode) return;
            EditorApplication.delayCall += () =>
            {
                const string key = "AmamiInstruments.SubmenuVisibilityRepair.v1";
                if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(key, false)) return;
                SessionState.SetBool(key, true);
                var repaired = 0;
                foreach (var avatar in Object.FindObjectsOfType<VRCAvatarDescriptor>(true))
                {
                    if (EditorUtility.IsPersistent(avatar) || UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(avatar.gameObject.scene)) continue;
                    foreach (var transform in avatar.GetComponentsInChildren<Transform>(true))
                        if (PrefabUtility.IsAnyPrefabInstanceRoot(transform.gameObject))
                            repaired += RepairPrefabPreview(transform.gameObject);
                }
                if (repaired > 0) Debug.Log("Amami Instruments: サブメニューによる楽器の常時表示を修復しました。シーンを保存してください（Undo可能）。");
            };
        }

        // Also handles instances whose registration was deleted locally: the prefab still identifies the model.
        internal static int RepairPrefabPreview(GameObject root)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(root);
            if (source == null) return 0;
            var repaired = 0;
            foreach (var entry in source.GetComponentsInChildren<ModularAvatarMenuItem>(true).Where(InstrumentProtocol.IsEntry))
            {
                var sourceTarget = PrefabTarget(entry);
                if (sourceTarget == null || !sourceTarget.transform.IsChildOf(source.transform)) continue;
                var path = AnimationUtility.CalculateTransformPath(sourceTarget.transform, source.transform);
                var target = path.Length == 0 ? root : root.transform.Find(path)?.gameObject;
                if (target == null) continue;
                var count = RemoveSubmenuVisibility(root, target);
                if (count > 0)
                {
                    SetEditModeVisibility(target, entry.isDefault);
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
                    repaired += count;
                }
            }
            return repaired;
        }

        internal static GameObject PrefabTarget(ModularAvatarMenuItem entry)
        {
            var toggle = entry.GetComponent<ModularAvatarObjectToggle>();
            if (toggle == null || toggle.Objects.Count != 1 || !toggle.Objects[0].Active) return null;
            // AvatarObjectReference.Get requires an avatar ancestor; prefab assets have none.
            using (var serialized = new SerializedObject(toggle))
                return serialized.FindProperty("m_objects").GetArrayElementAtIndex(0)
                    .FindPropertyRelative("Object.targetObject").objectReferenceValue as GameObject;
        }

        internal static int RemoveSubmenuVisibility(GameObject root, GameObject target)
        {
            var removed = 0;
            foreach (var toggle in root.GetComponentsInChildren<ModularAvatarObjectToggle>(true))
            {
                var menu = toggle.GetComponent<ModularAvatarMenuItem>();
                // A SubMenu has no selection condition in MA, so Active=true becomes an always-on rule.
                if (menu == null || menu.Control.type != VRCExpressionsMenu.Control.ControlType.SubMenu || toggle.Inverted) continue;
                var matches = toggle.Objects.Where(x => x.Active && x.Object.Get(toggle) == target).ToArray();
                if (matches.Length == 0) continue;
                Undo.RecordObject(toggle, "Remove unconditional instrument visibility");
                foreach (var item in matches) toggle.Objects.Remove(item);
                removed += matches.Length;
                if (toggle.Objects.Count == 0) Undo.DestroyObjectImmediate(toggle);
                else PrefabUtility.RecordPrefabInstancePropertyModifications(toggle);
            }
            return removed;
        }

        private static void SetEditModeVisibility(GameObject target, bool visible)
        {
            Undo.RecordObject(target, "Set instrument edit mode visibility");
            target.SetActive(visible);
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }

        internal static void Attach(ModularAvatarMenuItem entry, GameObject target, int slot, bool initial, bool persist)
        {
            var controller = CreateController(slot, initial);
            if (persist)
            {
                const string folder = "Assets/Umi-no-Yamaneko/YamanekoInstruments/Generated/Installed";
                Directory.CreateDirectory(folder);
                AssetDatabase.Refresh();
                SaveController(controller, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + ControllerPrefix + slot + ".controller"));
            }
            var merge = entry.GetComponents<ModularAvatarMergeAnimator>().SingleOrDefault(x =>
                x.animator != null && x.animator.name.StartsWith(ControllerPrefix, StringComparison.Ordinal));
            if (merge == null) merge = persist ? Undo.AddComponent<ModularAvatarMergeAnimator>(entry.gameObject) :
                entry.gameObject.AddComponent<ModularAvatarMergeAnimator>();
            if (persist) Undo.RecordObject(merge, "Update instrument FX transitions");
            merge.animator = controller;
            merge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
            merge.pathMode = MergeAnimatorPathMode.Relative;
            merge.relativePathRoot = new AvatarObjectReference(target);
            merge.deleteAttachedAnimator = false;
            merge.matchAvatarWriteDefaults = true;
            if (persist) PrefabUtility.RecordPrefabInstancePropertyModifications(merge);
        }

        internal static AnimatorController CreateController(int slot, bool initial)
        {
            var controller = new AnimatorController { name = ControllerPrefix + slot };
            controller.AddParameter(InstrumentProtocol.Selection, AnimatorControllerParameterType.Int);
            var machine = new AnimatorStateMachine { name = controller.name };
            controller.AddLayer(new AnimatorControllerLayer { name = machine.name, defaultWeight = 1, stateMachine = machine });
            AnimatorState State(bool visible)
            {
                var clip = new AnimationClip { name = visible ? "Show" : "Hide" };
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(GameObject), "m_IsActive"),
                    AnimationCurve.Constant(0, 0, visible ? 1 : 0));
                var state = machine.AddState(clip.name);
                state.motion = clip;
                state.writeDefaultValues = false;
                return state;
            }
            var on = State(true);
            var off = State(false);
            machine.defaultState = initial ? on : off;
            void Transition(AnimatorState from, AnimatorState to, AnimatorConditionMode condition)
            {
                var transition = from.AddTransition(to);
                transition.hasExitTime = false;
                transition.duration = 0;
                transition.AddCondition(condition, slot, InstrumentProtocol.Selection);
            }
            Transition(off, on, AnimatorConditionMode.Equals);
            Transition(on, off, AnimatorConditionMode.NotEqual);
            return controller;
        }

        internal static void SaveController(AnimatorController controller, string path)
        {
            // Keep explicit references before CreateAsset imports the controller. Transient
            // animator objects must all become subassets before their references are saved.
            var layers = controller.layers;
            var assets = layers.SelectMany(layer => new Object[] { layer.stateMachine }
                .Concat(layer.stateMachine.states.SelectMany(child => new Object[] { child.state, child.state.motion }
                    .Concat(child.state.transitions.Cast<Object>())))).Distinct().ToArray();
            AssetDatabase.CreateAsset(controller, path);
            foreach (var asset in assets)
                if (asset != null && !EditorUtility.IsPersistent(asset)) AssetDatabase.AddObjectToAsset(asset, controller);
            controller.layers = layers;
            foreach (var asset in assets) EditorUtility.SetDirty(asset);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }

        internal static ModularAvatarMenuItem FindRegistration(GameObject root, GameObject target = null)
        {
            var avatar = root.GetComponentInParent<VRCAvatarDescriptor>(true);
            if (avatar != null)
            {
                var matches = avatar.GetComponentsInChildren<ModularAvatarMenuItem>(true).Where(InstrumentProtocol.IsEntry)
                    .Where(e => InstrumentProtocol.Target(e) == (target != null ? target : root)).ToArray();
                if (matches.Length > 1) throw new InvalidOperationException("同じ表示対象の登録が重複しています。");
                if (matches.Length == 1) return matches[0];
            }
            var entries = root.GetComponentsInChildren<ModularAvatarMenuItem>(true).Where(x =>
                InstrumentProtocol.IsEntry(x) || PrefabUtility.GetCorrespondingObjectFromSource(x) is ModularAvatarMenuItem source && InstrumentProtocol.IsEntry(source)).ToArray();
            if (entries.Length > 1) throw new InvalidOperationException("複数楽器の共通親ではなく、1つの楽器ルートを指定してください。");
            return entries.SingleOrDefault();
        }

        internal static ModularAvatarMenuItem MoveRegistrationInside(GameObject root)
        {
            var entry = FindRegistration(root);
            if (entry == null) throw new InvalidOperationException("この楽器の登録がありません。先にインポートしてください。");
            var target = InstrumentProtocol.Target(entry);
            if (!target.transform.IsChildOf(root.transform)) throw new InvalidOperationException("表示対象を含む楽器ルートを指定してください。");
            if (entry.gameObject == root) throw new InvalidOperationException("登録項目ではなく楽器ルートを指定してください。");
            Undo.SetTransformParent(entry.transform, root.transform, "Move registration inside instrument");
            PrefabUtility.RecordPrefabInstancePropertyModifications(entry.transform);
            return entry;
        }

        internal static ModularAvatarMenuItem ImportPlaced(GameObject root, GameObject target, GameObject settings,
            string label, string category, int slot)
        {
            if (root == null || target == null || !target.transform.IsChildOf(root.transform))
                throw new InvalidOperationException("配置済みの楽器と、その配下の表示対象を指定してください。");
            if (slot < 0 || slot > 255) throw new InvalidOperationException("Instruments の値は0～255で指定してください。");
            var avatar = root.GetComponentInParent<VRCAvatarDescriptor>(true);
            if (avatar == null) throw new InvalidOperationException("アバターのヒエラルキーへ配置してからインポートしてください。");
            var entry = FindRegistration(root, target);
            foreach (var other in avatar.GetComponentsInChildren<ModularAvatarMenuItem>(true).Where(InstrumentProtocol.IsEntry))
                if (other != entry && (InstrumentProtocol.RequestedSlot(other) == slot || slot == 0 && other.isDefault))
                    throw new InvalidOperationException("Instruments=" + slot + " は別の楽器で使用されています。");
            var sourceEntry = entry == null ? null : PrefabUtility.GetCorrespondingObjectFromSource(entry);
            var identity = entry != null && InstrumentProtocol.IsEntry(entry) ? InstrumentProtocol.Identity(entry)[0] :
                sourceEntry != null && InstrumentProtocol.IsEntry(sourceEntry) ? InstrumentProtocol.Identity(sourceEntry)[0] : Guid.NewGuid().ToString("N");
            if (entry == null)
                entry = AmamiInstrumentsPackageTools.Register(root, settings, label, category);
            if (!entry.transform.IsChildOf(root.transform))
            {
                Undo.SetTransformParent(entry.transform, root.transform, "Move registration inside instrument");
                PrefabUtility.RecordPrefabInstancePropertyModifications(entry.transform);
            }
            Undo.RecordObject(entry, "Set Instruments value");
            entry.Control.type = VRCExpressionsMenu.Control.ControlType.Toggle;
            entry.Control.parameter.name = InstrumentProtocol.FixedEntry + identity + "|" + Uri.EscapeDataString(category) + "|" + slot;
            entry.Control.value = slot;
            entry.automaticValue = false;
            entry.label = label;
            // The placement Settings is a shared parent. Menu controls for this
            // instrument live inside the product that was just imported.
            entry.menuSource_otherObjectChildren = settings != null && root.transform.IsChildOf(settings.transform)
                ? root : settings;
            InstrumentProtocol.FlattenRegistrationFolders(root, true);
            InstrumentProtocol.SeparateRegistration(entry);
            var toggle = entry.GetComponent<ModularAvatarObjectToggle>();
            if (toggle == null) toggle = Undo.AddComponent<ModularAvatarObjectToggle>(entry.gameObject);
            Undo.RecordObject(toggle, "Set instrument target");
            toggle.Objects.Clear();
            toggle.Objects.Add(new ToggledObject { Object = new AvatarObjectReference(target), Active = true });
            Attach(entry, target, slot, entry.isDefault, true);
            RemoveSubmenuVisibility(root, target);
            SetAllEditVisibility(avatar.gameObject, EditorPrefs.GetBool(EditVisibilityKey, false), false);
            EditorUtility.SetDirty(entry);
            EditorUtility.SetDirty(toggle);
            PrefabUtility.RecordPrefabInstancePropertyModifications(entry);
            PrefabUtility.RecordPrefabInstancePropertyModifications(toggle);
            return entry;
        }
    }
}
