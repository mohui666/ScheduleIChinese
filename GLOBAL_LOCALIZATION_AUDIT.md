# Schedule I 中文本地化全局审计

审计日期：2026-07-29

游戏目录：`C:\Program Files (x86)\Steam\steamapps\common\Schedule I`
中文插件版本：ScheduleIChinese v1.3.54

## 结论与范围

本报告同时扫描了 Unity 场景/资源中的可见文本组件、剧情与任务等序列化内容、StreamingAssets JSON、IL2CPP 字符串字面量，以及插件现有的精确翻译、动态规则、保留名称和拒绝键。

- 全局提取原始唯一字符串：33,774 条。
- IL2CPP 字符串字面量：25,238 条。
- StreamingAssets JSON：75 个文件。
- 可见 Text/TextMeshPro 组件：7,952 个，共 951 条唯一预置文本。
- 剧情、任务、物品、顾客、教程、通知、消息、区域等内容资源：2,079 条唯一文本。

33,774 条原始字符串中包含程序集类型、方法名、调试日志、存档字段、资源 ID、着色器文本、按键名和设计上保留的英文专名，不能全部当作界面漏译。下面按可信度列出过滤后的结果。

## A. 已由截图或资源直接确认

### A1. 抢劫短信

| 英文原文/模板 | 当前问题 | 建议中文 |
|---|---|---|
| `This is what they got:` | 动态规则使用 `.` 匹配后续清单，但未启用跨行匹配；多行短信无法命中 | `他们抢走了这些东西：` |
| `{COUNT}x Jar of {PRODUCT} ({QUALITY} quality)` | 数量、包装、商品和品质组成的整行未被翻译 | `{COUNT}x 罐装{PRODUCT}（{QUALITY}品质）` |
| `${AMOUNT} cash` | 同一多行动态文本中的现金行未翻译 | `${AMOUNT} 现金` |
| `Benzies` | 被名称保护规则强制保留为英文，但其他文本又使用“本齐帮/本齐家族/Benzies 家族”，术语不统一 | 应统一选择 `本齐帮` 或保留 `Benzies` |

截图中的具体实例为：

```text
This is what they got:
8x Jar of Granddaddy Purple (Standard quality)
5x Jar of Granddaddy Purple (Standard quality)
$543 cash
```

### A2. 区域解锁条件

| 英文原文/模板 | 当前问题 | 建议中文 |
|---|---|---|
| `Reduce cartel influence in Docks to` | 实际界面由多个文本片段拼接，现有完整句精确翻译无法命中 | `将码头区的卡特尔影响力降至` |
| `to unlock this region` | 同上，尾部片段未覆盖 | `以解锁该区域` |
| `Reduce cartel influence in {REGION} to` | Downtown、Suburbia、Westville 等区域使用相同拼接逻辑 | `将{REGION}的卡特尔影响力降至` |

数值 `300 / 1000` 不需要翻译。

### A3. 顾客要求等级

游戏枚举为 `VeryLow / Low / Moderate / High / VeryHigh`。现状如下：

| 显示文本 | 状态 | 建议中文 |
|---|---|---|
| `Very Low` | 已有翻译 | `非常低` |
| `Low` | 被拒绝键规则排除，截图确认仍为英文 | `低` |
| `Moderate` | 已有翻译 | `中等` |
| `High` | 被拒绝键规则排除 | `高` |
| `Very High` | 无精确翻译 | `非常高` |

这里是顾客标准，不是物品品质。现有上下文补丁只处理物品品质 `Trash / Poor / Standard / Premium / Heavenly`，没有处理顾客标准。

### A4. 姓名中英混排

项目既有策略是人物姓名保留英文，因此以下两条属于错误的半翻译：

| 当前文本 | 应恢复为 |
|---|---|
| `Elizabeth 霍姆利` | `Elizabeth Homley` |
| `珀尔 Moore` | `Pearl Moore` |

## B. 序列化剧情资源中的确定漏译

