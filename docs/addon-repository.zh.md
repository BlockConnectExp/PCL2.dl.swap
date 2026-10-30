# 模块仓库 / Addon Repository

> Summary: a small JSON index that lets the swap UI list community modules and install one with a
> single click - download, SHA-256 verification and the normal installer all still apply.

---

## 1 索引格式

仓库索引托管在 `<仓库>/data/addons-index.json`，平台默认读取：

```
https://raw.githubusercontent.com/BlockConnectExp/PCL2.dl.swap/main/data/addons-index.json
```

格式：

```json
{
  "schema": 1,
  "updated": "2026-10-31",
  "modules": [
    {
      "id": "com.example.module",
      "name": "Example Module",
      "version": "1.0.0",
      "author": "Author",
      "description": "What it does.",
      "project": "https://github.com/author/project",
      "download": "https://github.com/author/project/releases/download/v1.0.0/module.swapaddon",
      "sha256": "<64 hex chars>",
      "size": 12345
    }
  ]
}
```

| 字段 | 必填 | 说明 |
|---|---|---|
| `id` | 是 | 与 manifest.id 一致 |
| `download` | 是 | 直接可下载的 `.swapaddon` URL |
| `sha256` | 强烈建议 | 下载后校验；不匹配会删除文件并报错 |
| `size` | 否 | 展示用 |
| `project` | 否 | 项目主页按钮 |

## 2 平台行为

1. 打开 `Swap Addon` 页 → 首次自动后台加载索引（失败时回落到 payload 内置索引）；
2. 卡片列出每个模块（名称/版本/作者/大小/描述）；已安装的显示「已安装」；
3. **一键安装**：后台下载 → 校验 SHA-256 → `Review` → 弹窗显示名称/版本/权限/信任 →
   用户确认后交给安装器（签名/文件清单/权限/作用域照常生效）→ 刷新页面与侧栏；
4. **更新检测**：索引版本更高时显示「可更新」与 Update 按钮；
5. **缓存**：成功索引写入 `<数据根>\repo\addons-index.json`，在线失败按 缓存 → 内置 回落；
   30 分钟内自动刷新使用缓存，「Refresh repository」强制网络刷新；
6. 「打开索引」按钮可查看索引原文。

## 3 发布一个模块

1. 用 `examples/demo-addon` 或自己的工程构建 `.swapaddon`；
2. 上传到 GitHub Release，拿到 `releases/download/<tag>/<file>.swapaddon` 直链；
3. 计算 `sha256` 与 `size`；
4. 向 `data/addons-index.json` 提交一条记录（PR 或直接由维护者合并）；
5. 用户侧下次刷新即可看到并安装。

## 4 安全模型

- 索引条目**不预先受信任**：安装仍走包信任链（签名/文件哈希/未知密钥提示）；
- `sha256` 只保证「下载内容与索引声明一致」，不替代包内签名；
- 所有安装仍受模块权限严格门控约束（声明了 permissions 的模块）。

## 5 当前索引

首个条目：PCL2 Themes v26.0-Alpha4（`StarsailsClover/PCL2Themes`）。

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
