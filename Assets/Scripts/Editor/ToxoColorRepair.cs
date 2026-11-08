using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Yamaneko.AmamiInstruments.Editor
{
    internal static class ToxoColorRepair
    {
        internal static int Repair(GameObject avatar)
        {
            int changed = 0;
            foreach (var merge in avatar.GetComponentsInChildren<ModularAvatarMergeAnimator>(true))
            {
                if (!(merge.animator is AnimatorController controller) ||
                    AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(controller)) != "75dea8ffd78bc5b4a8074139ff4358a1" ||
                    merge.pathMode != MergeAnimatorPathMode.Relative) continue;
                var renderers = merge.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (renderers.Length != 1) continue;
                var target = renderers[0].gameObject;
                if (merge.relativePathRoot.Get(merge) != target)
                {
                    Undo.RecordObject(merge, "Fix Toxo color animation target");
                    merge.relativePathRoot = new AvatarObjectReference(target);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(merge);
                    changed++;
                }
                var animator = target.GetComponent<Animator>();
                if (animator != null && animator.runtimeAnimatorController == controller)
                {
                    Undo.RecordObject(animator, "Disable duplicate Toxo color playback");
                    animator.runtimeAnimatorController = null;
                    animator.enabled = false;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
                    changed++;
                }
            }
            return changed;
        }

        [MenuItem("Tools/Yamaneko/Amami Instruments/Repair Toxo Color Binding")]
        private static void RepairOpenScenes()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            foreach (var avatar in Object.FindObjectsOfType<VRCAvatarDescriptor>(true))
            {
                if (EditorUtility.IsPersistent(avatar) || EditorSceneManager.IsPreviewScene(avatar.gameObject.scene)) continue;
                if (Repair(avatar.gameObject) == 0) continue;
                EditorSceneManager.MarkSceneDirty(avatar.gameObject.scene);
                Debug.Log("Amami Instruments: Toxoの色変更をメッシュへ接続し、重複したAnimatorを無効化しました。シーンを保存してください（Undo可能）。", avatar);
            }
        }

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            if (!Application.isBatchMode) EditorApplication.delayCall += RepairOpenScenes;
        }
    }
}