这些文本直接存储在 `DialogueModule` 等剧情资源中，且未被现有精确翻译或动态规则覆盖。

| # | 英文原文 | 建议中文 |
|---:|---|---|
| 1 | `Hey there` | `嘿。` |
| 2 | `I guess so` | `应该吧。` |
| 3 | `Man, it's rainy as hell` | `老兄，这雨也太大了。` |
| 4 | `Mornin' dude` | `早啊，老兄。` |
| 5 | `No thank you` | `不了，谢谢。` |
| 6 | `No thanks bro` | `不用了，兄弟。` |
| 7 | `Nope man` | `不了，老兄。` |
| 8 | `Not interested sorry bro` | `没兴趣，抱歉兄弟。` |
| 9 | `Not loving this rain, bro` | `我可不喜欢这场雨，兄弟。` |
| 10 | `Not right now broskies` | `现在不行，兄弟。` |
| 11 | `Not yet man` | `还不行，老兄。` |
| 12 | `Oh hell yeah` | `哦，那当然！` |
| 13 | `Ok then` | `那好吧。` |
| 14 | `Sounds good homie` | `听起来不错，哥们。` |
| 15 | `Stop it dude` | `住手，老兄。` |
| 16 | `Woah dude` | `哇，老兄。` |
| 17 | `Yeah ok` | `行吧。` |
| 18 | `Yeah, not what I'm after homie` | `不了，这不是我想要的，哥们。` |

以下对话资源中还存有：

```text
I want to buy a sewer access key
```

插件现有动态规则只覆盖带前置价格的形式，如 `[$123] I want to buy a sewer access key`。它很可能会在运行时拼上价格后正常命中，因此列为“需在实际购买界面确认”，不计入上面 18 条确定漏译。

## C. 房产和车辆价格选项中的确定漏译

基础名称大多已有翻译，但以下带 `<PRICE>` 的完整选择文本没有对应精确翻译，也没有通用动态规则：

| 英文原文 | 建议中文模板 |
|---|---|
| `Taco Ticklers (<PRICE>)` | `塔可挠痒痒（<PRICE>）` |
| `The Bruiser (<PRICE>)` | `猛男（<PRICE>）` |
| `The Bungalow (<PRICE>)` | `平房（<PRICE>）` |
| `The Car Wash (<PRICE>)` | `洗车场（<PRICE>）` |
| `The Cheetah (<PRICE>)` | `猎豹（<PRICE>）` |
| `The Dinkler (<PRICE>)` | `丁克勒（<PRICE>）` |
| `The Docks Warehouse (<PRICE>)` | `码头仓库（<PRICE>）` |
| `The Laundromat (<PRICE>)` | `自助洗衣店（<PRICE>）` |
| `The Post Office (<PRICE>)` | `邮局（<PRICE>）` |
| `The Shitbox (<PRICE>)` | `破车（<PRICE>）` |
| `The Veeper (<PRICE>)` | `维珀（<PRICE>）` |
| `Yes (<PRICE>)` | `是（<PRICE>）` |

中文名称应以项目现有术语表为准；此处中文仅用于说明缺失模板。

## D. 任务和错误界面中的确定漏译

| 类型 | 英文原文 | 当前问题 | 建议中文 |
|---|---|---|---|
| 任务目标 | `Kill the bandit who keeps stealing Stan's supplies at the docks` | 只有带 `(5)` 和 `(Inactive)` 的特定组合翻译，活动状态原始文本未覆盖 | `杀掉那个一直在码头偷斯坦物资的强盗` |
| 载入错误 | `Required file 'Game.json' is missing. Save file cannot be loaded.` | 可见 UI 文本组件中存在，未翻译 | `缺少必需文件“Game.json”，无法载入存档。` |

## E. IL2CPP 运行时高可信候选

以下文本来自游戏代码的字符串字面量，具有明显的玩家提示/对话语义，现有翻译未覆盖。它们不是场景中的静态组件，必须触发相应玩法才能最终确认是否会在显示前被二次拼接或替换。

