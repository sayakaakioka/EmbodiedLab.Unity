# EmbodiedLab.Unity 人間向けレビューガイド

## 所要時間

内容把握を主目的にした標準レビューは 90 分を見込む。

- 60 分: 公開 API、ジョブ lifecycle、成果物取得までの主導線
- 90 分: Replay、チュートリアル、テスト境界を含む標準レビュー
- 120 分: 実 Unity Editor / Standalone の動作確認まで含む完全レビュー

EnvForge との移行差分は第二段階で扱い、このレビュー時間には含めない。

## 1. Repository の責務を確認する（10分）

最初に次を読む。

1. `README.md`
2. `package.json`
3. `docs/vision/product-direction.md`
4. `docs/implementation/sdk-roadmap.md`

確認する点は、SDK が cloud infrastructure や scene UI を公開 API に漏らさず、
Scenario / Result / Replay の通信、ジョブ lifecycle、artifact download を
担当していることである。

## 2. Wire contract を確認する（15分）

次の順で読む。

1. `Schemas~/README.md`
2. `Schemas~/upstream.json`
3. `Runtime/Contracts/EmbodiedLabContracts.g.cs`
4. `Tests~/Fixtures/navigation_default_scenario_bundle.json`
5. `Tests~/Fixtures/navigation_completed_result_document.json`

重点確認点は次のとおり。

- EmbodiedLab の Pydantic model と JSON Schema が正本である。
- v0 snapshot は6 schemaで、各 byte digest と upstream commit を記録する。
- reward component は定義済み7要素をすべて明示する。
- Result Bundle の `observation_layout` は `obs_0` / `obs_1` である。
- 通常 ONNX は2 input、Sentis ONNX は固定長1 input で、どちらも
  `inputs` 配列と output metadata を持つ。

## 3. 公開 API とジョブ lifecycle を確認する（15分）

次を読む。

1. `Runtime/EmbodiedLabJob.cs`
2. `Runtime/EmbodiedLabEndpoints.cs`
3. `Runtime/EmbodiedLabReplay.cs`

`SubmitAsync`、`Restore`、`RefreshAsync`、`WaitForCompletionAsync`、
`CancelAsync`、model/replay download の順に追う。ローカル
`CancellationToken` と cloud cancel capability が別物であること、
terminal state を古い更新で巻き戻さないことを確認する。

## 4. Transport と失敗時の復旧を確認する（15分）

次を読む。

1. `Runtime/Transport/EmbodiedLabTransport.cs`
2. `Runtime/Transport/ResultMonitorTiming.cs`
3. `Tools~/TransportTests/Program.cs`

確認する点は次のとおり。

- remote endpoint は HTTPS / WSS、loopback だけ HTTP / WS を許可する。
- submission は idempotency key と cancel capability を request 前に生成する。
- WebSocket を主経路とし、失敗、切断、無通信時だけ HTTP へ再同期する。
- submission POST は Scenario と client-generated recovery values を送り、受付後の
  training dispatch は server が所有する。cancel POST は body を送らない。
- download は operation 固有の `.part` を使い、size と SHA-256 の検証失敗時にも
  既存 destination を保持する。

## 5. Replay と resource limit を確認する（10分）

次を読む。

1. `Runtime/EmbodiedLabReplay.cs`
2. `Runtime/ContractSemanticValidator.cs`
3. `Runtime/ResourceLimitedReadStream.cs`
4. `Tools~/ContractTests/Program.cs`
5. `Tools~/TransportTests/Program.cs`

manifest、chunk path、compressed/decompressed byte、1行、step 数の上限が
固定 invariant であることを確認する。Replay 行の `scenario_id` と `job_id` が
選択中の job と一致しない場合に拒否する経路も確認する。
各 episode の step 0 は action 適用前の reset state で、action と reward はゼロ、event は
空である。最初の action 適用後の状態は step 1 になることも確認する。

Result JSON は transport、Replay manifest／row は `EmbodiedLabReplay` が構造に加えて
状態間 invariant を検証する。`ScenarioBundleJson` は構造的 deserialize を担当し、
Scenario 全体の validation は submission 受付時の EmbodiedLab server が担当する。

## 6. チュートリアルで利用者の体験を確認する（15分）

次を読む。

1. `Samples~/Quickstart/README.md`
2. `Samples~/Quickstart/QuickstartCloudJob.cs`
3. `Samples~/Quickstart/QuickstartArtifacts.cs`
4. `Samples~/Quickstart/QuickstartController.cs`
5. `Samples~/Quickstart/QuickstartController.View.cs`
6. `Samples~/Quickstart/QuickstartWorldBuilder.cs`
7. `Samples~/Quickstart/QuickstartOnnxPolicy.cs`

README の6段階に沿って、canonical Scenario、submit、監視、cancel、artifact download、
Replay、Windows x64 ONNX inference を順に確認する。restore は補足 API として扱い、
sample-local history、credential store、Advanced UI が残っていないことも見る。

## 7. テスト境界と非対象を確認する（10分）

`README.md` の検証コマンドと `.github/workflows/contracts.yml` を確認する。
CI は schema provenance、codegen drift、.NET transport/Quickstart test を扱う。
Unity Editor と Standalone の検証は local runner で行う。

現時点の非対象は、Sentis 実行 API、一般認証、quota/billing、任意コード実行、
EnvForge 固有 UI、generated environment mode である。

## レビュー結果の残し方

指摘は次の3分類にする。

- 契約不整合: backend producer、schema、DTO、fixture の意味が異なる。
- SDK 責務逸脱: EnvForge 固有または cloud provider 固有の処理が公開 API に漏れる。
- 理解上の障害: 命名、文書、サンプルのために主導線を追えない。

好みだけの抽象化変更は分けて記録し、v0 の correctness 修正と混在させない。
