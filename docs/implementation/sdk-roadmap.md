# Unity SDK 実装ロードマップ

## 現在のフェーズ

キャンセル可能な v0 契約、WebSocket 優先の内部 transport、状態を持つ
`EmbodiedLabJob` facade、固定環境の段階的チュートリアル、canonical world 表示、
bounded artifact/replay reader、Replay playback、
remote endpoint の HTTPS／WSS invariant、Windows x64 ONNX inference まで実装済みである。

現在は、人間が SDK 全体を初めてレビューできる状態にするため、EmbodiedLab の
producer、Pydantic model、JSON Schema、generated DTO、fixture、SDK resource limit を
同じ v0 contract へ厳密に揃えている。EnvForge の現行実装はこの contract を制約せず、
EnvForge の再移行は第二段階とする。

## 合意済みの設計

- UPM package ID は `com.embodiedlab.unity` とする。
- 最低対応環境は Unity 2022.3.19f1 とし、Unity 6000.3.11f1 でも同じ package を検証する。
- 公開 API は状態を持つ小さな `EmbodiedLabJob` facade とし、非同期処理には
  両 Unity 世代で共通の .NET `Task` を使う。バージョン別の互換 wrapper は追加しない。
- EmbodiedLab の Pydantic モデルを正本とし、versioned JSON Schema を経由して
  C# DTO を生成する。
- 生成済み C# DTO は SDK リポジトリへコミットする。
- REST クライアント全体は生成せず、facade と transport は手書きする。
- 状態監視は WebSocket を主経路とし、接続が健全な間は定期 HTTP polling を
  行わない。HTTP の Result Document 取得は、接続失敗、切断、長時間更新なし、
  または利用者による明示更新時の照合に限定する。
- submit、train、cancel、成果物取得は一回性の HTTP 操作として扱う。
- train と cancel の POST は API 契約どおり request body を送らない。
- submission 作成時に一度だけ返される capability token を job handle が保持し、
  cloud cancel の Bearer token として使う。C# の `CancellationToken` はローカルの
  待機だけを中止し、cloud job の停止には `CancelAsync` を使う。
- EnvForge 固有の UI、再利用可能なジョブ履歴、credential 永続化は EnvForge に残す。
  package sample は一つの in-memory job を扱う段階的チュートリアルとし、restore は
  capability token の意味を含む補足 API として説明する。
- DTO 生成には NJsonSchema 11.6.1 と Newtonsoft.Json を使う。
- Pydantic の draft 2020-12 schema は、現在使っている `$defs`、ローカル参照、
  文字列 `const`、`schema | null` 形式の nullable、および現在の2つの
  discriminated union だけをビルド時に正規化する。形が変わった場合は失敗させ、
  汎用的な schema dialect 変換器やランタイム互換層にはしない。
- discriminated union は `SensorSpec` と `RewardComponent` の抽象基底型として
  生成し、Newtonsoft.Json の discriminator metadata で現在の具象型へ復元する。
- 生成 DTO は serialize/deserialize の契約に限定する。wire name、文字列 enum、
  discriminator に必要な Newtonsoft.Json metadata は残すが、入力検証用の
  `DataAnnotations` は生成しない。
- upstream schema で `additionalProperties` が省略された object は、コード生成時に
  宣言済みフィールドだけを持つ DTO とする。Pydantic の標準動作と同様に未宣言
  フィールドを保持せず、明示的に許可された Result Bundle、Result Document、
  辞書フィールドだけは追加フィールドを保持する。
- C# の型名、property 名、enum member 名は Unity 利用者向けに PascalCase とし、
  JSON 上の名前と値は Newtonsoft.Json metadata で保持する。
- Unity では公式 package `com.unity.nuget.newtonsoft-json` 3.2.2 を使う。
- Unity 固有の契約テストは package の `Tests/Editor` に置き、Editor Test
  assembly から Runtime assembly を参照する。
- canonical fixture は `Tests~/Fixtures` を単一の正本として維持し、Unity
  Test Runner から package の解決済みパスを通して読み込む。
- Unity 2022.3.19f1 と Unity 6000.3.11f1 の import と Test Runner は、それぞれ独立した
  ローカル最小検証プロジェクトで実行する。CI では Unity Editor を起動せず、schema、生成、.NET fixture、lint、
  JSON、再生成差分を検証する。
- CPU 版 ONNX Runtime は package-owned dependency とし、`1.24.4` の同じ managed／
  Windows x64 native binary をチュートリアルと EnvForge で共有する。native importer は
  Windows x64 Editor／Standalone だけを有効にする。
- ONNX inference は sample 内部の composition に限定し、public SDK API、Sentis、
  model converter、runtime abstraction、model-format fallback は追加しない。

## 完了済み

### EmbodiedLab の契約固定

