# ScheduleIChinese 1.3.57

适配 Schedule I v0.4.6f11 / Steam Build 24484559 的简体中文离线汉化。

本版不修改游戏原始 Unity 资源、`global-metadata.dat` 或存档。固定 UI、
动态对话、地点和效果名都由 BepInEx IL2CPP 插件在显示层使用本地词表
翻译；不调用在线翻译。人物姓名、基础品种名和玩家自定义名称按设计保留。

- 通过 TMP 文本变更事件统一处理 `text`/`SetText`，Harmony 仅拦截
  烘焙标签的 `OnEnable` 和旧版 uGUI Text，减少启动补丁开销。
- 场景切换、动态面板和关系面板事件会触发有界补扫。
- `NotoSansSC.otf` 作为动态多图集 TMP fallback。
- 翻译文件位于 `BepInEx/plugins/ScheduleIChinese/Translations`。
- 效果/形容词词表支持项目符号、颜色标签和 TMP 富文本组合。
- 在线自动翻译默认关闭，离线显示层翻译默认开启。
- 不对每帧或每两秒执行全局扫描，降低载入和 UI 卡顿。

## 1.3.57 变更

- 适配游戏 v0.4.6f11 的 UI 状态系统与重新生成的 IL2CPP 接口；插件的
  TMP/uGUI 拦截、字体注入和运行时补扫均已通过新版实机启动验证。
- 补全手柄设置、Xbox/PlayStation 方向键与摇杆提示、暂停失焦、快捷键、
  外观确认、商品筛选、车辆喷漆及新版交互说明等新增文本。
- 为设置面板增加上下文翻译，使全局黑名单中的 `Gamepad`、`Imperial`、
  `Metric`、`On`、`Off`、`Mouse` 只在可见设置标签中安全汉化。
- 根据新版程序集补充输入平台、面板导航和手柄状态枚举黑名单，避免显示
  层翻译污染游戏读取的内部值。

## 1.3.17 变更

- 商店商品名"消失"修复：商店列表的名称标签会被游戏与商品名比对，
  不一致即隐藏，故该层级（Listing*）下的文本一律不翻译，恢复英文显示。
- 通缉横幅（WANTED / WANTED DEAD OR ALIVE / UNDER ARREST）修复：
  这些文本由动画系统直接写字段、不经过任何托管 setter，新增滚动
  扫描器（OnEnable 注册 + 每帧少量巡检）持续捕获并翻译。

## 1.3.16 变更

- 修复商店商品名"消失"：翻译本身正常，但部分组件（商店格子等）对
  TMP fallback 链不响应，中文渲染为空白。现在给被翻译的组件直接
  指定 CJK 字体资源（Noto Sans SC），绕开 fallback 机制。

## 1.3.15 变更

- 根因修复：联系人详情等面板使用的是旧版 uGUI `Text`，其逐行追加赋值
  （"• Calming" → "• 舒缓
• Munchies"）会让旧的 `Translate()` 因字符串
  已含中文而整体放弃，导致第一行之后全部漏翻。`LegacyText` 补丁改走
  `TranslateDisplayText`（逐行、容忍已有中文）。

## 1.3.14 变更

- 地区名统一翻译为中文（北城区/西城区/市中心/郊区/上城区/码头区），
  从黑名单移除 `EMapRegion` 成员。
- `Meth`/`Methamphetamine` 按用户要求保留英文名（加入保护名单）。
- 补主语类词条（你被捕了/你被解雇了）、通缉等级（Wanted Level）词条。
- 新增 `SetText(StringBuilder)` 拦截（本作暂无此重载，作为兼容兜底）。

## 1.3.13 变更

- 新增烘焙标签翻译：拦截 `TextMeshProUGUI`/`TextMeshPro`/uGUI `Text`
  的 `OnEnable`，覆盖预制体里烘焙、从不经过文本 setter 的静态标签
  （手机应用、商店、订单面板、罚单等界面标题）。
- 联系人列表地区后缀统一为英文：删除 48 条按人名翻译地区的精确词条，
  所有人名标签统一走动态规则（地区名是 `EMapRegion` 枚举成员，在
  黑名单中，避免游戏读回出错）。
- 新增词条与动态规则：罚单标题/重度毒品没收、短信 "Deal??" 变体、
  每周最常购买/总消费、其余地区的卡特尔影响力。
- 自检更新：人名+地区样本不再要求输出含中文（地区按设计保留英文）。

## 1.3.12 变更

- 恢复了 1.3.11 中误删的 1119 条安全单词翻译（Add、Apply、Alarm 等
  常用 UI 词）。
- 拦截改为精确黑名单（`Translations/deny_keys.txt`，594 条）：只覆盖
  游戏自身会被代码读回的枚举成员——品质等级 `EQuality`、按键
  `KeyCode`/`ButtonCode`、设置面板枚举、等级 `ERank`、地区
  `EMapRegion`、星期 `EDay`、角色创建分类、UI 弹窗应答、员工/赌场/
  任务枚举——外加控制台命令与颜色值等垃圾键。名单由游戏 interop
  程序集提取（`tools/restore_safe_keys.py`），加载时最先读入、
  不区分大小写拒绝。

## 1.3.11 变更

- 回退了 1500 余条疑似变量名的翻译（控制台命令如 `addxp`、按键名如
  `Backspace`、枚举值如 `Standard`/`Premium`/`Heavenly`、颜色值等单标记
  词条）。这些字符串会被游戏代码读回，翻译后会导致金钱/联系人/短信等
  逻辑异常以及品质等级文本粘连。

构建：

```powershell
# 需要指定游戏根目录（仓库不包含任何游戏文件）：
dotnet build .\ScheduleIChinese.csproj -c Release -p:ScheduleIGameDir="C:\Program Files (x86)\Steam\steamapps\common\Schedule I"
```

验证：

```powershell
python C:\Users\mohui666\modtools\validate_translations.py .\Translations\zh_CN.txt
python .\tools\check_dynamic_rules.py
```
