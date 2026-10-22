# 互动包

运行目录的 `Plugins` 文件夹放置 JSON 互动包。编辑后，在设置里点击“重新加载”即可生效。

```json
{
  "enabled": true,
  "entries": [
    {
      "text": "{petName}来陪你啦。",
      "action": "wave",
      "weight": 3,
      "when": "always",
      "cooldownSeconds": 120
    }
  ]
}
```

`text` 最多 42 个字符，`{petName}` 会替换为设置中的名字。`weight` 是抽中权重，范围 1–20；`cooldownSeconds` 是该条互动的冷却时间，范围 0–86400，默认 120 秒。

`action` 可以使用：

| 值 | 动作 |
| --- | --- |
| `idle` | 待机 |
| `wave` | 挥手 |
| `jump`、`double-jump` | 跳一下、连续跳跃 |
| `think`、`review` | 思考、查看 |
| `look-up`、`look-left`、`look-right` | 抬头、向左看、向右看 |
| `shy` | 害羞 |

`when` 默认是 `always`，也可设为 `morning`（6–10 时）、`night`（23–4 时）或 `low-quota`（剩余额度不多于 20%）。时间按电脑本地时间判断。

每个包最多 64 KiB，最多加载 20 个包，每包最多 200 条互动。损坏的文件和不支持的动作会跳过；没有可用内容时使用备用台词。互动包只提供文字和已有动作，不执行代码。
