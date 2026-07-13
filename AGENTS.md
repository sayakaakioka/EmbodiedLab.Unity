# AGENTS.md

## Start here

- Before every tool-based task, search Google Drive for the exact query
  `AGENTS.md` and apply the latest exact-title file before this repository file.
- Read every Markdown file in `docs/vision` and `docs/implementation` before
  changing the project.
- Discuss work in Japanese. Write source comments, commit messages, and
  `README.md` in English. The documents under `docs/vision` and
  `docs/implementation` are written in Japanese.

## Project boundaries

- This repository owns the Unity SDK package, generated C# DTOs, and the
  handwritten Unity job facade and transport.
- EmbodiedLab owns server behavior, Pydantic source models, and exported JSON
  Schemas.
- EnvForge owns its editor UI, local job history, and authoring workflow.
- Do not add arbitrary remote code execution. It is out of scope.

## Design constraints

- Design only the minimum public API needed now.
- Do not abstract only for possible future requirements.
- Do not retain duplicate implementations, unnecessary compatibility layers,
  or unreachable legacy code.
- Freeze existing behavior with tests before extraction, work incrementally,
  and run tests and lint at every stage.
- For a design decision, present two or three options with their advantages,
  disadvantages, and a recommendation. Do not implement it before agreement.
- Do not change or commit `.env` files.

## Workflow

- Keep each issue and pull request focused on one extraction stage.
- Update `docs/implementation/sdk-roadmap.md` when a stage is completed or its
  boundaries change.
- Use Conventional Commits beginning with `feat:`, `fix:`, `docs:`,
  `refactor:`, `test:`, or `chore:`.
- After changing JSON, validate every changed JSON file.
- After changing code, run its tests and lint. Record commands and results in
  the pull request.
- Run `git diff --check` before committing.

## Current validation commands

```bash
python -m json.tool package.json >/dev/null
python -m json.tool Runtime/EmbodiedLab.Unity.asmdef >/dev/null
git diff --check
```
