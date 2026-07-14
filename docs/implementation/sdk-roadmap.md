# Unity SDK 実装ロードマップ

## 現在のフェーズ

EmbodiedLab のキャンセル可能なジョブライフサイクルと更新済み v0 契約を
Unity SDK へ同期する段階にある。契約同期後は、固定環境の submit、監視、
キャンセル、成果物取得を、合意済みの WebSocket 優先方針で内部 transport
として切り出す。

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
- EnvForge 固有の UI とジョブ履歴は EnvForge に残す。
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

EmbodiedLab.Unity Issue #8 では、この変更後の7 schemaを同期し、生成 DTO と
契約テストへ反映する。

## このフェーズのスコープ

- EmbodiedLab revision `2f2a80bd0502e351c23d99e6f06a3c42a152c81b` の
  7 schema と provenance の同期
- submission の `cancel_token`、独立した training response、`cancelling`、
  `cancelled` を生成 DTO と契約テストへ反映
- 次の transport 段階に必要な wire contract の確定

HTTP、WebSocket、成果物ダウンロードの実装と、`EmbodiedLabJob` facade の追加は
後続 Issue とする。Unity Editor は CI や EmbodiedLab の実行基盤へ追加しない。

## 次の段階

1. 固定環境の submit、監視、成果物取得を内部 transport として切り出す。
2. 合意済みの `EmbodiedLabJob` facade を最小の公開 API として追加する。
3. EnvForge を SDK 利用へ移行し、既存動作を回帰テストで確認する。
4. 固定モードと 4 分割壁パーツ生成モードの選択を追加する。

各段階を一つの Issue と小さな PR に分け、テストと lint が通った状態で次へ進む。

## 保留事項

- リポジトリのライセンスは未選定であり、最初のリリース前に決定する必要がある。
- Unity の対応確認はローカルの Unity 6000.3 Test Runner で行い、PR に正確な
  Editor version、コマンド、結果を記録する。
- 一般的な利用者認証、quota、billing、任意コード実行は現在の対象外である。

## 現行契約の検証

```bash
python3 -m unittest discover -s Tools~/tests -p 'test_*.py'
ruff check Tools~/contract_schemas.py Tools~/run_unity_tests.py Tools~/tests
ruff format --check Tools~/contract_schemas.py Tools~/run_unity_tests.py Tools~/tests
dotnet format Tools~/ContractCodeGen/ContractCodeGen.csproj --verify-no-changes
dotnet format Tools~/ContractTests/ContractTests.csproj --verify-no-changes
python3 Tools~/run_unity_tests.py --unity-editor <path-to-unity-6000.3.11f1>
git diff --check
```
