# Amami Instruments — 分離配布・ビルド時統合

対象: Unity 2022.3 / VRChat Avatars SDK 3.10.4 / Modular Avatar 1.18.1 / NDMF 1.14.4。
このプロジェクトに導入済みのバージョンを基準にしています。

## 利用方法

`Tools > Yamaneko > Amami Instruments > Manager` を開きます。

Bassの色メニューは `AquaBassColor`（Int）を使います。旧シーンのBass色メニューに残った `GuitarColor` 定義は、再コンパイル時およびビルド時に修正します。Bassの色FXは描画オブジェクトをRelative Rootで直接参照します。この修復ではオブジェクトの親子関係を変更しません。編集シーンの修復後は保存してください。

### プレハブをSettingsへ導入する手順

1. Hierarchyでアバターを選択して「アバターに初期セットアップ」を押します。アバター欄に直接指定することもできます。
2. Projectで楽器プレハブを選択し「Projectで選択したプレハブを使用」を押します。
3. Hierarchyで生成されたAmamiGuitarSettingsなどを選択し「Hierarchyで選択したSettingsを導入先にする」を押します。両欄ともドラッグ指定も可能です。
4. 「指定したSettingsへ楽器を導入」を押します。Settings配下への配置、楽器選択メニュー登録、未使用Instruments値の割当、表示切替FX設定を行います。
5. 位置を確認しシーンを保存します。同じプレハブを同じSettingsへ再導入しても重複配置しません。

登録済みプレハブは表示対象・カスタマイズ参照・表示名・分類を引き継ぎます。未登録プレハブは全体を表示対象として登録します。異なる表示対象が必要な場合は詳細操作で変更してください。

### 楽器がないアバターへの初期セットアップ

Managerの「初期セットアップ先アバター」にシーンのVRCAvatarDescriptorを指定し、「アバターに初期セットアップ」を押します。Hierarchyでアバター内を選択して実行することもできます。
共通ルート、共通メニューとその子、前後アンカー、Constraint付きAmamiInstruments格納先、空のAmamiGuitarSettings / AmamiAquaBassSettings / AmamiInstrumentsSettingsを生成します。楽器モデルや固有の色メニューは後から楽器プレハブとともに導入します。
HumanoidではChestに前後アンカーを配置します。Chestがない場合は共通ルート配下に配置します。アバターに合わせてアンカーの位置・回転を調整してください。登録済み楽器プレハブは格納先に追加できます。空のSettingsは配置済みモデルの設定用です。
再実行では既存の共通設定を再利用します。旧構成が残る場合は既存設定の移行を案内します。操作はUndo可能です。最後にシーンを保存してください。

### 配置済みプレハブを、指定した Instruments 値でインポート

1. Projectから楽器プレハブをアバターの楽器格納先へドラッグします。
2. Hierarchyでその楽器のSettingsルートを選び、Managerの **選択したヒエラルキーの楽器を読み込む** を押します。
3. **表示対象**を確認します。AquaBassの場合はSettingsの下のモデル本体を指定します。
4. **Instruments の値（0～255）**、表示名、分類、カスタマイズの子メニュー元を指定します。
5. **配置済み楽器をインポート / FX・メニュー更新** を押します。指定値が登録され、FXの `Hide → Show: Instruments == 値` と `Show → Hide: Instruments != 値` が生成されます。同じ楽器の再インポートは既存MA Merge Animatorを更新します。値の重複や範囲外は拒否します。
6. シーンを保存してPlayします。Radial Menuの「楽器の種類」から指定値で切り替えられます。

インポートしたFXは `Generated/Installed` に保存します。モデルへの参照はMA Merge AnimatorのRelative Rootを使い、ビルド時にも最新の階層から生成し直します。アバター本体のFXアセットを直接書き換える必要はありません。

編集時の標準は全楽器を非表示です。Managerの「編集時：すべての楽器を表示／非表示」で統一でき、以後のインポートにも選択を使います。Is Defaultは実行時の初期選択だけを決め、編集時の表示とは分離します。登録用InstrumentSelectionオブジェクトは編集時は非アクティブにし、ビルド時に有効化します（MA Menu Itemコンポーネントは有効のままにしてください）。色変更などのSubMenuに同じモデルをActive=trueにするMA Object Toggleがあると、MAのプレビューで常時表示されるため、その対象行だけをインポート時に取り除きます。色変更メニューや別の対象への制御は保持します。

