using System;
using System.IO;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using Object = UnityEngine.Object;

namespace Yamaneko.AmamiInstruments.Editor
{
    public sealed class AmamiInstrumentsPackageTools : EditorWindow
    {
        internal const string CorePath = "Assets/Umi-no-Yamaneko/YamanekoInstruments/Global/Scripts";
        private VRCAvatarDescriptor setupAvatar;
        private GameObject common, container, instrument, settings, addonPrefab;
        private GameObject visualTarget, installPrefab, installSettings;
        private int instrumentsValue = 1;
        private Transform front, rear;
        private string displayName = "New Instrument", category = "Guitar/Electric", message;
        private Vector2 scroll;

        [MenuItem("Tools/Yamaneko/Amami Instruments/Manager")]
        public static void Open() => GetWindow<AmamiInstrumentsPackageTools>("Amami Instruments");

        private void OnGUI()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox("インポートする前にPlay Modeを停止してください。", MessageType.Info);
                return;
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.HelpBox("1. アバターを初期セットアップ → 2. 楽器プレハブと導入先Settingsを指定して導入。操作はUndoできます。", MessageType.Info);
            setupAvatar = (VRCAvatarDescriptor)EditorGUILayout.ObjectField("初期セットアップ先アバター", setupAvatar, typeof(VRCAvatarDescriptor), true);
            if (GUILayout.Button("アバターに初期セットアップ")) Run(() => Edit(() =>
            {
                var avatar = setupAvatar != null ? setupAvatar : Selection.activeGameObject?.GetComponentInParent<VRCAvatarDescriptor>(true);
                common = InitializeAvatar(avatar);
                container = InstrumentProtocol.Target(common.GetComponentsInChildren<ModularAvatarMenuItem>(true)
                    .Single(x => InstrumentProtocol.Parameter(x) == InstrumentProtocol.Visible));
                front = rear = null;
                Selection.activeGameObject = common;
                EditorSceneManager.MarkSceneDirty(common.scene);
                message = "初期セットアップ完了。前後アンカーの位置を調整し、楽器を格納先へ追加してください。";
            }));
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("2. 楽器を導入", EditorStyles.boldLabel);
            installPrefab = (GameObject)EditorGUILayout.ObjectField("導入する楽器プレハブ", installPrefab, typeof(GameObject), false);
            if (GUILayout.Button("Projectで選択したプレハブを使用")) Run(() =>
            {
                var selected = Selection.activeGameObject;
                if (selected == null || !EditorUtility.IsPersistent(selected) || !PrefabUtility.IsPartOfPrefabAsset(selected))
                    throw new InvalidOperationException("Projectで楽器プレハブを選択してください。");
                installPrefab = selected;
            });
            installSettings = (GameObject)EditorGUILayout.ObjectField("導入先 Settings", installSettings, typeof(GameObject), true);
            if (GUILayout.Button("Hierarchyで選択したSettingsを導入先にする")) Run(() =>
            {
                ValidateInstallSettings(Selection.activeGameObject);
                installSettings = Selection.activeGameObject;
            });
            EditorGUILayout.HelpBox("楽器プレハブと、初期セットアップで生成されたSettingsを指定します。配置・メニュー登録・表示切替FXの設定をまとめて行います。", MessageType.Info);
            if (GUILayout.Button("指定したSettingsへ楽器を導入")) Run(() => Edit(() =>
            {
                var installed = InstallIntoSettings(installPrefab, installSettings);
                Selection.activeGameObject = installed;
                EditorSceneManager.MarkSceneDirty(installed.scene);
                message = "楽器を導入しました。位置を確認してシーンを保存してください。";
            }));
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("既存設定・詳細操作", EditorStyles.boldLabel);
            common = (GameObject)EditorGUILayout.ObjectField("共通ルート", common, typeof(GameObject), true);
            container = (GameObject)EditorGUILayout.ObjectField("楽器の格納先", container, typeof(GameObject), true);
            if (GUILayout.Button("選択物から候補を取得")) Run(() =>
            {
                common = Selection.activeGameObject;
                if (common == null) throw new InvalidOperationException("Hierarchy で共通ルートを選択してください。");
                var visible = common.GetComponentsInChildren<ModularAvatarMenuItem>(true)
                    .FirstOrDefault(x => InstrumentProtocol.Parameter(x) == InstrumentProtocol.Visible);
                if (visible != null) container = InstrumentProtocol.Target(visible);
                else
                {
                    var candidates = common.GetComponentsInChildren<Component>(true).Where(x => x is ParentConstraint ||
                        x != null && x.GetType().FullName == "VRC.SDK3.Dynamics.Constraint.Components.VRCParentConstraint").ToArray();
                    if (candidates.Length == 1) container = candidates[0].gameObject;
                }
            });
            front = (Transform)EditorGUILayout.ObjectField("前アンカー（新規作成時）", front, typeof(Transform), true);
            rear = (Transform)EditorGUILayout.ObjectField("後アンカー（新規作成時）", rear, typeof(Transform), true);
            if (GUILayout.Button("共通設定を作成 / 既存の共通制御を移行")) Run(() => Edit(() =>
            {
                RequireScene(common);
                if (container == null || container == common || !container.transform.IsChildOf(common.transform))
                    throw new InvalidOperationException("共通ルート配下の楽器格納先を指定してください。");
                if (front != null || rear != null)
                {
                    if (front == null || rear == null || front.IsChildOf(container.transform) || rear.IsChildOf(container.transform))
                        throw new InvalidOperationException("前後アンカーを表示対象の外側に2つ指定してください。");
                    if (container.GetComponent<ParentConstraint>() != null || container.GetComponents<Component>().Any(x =>
                        x != null && x.GetType().Name == "VRCParentConstraint"))
                        throw new InvalidOperationException("既存の Constraint を使用する場合は前後アンカー欄を空にしてください。");
                    var constraint = Undo.AddComponent<ParentConstraint>(container);
                    constraint.AddSource(new ConstraintSource { sourceTransform = front, weight = 1 });
                    constraint.AddSource(new ConstraintSource { sourceTransform = rear, weight = 0 });
                    constraint.weight = 1;
                    constraint.locked = true;
                    constraint.constraintActive = true;
                }
                InstrumentProtocol.PositionConstraint(container);
                Setup(common, container);
            }));
            EditorGUILayout.Space();
            void EditVisibility(bool visible)
            {
                var avatar = (common != null ? common : Selection.activeGameObject)?.GetComponentInParent<VRCAvatarDescriptor>(true);
                if (avatar == null) throw new InvalidOperationException("Hierarchyでアバター内のオブジェクトを選択してください。");
                InstrumentSelectionAnimator.SetAllEditVisibility(avatar.gameObject, visible);
            }
            if (GUILayout.Button("編集時：すべての楽器を表示")) Run(() => Edit(() => EditVisibility(true)));
            if (GUILayout.Button("編集時：すべての楽器を非表示")) Run(() => Edit(() => EditVisibility(false)));
            instrument = (GameObject)EditorGUILayout.ObjectField("楽器ルート（配布単位）", instrument, typeof(GameObject), true);
            visualTarget = (GameObject)EditorGUILayout.ObjectField("表示対象（省略で楽器全体）", visualTarget, typeof(GameObject), true);
            instrumentsValue = EditorGUILayout.IntField("Instruments の値（0～255）", instrumentsValue);
            settings = (GameObject)EditorGUILayout.ObjectField("カスタマイズの子メニュー元", settings, typeof(GameObject), true);
            displayName = EditorGUILayout.TextField("楽器の表示名", displayName);
            category = EditorGUILayout.TextField("分類（/ で階層化）", category);
            if (GUILayout.Button("選択したヒエラルキーの楽器を読み込む")) Run(() =>
            {
                instrument = Selection.activeGameObject;
                RequireScene(instrument);
                var entry = InstrumentSelectionAnimator.FindRegistration(instrument);
                visualTarget = entry != null ? InstrumentProtocol.Target(entry) : instrument;
                displayName = entry != null ? entry.label : instrument.name;
                var definition = entry != null && InstrumentProtocol.IsEntry(entry) ? entry :
                    entry != null ? PrefabUtility.GetCorrespondingObjectFromSource(entry) : null;
                if (definition != null && InstrumentProtocol.IsEntry(definition))
                {
                    instrumentsValue = InstrumentProtocol.RequestedSlot(definition) ?? 1;
                    category = InstrumentProtocol.Identity(definition)[1];
                }
                var menu = instrument.GetComponent<ModularAvatarMenuItem>();
                settings = entry != null && entry.menuSource_otherObjectChildren != null ? entry.menuSource_otherObjectChildren :
                    menu != null && menu.MenuSource == SubmenuSource.Children ? (menu.menuSource_otherObjectChildren != null ? menu.menuSource_otherObjectChildren : instrument) : instrument;
            });
            if (GUILayout.Button("登録情報を楽器ルート内へ移動")) Run(() => Edit(() =>
            {
                RequireScene(instrument);
                InstrumentSelectionAnimator.MoveRegistrationInside(instrument);
                EditorSceneManager.MarkSceneDirty(instrument.scene);
            }));
            if (GUILayout.Button("配布用プレハブを作成（Instruments値を未指定にする）")) Run(() =>
            {
                RequireScene(instrument);
                // Validate the sanitized export copy, preserving scene collider references.
                var path = EditorUtility.SaveFilePanelInProject("配布用プレハブ", instrument.name + "Addon", "prefab", "導入シーンとは別の配布用プレハブを作成します。");
                if (!string.IsNullOrEmpty(path)) CreateDistributionPrefab(instrument, path, visualTarget, settings, displayName, category);
            });
            if (GUILayout.Button("配置済み楽器をインポート / FX・メニュー更新")) Run(() => Edit(() =>
            {
                RequireScene(instrument);
                var entry = InstrumentSelectionAnimator.ImportPlaced(instrument, visualTarget != null ? visualTarget : instrument, settings,
                    displayName, category, instrumentsValue);
                Selection.activeGameObject = entry.gameObject;
                EditorSceneManager.MarkSceneDirty(instrument.scene);
                message = "登録完了: Instruments=" + instrumentsValue + "。FXのShow/Hide遷移を生成しました。";
            }));
            if (GUILayout.Button("自動値で登録 / 表示名と分類を更新（互換）")) Run(() => Edit(() =>
            {
                RequireScene(instrument);
                Register(instrument, settings, displayName, category);
            }));
            if (GUILayout.Button("格納先直下の既存楽器を一括登録")) Run(() => Edit(() =>
            {
                RequireScene(container);
                RegisterChildren(container);
            }));
            if (GUILayout.Button("選択した登録メニューに新しい製品IDを割り当て")) Run(() => Edit(() =>
            {
                var item = Selection.activeGameObject?.GetComponent<ModularAvatarMenuItem>();
                if (item == null || !InstrumentProtocol.IsEntry(item)) throw new InvalidOperationException("登録メニューの葉を選択してください。");
                Undo.RecordObject(item, "New instrument identity");
                item.Control.parameter.name = InstrumentProtocol.Entry + Guid.NewGuid().ToString("N") + "|" +
                    Uri.EscapeDataString(InstrumentProtocol.Identity(item)[1]);
            }));
            EditorGUILayout.Space();
            addonPrefab = (GameObject)EditorGUILayout.ObjectField("追加楽器プレハブ", addonPrefab, typeof(GameObject), false);
            if (GUILayout.Button("指定した格納先へ追加")) Run(() => Edit(() =>
            {
                RequireScene(container);
                if (addonPrefab == null || !PrefabUtility.IsPartOfPrefabAsset(addonPrefab)) throw new InvalidOperationException("楽器プレハブを指定してください。");
                if (!addonPrefab.GetComponentsInChildren<ModularAvatarMenuItem>(true).Any(InstrumentProtocol.IsEntry))
                    throw new InvalidOperationException("このプレハブは未登録です。シーンで登録してプレハブへ保存してください。");
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(addonPrefab, container.transform);
                Undo.RegisterCreatedObjectUndo(instance, "Install instrument");
                Selection.activeGameObject = instance;
            }));
            if (GUILayout.Button("アバターの設定を検証")) Run(() =>
            {
                var avatar = (common != null ? common : Selection.activeGameObject)?.GetComponentInParent<VRCAvatarDescriptor>(true);
                if (avatar == null) throw new InvalidOperationException("アバター内の共通ルートを指定してください。");
                var preview = EditorSceneManager.NewPreviewScene();
                GameObject clone = null;
                try
                {
                    clone = Object.Instantiate(avatar.gameObject);
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(clone, preview);
                    AmamiInstrumentsLegacy.UpgradeBuildClone(clone);
                    var layout = AmamiInstrumentsBuild.Inspect(clone);
                    if (layout == null) throw new InvalidOperationException("共通設定がありません。");
                    message = "検証OK: " + layout.Entries.Length + " 楽器。旧設定はビルド時に自動移行されます。共通同期パラメーターは10 bit（楽器固有分を除く）。";
                }
                finally
                {
                    if (clone != null) Object.DestroyImmediate(clone);
                    EditorSceneManager.ClosePreviewScene(preview);
                }
            });
            if (GUILayout.Button("選択した楽器プレハブを unitypackage に出力")) Run(ExportProduct);
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        private void Run(Action action)
        {
            try { message = "完了"; action(); }
            catch (Exception e) { message = e.Message; Debug.LogException(e); }
        }

