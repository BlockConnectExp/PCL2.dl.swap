---
name: Support a new PCL build
about: Provide the hashes and tokens for a new official build so it can be patched
title: "[Build] PCL "
labels: build-support
---

## Target

- PCL version + VersionCode (shown in PCL's own log):
- Source of the build:
- SHA-256 of the executable:
- Module MVID:

## Please attach

1. `data/methods-<build>.tsv` generated with `tools/MethodTable`.
2. The `PageDownloadInstall.LoaderInit` and `ModSecret.UpdateStart` IL dumps.
3. `tools/VerifyOffsets` output against the new sample.

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