| # | 英文原文 | 建议中文 |
|---:|---|---|
| 1 | `Can't destroy vehicle while occupied.` | `车内有人时无法销毁车辆。` |
| 2 | `Can't sleep while athletic!` | `处于运动状态时无法睡觉！` |
| 3 | `Can't sleep while energized!` | `精力充沛时无法睡觉！` |
| 4 | `Hey boss, I've heard there's a Benzies deal happening in {0}, {1}. Might be worth checking out.` | `老板，我听说本齐帮会在{0}的{1}进行交易，或许值得去看看。` |
| 5 | `In the middle of the night, the door is kicked in and you are dragged into a vehicle trunk...` | `半夜，房门被人踹开，你被拖进了汽车后备厢……` |
| 6 | `Mick doesn't want to do business with you right now.` | `米克现在不想和你做生意。` |
| 7 | `Most legal shops will only accept <h1>card payments</h>, while most illegal shops only take cash. Visit an <h1>ATM</h> to deposit and withdraw cash.` | `大多数合法商店只接受<h1>刷卡支付</h>，而非法商店通常只收现金。前往<h1>自动取款机</h>存取现金。` |
| 8 | `My friend <NAME> can hook you up with <PRODUCT>. I've passed your number on to them.` | `我的朋友<NAME>能给你弄到<PRODUCT>。我已经把你的号码给他了。` |
| 9 | `None of your business!` | `不关你的事！` |
| 10 | `Only the host can start the game.` | `只有房主可以开始游戏。` |
| 11 | `Round 1\nPredict if the next card will be red or black.\nYou can also forfeit and cash out.` | `第1轮\n猜下一张牌是红色还是黑色。\n你也可以弃权并兑现奖金。` |
| 12 | `Round 2\nPredict if the next card will be higher or lower.\nYou can also forfeit and cash out.` | `第2轮\n猜下一张牌更大还是更小。\n你也可以弃权并兑现奖金。` |
| 13 | `Round 3\nPredict if the next card will be inside or outside the previous two cards.\nAce counts as 11.\nYou can also forfeit and cash out.` | `第3轮\n猜下一张牌的点数是在前两张牌之间还是之外。\nA按11点计算。\n你也可以弃权并兑现奖金。` |
| 14 | `Round 4\nPredict the suit of the next card.\nYou can also forfeit and cash out.` | `第4轮\n猜下一张牌的花色。\n你也可以弃权并兑现奖金。` |
| 15 | `Thanks. I'll now check your vehicle.` | `谢谢。现在我要检查你的车辆。` |
| 16 | `There is a mushroom colony ready for harvest, but it has no destination or it's destination is full.` | `有一处蘑菇菌落已经可以收获，但没有指定目的地，或目的地已满。` |
| 17 | `This trash can is not usable by cleaners.` | `清洁工无法使用这个垃圾桶。` |
| 18 | `Vehicle won't fit everything. Some items will be placed on the pallets.` | `车辆装不下全部物品，部分物品将放在托盘上。` |
| 19 | `Waiting for a rival dealer.` | `正在等待敌对经销商。` |
| 20 | `While dragging an item, press <Input_Left> or <Input_Right> to rotate it.` | `拖动物品时，按<Input_Left>或<Input_Right>旋转。` |
| 21 | `You can now order <PRODUCT> from <NAME>. <PRODUCT> can be used to <PURPOSE>.` | `现在可以从<NAME>处订购<PRODUCT>。<PRODUCT>可用于<PURPOSE>。` |
| 22 | `You can use your management clipboard to assign me a locker.` | `你可以用管理剪贴板给我分配一个储物柜。` |

## F. IL2CPP 运行时次级候选

这些文本看起来像 UI 状态、奖励、违法原因或交互失败提示，但也可能只用于内部状态、日志，或会在显示前参与模板拼接。应通过开启插件的 `DumpUntranslated` 并实际触发玩法来确认。

