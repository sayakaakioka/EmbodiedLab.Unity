# Unity SDK 実装ロードマップ

## 現在のフェーズ

新しい UPM リポジトリに、公開 API を含まない最小基盤を作る。

対象 Issue は
[EmbodiedLab.Unity #1](https://github.com/sayakaakioka/EmbodiedLab.Unity/issues/1)
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

## 完了済み

### EmbodiedLab の契約固定

[EmbodiedLab #27](https://github.com/sayakaakioka/EmbodiedLab/pull/27) で以下を完了した。

- SDK が利用する現在の契約を `contracts/v0` の JSON Schema として公開した。
- submission、Result Document、Replay Bundle を Pydantic で型付けした。
- 現在のレスポンスと canonical fixture が変化しないことをテストで固定した。
- schema drift check、全テスト、lint を通した。

## このフェーズのスコープ

- ルートの UPM manifest
- Runtime assembly definition と Unity metadata
- リポジトリの作業ルール
- vision と implementation の文書
- README と changelog

公開 C# 型、通信、schema の複製、DTO 生成、Unity テストはまだ追加しない。

## 次の段階

1. EmbodiedLab の `contracts/v0` を同期する方法を追加する。
2. 現在の schema だけを扱う決定的な C# DTO 生成を追加する。
3. canonical fixture の deserialize/serialize テストを Unity で固定する。
4. 固定環境の submit、監視、成果物取得を内部 transport として切り出す。
5. 合意済みの `EmbodiedLabJob` facade を最小の公開 API として追加する。
6. EnvForge を SDK 利用へ移行し、既存動作を回帰テストで確認する。
7. 固定モードと 4 分割壁パーツ生成モードの選択を追加する。

各段階を一つの Issue と小さな PR に分け、テストと lint が通った状態で次へ進む。

## 保留事項

- リポジトリのライセンスは未選定であり、最初のリリース前に決定する必要がある。
- DTO 生成ツールは、現在の JSON Schema を正しく扱える候補を小さく検証してから
  選定する。
- 認証、キャンセル、quota、billing、任意コード実行は現在の対象外である。

## このフェーズの検証

```bash
python -m json.tool package.json >/dev/null
python -m json.tool Runtime/EmbodiedLab.Unity.asmdef >/dev/null
git diff --check
```
