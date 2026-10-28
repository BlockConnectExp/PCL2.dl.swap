# PCL2.dl.swap v26.1-Alpha 2

> Second Alpha of the v26.1 line: packages get a **verifiable identity**. A module author can sign a
> `.swapaddon`; the payload carries the SHA-256 of every file; the manager shows the verdict before
> installing and refuses anything whose bytes do not match. Plus two version-level warnings
> (downgrade, same-version-different-content) in the install preview.

![安装预览：信任状态](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.1-Alpha2/shot-trust-preview.png)

![模块详情：签名状态与负载校验](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.1-Alpha2/shot-signed-detail.png)

---

## What's new

### 1. 包信任链

- 打包器在 `manifest.json` 里写入 **`files` 清单**（每个负载文件的 SHA-256），并用 `--sign` 对
  `manifest.json` 的**原始字节**做 RSA-SHA256 签名 → `signature.json`（含算法、keyId、签名）。
- 受信任公钥放在 `<数据根>\trusted-keys\*.xml`；keyId 由公钥 XML 的 SHA-256 前 16 位派生，
  打包器与宿主算法一致，不需要额外的 id 注册表。
- 判定与策略：

| 状态 | 含义 | 安装 |
|---|---|---|
| `Trusted` | 签名密钥受信任 + 文件全对 | 放行 |
| `Unsigned` | 老包没有签名（有清单仍校验） | 放行 |
| `UnknownKey` | 有签名但密钥不在受信任集合 | 放行并明确提示 |
| `InvalidSignature` | 受信任密钥的签名校验失败 | **拒绝** |
| `TamperedFiles` | 签名有效但文件与清单不符（缺失/哈希不符/**未列入清单的 dll**） | **拒绝** |

启动扫描时跑同一套校验：被篡改的已装模块会被标为失败并且**不启用**。

### 2. 版本级告警（安装预览）

- **降级安装**：包内版本低于已安装版本时提示「已装 X → 包内 Y」。
- **同版本换内容**：同一 id+版本但负载清单摘要不同 → 「包内容与已安装的不一致」。
  用负载摘要而不是 zip 文件哈希，重新打包同一份内容不会误报。

### 3. 作者工具

```powershell
PclAddonPack --gen-key .\keys\me.key.xml          # 生成 RSA-2048 密钥对（私钥 + .pub.xml + keyId）
PclAddonPack --project .\bin --id demo --name Demo --version 1.0.0 `
             --entry Demo.Addon --sign .\keys\me.key.xml   # 打包并签名
```

## 已实测

| 场景 | 结果 |
|---|---|
| harness 六套件 | `--addontest`（新增 9 项信任链断言）/ `--pcltest` / `--pagetest` / `--scantest` / `--loaderhooktest` / `--surfacetest` 全部 PASS |
| harness 信任断言 | 老包 `Unsigned`；受信任签名 `Trusted`；安装记录负载摘要；未知密钥 `UnknownKey` 且不阻塞；篡改负载 `TamperedFiles`；篡改包**拒绝安装**；降级与同版本换内容告警；预览打印信任状态 |
| 真机 2.99.0：预览 | 用打包器生成的签名包 + 公钥放进 `trusted-keys` → 预览显示 `信任状态：已签名（密钥 2487732295f39f81），文件校验通过（1 个）`（见上图） |
| 真机 2.99.0：详情 | 安装后详情页「签名状态」同款文案，模块正常启用、钩子挂载成功 |

## Artifacts

| File | Purpose |
|---|---|
| `Pcl2Swap.Shim.x64.dll` | the ExLoader injection shim (upload this on the injector card) |
| `Pcl2Swap.Shim.x64.dat` | the same build as a decimal byte dump for ExLoader's userFiles encoding |
| `PCL2.dl.swap-v26.1-Alpha2.zip` | shim + dat + `SHA256SUMS.txt` |
| `source-v26.1-Alpha2.zip` | source snapshot of this release |
| `PCL2.dl.swap-Loader-v26.1-Alpha2.zip` | the disk-patch shape (Loader + Patcher + Payload + dependencies), no ExLoader needed |

SHA-256:

```
106136b716f92c18b0ab6ce9595bab59bf8ef2aae7b4380b2b95e891befab27f  Pcl2Swap.Shim.x64.dll
03b5fac1889a495dbdeba1cdaeaa2d6721ad85988d1f2ff740dbca2ab24e23af  Pcl2Swap.Shim.x64.dat
```

## How to verify

1. Re-import `Pcl2Swap.Shim.x64.dll` in ExLoader and start PCL (fully close PCL first).
2. `PclAddonPack --gen-key` 生成密钥，把 `.pub.xml` 复制到 `%APPDATA%\PCL2Swap\trusted-keys\`；
   用 `--sign` 打包一个模块 → 设置 → Swap Addon → 安装模块…：预览里应出现
   「信任状态：已签名（密钥 …），文件校验通过（N 个）」。
3. 把包内某个 dll 用任意方式改掉（或用旧包里的 dll 覆盖）后再次安装：应被拒绝并给出
   「文件哈希不一致 / 未列入清单的代码文件」。
4. 安装同 id 的低版本包 → 预览出现降级提示；同版本换 dll 重打包 → 预览出现内容不一致提示。

## Known Limitations

- 信任是**可选**的：未签名的包照常安装（老包兼容），平台不假装未签名的代码可信。
- keyId 只是公钥指纹，没有信任链/吊销机制；换密钥后旧签名的包会变成 `UnknownKey`。
- 签名覆盖清单与负载哈希，**不覆盖**模块运行时行为：同进程模型下签名不是沙箱边界。
- `files` 清单之外的**非 dll** 附件（图片等）不参与校验（它们不携带代码）。

**Network**: the module itself makes no network connections - PCL keeps doing all of its own
networking. The only local processes it may start are the on-disk malware scanners you enable.

---

License: MIT

GitHub@BlockConnectExp

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
