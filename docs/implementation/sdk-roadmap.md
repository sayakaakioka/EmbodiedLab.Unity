# Unity SDK 実装ロードマップ

## 現在のフェーズ

キャンセル可能な v0 契約、WebSocket 優先の内部 transport、状態を持つ
`EmbodiedLabJob` facade、用途別の最小シナリオ／リプレイ API、EnvForge
の SDK 移行、固定環境の Quickstart sample、canonical world 表示、sample-local
job history まで完了した。次は、Quickstart に Replay playback と ONNX inference
を追加する段階である。

## 合意済みの設計

- UPM package ID は `com.embodiedlab.unity` とする。
- 最初の対応環境は Unity 6000.3 系とする。
- 公開 API は状態を持つ小さな `EmbodiedLabJob` facade とし、非同期処理には
  Unity 6 の `Awaitable` を使う。
- EmbodiedLab の Pydantic モデルを正本とし、versioned JSON Schema を経由して
  C# DTO を生成する。
- 生成済み C# DTO は SDK リポジトリへコミットする。
- REST クライアント全体は生成せず、facade と transport は手書きする。
- 状態監視は WebSocket を主経路とし、接続が健全な間は定期 HTTP polling を
  行わない。HTTP の Result Document 取得は、接続失敗、切断、長時間更新なし、
  または利用者による明示更新時の照合に限定する。
- submit、train、cancel、成果物取得は一回性の HTTP 操作として扱う。
- submission 作成時に一度だけ返される capability token を job handle が保持し、
  cloud cancel の Bearer token として使う。C# の `CancellationToken` はローカルの
  待機だけを中止し、cloud job の停止には `CancelAsync` を使う。
- EnvForge 固有の UI と再利用可能なジョブ履歴は EnvForge に残す。Quickstart
  sample 内には、restore と監視再開を説明する最小限のローカル履歴だけを置く。
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
- Unity 6000.3 の import と Test Runner はローカルの最小検証プロジェクトで
  実行する。CI では Unity Editor を起動せず、schema、生成、.NET fixture、lint、
  JSON、再生成差分を検証する。

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

- EmbodiedLab `contracts/v0` の 6 schema と upstream provenance を同期した。
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
- 固定環境の job lifecycle を一画面で確認できる importable Quickstart sample
- exact scenario を可視化し、再起動後に job を復元できる sample-local history
- facade の Unity Editor test と .NET compatibility / behavior test

再利用可能なローカル履歴と Editor UI は EnvForge に残す。Unity Editor は CI や
EmbodiedLab の実行基盤へ追加しない。

## 次の段階

1. Quickstart に Replay playback と ONNX inference を追加する。
2. 固定モードと 4 分割壁パーツ生成モードの選択を追加する。

各段階を一つの Issue と小さな PR に分け、テストと lint が通った状態で次へ進む。

## 保留事項

- リポジトリのライセンスは未選定であり、最初のリリース前に決定する必要がある。
- Unity の対応確認はローカルの Unity 6000.3 Test Runner で行い、PR に正確な
  Editor version、コマンド、結果を記録する。
- non-loopback endpoint の HTTPS／WSS 強制は
  [EmbodiedLab.Unity #23](https://github.com/sayakaakioka/EmbodiedLab.Unity/issues/23)
  で公開 endpoint invariant として扱う。
- import 済み Quickstart の実 Unity compiler 検証と canonical world の
  repeatable behavior test は
  [EmbodiedLab.Unity #24](https://github.com/sayakaakioka/EmbodiedLab.Unity/issues/24)
  で local Unity runner を拡張して扱う。
- 一般的な利用者認証、quota、billing、任意コード実行は現在の対象外である。

## 現行契約の検証

```bash
python3 -m unittest discover -s Tools~/tests -p 'test_*.py'
ruff check Tools~/contract_schemas.py Tools~/run_unity_tests.py Tools~/tests
ruff format --check Tools~/contract_schemas.py Tools~/run_unity_tests.py Tools~/tests
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
python3 Tools~/run_unity_tests.py --unity-editor <path-to-unity-6000.3.11f1>
git diff --check
```
