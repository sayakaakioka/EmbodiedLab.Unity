# EmbodiedLab Unity SDK のプロダクト方針

## 目的

EmbodiedLab のクラウド学習機能を EnvForge から切り離し、Unity
プロジェクトへ直接組み込める再利用可能な SDK として提供する。

利用者は EnvForge と同等のフロントエンドを作る場合も、独自の Unity
コードからジョブを投入する場合も、同じ契約とジョブライフサイクルを利用できる。

## リポジトリの責務

- `EmbodiedLab` はサーバーの動作、Pydantic モデル、versioned JSON Schema
  の正本を所有する。
- `EmbodiedLab.Unity` は JSON Schema から生成した C# DTO、Unity 向けの
  ジョブ API、通信と成果物ダウンロードを所有する。
- `EnvForge` は再利用可能な Editor UI、ローカルのジョブ履歴、credential 永続化、
  シーン作成フローを所有し、クラウド操作にはこの SDK を利用する。SDK sample は
  一つの in-memory job を小さな段階で扱うチュートリアルとし、restore は API の補足として
  説明する。汎用履歴や再起動後の workflow は実装しない。

CPU 版 ONNX Runtime 1.24.4 の managed assembly と Windows x64 native library は
`EmbodiedLab.Unity` package が一つだけ所有する。チュートリアルと EnvForge が同じ binary
dependency を使い、各 frontend に duplicate を残さない。推論 controller は sample
固有の内部実装とし、現段階では public SDK inference API や汎用 model abstraction を
追加しない。

責務をまたぐ同じ実装や互換ラッパーは残さない。

## 最初に提供する利用体験

最初の公開範囲は、固定した環境を使う学習ジョブに限定する。

1. Unity からジョブを投入する。
2. ジョブの状態と進捗を監視する。
3. Result Document、Replay Bundle、学習済みモデルを取得する。
4. Windows x64 のチュートリアルで同じ canonical world／robot を使い、Replay と排他的に
   `policy.onnx` をローカル推論する。

公開 API は、状態を保持する小さな `EmbodiedLabJob` facade と .NET の `Task` を中心にする。
Unity バージョンごとに非同期 API や互換 wrapper を分岐させない。データ契約は
EmbodiedLab が出力する JSON Schema
から生成し、C# DTO をリポジトリへコミットする。HTTP、WebSocket、ファイル取得の
詳細は内部実装とする。

## 後続の環境生成

固定マップのジョブライフサイクルを切り出した後、学習中の環境モードとして次を
選べるようにする。

- 学習中に同じマップを使う固定モード
- マップを 4 分割し、各領域へ壁パーツをエピソードごとに配置する生成モード

生成モードは具体的な現行要件として扱うが、最初の SDK 公開 API へ先回りして
抽象化しない。任意コードのクラウド実行は当面の対象外とする。

## 設計原則

- 現在必要な最小限の公開 API だけを設計する。
- 将来の可能性だけを理由に抽象化しない。
- 重複、不要な互換層、到達不能な旧コードを残さない。
- 既存動作をテストで固定してから段階的に切り出す。
- 各段階でテストと lint を実行する。
- 公開 API、互換性、契約の判断は、選択肢と長短を比較して合意してから実装する。

## 対応環境

最低対応環境は、利用者環境の下限である Unity 2022.3.19f1 とする。Unity 2022.3.19f1
と Unity 6000.3.11f1 の両方で同じ package、公開 API、チュートリアルを検証し、
バージョン固有の互換層は設けない。

ONNX inference の検証対象は Unity 2022.3.19f1 と Unity 6000.3.11f1 の Windows x64
Editor／Standalone に限定する。他 OS／CPU 用 native library、Sentis、model conversion、
model format fallback は、具体的な要件と検証環境が合意されるまで追加しない。
