# Unity SDK 実装ロードマップ

## 現在のフェーズ

EmbodiedLab が公開した v0 schema を同期し、現在の契約だけを対象に C# DTO を
決定的に生成する。

対象 Issue は
[EmbodiedLab.Unity #3](https://github.com/sayakaakioka/EmbodiedLab.Unity/issues/3)
である。

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
- C# の型名、property 名、enum member 名は Unity 利用者向けに PascalCase とし、
  JSON 上の名前と値は Newtonsoft.Json metadata で保持する。
- Unity では公式 package `com.unity.nuget.newtonsoft-json` 3.2.2 を使う。

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

## このフェーズのスコープ

- EmbodiedLab `contracts/v0` の 6 schema と upstream commit・hash の同期
- 現在の schema 構文だけを扱い、未対応構文で明示的に失敗する正規化
- NJsonSchema による C# DTO の生成と生成物のコミット
- Newtonsoft.Json 13.0.2 相当を使った canonical fixture の deserialize/serialize
- CI での再生成差分、コンパイル、テスト、lint

HTTP、WebSocket、成果物ダウンロード、`EmbodiedLabJob` facade は追加しない。

## 次の段階

1. Unity 6000.3 の Test Runner でも canonical fixture を固定する。
2. 固定環境の submit、監視、成果物取得を内部 transport として切り出す。
3. 合意済みの `EmbodiedLabJob` facade を最小の公開 API として追加する。
4. EnvForge を SDK 利用へ移行し、既存動作を回帰テストで確認する。
5. 固定モードと 4 分割壁パーツ生成モードの選択を追加する。

各段階を一つの Issue と小さな PR に分け、テストと lint が通った状態で次へ進む。

## 保留事項

- リポジトリのライセンスは未選定であり、最初のリリース前に決定する必要がある。
- Unity 6000.3 上の import と Test Runner は、このフェーズの .NET コンパイルとは
  別に確認する必要がある。
- 認証、キャンセル、quota、billing、任意コード実行は現在の対象外である。

## このフェーズの検証

```bash
python3 -m unittest discover -s Tools~/tests -p 'test_*.py'
ruff check Tools~/contract_schemas.py Tools~/tests
ruff format --check Tools~/contract_schemas.py Tools~/tests
dotnet format Tools~/ContractCodeGen/ContractCodeGen.csproj --verify-no-changes
dotnet format Tools~/ContractTests/ContractTests.csproj --verify-no-changes
git diff --check
```