```text
Already preparing a dead drop
Already waiting for a dead drop
Dry Clay Soil
Employee limit reached (
Exceeded Quality Bonus
Failure to comply with police instruction
I haven't been assigned a locker
Insert unpackaged product and mixing ingredient
Insufficient inventory space
No RDX in inventory
No product equipped
No quality item equipped
Order is too large for delivery vehicle
Order too large
Possession of low-severity drug
Possession of moderate-severity drug
Possession of high-severity drug
Quick Delivery Bonus
Reach the rank of <h1>
This delivery is waiting for loading dock
Vehicle must be parked inside the shop
Won't fit in inventory
You have no vehicle to recover
[Complete Deal]
Cash Deposit
Cash Withdrawal
Potential Customer
Potential Dealer
Region unlocked: {0}
Vehicle full
Violating curfew
```

## G. AM/PM 时间格式恢复

中文翻译原来把多处 `AM/PM` 改成了“上午/下午/早/晚”。现已新增末尾覆盖文件，恢复游戏的 12 小时制 `AM/PM` 表示，同时保留周围中文句子。

- 动态覆盖规则：19 条。
- 精确覆盖规则：43 条。
- 动态规则检查：0 个错误，0 个警告。
- 项目构建：0 个错误，0 个警告。
- 源码翻译文件和游戏部署文件内容一致。

覆盖文件：

```text
ScheduleIChinese/ScheduleIChinese/Translations/zzz_time_notation_overrides.txt
BepInEx/plugins/ScheduleIChinese/Translations/zzz_time_notation_overrides.txt
```

翻译文件会在插件启动时载入，因此需要完全退出并重新启动游戏后生效。

## H. 不应计为漏译的项目

以下类别在全局字符串差异中数量很大，但不应直接加入翻译：

- 程序类型名、方法名、事件名、资源路径、GUID、存档字段和调试日志。
- 控制器/键盘按键名，如 `L1`、`R1`、`Space`、`E`。
- 编辑器占位文本，如 `New Text`、`Entry`、`Problem`、`Choice Text`、`PlayerName0233`。
- 按项目策略保留的人名、商品名和帮派名。
- 商店列表中与游戏逻辑绑定的英文商品组合文本；项目说明指出直接翻译可能导致商品消失。
- 已在运行时格式化后被动态规则覆盖的模板。例如原始字面量 `CURFEW SOON\n{0} MINS` 会在替换成具体分钟数后命中现有规则。

## I. 后续验证建议

要得到运行时意义上的最终完整清单，需要将插件配置中的 `DumpUntranslated` 临时设为 `true`，依次触发新游戏、旧存档载入、全部区域解锁、顾客交易、抢劫短信、房产/车辆购买、卡牌游戏、车辆与仓库失败提示等玩法，再把日志新增项与本报告 E/F 两组交叉核对。静态扫描已经覆盖打包资源，但无法证明每个代码字面量一定会直接显示给玩家。

## J. 修复状态

2026-07-29：本报告 A–F 组列出的确定漏译和运行时候选均已加入
`Translations/zzz_global_localization_fixes.txt`。顾客标准和手机内帮派名称使用
`TextPatch` 的安全界面上下文处理；人物全名已加入保护名单；原有会吞掉多行抢劫清单的
错误动态规则已移除。修复版本已成功构建并部署到当前游戏目录。

## K. 实体店铺招牌

2026-07-29：新增 `StorefrontTranslations` 显示专用映射，共覆盖 67 个店铺名称、
大小写、空格和换行变体。该映射直接用于 TMP 与旧版 uGUI 文本，只改变画面显示，
不会修改游戏内部商店、企业或地点标识。覆盖范围包括五金店、药店、银行、加油站超市、
洗衣店、邮局、餐馆、酒吧、精品店、当铺、赌场、汽车旅馆、纹身店及各类经营场所招牌。
