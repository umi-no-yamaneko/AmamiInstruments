using System;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor.Animations;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;
using Object = UnityEngine.Object;

namespace Yamaneko.AmamiInstruments.Editor
{
    // Compatibility for scene-local setups which do not inherit the updated common prefab.
    // Called ONLY on the NDMF build clone, before collecting the v1 registrations.
    internal static class AmamiInstrumentsLegacy
    {
        internal static bool RepairBassColor(GameObject avatar, bool undo)
        {
            const string parameter = "AquaBassColor";
            var controllers = avatar.GetComponentsInChildren<ModularAvatarMergeAnimator>(true).Where(m =>
                m.animator is AnimatorController c && c.parameters.Any(p => p.name == parameter)).ToArray();
            if (controllers.Length == 0) return false;
            var changed = false;
            foreach (var parameters in avatar.GetComponentsInChildren<ModularAvatarParameters>(true))
            {
                var controls = parameters.GetComponentsInChildren<ModularAvatarMenuItem>(true);
                if (!controls.Any(c => InstrumentProtocol.Parameter(c) == parameter) ||
                    controls.Any(c => InstrumentProtocol.Parameter(c) == "GuitarColor")) continue;
                var index = parameters.parameters.FindIndex(p => p.nameOrPrefix == "GuitarColor" && !p.isPrefix);
                if (index < 0) continue;
                var wrong = parameters.parameters[index];
                if (undo) Undo.RecordObject(parameters, "Fix Bass color parameter");
                if (parameters.parameters.Any(p => p.nameOrPrefix == parameter && !p.isPrefix)) parameters.parameters.Remove(wrong);
                else
                {
                    wrong.nameOrPrefix = parameter;
                    wrong.syncType = ParameterSyncType.Int;
                    parameters.parameters[index] = wrong;
                }
                if (undo) PrefabUtility.RecordPrefabInstancePropertyModifications(parameters);
                changed = true;
            }
            foreach (var merge in controllers)
            {
                // Existing color clips use an empty path and are authored on the actual renderer.
                // Pin that renderer explicitly without moving the model or the menu objects.
                if (merge.GetComponent<SkinnedMeshRenderer>() == null || merge.pathMode != MergeAnimatorPathMode.Relative ||
                    merge.relativePathRoot.Get(merge) != null) continue;
                if (undo) Undo.RecordObject(merge, "Bind Bass color to renderer");
                merge.relativePathRoot = new AvatarObjectReference(merge.gameObject);
                if (undo) PrefabUtility.RecordPrefabInstancePropertyModifications(merge);
                changed = true;
            }
            return changed;
        }

        [InitializeOnLoadMethod]
        private static void RepairOpenSceneBassColor()
        {
            if (Application.isBatchMode) return;
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                foreach (var avatar in Object.FindObjectsOfType<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>(true))
                {
                    if (EditorUtility.IsPersistent(avatar) || UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(avatar.gameObject.scene)) continue;
                    if (!RepairBassColor(avatar.gameObject, true)) continue;
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(avatar.gameObject.scene);
                    Debug.Log("Amami Instruments: Bassの色パラメーターと参照を修正しました。階層は変更していません。シーンを保存してください。", avatar);
                }
            };
        }

