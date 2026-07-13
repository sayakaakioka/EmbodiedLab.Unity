# EmbodiedLab.Unity

Unity SDK for submitting and monitoring EmbodiedLab cloud training jobs and
downloading results, replays, and trained models.

> [!IMPORTANT]
> This package is in early development. It does not have a stable public API or
> a published release yet.

## Scope

This repository will provide the reusable Unity-side cloud job functionality
shared by EnvForge and custom Unity frontends. The first supported workflow is:

- submit a fixed-environment training job;
- monitor its lifecycle;
- download its result document, replay bundle, and trained model.

Editor UI, local job history, and EnvForge-specific scene authoring remain in
[EnvForge](https://github.com/sayakaakioka/EnvForge). Server behavior and the
source contract models remain in
[EmbodiedLab](https://github.com/sayakaakioka/EmbodiedLab).

## Requirements

- Unity 6000.3 or later
- Git 2.14 or later when installing from a Git URL

## Installation

Until versioned releases are available, add the repository from Unity Package
Manager using this Git URL:

```text
https://github.com/sayakaakioka/EmbodiedLab.Unity.git
```

The package identifier is `com.embodiedlab.unity`.

## Development

The implementation is intentionally incremental. EmbodiedLab publishes the
versioned JSON Schemas, this repository generates and commits matching C# DTOs,
and handwritten Unity APIs are added only for current use cases.

The contract generator requires Python 3 and the .NET 8 SDK. To regenerate the
DTOs from the committed schemas:

```bash
python3 Tools~/contract_schemas.py normalize --output /tmp/embodiedlab-contracts.schema.json
dotnet run --project Tools~/ContractCodeGen/ContractCodeGen.csproj --configuration Release -- /tmp/embodiedlab-contracts.schema.json Runtime/Contracts/EmbodiedLabContracts.g.cs
```

`Schemas~/upstream.json` records the exact EmbodiedLab commit and SHA-256 hash
of every synchronized schema. The CI workflow regenerates the DTOs, compiles
them, exercises the canonical JSON fixtures, and rejects drift.

See [the product direction](docs/vision/product-direction.md) and
[the implementation roadmap](docs/implementation/sdk-roadmap.md) for the
current boundaries and progress.

## License

A repository license has not been selected yet. Treat the current source as
pre-release material until licensing is resolved.