[EmbodiedLab #27](https://github.com/sayakaakioka/EmbodiedLab/pull/27) で以下を完了した。

- SDK が利用する現在の契約を `contracts/v0` の JSON Schema として公開した。
- submission、Result Document、Replay Bundle を Pydantic で型付けした。
- 現在のレスポンスと canonical fixture が変化しないことをテストで固定した。
- schema drift check、全テスト、lint を通した。

### UPM の最小基盤

[EmbodiedLab.Unity #2](https://github.com/sayakaakioka/EmbodiedLab.Unity/pull/2)
で package manifest、Runtime assembly definition、作業ルール、基本文書を追加した。

### v0 契約 DTO の決定的生成

[EmbodiedLab.Unity #4](https://github.com/sayakaakioka/EmbodiedLab.Unity/pull/4)
で以下を完了した。

- EmbodiedLab `contracts/v0` の当時の6 schema と upstream provenance を同期した。
- 現在の schema 構文だけを正規化し、NJsonSchema で C# DTO を決定的に生成した。
- canonical fixture の .NET round trip、具象型、Replay Log の検証を追加した。
- CI で schema drift、再生成差分、コンパイル、テスト、lint、JSON を検証した。

### Unity 6000.3 における v0 契約検証

[EmbodiedLab.Unity #6](https://github.com/sayakaakioka/EmbodiedLab.Unity/pull/6)
で以下を完了した。

- package の Editor Test assembly と最小のローカル検証プロジェクトを追加した。
- canonical fixture を Unity から読み、現在の具象型と Replay Log の2 stepを
  確認した。
- Unity 6000.3.11f1 の反復可能なローカル runner を追加し、古い結果による
  偽成功を防いだ。
- Unity Editor を CI に追加せず、Python、.NET、schema、lint、JSON、再生成差分の
  検証を維持した。

### Unity 契約検証のマージ後 hardening

マージ後レビューに基づき、以下を追加で固定した。

- 必須Unityテスト集合と、skip・inconclusiveを許容しない成功条件
- canonical schemaのdefaultだけを許容する入力JSON payload全体の保持
- CI上の Replay Log 2 step

### キャンセル可能なバックエンド契約

[EmbodiedLab #29](https://github.com/sayakaakioka/EmbodiedLab/pull/29) で以下を完了した。

- submission ごとの capability token と、完全な Cloud Run Execution 名の保存
- `POST /submissions/{submission_id}/cancel`
- `cancelling` と `cancelled` の Result status、Pub/Sub、WebSocket 通知
- WebSocket 障害時だけ使う、保存済み Execution に限定した HTTP 照合
- raw token を保存しない hash 検証と、`run.executions.cancel` だけの最小権限 IAM

### キャンセル可能な v0 契約の Unity 同期

[EmbodiedLab.Unity #9](https://github.com/sayakaakioka/EmbodiedLab.Unity/pull/9)
で以下を完了した。

- EmbodiedLab revision `2f2a80bd0502e351c23d99e6f06a3c42a152c81b` の
  7 schema と SHA-256 provenance の同期
- submission の `cancel_token`、独立した training response、`cancelling`、
  `cancelled` の生成 DTO への反映
- Python、.NET、CI による schema、response、生成差分の検証

### Result artifact の正規配置への同期

[EmbodiedLab #31](https://github.com/sayakaakioka/EmbodiedLab/pull/31) と
[EmbodiedLab.Unity #16](https://github.com/sayakaakioka/EmbodiedLab.Unity/issues/16)
で以下を固定した。

- artifact の正規配置を `result_bundle.artifacts` のみに限定
- 生成 DTO から旧 `ResultDocument.Artifacts` を削除
- canonical fixture、schema、provenance、.NET／Unity 契約テストを同期
- 旧 top-level `artifacts` の互換 property や fallback parser は追加しない

### WebSocket 優先の内部 transport

[EmbodiedLab.Unity #11](https://github.com/sayakaakioka/EmbodiedLab.Unity/pull/11)
で以下を完了した。

- `HttpClient` による submit、train、cancel、明示的 result refresh
- `ClientWebSocket` による snapshot と status event の監視
- 接続失敗、切断、無通信時だけの HTTP result 照合
- 上限付き指数 backoff とローカル `CancellationToken` による監視停止
- 一時ファイルを使った public GCS artifact の stream download
- fake HTTP / WebSocket による 8 つの transport 振る舞いテスト

### 状態を持つジョブ facade

[EmbodiedLab.Unity #13](https://github.com/sayakaakioka/EmbodiedLab.Unity/pull/13)
で以下を完了した。

- submit と train を一つの操作として開始する `EmbodiedLabJob.SubmitAsync`
- submission ID と capability token からの `Restore`
- WebSocket 優先の完了待機、明示更新、cloud cancel
- Unity main context 上の Result 更新 event
- Replay Bundle manifest と学習済み model の download
- .NET compatibility build、fake transport、Unity Editor test

### 用途別のシナリオ永続化／リプレイ API

[EmbodiedLab.Unity #14](https://github.com/sayakaakioka/EmbodiedLab.Unity/issues/14)
で以下を固定した。

- `ScenarioBundleJson` による契約型を保った保存と復元
- `EmbodiedLabReplay` による manifest、JSONL、JSONL.GZ の読み込み
- manifest を起点とした `EmbodiedLabJob.DownloadReplayChunkAsync` の遅延取得
- 汎用 JSON／artifact API や Replay Bundle の一括 download は追加しない
- canonical fixture、gzip、相対 chunk path の回帰テスト

### EnvForge の SDK 移行

[EnvForge #16](https://github.com/sayakaakioka/EnvForge/pull/16) で以下を完了した。

- EmbodiedLab.Unity を Git revision で固定
- EnvForge 内の重複する cloud transport、契約 DTO、artifact download を削除
- EnvForge 固有の Editor UI とローカル job history だけを残す境界テスト

### 固定環境の Unity Quickstart sample

[EmbodiedLab.Unity #18](https://github.com/sayakaakioka/EmbodiedLab.Unity/issues/18)
で以下を固定した。

- Package Manager から import できる `Samples~/Quickstart`
- 固定した canonical scenario による submit と WebSocket 状態監視
- cloud cancel と完了済み ONNX model の download
- package metadata、scene と asset GUID の構造テスト、および .NET compatibility
  build による sample と実 SDK API のコンパイル
- EnvForge 固有の map authoring、履歴、Replay UI、推論は追加しない

### Quickstart の canonical world と sample-local history

[EmbodiedLab.Unity #20](https://github.com/sayakaakioka/EmbodiedLab.Unity/issues/20)
で以下を固定した。

- submit する `NavigationScenario.json` と同じ `ScenarioBundle` から floor、全 wall、
  全 obstacle、robot start、goal、overview camera、light を生成
- `<Application.persistentDataPath>/EmbodiedLabQuickstart/job-history.json` への
  atomic な履歴保存と newest-first 表示
- submission ID、時刻、endpoint、exact scenario、最新 status/progress、active job
  の cancel capability、ローカル Replay／ONNX path の保存
- 履歴選択時の `EmbodiedLabJob.Restore`、明示 refresh、非終端 job の WebSocket
  監視再開、および終端時の cancel capability 消去
- 二段階確認による履歴 record だけの削除。cloud cancel、cloud delete、ローカル
  artifact 削除は行わない
- submit 成功直後に job handle を確保し、world 生成や履歴保存に失敗しても監視と
  cloud cancel を継続できる局所的なエラー分離。save failure は dirty として再試行し、
  submit response と Play Mode 終了が競合した場合も最小履歴を best-effort 保存する
- submit、restore、cancel、download の操作競合防止、read-only な cloud target
  表示、および cloud cancel の二段階確認
- submission ID をローカル artifact directory に使う際の traversal 防止
- 終端3状態での cancel capability 消去、保存失敗時の履歴整合性、hard-crash 後の
  valid temp file 回収、local path の behavior test
- Quickstart 専用の履歴 behavior test と、全 sample source の実 SDK API に対する
  .NET compatibility build
- Replay playback、ONNX inference、固定／生成 map の選択は追加しない

### 成果物 download と replay reader の resource limit

[EmbodiedLab.Unity #26](https://github.com/sayakaakioka/EmbodiedLab.Unity/issues/26)
で以下を固定した。

- JSON は 1 MiB、JSONL／JSONL.GZ は 64 MiB、ONNX／ZIP は 1 GiB までとし、
  `Content-Length` と実際の stream byte 数の両方で検証
- 拒否または中断時の `.part` 削除と既存 destination の保持
- replay manifest は 1 MiB、4,096 chunk、path 1,024文字、chunk ごとの宣言
  step 数 100,000 までに制限
- replay log は展開後 256 MiB、UTF-8 JSONL 1行 1 MiB、返却 step 数 100,000
  までに制限
- backend は `eval_episodes * max_episode_steps <= 100000` を検証し、deterministic
  evaluation を SDK が読める一つの chunk に収める
- backend は Replay JSONL を書く前に各行を検証し、manifest と同じ
  `scenario_id` / `job_id` を必ず付与する
- 公開 API や runtime 設定を増やさず、SDK 内部の固定 invariant として実装

### Quickstart の Replay playback

[EmbodiedLab.Unity #21](https://github.com/sayakaakioka/EmbodiedLab.Unity/issues/21)
で以下を固定した。

- 完了済み履歴の明示 refresh 後に Replay manifest を取得し、最新 checkpoint の
  deterministic evaluation chunk だけを選択して遅延 download
- canonical な `EmbodiedLabReplay.ReadManifest` と `ReadSteps` を使い、manifest と
  chunk のローカル path を sample-local history に保存
- canonical world と同じ robot へ replay の X/Z 座標と yaw を適用
- `time_seconds` に従う同一 episode 内補間、episode 境界の短い pause、および
  Stop 時の最初の step への reset
- history 選択、world 再構築、Play Mode 終了、別モード開始時の playback 停止
- chunk 選択、欠落、episode 境界、clock、Stop reset の純粋ロジックテスト

### import 済み Quickstart の Unity 検証

[EmbodiedLab.Unity #24](https://github.com/sayakaakioka/EmbodiedLab.Unity/issues/24)
で以下を固定した。

- committed `Samples~/Quickstart` をローカル検証 project の disposable な Assets
  配下へ毎回 staging し、実 Unity compiler で sample assembly を compile
- package Editor tests と imported-sample tests を一つの runner command で実行
- stale result と stale stage を削除し、成功時・失敗時の両方で staged source を cleanup
- canonical scenario から生成した floor、wall、obstacle、robot、goal、camera、light の
  object 数と contract-derived transform、および Dispose 後の hierarchy cleanup を検証
- Unity Editor は引き続き local runner だけで使い、CI へ licensed job を追加しない

### remote endpoint の暗号化 invariant

[EmbodiedLab.Unity #23](https://github.com/sayakaakioka/EmbodiedLab.Unity/issues/23)
で以下を固定した。

- non-loopback API endpoint は HTTPS、result stream は WSS を必須化
- parsed `localhost`、IPv4 loopback、IPv6 loopback に限り HTTP／WS を許可
- public `EmbodiedLabEndpoints` と internal transport construction の両方へ同じ検証を適用
- query／fragment 拒否、末尾 slash normalization、parameter name を含む例外を維持
- endpoint の自動書換えや新しい認証 API は追加しない

### Quickstart の package-owned ONNX inference

[EmbodiedLab.Unity #22](https://github.com/sayakaakioka/EmbodiedLab.Unity/issues/22)
で以下を固定した。

- EnvForge で検証済みの CPU ONNX Runtime `1.24.4` managed assembly と Windows x64
  native library を `Runtime/Plugins/ONNXRuntime` へ移し、SHA-256、upstream MIT
  license、third-party notices、Windows x64 限定 importer を記録
- download 済み `policy.onnx` を一つの cached `InferenceSession` で読み、`obs_0` の
  `3x84x112` RGB と `obs_1` の signed goal angle／distance、および2つ以上の float
  action を fail-closed で検証
- submitted `ForwardCameraSensor` の mount、pitch、FOV、clip、size を使い、traversable
  を緑、blocked／background を青、robot／goal を非表示にした semantic image を
  vertical flip、CHW、normalized RGB へ変換
- forward を `[0,1]`、turn を `[-1,1]` へ clamp し、contract violation を status に
  明示。0.1秒ごとに最大0.2m／15度を同じ canonical robot へ適用
- Replay と inference の相互排他、submitted start pose への deterministic reset、
  history 選択／world rebuild／Stop／destroy 時の native resource release
- malformed model／tensor、graphics 不足、native failure、wall collision、goal reach の
  明示停止
- Unity 6000.3.11f1 Windows x64 で package import、実 `policy.onnx` の Editor semantic
  inference、Replay→Inference、Standalone build／実行 smoke を検証
- public SDK inference API、Sentis、compatibility wrapper、別 sample／world は追加しない
- dependent migration は [EnvForge #18](https://github.com/sayakaakioka/EnvForge/issues/18)
  で追跡し、SDK revision 更新直後に EnvForge 内の duplicate ONNX Runtime binary を削除する

### job recovery と Quickstart 表示のレビュー強化

2026-07-20 のレビュー対応で以下を固定した。

- submission 作成後の training-start 失敗は、submission ID と cancel capability を保持する
  `EmbodiedLabTrainingStartException.Job` として返し、Quickstart は履歴保存、監視、cancel を継続
- submission 作成前に SDK が idempotency key と cancel capability を生成し、曖昧なHTTP失敗を
  同じ値で一度だけ再試行。APIが同じsubmissionとcapabilityを返した場合だけ処理を継続
- 履歴 load 失敗後は store を read-only にし、復旧可能な既存 record を後続の Upsert で上書きしない
- terminal state から別 state への巻き戻しと古い `updated_at` を拒否し、同じ terminal state の
  新しい artifact 情報は受け入れる
- result WebSocket の一メッセージを 1 MiB と一 silence interval に制限し、違反時は socket を abort
- model download は canonical `onnx_model` と `onnx` format の組合せだけを許可
- Standalone smoke は policy action 成功を必須化
- Quickstart の最新7件の activity を、背景 panel なしの左上 overlay として severity 色付きで表示
- Quickstart の既定表示を connect、train、result 利用の3段階に絞り、cloud cancel、
  local history、artifact path、Replay／inference の診断値は Advanced に残す
- Quickstart の文字と操作要素を QHD Game View で読みやすい大きさへ拡大し、全体を
  scroll 可能にする
- canonical `navigation_default` の `goal.radius` を robot radius と同じ 0.45 m にし、
  EmbodiedLab training、Unity inference、Unity の goal 表示で同じ到達範囲を使う

### Quickstart から段階的チュートリアルへの再構成

2026-08-03 に、人間が SDK の主導線を小さな単位で理解できるよう以下を変更した。

- Package Manager の表示名を `Tutorial` とし、README を scenario、接続、submit／監視、
  artifact、Replay、Windows x64 inference の6章へ再構成
- cloud job lifecycle、artifact download、表示、world、Replay、inference を実責務ごとの
  sample-internal file へ分離し、public SDK API は追加しない
- sample-local job history、atomic save／recovery／retry、history 選択／削除、Advanced panel、
  activity log overlay を削除
- restore は `EmbodiedLabJob.Restore` と capability token の意味を説明する補足章へ移し、
  credential 永続化をチュートリアルへ実装しない
- `queued` かつ `total_steps = 0` は `0/0` と表示せず、trainer 起動待ちと明示
- Replay、ONNX contract、observation／action、unsafe local path の純粋ロジックテストは維持し、
  sample 構造テストは private method の並びではなく、学習順序、責務境界、importable scene、
  実 SDK API に対する compile を検証
- tutorial sample は input API 非依存のまま維持し、Unity 2022.3／6.3 検証プロジェクトを
  Input System 1.17.0 のみへ切り替えて、非推奨の Input Manager 警告を解消

過去の Quickstart history／Advanced UI の完了記録は当時の経緯として上節に残すが、
現在の package sample には含めない。詳細は
[tutorial-sample.md](tutorial-sample.md) を参照する。

### tutorial を基準にした公開 API の再設計

2026-08-04 の人間レビューで、sample-internal helper を増やして見た目だけを短くする方針を
改めた。これは補助 class の単純な移動や改名ではなく、SDK の公開 API を先にきっちり設計し、
その API を使う最小の tutorial を組み直す作業とする。

- 現在の public API と約2,000行の `Quickstart*` helper の責務を棚卸しし、各責務を
  「既存 public API を直接使う」「汎用機能として抽出する」「tutorial 固有として残す」の
  いずれかに分類する。
- `QuickstartCloudJob` は facade として公開せず、tutorial から `EmbodiedLabJob` の submit、
  monitor、cancel を直接読める形にする。
- Scenario Bundle からの Unity world 構築、Replay timeline／playback、semantic camera と
  ONNX contract／session／action 適用、artifact の取得と検証は、frontend 非依存性を確認した
  うえで少数の型付き public API へ整理する。
- 現在の `Quickstart*` class を丸ごと public にしたり、sample 固有の UI state、保存 path、
  status text を SDK API に混ぜたりしない。任意 workflow、job history、credential persistence、
  Editor UI は引き続き frontend の責務とする。
- public API は namespace と名称、入力／出力型、ownership と dispose、async cancellation、
  Unity main thread、resource limit、invalid state、exception type を設計対象とし、XML documentation
  と利用例を用意する。pre-release のため、置き換えた sample helper の互換 wrapper は残さない。
- 抽出前に現在 behavior を test で固定し、抽出後は package-owned unit／Unity Editor test で
  API 自体を検証する。Unity 2022.3.19f1 と 6000.3.11f1、実 ONNX model の Editor／Standalone
  smoke test を維持する。
- API 設計と test が確定してから tutorial を書き換える。controller は整理済み public API の
  composition と画面表示に限定し、主要 workflow を150～250行程度で追えることを目安とする。
  短さそのものより、利用者が public API の責務と呼び出し順を誤解なく読めることを優先する。
- tutorial README は public API の役割表、最小 code、詳細 API document への導線を持ち、
  tutorial の内部 helper を SDK の主要利用面として説明しない。
- cloud job の受付は server-owned workflow とする。client-visible な `POST /submissions` が
  Scenario の検証／保存、queued Result Document の作成、training dispatch の開始までを受け付け、
  保存後の dispatch／training 失敗と結果不明状態の調停は server と reconciliation が
  Result Document に反映する。submission 保存自体に失敗した場合だけ、job handle を作らず
  client へ HTTP error を返す。
- client-visible な `POST /submissions/{submission_id}/train`、Unity transport の
  `StartTrainingAsync`、`EmbodiedLabTrainingStartException`、例外から disposable job を回収する
  sample 処理は削除する。SDK の `EmbodiedLabJob.SubmitAsync` は一つの受付操作として job を返し、
  以降の dispatch／training failure は通常の `ResultDocument` の terminal `failed` として扱う。
- `EmbodiedLabJob` は一つの result monitor を内部で所有し、複数の
  `WaitForCompletionAsync` caller が同じ terminal result を待てるようにする。各 caller の
  `CancellationToken` はその caller の local wait だけを止め、共有 monitor や cloud job は
  止めない。共有 monitor は terminal result、明示的な local stop、または `Dispose` まで維持する。
- `ResultUpdated` は job 作成時に capture した `SynchronizationContext` へ通知する契約とし、
  Unity main thread から `SubmitAsync`／`Restore` した場合は main thread notification を保証する。
  context がない場合の実行 thread も API document に明記し、暗黙の保証を作らない。
- `Dispose` は local monitor、transport、native／managed resource の解放だけを行い、cloud job を
  cancel しない。cloud cancellation は `CancelAsync` だけが行うことを API 名、XML documentation、
  tutorial、test で固定する。
- wire deserialization 用の mutable `ResultDocument` を、そのまま job の public state として
  共有しない。`LatestResult`、`ResultUpdated`、completion result は caller が変更できない
  immutable snapshot を公開し、内部状態を consumer の mutation から隔離する。
- 既存 API を含むすべての public type／member に XML documentation を付け、ownership、state
  transition、thread、cancellation、dispose、cloud cancellation、exception、resource limit を
  記載する。public API test はこれら5点を behavior として検証し、sample helper に同じ lifecycle
  制御を重複実装しない。
- model と Replay は、一括 facade ではなく個別の型付き download API とする。model download は
  検証済み metadata と local path を持つ immutable result を返す。Replay download は typed な
  selection（固定 tutorial では latest deterministic evaluation）を受け取り、manifest と chunk の
  download、selection、identity／step count 検証、parse を SDK 内で完結し、検証済み manifest、
  selected chunk、read-only steps、local path を持つ immutable result を返す。
- Replay manifest の job／scenario identity、chunk の phase／policy mode／checkpoint／step count、
  全 step の job／scenario identity は sample 固有の検証にせず、public Replay API の契約とする。
  remote chunk path と resource limit の既存検証も維持し、sample は保存 root、UI state、再生開始の
  判断だけを所有する。
- すべての downloadable artifact metadata に `size_bytes` と `sha256` を必須追加する。model と
  Replay manifest は Result Bundle の artifact location に、Replay chunk は manifest の各 chunk
  entry に記録する。`size_bytes` は保存／転送される object の正確な byte 数、`sha256` は同じ
  byte 列の lowercase hexadecimal SHA-256 digest とする。圧縮 Replay chunk では展開後ではなく
  download される圧縮 object を対象にする。
- trainer／artifact uploader は upload 前に size と digest を算出して metadata と同じ object を
  保存する。Unity SDK は `.part` への streaming download 中に byte 数と SHA-256 を検証し、両方が
  一致した場合だけ既存の atomic replace を行う。不一致、早すぎる EOF、超過、cancel、network
  failure では一時 file を削除して明確な integrity error とし、既存 file を壊さない。
- metadata の declared size は既存 resource limit を緩める根拠にしない。contract 上の size／digest、
  HTTP content length、streamed byte count、format ごとの maximum の不一致を test し、EmbodiedLab、
  EmbodiedLab.Unity、EnvForge の fixture と result compatibility を同時更新する。
- tutorial は `Application.persistentDataPath` 配下の保存 root だけを選び、remote relative path、filename、
  root 外脱出、`.part`、atomic replace、resource limit、size／digest の検証は SDK の typed artifact／
  Replay download API が所有する。download result は検証済み final local path を immutable value として
  返し、現在の `QuickstartLocalPaths` は削除する。
- progress 文言、button enable 状態、confirmation、activity／error 表示、`Debug.LogException` は
  tutorial presentation の責務とし、public SDK API へ含めない。typed exception と機械判定可能な
  state は SDK が返し、小さな `QuickstartProgressText` は view へ統合する。
- Replay playback は二層の公開 API とする。Unity 非依存の timeline は検証済み immutable Replay、
  明示的な playback options、clock state を所有し、immutable frame を返す。Unity player は timeline
  の frame を対象 `Transform` へ適用する薄い層とし、UI status、button state、保存 path を持たない。
- timeline の操作は `Play`、現在位置を維持する `Pause`、先頭へ戻す `Reset` を分離し、現在の
  `Stop` が pause と rewind を兼ねる曖昧さを除く。時間は caller が `Advance(deltaSeconds)` へ
  渡し、scaled／unscaled Unity time の選択を SDK 内へ隠さない。
- episode 間 pause、playback speed、補間方針などの表示上の値は typed playback options とし、
  固定 tutorial では `episode_pause_seconds: 0.5` と `playback_speed: 1.0` を C# で明示する。
  これらは Scenario／Replay の cloud data contract ではないため Scenario JSON には追加しない。
- frame は補間済み X／Z／yaw、episode／step identity、再生状態を read-only value として公開し、
  mutable `ReplayLogStep` への参照を外へ出さない。Unity player は contract の X／Z／yaw を反映して
  対象 Transform の Y を維持することを XML documentation と test で固定する。
- 連続 step の補間、不連続 step の hold／snap、shortest-path yaw、episode boundary、pause、reset、
  大きな delta、invalid numeric value の既存 behavior test を package-owned timeline test へ移す。
- local ONNX inference は、型付き `PolicyContract` と disposable な `PolicySession` を公開 API とする。
  contract は Scenario Bundle の observation／action 宣言と download 済み model metadata から構成し、
  実 ONNX session の input／output name、dtype、layout、shape、action mapping を完全照合する。
- `obs_0`／`obs_1`、112 x 84、RGB channel 数、numeric value 数、最初に見つかった2値以上の float
  output といった sample 内の判定を削除する。output name／layout／mapping は Result Bundle の
  型付き metadata を正本とし、現在の文字列 dictionary の `action_mapping` は検証可能な contract
  type へ置き換える。
- `PolicySession` は integrity 検証済み model path、`PolicyContract`、typed session options を受け、
  native ONNX session と tensor lifetime を所有する。thread 数、memory arena、memory pattern、graph
  optimization は端末固有の local execution options とし、Scenario JSON へ混ぜず tutorial C# で
  現在値を明示する。
- package は inference 対応 platform／architecture を native library load 前に判定できる public
  support API を提供する。現在の bundled ONNX Runtime 対応外では専用 unsupported error を返し、
  `DllNotFoundException` などの偶発的な native load failure を capability 判定として使わない。
- contract mismatch、unsupported platform、model load、run、non-finite output、dispose 後の利用を
  package-owned test で検証し、tutorial 固有の status text や `Debug.LogException` は session API に
  含めない。
- `PolicySession.Run` は model metadata の action mapping と range を検証し、有限かつ契約範囲内の
  値だけを immutable な型付き action として返す。現在の exported ONNX は forward を `[0, 1]`、
  turn を `[-1, 1]` へ変換済みであるため、範囲外を Unity 側で clamp して推論を継続しない。
  non-finite または範囲外の output は contract mismatch として明示的に停止させ、sample 固有の
  `QuickstartRawAction`／`QuickstartAppliedAction` と重複した clamp／warning 表示は削除する。
- navigation action の適用は、Scenario Bundle、現在の X／Z／yaw pose、型付き action を受け取る
  Unity 非依存の deterministic `NavigationStepper` として公開する。結果は次の immutable pose、
  collision identity、goal 到達状態を持ち、Unity frontend はその結果を対象 `Transform` へ反映する。
- `NavigationStepper` は EmbodiedLab runtime と同じ world bounds、robot radius、回転付き 2D box、
  segment collision、goal radius、旋回後の前進順序を実装する。Python と C# に同じ canonical
  Scenario／pose／action fixture を与え、step ごとの pose、collision、goal 判定を完全照合する。
  Unity `Physics.CapsuleCast` を同じ契約の代替実装として残さない。
- `forward_step_meters` と `turn_degrees_per_step` は Scenario JSON の action contract から取得する。
  `decision_interval_seconds` は学習結果を決める空間的な action contract ではなく local loop の実時間
  scheduling option とし、tutorial C# で現在値 `0.1` を明示する。
- SDK は semantic observation、numeric observation、`PolicySession`、`NavigationStepper`、typed
  result という小さな primitive を公開し、それらを隠す総合 `PolicyRunner` facade は追加しない。
  tutorial の `Update` は、明示した local interval に従って observation 作成、model 実行、navigation
  step、`Transform` 反映を順番に呼び、主要な推論 loop を20～30行程度で読める形にする。
- Replay と inference の排他、scaled／unscaled time、開始／停止 button、status text、停止時の pose
  reset は frontend の責務とする。SDK の session／provider／stepper は tutorial UI state や
  `Time.deltaTime` を所有しない。
- Scenario Bundle の契約解釈は、検証済み immutable `NavigationWorld` として public API へ整理する。
  world bounds、回転付き obstacle、robot 寸法と start pose、goal、camera／action contract を一度だけ
  解決し、`NavigationStepper` と semantic observation contract は同じ world definition を使う。
- Unity primitive、material、表示色、overview camera、light、GameObject hierarchy は契約の意味ではなく
  tutorial presentation であるため、公開 world builder へ含めない。現在の `QuickstartWorldBuilder` は
  丸ごと公開せず、検証済み `NavigationWorld`を表示して pose を `Transform` へ反映する小さな
  `TutorialWorldView` へ置き換える。EnvForge の scene authoring は引き続き EnvForge の責務とする。
- semantic image observation は、Scenario Bundle と `PolicyContract` から構成する型付き observation
  contract と、Unity の `Camera` から検証済み tensor を生成する小さな public provider に整理する。
  resolution、semantic mode、camera mount、pitch、FOV、clip、channel order、origin、layout、dtype、
  normalization を重複した sample 定数として持たない。
- policy input は mutable な `float[]` と固定 index を tutorial へ公開せず、semantic frame、
  `GoalAngleDegrees`、`GoalDistanceMeters` を名前付きで持つ immutable `NavigationObservation` とする。
  `NavigationObservation` は `NavigationWorld` と現在 pose から numeric 値を構成し、`PolicySession` が
  `PolicyContract` の input mapping に従って tensor 順序と shape へ encode する。tutorial は
  `obs_0`／`obs_1`、要素数、配列 index を扱わない。
- goal angle の正規化、四象限、±180度境界、goal 上のゼロ距離、距離単位、tensor encoding は
  EmbodiedLab と C# の canonical fixture で照合する。world、pose、camera を直接 `PolicySession` へ
  渡して observation 作成を session 内へ隠さない。
- semantic camera capture は Unity main thread で同期実行し、`PolicySession.Run` も caller thread で
  一つの navigation decision を同期的に完了させる。同じ session の並行 `Run` は許可せず、SDK 内に
  background worker、連続 runner、暗黙の `Task.Run` を追加しない。
- semantic provider は render texture、readback texture、変換用 buffer を再利用して所有する。
  capture が返す read-only frame は次の capture または provider の `Dispose` まで有効とし、tutorial は
  capture 後すぐに observation を構成して同期 `Run` へ渡す。main thread 負荷が実測上の問題になった
  場合だけ、buffer lifetime、pose snapshot、cancel、in-flight decision を含む非同期 API を別途設計する。
- Unity Camera provider の採用条件として、同じ Scenario と canonical pose から EmbodiedLab の解析的
  renderer と Unity が生成する semantic class map の cross-runtime conformance test を追加する。
  現在の Editor／Standalone smoke は model が実行できることの確認として維持するが、観測画素の
  一致を証明する test の代わりにはしない。
- conformance test が一致しない場合は、投影、pixel center、vertical origin、clip、color space、
  anti-aliasing、geometry、shader の設定を先に修正する。それでも契約どおりに一致させられない場合は、
  C# の解析的 renderer を唯一の provider として実装し、Unity Camera と二方式の fallback は残さない。
- semantic observation は package-owned shader を必須とし、URP／built-in の一般 shader へ暗黙に
  fallback しない。graphics device がない実行環境は native resource 作成前に専用 unsupported error
  とし、Unity Camera provider はローカル Unity 推論だけで使用する。EmbodiedLab の Cloud Run 学習や
  CI に Unity Editor／graphics runtime を追加しない。

### Unity 2022.3.19f1 対応

2026-08-03 に、利用者環境の下限である Unity 2022.3.19f1 を package の最小対応版とした。

- Unity 2022 に存在しない `Awaitable` を公開 API から除き、.NET／Unity の標準
  `Task`／`Task<T>` へ置き換えた。旧 API の shim や互換 wrapper は残していない。
- Unity 2022.3.19f1 と 6000.3.11f1 の小さな検証 project を別々に保持し、同じ
  `run_unity_tests.py`／`run_unity_standalone_smoke.py` を `--unity-version` で切り替える。
- 両 project で Input System 1.17.0 のみを有効にし、SDK package 自体は Input System
  非依存のままとした。
- 両 Editor で package／import 済み tutorial test はそれぞれ16件全件成功、実
  `policy.onnx` を使った2件を含めて skip は0件だった。
- 両 Editor の Windows x64 standalone で ONNX Runtime 1.24.4 の model load、画像 observation、
  inference、action 適用、正常終了を確認した。
- .NET は contract runner、Quickstart 18件、transport 27件、codegen／compatibility build を
  成功させ、5 project の `dotnet format` を通した。Python は31件中30件成功、Windows では
  不要な WSL path test 1件のみ skip とし、今回変更した4ファイルの Ruff check／format を
  通した。
- Editor log には Input Manager の非推奨警告、Audio Listener の package 警告、C# compile
  error のいずれもなかった。

## 完了した SDK スコープ

- API と WebSocket の base URL だけを持つ `EmbodiedLabEndpoints`
- submit と train を一つの操作として開始する `EmbodiedLabJob.SubmitAsync`
- submission ID と任意の capability token からの `Restore`
- WebSocket 優先の `WaitForCompletionAsync` と明示的な `RefreshAsync`
- cloud job を停止する `CancelAsync`
- Result Document の最新状態と Unity main context 上の更新 event
- Replay Bundle manifest と学習済み ONNX model の download
- シナリオの保存／復元、Replay manifest／step の読み込み
- 選択した Replay chunk の遅延 download
- 固定環境の job lifecycle を6段階で確認できる importable tutorial
- exact scenario を可視化する一つの in-memory job session
- 最新 deterministic evaluation chunk を同じ robot で再生する sample-local replay
- download 済み ONNX model を同じ world／robot で実行する Windows x64 sample-local inference
- facade の Unity Editor test と .NET compatibility / behavior test

再利用可能なローカル履歴、credential 永続化、Editor UI は EnvForge に残す。Unity Editor
は CI や EmbodiedLab の実行基盤へ追加しない。

## 次の段階

1. [human-review-guide.md](human-review-guide.md) に沿って SDK の責務と主導線を
   人間が確認する。
2. human review の決定に沿って contract と公開 API を整理し、その API で tutorial を
   書き直す。
3. package version、tag、release 手順を決める。
4. 第二段階として EnvForge を確定した SDK contract と公開 API へ追従させる。
5. その後、固定 mode と宣言的 generated mode の選択を設計する。

各段階を一つの Issue と小さな PR に分け、テストと lint が通った状態で次へ進む。

### `envforge_min_version` の削除方針

2026-08-04 の人間レビューで、`envforge_min_version` は現在の責務分担に不要と判断した。
human review 完了後、EmbodiedLab、EmbodiedLab.Unity、EnvForge を一つの契約変更として
同時に更新する。

- pre-release の v0 contract から直接削除し、旧 field の互換 layer や新しい v1 contract は
  作らない。
- `coordinate_system` の `envforge_xz_meters` も product-neutral で座標軸、向き、単位を
  明示する値へ改名し、旧 enum value は残さない。正確な名称は3 repository の実装時に
  contract 全体と照合して決める。
- `action_space` に `forward_step_meters: 0.2` と `turn_degrees_per_step: 15.0` を追加し、
  EmbodiedLab training runtime と Unity local inference が同じ Scenario Bundle の値を使う。
  両 runtime の重複した直書き定数は削除し、値を変えた scenario では再学習を必須とする。
- camera resolution と semantic mode は Scenario Bundle を正本とする。EmbodiedLab の
  observation／policy network と Unity の render texture／readback／ONNX tensor 検証は
  `ForwardCameraSensor` の値から構成し、112 x 84 や mode を重複した定数として持たない。
  download した ONNX metadata が scenario の input shape と一致しない場合は明確に失敗させる。
  現在未対応の semantic mode は値を無視せず、mode 選択時に unsupported error とする。
- 固定 tutorial では policy input に使っていない `front_distance` を Scenario の sensor から
  削除する。汎用 `DistanceSensor` 型は将来の対応用に contract へ残すが、sensor 不在時に
  5 meter を補う training fallback と固定 Replay 診断出力は削除する。
- 実際の numeric policy input である goal angle と goal distance を Scenario／model contract に
  明示し、EmbodiedLab と Unity が同じ宣言から `obs_1` を構成する。`NumericValueCount = 2`、
  `observation_layout` の不正確な初期値、配列 index の重複直書きは契約由来へ置き換える。
- reward shaping は、すべてを `per_step` として表す現在の形をやめ、条件付き報酬ごとに
  意味の合う contract type と判定値を持たせる。現行挙動を保つため、`goal_progress` の
  `minimum_delta_meters: 0.005`、`wide_angle_penalty` の
  `minimum_absolute_angle_degrees: 90.0`、`rear_angle_penalty` の
  `minimum_absolute_angle_degrees: 150.0`、`inactive_penalty` の
  `maximum_absolute_forward: 0.001` を Scenario Bundle に明示する。field 名は3 repository の
  contract 更新時に型全体と照合して確定する。
- `movement_threshold` を報酬 component として表す現在の疑似的な `per_step` component は
  削除し、inactive 判定値として `inactive_penalty` へ統合する。EmbodiedLab runtime の
  0.005／90／150 などの判定定数、および default Scenario を重ねて補う処理も削除し、
  Scenario Bundle の値だけから判定する。既存の weight と条件分岐の優先順位は維持し、
  reward 設定を変更した scenario では再学習を必須とする。
- training は、学習結果または使用 resource に影響する値を Scenario Bundle の JSON から
  すべて受け取る。現在省略されている `n_envs: 1`、`cpu_count: null`、
  `torch_num_threads: null`、`n_epochs: 3` を固定 tutorial JSON に明記し、nullable resource 値の
  `null` は runtime による自動選択を意味するものとして contract に定義する。
- Stable-Baselines3 PPO に現在渡していない学習用の既定値も、型付きの training contract と
  固定 tutorial JSON に明記する。少なくとも advantage、clipping、value loss、gradient、
  state-dependent exploration、KL 制限に関する設定を対象にし、library version の既定値に
  学習挙動を依存させない。正確な field 一覧と現在値は、固定している Stable-Baselines3
  version の constructor と照合して3 repository の contract 更新時に確定する。
- `cpu_count`、`torch_num_threads`、`n_envs`、device などの resource 指定は、受理してログへ
  出すだけにせず、training job の実行環境へ反映する。利用可能な resource を超える指定や
  未対応の device は黙って補正せず、submission validation または job 開始時に明確に失敗させる。
- server 管理の artifact path、callback、任意の Python class、`verbose` など、学習内容や
  resource 要求ではない実装用の値は Scenario Bundle へ公開しない。任意 class 名や
  `policy_kwargs` をそのまま受け取る汎用実行 API にはせず、公開する policy 設定は検証可能な
  型付き field に限定する。実行時に解決した全 training 値、library version、resource 値は
  Result Bundle に記録し、再現可能にする。
- 新しい training field を追加するまでは、未定義 field が Pydantic に無視されて「指定したが
  反映されない」状態を避ける。training model も unknown field を forbid し、3 repository の
  contract と runtime が揃うまでは tutorial JSON に先行追加しない。
- EmbodiedLab の Pydantic model、JSON Schema、result compatibility、fixture、test、文書を
  source of truth として先に更新する。
- EmbodiedLab.Unity は同期した Schema から C# contract を再生成し、sample、fixture、test を
  更新する。
- 現在の EnvForge runtime はこの field を消費していないため、まず fixture と契約説明を
  更新する。SDK を使う実装への全面移行は、上記の第二段階として別途行う。
- 3 repository の開発と整合性検証は並行して行い、merge は EmbodiedLab、
  EmbodiedLab.Unity、EnvForge の依存順とする。

## 保留事項

- リポジトリのライセンスは未選定であり、最初のリリース前に決定する必要がある。
- Unity の対応確認はローカルの Unity 2022.3／6000.3 Test Runner で行い、PR に正確な
  Editor version、コマンド、結果を記録する。
- 一般的な利用者認証、quota、billing、任意コード実行は現在の対象外である。

## 現行契約の検証

```bash
python3 -m unittest discover -s Tools~/tests -p 'test_*.py'
ruff check Tools~/contract_schemas.py Tools~/run_unity_tests.py \
  Tools~/run_unity_standalone_smoke.py Tools~/tests
ruff format --check Tools~/contract_schemas.py Tools~/run_unity_tests.py \
  Tools~/run_unity_standalone_smoke.py Tools~/tests
dotnet format Tools~/ContractCodeGen/ContractCodeGen.csproj --verify-no-changes
dotnet format Tools~/ContractTests/ContractTests.csproj --verify-no-changes
dotnet build Tools~/TransportCompatibility/TransportCompatibility.csproj \
  --configuration Release
dotnet format Tools~/TransportCompatibility/TransportCompatibility.csproj --verify-no-changes
dotnet run --project Tools~/QuickstartTests/QuickstartTests.csproj \
  --configuration Release
dotnet format Tools~/QuickstartTests/QuickstartTests.csproj --verify-no-changes
dotnet run --project Tools~/TransportTests/TransportTests.csproj \
  --configuration Release
dotnet format Tools~/TransportTests/TransportTests.csproj --verify-no-changes
python3 Tools~/run_unity_tests.py --unity-version 2022.3 \
  --unity-editor <path-to-unity-2022.3.19f1>
python3 Tools~/run_unity_tests.py --unity-version 6000.3 \
  --unity-editor <path-to-unity-6000.3.11f1>
python3 Tools~/run_unity_tests.py --unity-version <2022.3-or-6000.3> \
  --unity-editor <path-to-matching-unity-editor> \
  --policy <path-to-policy.onnx> --with-graphics
python3 Tools~/run_unity_standalone_smoke.py \
  --unity-version <2022.3-or-6000.3> \
  --unity-editor <path-to-matching-unity-editor> \
  --policy <path-to-policy.onnx> --output-directory <temporary-output>
git diff --check
```