        internal static void UpgradeBuildClone(GameObject avatar)
        {
            var names = new[] { "Instruments", "InstrumentsToggle", "InstrumentsFront" };
            var systems = avatar.GetComponentsInChildren<ModularAvatarParameters>(true).Where(p =>
                p.parameters.Any(x => x.nameOrPrefix == "InstrumentsToggle") &&
                p.parameters.Any(x => x.nameOrPrefix == "InstrumentsFront")).ToArray();
            foreach (var system in systems)
            {
                var common = system.gameObject;
                var items = common.GetComponentsInChildren<ModularAvatarMenuItem>(true);
                var global = items.SingleOrDefault(x => InstrumentProtocol.Parameter(x) == InstrumentProtocol.Root);
                var visible = global == null ? null : global.GetComponentsInChildren<ModularAvatarMenuItem>(true)
                    .Single(x => InstrumentProtocol.Parameter(x) == InstrumentProtocol.Visible);
                GameObject container;
                if (visible != null) container = InstrumentProtocol.Target(visible);
                else
                {
                    var candidates = common.GetComponentsInChildren<Component>(true).Where(x => x != null &&
                        (x is UnityEngine.Animations.ParentConstraint || x.GetType().Name == "VRCParentConstraint"))
                        .Where(x =>
                        {
                            try { InstrumentProtocol.PositionConstraint(x.gameObject); return true; }
                            catch (InvalidOperationException) { return false; }
                        }).ToArray();
                    if (candidates.Length != 1)
                        throw new InvalidOperationException("Amami Instruments: 旧シーンの格納先を一意に特定できません。Managerで共通ルートと格納先を指定してください。");
                    container = candidates[0].gameObject;
                }
                // Preflight before modifying even the clone. Do not remove a mixed material/selection controller.
                var controllers = common.GetComponentsInChildren<ModularAvatarMergeAnimator>(true).Where(x =>
                    x.animator is AnimatorController c && !c.name.StartsWith(InstrumentSelectionAnimator.ControllerPrefix, StringComparison.Ordinal) &&
                    c.parameters.Any(p => names.Contains(p.name))).ToArray();
                foreach (var merge in controllers)
                    if (((AnimatorController)merge.animator).parameters.Any(p => !names.Contains(p.name) && p.name != "AFK"))
                        throw new InvalidOperationException("Amami Instruments: 旧共通制御と固有制御を分離してください: " + merge.animator.name);
                var oldDefault = items.FirstOrDefault(x => InstrumentProtocol.Parameter(x) == "Instruments" && x.Control.value == 0);
                var defaultLabel = oldDefault == null ? null : string.IsNullOrEmpty(oldDefault.label) ? oldDefault.name : oldDefault.label;
                foreach (var merge in controllers) Object.DestroyImmediate(merge);
                foreach (var p in common.GetComponentsInChildren<ModularAvatarParameters>(true))
                    p.parameters.RemoveAll(x => names.Contains(x.nameOrPrefix));
                foreach (var item in items)
                    if (names.Contains(InstrumentProtocol.Parameter(item))) Object.DestroyImmediate(item);
                foreach (var installer in common.GetComponents<ModularAvatarMenuInstaller>()) Object.DestroyImmediate(installer);
                foreach (var item in common.GetComponents<ModularAvatarMenuItem>()) Object.DestroyImmediate(item);
                if (global == null)
                {
                    global = InstrumentProtocol.Menu(common.transform, "AmamiInstrumentsGlobalSettings",
                        VRCExpressionsMenu.Control.ControlType.SubMenu, InstrumentProtocol.Root);
                    global.gameObject.AddComponent<ModularAvatarMenuInstaller>();
                    InstrumentProtocol.Toggle(InstrumentProtocol.Menu(global.transform, "楽器の表示",
                        VRCExpressionsMenu.Control.ControlType.Toggle, InstrumentProtocol.Visible), container);
                    InstrumentProtocol.Menu(global.transform, "前に配置（OFFで後ろ）",
                        VRCExpressionsMenu.Control.ControlType.Toggle, InstrumentProtocol.Front);
                    InstrumentProtocol.Menu(global.transform, "楽器の種類",
                        VRCExpressionsMenu.Control.ControlType.SubMenu, InstrumentProtocol.Catalog);
                }
                foreach (Transform child in container.transform)
                {
                    if (child.GetComponentsInChildren<ModularAvatarMenuItem>(true).Any(InstrumentProtocol.IsEntry)) continue;
                    var customization = child.GetComponent<ModularAvatarMenuItem>();
                    var first = !container.GetComponentsInChildren<ModularAvatarMenuItem>(true).Any(x => InstrumentProtocol.IsEntry(x) && x.isDefault);
                    var label = first && defaultLabel != null ? defaultLabel : child.name;
                    var entry = InstrumentProtocol.Menu(child, label, VRCExpressionsMenu.Control.ControlType.Toggle,
                        InstrumentProtocol.Entry + Guid.NewGuid().ToString("N") + "|");
                    InstrumentProtocol.Toggle(entry, InstrumentSelectionAnimator.LegacyVisualTarget(child.gameObject));
                    InstrumentProtocol.SeparateRegistration(entry);
                    entry.isDefault = first;
                    if (customization != null && customization.MenuSource == SubmenuSource.Children)
                        entry.menuSource_otherObjectChildren = customization.menuSource_otherObjectChildren != null ? customization.menuSource_otherObjectChildren : child.gameObject;
                }
            }
        }
    }
}