この修正版を読み込んだとき、開いているアバター内の登録済みプレハブについて、同じSubMenuの重複制御を一度修復します。プレハブの登録参照から対象を解決するため、アバター名や共通ルート名には依存しません。修復したシーンは自動保存しません。Consoleに修復完了が出たらシーンを保存してください。修復はUndoできます。

選択用の登録 `InstrumentSelection` は楽器のSettings直下に置きます。`__AmamiInstrumentRegistration` は不要です。ビルド時にMAのメニュー収集より先に登録項目を楽器選択メニューへ移すため、カスタマイズメニューには混入しません。カスタマイズ項目のないSettingsはビルド時に省略します。

旧登録フォルダー内の登録項目は、開いているシーンの再コンパイル時・インポート時に一段上へ移動し、空になったフォルダーを削除します。シーンの変更はUndo可能で、自動保存しません。独自に追加した子オブジェクトやコンポーネントがあるフォルダーは保護します。配布用の旧プレハブには `Migrate Generated Templates` を実行します。「既存の楽器」という分類階層も省略し、その配下の項目・分類は保持します。

1. **共通ルート**と**楽器の格納先**をドラッグして指定します。候補取得は補助機能で、名前検索はしません。
2. 格納先には前・後の2ソースを持つ有効な Parent Constraint / VRC Parent Constraint を設定します。新規の場合は前後アンカーを指定すると Parent Constraint を作成します。既存の Constraint を使う場合はアンカー欄を空にします。ソース0が前、ソース1が後です。
3. **共通設定を作成 / 既存の共通制御を移行**を実行します。旧 Instruments / InstrumentsToggle / InstrumentsFront の共通FXとメニューだけを除去し、新しい共通メニューを追加します。AFK中はVRChat標準の `AFK` パラメーターで前側を優先し、解除後はメニューで選択した位置へ戻ります。楽器の種類と表示／非表示は維持します。
4. 各楽器の表示対象と表示名・分類を指定して**楽器を登録**します。既存の格納先直下の楽器には一括登録も使えます。
5. **カスタマイズの子メニュー元**には、材質などの MA Menu Item を子に持つオブジェクトを指定します。既存の MA Menu Item が Source Object Override を使用している場合は、その参照先を指定します。
6. **アバターの設定を検証**し、UnityのPlay ModeまたはNDMF Manual Bakeでメニューと表示を確認します。

旧ツールが作った2つの Generated プレハブには `Migrate Generated Templates` を用意しています。旧 `InstrumentsToggle` / `InstrumentsFront` 定義が残るシーンは、Play Mode・アップロード時にビルドクローン上で自動移行します。元シーンは変更しません。格納先のConstraintを一意に特定できない構成だけは Manager で明示的に設定してください。格納先以外のギミックを一括登録対象に含めないでください。

AquaBassはラッパーではなく、モデル本体を表示対象として登録します。旧シーンにモデルの `m_IsActive=false` 上書きが残っていても、楽器選択から直接表示できます。プレイ中に更新した場合は、一度停止してUnityのコンパイル完了後に再度Playしてください。旧 `SwitchInstruments` は、新しい「楽器の種類」メニューへ置き換わります。

登録済みの追加楽器は、プレハブを格納先の配下へドラッグするだけで導入できます。中間フォルダーがあっても構いません。削除した楽器は次回ビルド時にメニューから消えます。Installer を実行して一覧を再生成する必要はありません。

アバター名・共通ルート名・格納先名は自由です。格納先は通常 `AmamiInstruments`、楽器側は `<楽器名>Settings` などを使用してください。複数アバターはそれぞれ独立して処理します。同じアバター内の共通設定は1つです。

## 仕組み

共通コードは **Editor の NDMF plugin** です。独自 MonoBehaviour や実行時スクリプトを一切追加しません。設定は MA Menu Item / MA Object Toggle / MA Menu Installer の標準コンポーネントで保存されます。これらは通常のMAビルド処理で除去されます。

