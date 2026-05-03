# OsuManiaMapAnalyser.Core

核心入口：

```csharp
BeatmapAnalyzer.AnalyzeJsonToJson(inputJson);
```

## 输入 JSON

```json
{
  "beatmap": {
    "osuText": "完整 .osu 文本"
  },
  "settings": {
    "speedRate": 1,
    "odFlag": null,   // 可为 `null`、数值、`"HR"`、`"EZ"`
    "cvtFlag": null   // 可为 `null`、`"IN"`、`"HO"` 或组合字符串
  }
}
```

## 输出 JSON

```json
{
  "metadata": {
    "title": "Song Title",
    "titleUnicode": "Song Title",
    "artist": "Artist",
    "artistUnicode": "Artist",
    "creator": "Mapper",
    "version": "Difficulty",
    "statusText": "Artist - Song Title [Difficulty] // Mapper"
  },
  "beatmap": {
    "columnCount": 4,
    "lnRatio": 0.22858472998137802
  },
  "card": {
    "contentBar": "None",
    "modeTag": "Mix",
    "leftCapsule": {
      "mode": "ReworkSR",
      "value": 5.78,
      "displayValue": "5.78",
      "unit": "SR"
    },
    "difficulty": {
      "caption": "Estimate Difficulty(RC7.38)",
      "text": "Reform 7 high\nLN 8 mid/low",
      "rawText": "Reform 7 high || LN 8 mid/low",
      "numericDifficulty": 7.38,
      "vibro": false
    }
  }
}
```

如果要直接拿 JSON 字符串输出，就调用：

```csharp
BeatmapAnalyzer.AnalyzeJsonToJson(inputJson);
```
