# EmbodiedLab Unity チュートリアル再構成

## 目的

従来の Quickstart は、固定環境の表示、cloud job の投入と監視、local history、
Replay、ONNX inference、診断 UI を一つの controller と一つの画面へ集約していた。
機能の存在は確認できる一方、初めて読む利用者が SDK の主導線を小さな単位で
理解することが難しい。

この段階では、一画面に全機能を載せる sample を廃止し、一つずつ小さな責務を
積み上げるチュートリアルへ移行する。最後まで進めると固定環境の job lifecycle、
artifact、Replay、Windows x64 ONNX inference を一通り理解できる状態を目標とする。

## 合意した構成

- Package Manager から import する sample directory と Unity scene は一つに保つ。
- README を順番に進むチュートリアル本文とする。
- scene に割り当てる controller は composition root と画面表示に限定する。
- world、Replay、inference の実装は、それぞれ現在の実責務に対応する小さな内部型へ
  分ける。
- 各章の完成版を別 directory へ複製しない。重複する sample を複数保守しない。
- sample を先に短くするためだけに内部 helper を増やさない。まず SDK の公開 API を責務、
  型、命名、所有権、lifecycle、error、thread／cancellation 境界まで再設計し、world 構築、
  Replay 再生、local ONNX inference、artifact 取得のうち frontend に依存しない機能を
  小さな公開 API として抽出する。
- 現在の `Quickstart*` 型を名前だけ変えてそのまま公開しない。既存 behavior を test で固定し、
  汎用部分と tutorial 固有の表示／composition を分離した後に公開面を決める。
- tutorial の controller は、整理後の公開 API を直接組み合わせる composition root と画面表示に
  限定する。利用者が SDK の主要 API と処理順序をコードから追えることを、sample の
  acceptance criterion とする。
- 利用者が将来変更する world geometry の値は、Schema の既定値に隠さず固定 scenario 内へ
  明記する。外周壁の `height` は現在値 `2.0` meter、内側 obstacle は `1.0` meter を
  各要素に記述する。内側要素の ID は型と一致する `obstacle_*` とする。
- 観測へ影響する active sensor parameter も Schema の既定値に隠さない。forward camera の
  resolution、semantic mode、mount height、pitch、vertical FOV、near／far clip は固定
  scenario 内へ現在値を明記する。

## 学習順序

1. canonical な `ScenarioBundle` を読み、同じ固定環境を表示する。
2. API／Result WebSocket endpoint を設定し、`EmbodiedLabJob.SubmitAsync` で job を投入する。
3. `ResultUpdated` と `WaitForCompletionAsync` で `queued`、`running`、terminal state を監視する。
4. 完了した job から model と Replay manifest／chunk を明示的に download する。
5. deterministic evaluation Replay を同じ robot で再生する。
6. Windows x64 では download 済み `policy.onnx` を同じ world／robot で実行する。

cloud cancel は監視章の補足操作として残す。`Restore` は capability token の扱いを含む
補足章で説明するが、チュートリアル内に汎用 job history や永続 credential store は
実装しない。

## 残す機能

- canonical scenario と同じ world／robot／goal の表示
- submit、WebSocket 優先監視、cloud cancel
- model download
- deterministic evaluation Replay の選択、download、再生
- package-owned ONNX Runtime を使う Windows x64 inference
- Replay と inference の相互排他と deterministic reset
- artifact path traversal 防止、Replay identity／resource limit の検証
- 大きく読みやすい、scroll 可能な最小 UI

## 削除する機能

- sample-local job history とその一覧 UI
- history の atomic save、temp file 復旧、dirty retry
- history record の選択、削除、削除確認
- history からの自動 restore、監視再開、download 済み artifact の自動再読込
- Advanced panel と診断値一覧
- activity の重複 log overlay
- 機能ごとの複数 boolean による統合 operation guard

汎用の job history、credential 保存、再起動後の workflow は EnvForge の責務とする。
サンプル削減のために SDK の cancel capability、artifact validation、resource limit を
弱めない。

## 表示上の契約

- `queued` かつ `total_steps = 0` の間は `0/0` を表示せず、trainer の起動待ちと示す。
- `running` で総 step 数が分かった後だけ数値進捗を表示する。
- local monitoring の停止と cloud cancel は別操作であることを明記する。
- Replay と inference の現在の実行状態だけを表示し、内部診断値を常時列挙しない。
- 操作 panel は左、canonical world は右の独立した viewport に表示し、互いに重ねない。
  QHD では左 panel を読みやすい最大幅に保ち、world 側へより広い領域を割り当てる。
  画面幅が狭い場合も両領域を縮め、world を panel の背面へ戻さない。

## テスト境界

- SDK facade、transport、schema、Replay reader の既存 behavior を維持し、公開 API へ抽出する
  world、Replay playback、inference、artifact 処理にも package-owned test を追加する。
- 新しい公開 API は XML documentation、null／invalid state、resource limit、dispose、
  cancellation、Unity main thread 境界を検証し、sample の private helper test だけに依存しない。
- Quickstart behavior test では、履歴保存固有のテストを削除する。
- Replay timeline、path validation、ONNX input／output contract、observation／action math は
  純粋ロジックとして維持する。
- sample 構造テストは、ソース文字列や private method の並びではなく、importable scene、
  必須ファイル、README の学習順序、実 SDK API に対する compile を検証する。
- Unity 2022.3.19f1 と Unity 6000.3.11f1 で package test と import 済み sample test を
  実行する。
- tutorial sample 自体は Unity の input API に依存しない。Unity 2022.3／6.3 の検証プロジェクトは
  Input System 1.17.0 と `activeInputHandler: 1` を使用し、旧 Input Manager を有効にしない。
  SDK package の依存関係には Input System を追加しない。

## 対象外

- EnvForge の SDK revision 更新と履歴 UI 移行
- Sentis、model conversion、他 OS 向け ONNX Runtime
- 一般認証、quota、billing、任意 remote code execution
- 独立した複数 sample や旧 Quickstart の互換 copy
- 汎用 job history、credential store、Editor UI を SDK 公開 API に含めること