ビルドの Resolving フェーズで MA より前に、アバター内の登録情報を収集します。各表示対象は MA AvatarObjectReference で参照するため、アバター名や祖先の名前・階層をアニメーションに固定しません。各楽器のShow/Hide遷移と前後位置のコントローラーをビルドクローン上で生成し、MA Merge AnimatorのRelative Rootをそれぞれの表示対象・格納先に設定します。元シーン・元プレハブ・共有コントローラーにはビルド処理で書き込みません。

楽器選択は全楽器で1つのInt `Instruments`を共有します。ツールで指定した値を優先し、旧v1登録の楽器だけは未使用値を自動割当します。選択解除すると0になり、0を割り当てた楽器を表示します（0が未割当なら非表示）。表示をOFFにすると格納先全体が非表示になり、選択値は維持されます。分類は `Guitar/Electric`、`Guitar/Acoustic`、`Guitar/Bass` など `/` 区切り。8項目を超えるメニューのページ分割はMAが行います。

| パラメーター | 型 | 同期 | 保存 | 初期値 |
| --- | --- | --- | --- | --- |
| Instruments | Int | 有 | 有 | 初期楽器の値 |
| Yamaneko/AmamiInstruments/Visible | Bool | 有 | 有 | 1 |
| Yamaneko/AmamiInstruments/Front | Bool | 有 | 有 | 1 |

共通分は10 bit。楽器ごとのカスタマイズは別途加算され、アバター全体の制限はSDK/MAのビルド検証対象です。選択した楽器はSavedを有効にし、ワールド移動やアバター再読み込み時に復元します。GuitarColorとAquaBassColorも既存の保存設定を維持します。保存値がない場合やアバターのリセット時の初期楽器は、登録メニューの `Is Default` で指定します。楽器の追加・削除後も保存済みの選択が同じ楽器を指すよう、導入時に指定したInstrumentsの番号を維持してください。

