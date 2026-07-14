# Unity SDK 実装ロードマップ

## 現在のフェーズ

Unity 6000.3 における v0 契約 DTO の検証は完了した。次は固定環境の submit、
監視、成果物取得を内部 transport として切り出す前に、現在の
EnvForge・EmbodiedLab 間の通信経路を確認し、最小の実装境界を合意する。

次の実装 Issue はまだ作成していない。設計案の比較と合意が終わるまで、
transport や公開 facade の実装は開始しない。

## 合意済みの設計

- UPM package ID は `com.embodiedlab.unity` とする。
- 最初の対応環境は Unity 6000.3 系とする。
- 公開 API は状態を持つ小さな `EmbodiedLabJob` facade とし、非同期処理には
  Unity 6 の `Awaitable` を使う。
- EmbodiedLab の Pydantic モデルを正本とし、versioned JSON Schema を経由して
  C# DTO を生成する。
- 生成済み C# DTO は SDK リポジトリへコミットする。
- REST クライアント全体は生成せず、facade と transport は手書きする。
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

[EmbodiedLab.Unity #2](https://github.com/sayakaakioka/EmbodiedLab.Unity/pull/2) で
package manifest、Runtime assembly definition、作業ルール、基本文書を追加した。

### v0 契約 DTO の決定的生成

[EmbodiedLab.Unity #4](https://github.com/sayakaakioka/EmbodiedLab.Unity/pull/4) で
以下を完了した。

- EmbodiedLab `contracts/v0` の 6 schema と upstream provenance を同期した。
- 現在の schema 構文だけを正規化し、NJsonSchema で C# DTO を決定的に生成した。
- canonical fixture の .NET round trip、具象型、Replay Log の検証を追加した。
- CI で schema drift、再生成差分、コンパイル、テスト、lint、JSON を検証した。

### Unity 6000.3 における v0 契約検証

[EmbodiedLab.Unity #6](https://github.com/sayakaakioka/EmbodiedLab.Unity/pull/6) で
以下を完了した。

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

## このフェーズのスコープ

- 現在の EnvForge・EmbodiedLab 間の submit、状態監視、成果物取得経路の確認
- 固定環境だけを対象にした内部 transport の最小責務と境界の設計
- 2から3の設計案、長所・短所、推奨案を記録した次期 Issue の作成

HTTP、WebSocket、成果物ダウンロードの実装と、`EmbodiedLabJob` facade の追加は、
設計合意後の別 Issue とする。Unity Editor は CI や EmbodiedLab の実行基盤へ
追加しない。

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
- 認証、キャンセル、quota、billing、任意コード実行は現在の対象外である。

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
