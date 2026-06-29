# 项目说明

本项目是 [osumania_map_analyser](https://github.com/LeoBlackMT/osumania_map_analyser) 的ruleset改版。

## 功能

仅保留预估对应rf和ln段位，如图

![设置页](img1.png)
![效果](img2.png)

> 和原项目有些差异较大，原因：为wasm嵌入计算etta键型分布并模型修正

## 安装

右侧Release下载文件，将下载的dll文件放到rulesets目录下

## 构建

```bash
dotnet build osu.Game.Rulesets.ManiaMapAnalyser\osu.Game.Rulesets.ManiaMapAnalyser.csproj -c Release
```
