# Versioning / 版本规范

> Summary of this document: which version scheme this repository follows and how a release is produced.

> [!IMPORTANT]
> The artifact name encodes both the injected PCL build and this tool's own version; a mismatch
> between the two is a release blocker.

---

## 1 Scheme

This repository follows the BlockConnect version scheme:

```
v{Year}.{Major}-Alpha {AlphaVer.}
```

| Component | Definition | Range | Example |
|---|---|---|---|
| `Year` | last two digits of the year | - | `26` |
| `Major` | major version | 0-∞ | `0` |
| `Alpha` | unfinished version marker | - | `Alpha` |
| `AlphaVer.` | serial number of unfinished versions | 1-9 | `1` |

Ten Alpha releases are planned per Major version. Alpha 10 is normally hidden from the display and
serves as the LTS release of that Major version.

In practice (2026-10-28):

- Alphas 1-9 are published as GitHub **Pre-Releases** (`v26.0-Alpha 1` … `v26.0-Alpha 9`).
- **Alpha 10 becomes the official release**: it is published once, as a normal (non-pre-release)
  release whose name and tag are simply `v26.0` — the `-Alpha10` spelling stays available on the
  pre-release line for traceability, but the public entry point is `v26.0`.
- Work that lands **after** the official release opens the next minor cycle and restarts the alpha
  counter: `v26.1-Alpha 1`, `v26.1-Alpha 2`, … (again ten of them, with Alpha 10 becoming `v26.1`).

> Do not publish `v26.0-Alpha 11` and friends: a Major version has exactly ten Alphas, and anything
> past Alpha 10 belongs to the next major version's alpha series.

## 2 Artifact Naming

```
pcl2swap-{PCL2Ver.}-{Ver.}.dll
```

| Field | Meaning | Example |
|---|---|---|
| `PCL2Ver.` | the PCL build the patch is validated against | `2.99.0` |
| `Ver.` | this tool's own version | `v26.0-Alpha1` |

The on-disk form replaces the space in `Alpha 1` with nothing, because a space inside a file name is
error-prone on Windows shells:

```
pcl2swap-2.99.0-v26.0-Alpha1.dll
```

The tag and the displayed release name keep the canonical spaced form:

```
display name : v26.0-Alpha 1
git tag      : v26.0-Alpha1     (git refnames cannot contain a space)
release file : PCL2.dl.swap-v26.0-Alpha1.zip
```

## 3 Branches and Releases

| Purpose | Name | Notes |
|---|---|---|
| Mainline | `main` | always the released state of the current Major version |
| Major version branch | `v26.0` | mirrors `main` while only one Major is active |
| Pre-release | `v26.0-Alpha N` | published as a GitHub Pre-Release, tag signed |
| Release | `v26.0` | published once three rounds of production testing are done |

## 4 Release Checklist

1. Working tree clean, `docs/CHANGELOG.md` updated.
2. `scripts/build.ps1` produces `dist/pcl2swap-<pcl2>-<ver>.dll` plus its loader bundle.
3. `tools/VerifyOffsets` reports PASS against every sample in `data/offset-manifest.json`.
4. Patched binary launches and reports the expected PCL version in its own log.
5. Commit is SSH-signed (the repository enables `commit.gpgsign`).
6. Tag and Pre-Release are SSH-signed, release notes attached.
7. Release notes carry screenshots: upload the PNGs as release assets and reference them by their
   `https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/<tag>/<file>` URL (imgur is not
   used - it is unreachable from the build host).

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