        private static void RequireScene(GameObject go)
        {
            if (go == null || EditorUtility.IsPersistent(go)) throw new InvalidOperationException("シーンまたは Prefab Mode 内のオブジェクトを指定してください。");
        }

        private static void Edit(Action action)
        {
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Configure Amami Instruments");
            try
            {
                action();
                foreach (var root in Selection.gameObjects)
                    if (root.scene.IsValid()) EditorSceneManager.MarkSceneDirty(root.scene);
                Undo.CollapseUndoOperations(group);
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
        }

        private static VRCAvatarDescriptor ValidateInstallSettings(GameObject destination)
        {
            RequireScene(destination);
            var avatar = destination.GetComponentInParent<VRCAvatarDescriptor>(true);
            if (avatar == null) throw new InvalidOperationException("アバター内のSettingsを指定してください。");
            var roots = avatar.GetComponentsInChildren<ModularAvatarMenuItem>(true)
                .Where(x => InstrumentProtocol.Parameter(x) == InstrumentProtocol.Root).ToArray();
            if (roots.Length != 1) throw new InvalidOperationException("先にアバターの初期セットアップを行ってください。");
            var visible = roots[0].GetComponentsInChildren<ModularAvatarMenuItem>(true)
                .Single(x => InstrumentProtocol.Parameter(x) == InstrumentProtocol.Visible);
            var storage = InstrumentProtocol.Target(visible);
            if (destination == storage || destination.transform.parent != storage.transform ||
                !destination.name.EndsWith("Settings", StringComparison.Ordinal))
                throw new InvalidOperationException("楽器格納先の直下にあるSettingsを選択してください。");
            InstrumentProtocol.PositionConstraint(storage);
            if (!destination.activeInHierarchy) throw new InvalidOperationException("導入先Settingsとその親を有効にしてください。");
            return avatar;
        }

        internal static GameObject InstallIntoSettings(GameObject prefab, GameObject destination)
        {
            var avatar = ValidateInstallSettings(destination);
            if (prefab == null || !EditorUtility.IsPersistent(prefab) || !PrefabUtility.IsPartOfPrefabAsset(prefab) ||
                AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(prefab)) != prefab)
                throw new InvalidOperationException("Projectの楽器プレハブのルートを指定してください。");
            // Repeating the same installation must not duplicate an instrument or its identity.
            var duplicate = destination.transform.Cast<Transform>().FirstOrDefault(t =>
                PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject) == prefab);
            if (duplicate != null) return duplicate.gameObject;
            var definitions = prefab.GetComponentsInChildren<ModularAvatarMenuItem>(true).Where(InstrumentProtocol.IsEntry).ToArray();
            if (definitions.Length > 1) throw new InvalidOperationException("1つの楽器を含むプレハブを指定してください。");
            var existing = avatar.GetComponentsInChildren<ModularAvatarMenuItem>(true).Where(InstrumentProtocol.IsEntry).ToArray();
            if (definitions.Length == 1 && existing.Any(e => InstrumentProtocol.Identity(e)[0] == InstrumentProtocol.Identity(definitions[0])[0]))
                throw new InvalidOperationException("この楽器は既にアバターに導入されています。");
            var used = InstrumentProtocol.ResolveSlots(existing);
            var free = Enumerable.Range(1, 255).Concat(new[] { 0 }).Where(x => !used.Contains(x)).ToArray();
            if (free.Length == 0) throw new InvalidOperationException("登録できる楽器は最大256個です。");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, destination.transform);
            Undo.RegisterCreatedObjectUndo(instance, "Install instrument into Settings");
            var entry = InstrumentSelectionAnimator.FindRegistration(instance);
            var target = entry != null ? InstrumentProtocol.Target(entry) : instance;
            if (target == null || !target.transform.IsChildOf(instance.transform))
                throw new InvalidOperationException("楽器プレハブの表示対象参照を確認してください。");
            var menu = instance.GetComponent<ModularAvatarMenuItem>();
            var customization = entry != null && entry.menuSource_otherObjectChildren != null ? entry.menuSource_otherObjectChildren :
                menu != null && menu.menuSource_otherObjectChildren != null ? menu.menuSource_otherObjectChildren : instance;
            var label = entry != null ? entry.label : prefab.name;
            var instrumentCategory = entry != null ? InstrumentProtocol.Identity(entry)[1] : "";
            var registered = InstrumentSelectionAnimator.ImportPlaced(instance, target, customization, label, instrumentCategory, free[0]);
            if (existing.Length == 0)
            {
                Undo.RecordObject(registered, "Set initial instrument");
                registered.isDefault = true;
                PrefabUtility.RecordPrefabInstancePropertyModifications(registered);
            }
            AmamiInstrumentsBuild.Inspect(avatar.gameObject);
            return instance;
        }

        internal static GameObject InitializeAvatar(VRCAvatarDescriptor avatar)
        {
            if (avatar == null) throw new InvalidOperationException("セットアップ先のアバターを指定してください。");
            RequireScene(avatar.gameObject);
            var roots = avatar.GetComponentsInChildren<ModularAvatarMenuItem>(true)
                .Where(x => InstrumentProtocol.Parameter(x) == InstrumentProtocol.Root).ToArray();
            if (roots.Length > 1) throw new InvalidOperationException("共通設定が複数あります。既存設定を確認してください。");
            if (roots.Length == 1)
            {
                var visible = roots[0].GetComponentsInChildren<ModularAvatarMenuItem>(true)
                    .Single(x => InstrumentProtocol.Parameter(x) == InstrumentProtocol.Visible);
                InstrumentProtocol.PositionConstraint(InstrumentProtocol.Target(visible));
                return roots[0].transform.parent.gameObject;
            }
            if (avatar.GetComponentsInChildren<Transform>(true).Any(x => x.name == "YamanekoGimmicAmamiInstruments"))
                throw new InvalidOperationException("既存の共通ルートがあります。共通ルートと格納先を指定し、既存の共通制御を移行してください。");
            GameObject Create(Transform parent, string name)
            {
                var go = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(go, "Initialize Amami Instruments");
                go.transform.SetParent(parent, false);
                return go;
            }
            var root = Create(avatar.transform, "YamanekoGimmicAmamiInstruments");
            var frontAnchor = Create(root.transform, "InstrumentsFront");
            var rearAnchor = Create(root.transform, "InstrumentsRear");
            var animator = avatar.GetComponent<Animator>();
            var chest = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Chest) : null;
            var anchorParent = chest != null ? chest : root.transform;
            frontAnchor.transform.SetParent(anchorParent, false);
            rearAnchor.transform.SetParent(anchorParent, false);
            frontAnchor.transform.localPosition = new Vector3(0, 0, 0.15f);
            rearAnchor.transform.localPosition = new Vector3(0, 0, -0.15f);
            var target = Create(root.transform, "AmamiInstruments");
            var constraint = Undo.AddComponent<ParentConstraint>(target);
            constraint.AddSource(new ConstraintSource { sourceTransform = frontAnchor.transform, weight = 1 });
            constraint.AddSource(new ConstraintSource { sourceTransform = rearAnchor.transform, weight = 0 });
            constraint.weight = 1;
            constraint.locked = true;
            constraint.constraintActive = true;
            Create(target.transform, "AmamiGuitarSettings");
            Create(target.transform, "AmamiAquaBassSettings");
            Create(target.transform, "AmamiInstrumentsSettings");
            Setup(root, target);
            return root;
        }

        internal static ModularAvatarMenuItem Setup(GameObject common, GameObject container)
        {
            var existing = common.GetComponentsInChildren<ModularAvatarMenuItem>(true)
                .SingleOrDefault(x => InstrumentProtocol.Parameter(x) == InstrumentProtocol.Root);
            RemoveLegacy(common);
            if (existing != null) return existing;
            var global = InstrumentProtocol.Menu(common.transform, "AmamiInstrumentsGlobalSettings",
                VRCExpressionsMenu.Control.ControlType.SubMenu, InstrumentProtocol.Root);
            Undo.RegisterCreatedObjectUndo(global.gameObject, "Create global menu");
            global.gameObject.AddComponent<ModularAvatarMenuInstaller>();
            InstrumentProtocol.Toggle(InstrumentProtocol.Menu(global.transform, "楽器の表示",
                VRCExpressionsMenu.Control.ControlType.Toggle, InstrumentProtocol.Visible), container);
            InstrumentProtocol.Menu(global.transform, "前に配置（OFFで後ろ）", VRCExpressionsMenu.Control.ControlType.Toggle, InstrumentProtocol.Front);
            InstrumentProtocol.Menu(global.transform, "楽器の種類", VRCExpressionsMenu.Control.ControlType.SubMenu, InstrumentProtocol.Catalog);
            return global;
        }

        internal static ModularAvatarMenuItem Register(GameObject target, GameObject settings, string label, string category)
        {
            if (string.IsNullOrWhiteSpace(label)) throw new InvalidOperationException("楽器の表示名を入力してください。");
            var entries = target.GetComponentsInChildren<ModularAvatarMenuItem>(true).Where(InstrumentProtocol.IsEntry).ToArray();
            if (entries.Length > 1) throw new InvalidOperationException("楽器の表示対象を1つに絞ってください。");
            var item = entries.SingleOrDefault();
            if (item == null)
            {
                item = InstrumentProtocol.Menu(target.transform, label, VRCExpressionsMenu.Control.ControlType.Toggle,
                    InstrumentProtocol.Entry + Guid.NewGuid().ToString("N") + "|");
                InstrumentProtocol.Toggle(item, target);
                Undo.RegisterCreatedObjectUndo(item.gameObject, "Register instrument");
            }
            Undo.RecordObject(item, "Configure instrument");
            var id = InstrumentProtocol.Identity(item)[0];
            var requested = InstrumentProtocol.RequestedSlot(item);
            item.Control.parameter.name = (requested.HasValue ? InstrumentProtocol.FixedEntry : InstrumentProtocol.Entry) + id + "|" +
                Uri.EscapeDataString(category.Trim('/').Trim()) + (requested.HasValue ? "|" + requested.Value : "");
            item.label = label;
            item.menuSource_otherObjectChildren = settings;
            InstrumentProtocol.FlattenRegistrationFolders(target, true);
            InstrumentProtocol.SeparateRegistration(item);
            EditorUtility.SetDirty(item);
            PrefabUtility.RecordPrefabInstancePropertyModifications(item);
            return item;
        }

        internal static void RegisterChildren(GameObject container)
        {
            foreach (Transform child in container.transform)
            {
                if (child.GetComponentsInChildren<ModularAvatarMenuItem>(true).Any(InstrumentProtocol.IsEntry)) continue;
                var menu = child.GetComponent<ModularAvatarMenuItem>();
                var source = menu != null && menu.MenuSource == SubmenuSource.Children
                    ? (menu.menuSource_otherObjectChildren != null ? menu.menuSource_otherObjectChildren : child.gameObject) : null;
                Register(child.gameObject, source, menu != null && !string.IsNullOrEmpty(menu.label) ? menu.label : child.name, "未分類");
            }
        }

        [MenuItem("Tools/Yamaneko/Amami Instruments/Migrate Generated Templates")]
        public static void MigrateTemplates()
        {
            const string generated = "Assets/Umi-no-Yamaneko/YamanekoInstruments/Generated/";
            void Migrate(string path, bool isBase)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return;
                var contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (isBase && contents.GetComponentsInChildren<ModularAvatarMenuItem>(true).Any(x =>
                        InstrumentProtocol.Parameter(x) == InstrumentProtocol.Root))
                    {
                        InstrumentProtocol.FlattenRegistrationFolders(contents, false);
                        PrefabUtility.SaveAsPrefabAsset(contents, path);
                        return;
                    }
                    if (isBase)
                    {
                        var constraint = contents.GetComponentsInChildren<Component>(true).Single(x =>
                            x != null && x.GetType().Name == "VRCParentConstraint");
                        Setup(contents, constraint.gameObject);
                        RegisterChildren(constraint.gameObject);
                        var initial = constraint.GetComponentsInChildren<ModularAvatarMenuItem>(true).Where(InstrumentProtocol.IsEntry).ToArray();
                        if (initial.Length == 1)
                        {
                            initial[0].isDefault = true;
                            initial[0].label = "Aqua";
                            initial[0].Control.parameter.name = InstrumentProtocol.Entry + InstrumentProtocol.Identity(initial[0])[0] + "|Guitar%2FElectric";
                        }
                    }
                    else
                    {
                        RemoveLegacy(contents);
                        var entry = Register(contents, null, "Aqua Bass", "Guitar/Bass");
                        // Control the model itself: existing scenes may override its active state to false.
                        // Enabling only the wrapper would leave that model permanently hidden.
                        var model = contents.transform.Cast<Transform>().Single(x => x.GetComponentsInChildren<Renderer>(true).Length > 0);
                        model.gameObject.SetActive(false);
                        var toggle = entry.GetComponent<ModularAvatarObjectToggle>();
                        var reference = new AvatarObjectReference(model.gameObject);
                        if (string.IsNullOrEmpty(reference.referencePath)) reference.referencePath = "__AmamiPrefabReference__/" + Guid.NewGuid().ToString("N");
                        toggle.Objects[0] = new ToggledObject { Object = reference, Active = true };
                    }
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
            }
            Migrate(generated + "YamanekoGimmicAmamiInstruments.prefab", true);
            Migrate(generated + "AquaBass/AmamiAquaBassAddon.prefab", false);
            AssetDatabase.SaveAssets();
            Debug.Log("Amami Instruments: 生成済みプレハブの移行完了。旧共通設定が残るシーンはビルド時に自動移行されます。");
        }

        // Migration is deliberately limited to the known old shared parameters/controllers.
        // Material controllers and other avatar systems are never deleted.
        internal static void RemoveLegacy(GameObject common)
        {
            var legacy = new[] { "Instruments", "InstrumentsToggle", "InstrumentsFront" };
            foreach (var merge in common.GetComponentsInChildren<ModularAvatarMergeAnimator>(true))
            {
                if (!(merge.animator is AnimatorController controller)) continue;
                if (controller.name.StartsWith(InstrumentSelectionAnimator.ControllerPrefix, StringComparison.Ordinal)) continue;
                if (!controller.parameters.Any(p => legacy.Contains(p.name))) continue;
                if (controller.parameters.Any(p => !legacy.Contains(p.name) && p.name != "AFK"))
                    throw new InvalidOperationException("旧共通制御と固有設定が混在しています。Controller を分離してください: " + controller.name);
                Undo.DestroyObjectImmediate(merge);
            }
            foreach (var parameters in common.GetComponentsInChildren<ModularAvatarParameters>(true))
            {
                Undo.RecordObject(parameters, "Remove legacy parameters");
                parameters.parameters.RemoveAll(p => legacy.Contains(p.nameOrPrefix));
                EditorUtility.SetDirty(parameters);
            }
            foreach (var item in common.GetComponentsInChildren<ModularAvatarMenuItem>(true))
                if (legacy.Contains(InstrumentProtocol.Parameter(item))) Undo.DestroyObjectImmediate(item);
            // The old top-level installer also exposed the old global menu. New customization is explicitly routed.
            foreach (var installer in common.GetComponents<ModularAvatarMenuInstaller>()) Undo.DestroyObjectImmediate(installer);
            foreach (var item in common.GetComponents<ModularAvatarMenuItem>()) Undo.DestroyObjectImmediate(item);
        }

        internal static void ValidateProductReferences(GameObject root)
        {
            var entries = root.GetComponentsInChildren<ModularAvatarMenuItem>(true).Where(InstrumentProtocol.IsEntry).ToArray();
            if (entries.Length != 1 || root.GetComponentsInChildren<ModularAvatarMenuItem>(true).Any(x => InstrumentProtocol.Parameter(x) == InstrumentProtocol.Root))
                throw new InvalidOperationException("登録情報を1つ含む楽器単体のルートを指定してください。共通設定や別の楽器を含めないでください。");
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null) throw new InvalidOperationException("Missing Scriptを取り除いてください。");
                // Transform.parent is intentionally outside the product when authoring in an avatar.
                if (component is Transform) continue;
                using (var serialized = new SerializedObject(component))
                {
                    var property = serialized.GetIterator();
                    while (property.NextVisible(true))
                    {
                        if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                        var value = property.objectReferenceValue;
                        var obj = value is GameObject go ? go : value is Component c ? c.gameObject : null;
                        if (obj != null && !obj.transform.IsChildOf(root.transform))
                            throw new InvalidOperationException("配布ルート外への参照があります: " + component.name + " / " + property.propertyPath + " → " + obj.name +
                                "。固有メニュー等を楽器内に含め、参照を設定し直してください。");
                    }
                }
            }
        }

        internal static GameObject CreateDistributionPrefab(GameObject root, string path, GameObject exportTarget = null, GameObject exportSettings = null, string exportLabel = null, string exportCategory = "")
        {
            RequireScene(root);
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ||
                File.Exists(path)) throw new InvalidOperationException("Assets内の新しい.prefabパスを指定してください。");
            var preview = EditorSceneManager.NewPreviewScene();
            GameObject copy = null;
            try
            {
                copy = Object.Instantiate(root);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(copy, preview);
                copy.name = root.name;
                // Legacy scene instruments may only gain registration on the build clone.
                // Prepare registration on the export copy without modifying the source scene.
                if (!copy.GetComponentsInChildren<ModularAvatarMenuItem>(true).Any(InstrumentProtocol.IsEntry))
                {
                    var sourceEntry = InstrumentSelectionAnimator.FindRegistration(root);
                    var sourceTarget = sourceEntry != null ? InstrumentProtocol.Target(sourceEntry) :
                        exportTarget != null ? exportTarget : InstrumentSelectionAnimator.LegacyVisualTarget(root);
                    GameObject MapInternal(GameObject source)
                    {
                        if (source == null) return null;
                        if (!source.transform.IsChildOf(root.transform)) return source;
                        var relative = AnimationUtility.CalculateTransformPath(source.transform, root.transform);
                        return relative.Length == 0 ? copy : copy.transform.Find(relative)?.gameObject;
                    }
                    if (sourceTarget == null || !sourceTarget.transform.IsChildOf(root.transform))
                        throw new InvalidOperationException("配布する楽器の内部にある表示対象を指定してください。");
                    var productId = sourceEntry != null ? InstrumentProtocol.Identity(sourceEntry)[0] : Guid.NewGuid().ToString("N");
                    var productCategory = sourceEntry != null ? InstrumentProtocol.Identity(sourceEntry)[1] : exportCategory;
                    var productLabel = sourceEntry != null ? sourceEntry.label :
                        !string.IsNullOrWhiteSpace(exportLabel) ? exportLabel : root.name;
                    var registration = InstrumentProtocol.Menu(copy.transform, "InstrumentSelection",
                        VRCExpressionsMenu.Control.ControlType.Toggle,
                        InstrumentProtocol.Entry + productId + "|" + Uri.EscapeDataString(productCategory ?? ""));
                    registration.label = productLabel;
                    InstrumentProtocol.Toggle(registration, MapInternal(sourceTarget));
                    registration.menuSource_otherObjectChildren = MapInternal(sourceEntry != null ?
                        sourceEntry.menuSource_otherObjectChildren : exportSettings != null ? exportSettings : root);
                }
                // Avatar-specific clothing colliders are not part of a portable instrument.
                foreach (var bone in copy.GetComponentsInChildren<VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone>(true))
                {
                    if (bone.gameObject.name != "PB_Guitar") continue;
                    using (var serialized = new SerializedObject(bone))
                    {
                        var colliders = serialized.FindProperty("colliders");
                        if (colliders == null || !colliders.isArray)
                            throw new InvalidOperationException("PB_Guitarのコライダー一覧を取得できません。");
                        colliders.arraySize = 0;
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                    }
                }
                // Initial-setup Settings belong to the destination avatar, not the product.
                // Only detach known ancestor Settings; unrelated external references remain errors.
                foreach (var registration in copy.GetComponentsInChildren<ModularAvatarMenuItem>(true).Where(InstrumentProtocol.IsEntry))
                {
                    var source = registration.menuSource_otherObjectChildren;
                    if (source == null || source.transform.IsChildOf(copy.transform)) continue;
                    if ((source.name == "AmamiGuitarSettings" || source.name == "AmamiAquaBassSettings" ||
                         source.name == "AmamiInstrumentsSettings") && root.transform.IsChildOf(source.transform))
                        registration.menuSource_otherObjectChildren = null;
                }
                ValidateProductReferences(copy);
                var entry = copy.GetComponentsInChildren<ModularAvatarMenuItem>(true).Single(InstrumentProtocol.IsEntry);
                var identity = InstrumentProtocol.Identity(entry);
                entry.Control.parameter.name = InstrumentProtocol.Entry + identity[0] + "|" + Uri.EscapeDataString(identity[1]);
                entry.Control.value = 1;
                entry.isDefault = false;
                foreach (var merge in entry.GetComponents<ModularAvatarMergeAnimator>().Where(m => m.animator != null &&
                    m.animator.name.StartsWith(InstrumentSelectionAnimator.ControllerPrefix, StringComparison.Ordinal)).ToArray()) Object.DestroyImmediate(merge);
                var target = InstrumentSelectionAnimator.PrefabTarget(entry);
                if (target == null) throw new InvalidOperationException("表示対象の参照がありません。");
                target.SetActive(false);
                var result = PrefabUtility.SaveAsPrefabAsset(copy, path);
                if (result == null) throw new InvalidOperationException("配布用プレハブの保存に失敗しました。");
                return result;
            }
            finally
            {
                if (copy != null) Object.DestroyImmediate(copy);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        private static void ExportProduct()
        {
            var prefab = Selection.activeObject as GameObject;
            if (prefab == null || !PrefabUtility.IsPartOfPrefabAsset(prefab)) throw new InvalidOperationException("Project ビューで楽器プレハブを選択してください。");
            var entries = prefab.GetComponentsInChildren<ModularAvatarMenuItem>(true).Where(InstrumentProtocol.IsEntry).ToArray();
            ValidateProductReferences(prefab);
            if (entries.Any(e => InstrumentProtocol.RequestedSlot(e).HasValue))
                throw new InvalidOperationException("導入先の値が固定されています。「配布用プレハブを作成」で値を未指定にしてから出力してください。");
            if (entries.Length != 1 || prefab.GetComponentsInChildren<ModularAvatarMenuItem>(true)
                .Any(x => InstrumentProtocol.Parameter(x) == InstrumentProtocol.Root))
                throw new InvalidOperationException("共通設定を含まない、登録楽器が1つのプレハブを指定してください。");
            var dependencies = AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(prefab), true);
            if (dependencies.Any(p => p.StartsWith(CorePath + "/", StringComparison.Ordinal) ||
                p.StartsWith("Packages/com.umi-no-yamaneko.amami-instruments/", StringComparison.Ordinal) ||
                p.Contains("/Global/Animations/") || p.EndsWith("/InstrumentsSelector.controller", StringComparison.Ordinal)))
                throw new InvalidOperationException("旧共通FXへの依存が残っています。共通制御を移行してから出力してください。");
            var paths = dependencies.Where(p => p.StartsWith("Assets/", StringComparison.Ordinal)).ToArray();
            var destination = EditorUtility.SaveFilePanel("Export instrument (core is distributed separately)", "", prefab.name, "unitypackage");
            if (string.IsNullOrEmpty(destination)) return;
            AssetDatabase.ExportPackage(paths, destination, ExportPackageOptions.Default);
        }
    }
}
