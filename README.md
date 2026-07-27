# Schedule I 简体中文汉化

《Schedule I》（v0.4.5f2 / Steam Build 22829923）的简体中文离线汉化源码仓库。

**本仓库只包含汉化插件与安装器的源码，不包含任何游戏本体文件。**
安装器载荷（`payload.zip`）因包含游戏资源文件，同样不包含在仓库中。

## 目录结构

- `ScheduleIChinese/` — BepInEx IL2CPP 显示层汉化插件（推荐使用）。
  只在显示层翻译文本，不修改游戏资源、`global-metadata.dat` 或存档。
- `ScheduleIChineseInstaller/` — BepInEx + 插件一键安装器源码。
- `ScheduleIChineseStaticInstaller/` — 静态资源版安装器源码（已弃用）。
  该方案直接改写游戏资源文件与 IL2CPP 元数据，会破坏金钱、短信、
  角色创建、联系人好感度等游戏逻辑，请勿使用；保留源码仅供参考。

## 构建插件

插件引用游戏目录下的 BepInEx 与 IL2CPP interop 程序集，需要通过
`ScheduleIGameDir` 指定游戏根目录：

```powershell
cd ScheduleIChinese
dotnet build .\ScheduleIChinese.csproj -c Release `
  -p:ScheduleIGameDir="C:\Program Files (x86)\Steam\steamapps\common\Schedule I"
```

构建产物为 `bin/Release/net6.0/ScheduleIChinese.dll`，与 `Translations/`
和 `assets/` 一起放入 `BepInEx/plugins/ScheduleIChinese/` 即可。

## 翻译文件维护约定

- 词条格式为每行一条 `原文=译文`，支持 `\\`、`\n`、`\r`、`\=` 转义。
- **不要添加疑似变量名的词条**：插件在加载时会拒绝所有形如
  `^[A-Za-z0-9_]+$` 的裸单词键（效果词表 `effects_zh_CN.txt` 除外）。
  这类字符串（枚举值、按键名、控制台命令等）会被游戏代码读回，
  翻译后会破坏游戏逻辑（1.3.11 修复的金钱消失、短信不显示、
  无法创建角色、联系人进度丢失、品质等级粘连等 bug 均源于此）。
- 人物姓名与基础品种名通过 `preserve_names.txt` 保留英文。
- 动态规则（`dynamic_zh_CN.txt`）必须匹配整段文本，修改后运行
  `python tools/check_dynamic_rules.py` 校验。

## 许可

汉化文本与插件代码可自由使用与修改；游戏本体内容归 TVGS 所有。