保存の仕組みは [VRChatのSavedパラメーター](https://creators.vrchat.com/avatars/expression-menu-and-controls/) を使用します。設定変更をVRChatへ反映するにはアバターを再ビルド・アップロードしてください。

## 楽器を配布する側

- 表示対象を1つのルートにまとめ、その中にモデル・ギミック・固有の `<楽器名>Settings` を置きます。非表示対象を入れ子で二重登録しないでください。
- Managerで登録すると、対象の子に専用の MA Menu Item と MA Object Toggle を持つ登録用の葉が作られます。この葉にモデルや別のギミックを追加しないでください。
- 製品IDは初回登録時にGUIDを生成し、同じ製品の更新では維持します。別製品を複製して作る場合だけ新しいIDを割り当てます。同じIDの重複導入はビルドエラーになります。
- 固有アニメーションは楽器側の MA Merge Animator を Relative モードで使います。色・材質などのパラメーターは製品GUIDを含む名前にするか、適切なスコープの MA Parameters で Internal を指定します。共通の3パラメーターを楽器のFXから操作しないでください。
- Projectビューで登録済み楽器プレハブを選択し、Managerの出力ボタンで unitypackage を作成します。出力にはそのプレハブのAssets依存だけを含め、MA/NDMF/SDKやCoreを同梱しません。旧共通FXへの依存が残っていれば出力を拒否します。
- **Core（Global/Scripts）と共通プレハブは別配布・別更新**にします。各楽器にCoreを同梱してはいけません。古い楽器packageの再インポートが共通実装を巻き戻すことを防ぎます。Coreを先に導入することを商品説明に記載してください。

## 登録形式 v1

独自コンポーネントを避けるため、未使用のMAフィールドに以下の規約を持たせています。管理UIから編集するのが通常の手順です。

- 共通 SubMenu の Parameter: `Yamaneko/AmamiInstruments/v1/System`。
- 種類 SubMenu の Parameter: `Yamaneko/AmamiInstruments/v1/Catalog`。
- 自動値の楽器登録 Menu Item: `Yamaneko/AmamiInstruments/v1/Entry/<32桁GUID>|<URIエンコードした分類>`。
- 指定値の楽器登録 Menu Item: `Yamaneko/AmamiInstruments/v2/Entry/<32桁GUID>|<URIエンコードした分類>|<0～255>`。指定値はControl.valueにも表示します。ビルド時にParameterを`Instruments`へ変換します。
- 登録葉の MA Object Toggle: 表示対象1つ、Active=true、Invert=false。
- 登録葉の Source Object Override: カスタマイズの子メニュー元へのオブジェクト参照（省略可）。

ビルド時にこのメタデータを通常のメニューへ変換し、登録用パラメーターは最終アバターに残しません。形式変更時はCore側で旧形式を読み取る移行処理を追加し、製品データを一斉に差し替える設計にしないでください。

## 検証

`Tools > Yamaneko > Amami Instruments > Run Architecture Tests` は一時的なアバターを作って以下を検証します。結果はConsoleに `AMAMI_TESTS_PASSED` と出力されます。

- 名前変更・階層変更後の参照解決、ビルド元の非破壊性。
- 排他的選択用の一意な値、分類、初期値・保存設定。
- 製品ID重複・参照切れ・257楽器の拒否。
- NDMFとMAを通した最終メニューのページ分割、FXの生成・参照先、登録メタデータとMAコンポーネントの除去。

参考: [MA Menu Item](https://modular-avatar.nadena.dev/docs/reference/menu-item)、[MA Reactive Components](https://modular-avatar.nadena.dev/docs/reference/reaction)、[VRChatパラメーターの範囲と同期量](https://creators.vrchat.com/avatars/animator-parameters/)。

Bassの色メニューがモデル等の通常オブジェクト配下にあっても、ビルド時にその経路へMA Menu Groupを補い、「楽器別カスタマイズ」から辿れるようにします。この補完はビルド用コピーに限定し、元シーンのオブジェクト・親子関係・コンポーネントは変更しません。

複数の楽器が同じカスタマイズ元オブジェクトを参照する場合、「楽器別カスタマイズ」にはその元につき1項目だけ生成します。表示名だけで統合しないため、同名でも別のカスタマイズ元であれば保持します。


## 楽器単体で配布する構成

製品ルートの下に、モデル、固有カスタマイズ、InstrumentSelectionを収めます。アバター共通のAmamiGuitarSettingsを製品ルートに指定すると別楽器や共有設定を巻き込むため、個々のAmamiGuitarAqua・AmamiGuitarToxo等を指定してください。

1. Hierarchyで製品ルートを選び「選択したヒエラルキーの楽器を読み込む」を押します。表示対象の参照から、親に置かれた既存登録も検索します。
2. 「登録情報を楽器ルート内へ移動」で登録をルート直下に移します。製品ID、導入済みの値、カスタマイズ元は維持します。通常の再インポートでも外側の同じ対象の登録を再利用します。
3. 固有の色メニュー等を製品ルート内に収め、カスタマイズの子メニュー元をその内部のオブジェクトへ設定します。移動ボタンは共有メニューやモデルを自動移動しません。
4. 「配布用プレハブを作成（Instruments値を未指定にする）」で新しいプレハブを保存します。配布用コピーのみ値とIs Defaultを解除し、導入時に生成した選択FXを除去します。元シーンの値やFXは変更しません。
5. 作成したプレハブをProjectで選択し、unitypackage出力を実行します。製品IDは維持し、Core・MA・SDKは別途導入します。
6. 購入者はプレハブをアバターへ配置し、Managerで楽器を読み込み、Instrumentsの値を指定してインポートします。

配布前に製品外のオブジェクト参照を検査します。色メニューやPhysBone Collider等が外部を参照していれば、該当コンポーネント・プロパティ・参照先を表示して出力を止めます。アバター固有の共有コライダーは導入後に接続するか、製品専用のコライダーと追従設定を製品内に用意してください。外部参照を黙って削除しません。

旧Settings全体を切り替えていた登録は、プレハブのモデル参照、または描画モデルの枝が1つだけのSettingsから表示対象を解決します。中間のSettingsは有効に保ち、モデル本体のチェックがOFFでも実行時に選択できます。編集中シーンはUndo可能な状態変更のみ行い、自動保存しません。
