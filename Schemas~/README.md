# Contract schema snapshot

The `v0` directory is an exact snapshot of the six JSON Schemas published by
EmbodiedLab. `upstream.json` records the source commit and the SHA-256 digest of
each file.

To update the snapshot from a local EmbodiedLab checkout, run:

```bash
python3 Tools~/contract_schemas.py sync \
  --source ../EmbodiedLab/contracts/v0 \
  --revision <40-character-commit-sha>
```

The sync command rejects missing or additional schemas and validates the
expected dialect and root titles. Generation also verifies every recorded
digest before reading a schema.
