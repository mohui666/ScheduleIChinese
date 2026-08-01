Schedule I 简体中文离线汉化 1.3.57
适配版本：Schedule I v0.4.6f11 / Steam Build 24484559

【安装方式一：一键 EXE（推荐）】
1. 完全退出游戏。
2. 运行 ScheduleIChineseInstaller-v1.3.57.exe。
3. 安装器会自动识别 Steam 游戏目录；未识别时请选择包含
   Schedule I.exe 的游戏根目录。
4. 点击“安装 / 更新汉化”，完成后从 Steam 启动游戏。

安装器已经包含 BepInEx 6 IL2CPP。若目录中已有 BepInEx，安装器会保留
现有加载器和其他模组，只更新本汉化插件与配置。

【安装方式二：ZIP】
1. 完全退出游戏。
2. 将 ZIP 内全部文件解压到游戏根目录并允许覆盖。
3. 从 Steam 启动游戏。

【卸载】
运行一键安装器并点击“卸载汉化”。也可手动删除：
  BepInEx\plugins\ScheduleIChinese
  BepInEx\config\com.schedulei.chinesemod.cfg

卸载器会保留 BepInEx 和其他模组。汉化不修改存档和游戏原始资源，
因此不需要 Steam 验证文件。

【离线配置】
BepInEx\config\com.schedulei.chinesemod.cfg

EnableRuntimeTranslationFallback = true
EnableAutoTranslate = false
DumpUntranslated = false

本版使用本地词表在游戏显示层翻译动态文本，运行时不联网。不要开启
EnableAutoTranslate；该选项仅保留为开发调试能力。

【翻译与验证】
- 主翻译表：10,400 个唯一键，校验错误 0。
- 显示层精修/补漏：550 条，含 v0.4.6f11 新增文本 48 条。
- 效果与形容词：51 个唯一词条，校验错误 0。
- 动态格式规则：354 条，错误 0，警告 0。
- 人名与自定义名称保护映射：6,432 条。
- 内部枚举与逻辑键黑名单：601 条。
- 离线关键用例自检：73/73 通过。
- 占位符、TMP 标签、换行、重复键和乱码检查均通过。
- 在线自动翻译默认关闭。

【已知限制】
- 这是 BepInEx IL2CPP 显示层汉化，启动时仍需加载 BepInEx；不是直接
  覆盖 Unity 资源的无加载器版本。
- 人物姓名、基础品种名和玩家自定义名称按设计保留原文。
- 图片贴图中烘焙的英文、其他模组新增文本和服务器下发的新文本可能
  仍显示英文。
- 游戏更新后需要重新提取并验证新增文本。
- 安装器为本地自编译的未签名程序，Windows SmartScreen 可能显示
  “未知发布者”；安装器与汉化默认均不联网。

字体许可和第三方说明见同目录 OFL.txt 与 THIRD_PARTY_NOTICES.txt。
