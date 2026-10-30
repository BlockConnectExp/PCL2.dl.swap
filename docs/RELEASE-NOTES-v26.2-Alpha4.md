# PCL2.dl.swap v26.2-Alpha 4

> Fourth Alpha of v26.2: the repository index is now **signable**. A `addons-index.json.sig`
> (RSA-SHA256) is verified against the built-in official public key or the user's
> `trusted-keys`, so a tampered index is rejected before any module is shown.

---

## What's new

### 1. 索引签名

- 签名文件：`addons-index.json.sig`
  ```json
  {"algorithm":"RSA-SHA256","keyId":"465f48e7b01ca238","signature":"<base64>"}
  ```
- 签名覆盖 **索引 JSON 的原始字节**；
- 信任来源：payload 内置官方公钥（`data/index-key.pub.xml`）+ `<数据根>\trusted-keys\*.xml`；
- **Unsigned 放行**（没有 `.sig` 视为兼容），**签名无效拒绝**并回落到 磁盘缓存 → 内置索引；
- 本地索引（自定义源指向文件）同样校验同名 `.sig`。

### 2. 签名工具

```powershell
.\scripts\sign-index.ps1 -PrivateKey keys\index-signing.key.xml
# -> data\addons-index.json.sig
```

私钥在 `keys/`（已 gitignore，不随仓库分发）；公钥 `data/index-key.pub.xml` 入库。

## 已实测

| 场景 | 结果 |
|---|---|
| 自检签名/篡改 | `仓库.签名校验 = True`（临时密钥写入 trusted-keys：正确签名通过、篡改后拒绝） |
| 真机本地签名索引 | `[repo] 签名有效（keyId 465f48e7b01ca238，内置公钥）` + 解析 1 个模块 |
| 自检 | `ok=51 fail=0` |

## Artifacts

| File | Purpose |
|---|---|
| `PCL2.dl.swap-Injector-v26.2-Alpha4.zip` | WinUI 3 注入器（self-contained，含 shim 种子） |
| `Pcl2Swap.Shim.x64.dll` / `.dat` | ExLoader 通道的注入包 |
| `PCL2.dl.swap-v26.2-Alpha4.zip` | shim + dat + SHA256SUMS |
| `PCL2.dl.swap-Loader-v26.2-Alpha4.zip` | 磁盘补丁形态 |
| `pcl2swap-2.99.0-v26.2-Alpha4.dll` | 磁盘补丁产物 |

---

> 本轮为安全增强，无新增界面元素，故未附 UI 截图；签名生效证据见上表日志。

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
