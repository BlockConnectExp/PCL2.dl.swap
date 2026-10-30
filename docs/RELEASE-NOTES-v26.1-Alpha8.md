# PCL2.dl.swap v26.1-Alpha 8

> Eighth Alpha of the v26.1 line: the English layer reaches the Help and About pages. Guides,
> special-thanks lines, the Swap card description, the current-version row and the download page
> labels are now bilingual (about 40 new entries/phrases).

![English Help page](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.1-Alpha8/shot-help-en.png)

![English About & Credits page](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.1-Alpha8/shot-about-en.png)

---

## What's new

### 英文覆盖深化

| 区域 | 新增内容 |
|---|---|
| 帮助页 | Minecraft guide / Modpack guide / Resource install guide 三张卡片及全部描述、Search help |
| 关于页 | 开发者/协力开发者说明、Check for updates、Sponsor PCL!、Special thanks 各鸣谢行与按钮（Sponsor mirror / Open wiki / MCIM homepage） |
| 下载页 | `最新正式版，发布于 …` / `最新预览版，发布于 …` 动态行 |
| Swap 卡 | `Version vX · Download restore, silent OTA blocking and cloud-download AV sandbox` |
| 当前版本行 | `Current version: Release 2.99.0.1（415.50）` |
| 常规词汇 | 常规/高级/主题/深色/浅色/正版登录/离线登录/用户名/密码/登录 等 |

词典总量约 140 条（含短语表）。

### 已知未覆盖

- 「当前版本」行的全角括号 `（415.50）` 仍保留中文括号样式；
- EasyTier 等个别鸣谢条目尚未覆盖（后续 Alpha 继续补）。

## 已实测

| 场景 | 结果 |
|---|---|
| 2.99.0.1 真机英文 | Help 页全英文（Minecraft guide / Modpack guide / Resource install guide 及描述） |
| 2.99.0.1 真机英文 | About 页：Developer/Co-developer、Check for updates、Sponsor PCL!、Sponsor mirror / Open wiki / MCIM homepage、BMCLAPI/Mod metadata 鸣谢行 |
| 自检 | 英文切换与回退（I18n.Roundtrip）保持通过 |

## Artifacts

| File | Purpose |
|---|---|
| `PCL2.dl.swap-Injector-v26.1-Alpha8.zip` | WinUI 3 注入器（self-contained，含 shim 种子） |
| `Pcl2Swap.Shim.x64.dll` / `.dat` | ExLoader 通道的注入包 |
| `PCL2.dl.swap-v26.1-Alpha8.zip` | shim + dat + SHA256SUMS |
| `PCL2.dl.swap-Loader-v26.1-Alpha8.zip` | 磁盘补丁形态 |
| `pcl2swap-2.99.0-v26.1-Alpha8.dll` | 磁盘补丁产物 |

---

> 英文层继续按「词典 + 短语表」渐进覆盖，扩展方法见 `docs/i18n.zh.md`。

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
