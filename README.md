# AdoFalling — 冰与火之舞「下落式音游」叠加层 MOD

在原版《A Dance of Fire and Ice》画面上叠加一个下落式音游（4K/VSRG 风格）的界面：
**判定线、轨道、下落按键不透明，其余全部透明**，所以原版谱面（星球、方块、路径）依然完整可见。
本 MOD 只做「可视化读谱」，不接管判定：打歌仍由原版负责，叠加层把谱面按时间轴显示成下落按键。

---

## 1. 当前状态

| 项目 | 状态 |
|---|---|
| 游戏版本 | 3.3.1（Unity 6000.3.10f1，build 24397494） |
| 加载器 | BepInEx 5.4.23.5（已装到游戏目录，用 `version.dll` 作 Doorstop 代理） |
| 插件 | `BepInEx\plugins\AdoFalling.dll`（已安装） |
| 实机验证 | 插件加载 ✅、反射绑定 ✅、叠加层渲染 ✅、读谱（168 音符）✅、下落时间轴 ✅、判定钩子 ✅ |
| 待确认 | 在**真实关卡**里跟谱面的时间/手感对齐，以及打中时的判定动画（需实际演奏） |

### 验证截图

- `refs/qa_imgui.png` — 判定线 + 轨道叠加在原版标题画面上
- `refs/qa_lane_top2.png` — 轨道自屏幕顶部下延到判定线，音符自上而下下落

## 1.1 观感与性能要点

- **轨道方向**：轨道是从**屏幕顶边**垂直向下延伸到判定线的条带，音符从顶部下落穿过轨道到达判定线。
- **帧率与同步**：显示时钟**每帧直接读取游戏自己的歌曲时间**（`scrConductor.songposition_minusi`），
  `显示时间 = 歌曲时间 + 偏移`。这样显示时间与游戏**同源**，误差恒等于 0，音符不可能与音乐逐渐脱节。

  > 这一点踩过坑：早期版本为省开销只在 `PollInterval`（50ms）读一次、中间用"实测速率"外推。
  > 外推速度永远无法与歌曲完全一致，误差会持续累积 —— 现象就是**音符与谱面越对不上**（面板上
  > `step` 会小于 `dt`、`lead` 从初始值缓慢漂移）。`songposition_minusi` 本身由音频 DSP 时钟驱动、
  > 每帧都在推进，所以直接每帧读取既准确又不会退回 20Hz。读取只走反射读一个字段，开销可忽略。

- **读谱时机**：谱面在**关卡场景加载时**就已建好，所以只要 `scrLevelMaker` 存在且已有方块就开始读取
  （判据是"关卡对象存在"，不是等到 `PlayerControl`）。
- **反应时间（开场/倒计时）**：这是本轮修的核心问题。实测日志发现三件事叠加：
  原版在很多情况下**根本不进入 `Countdown` 相位**（开场跳过、自动播放、重打时是 `Start → PlayerControl`）；
  **谱面是在倒计时中途才可读的**（日志里 907 个音符在倒计时期间一次性生成）；
  而且有些谱的**第一个音符 `HitTime` 是负数**。结果就是音符在音乐开始的一瞬间同时出现在判定位上，反应时间为 0。

  现在改为**整体平移时间轴**（而不是钳制时钟）：令 `显示时钟 = 游戏时钟 + offset`，其中
  `offset = 第一个音符时间 - 一整屏下落时间 - LeadInSeconds`。
  于是**第一个音符保证从屏幕顶端开始下落**；整条时间轴刚性平移，**再长的倒计时也不会让音符越过判定线**，
  也不受"谱面何时才可读"的影响。偏移在关卡真正开始后冻结，打歌途中不会变动。
  开关：`Chart/CountdownLead`（默认 `true`）、`Chart/LeadInSeconds`（默认 `0.35`，额外余量）。

- **长按音符**：`holdLength` 是节奏单位，需要换算成秒。插件复刻了原版 `scrLevelMaker` 的算法：
  `halfTurn = scrMisc.GetTimeBetweenAngles(entryangle, exitangle, speed, bpm, isCW)`，
  长按时长 `= holdLength * 2 * halfTurn`。绘制为从判定线向后延伸的**长条尾迹**
  （长度 = 时长 × 下落速度，所以尾迹末端正好是松手时刻），透明度由 `Style/HoldBodyOpacity`（默认 `0.45`）控制。
  读谱日志会报告数量，例如 `chart read: 907 notes ... holds 16`。

- **判定动画**：挂钩游戏自身的 `scrPlayer.onHit`（`Action<scrFloor>`，游戏每次成功命中都会调用），
  打中时会做三件事：
  1. **被击中的音符立即消失**（不再继续落到判定线以下）；
  2. **所在轨道整条泛白高亮** + 亮边，靠近判定线处最亮；
  3. **判定线整体变白加粗并向外发光**，并在被打中的那条轨道上叠一道竖向光柱；
  4. 判定线上浮出判定文字（文字**最后绘制**，不会被特效遮挡）。

## 1.2 中文控制面板（F5）

在**菜单界面左上角**会显示一行提示「**按 F5 调出控制面板**」，进入谱面后自动隐藏（判断依据是是否读到谱面）。
所以不用记热键，打开游戏就能看到入口。

按 **F5** 呼出**中文控制面板**，所有可调参数都在里面，**每个参数都有滑块 + 可直接输入数值的输入框**，
改动**即时生效**，关闭面板（再按 F5）时**自动保存**到配置文件。

面板外观：**40% 不透明度的黑色填充 + 蓝色细边框**，后面的游戏画面仍可透出。

面板内容：

| 分区 | 项目 |
|---|---|
| **显示（一键切换）** | ☑ 显示判定线　☑ 显示轨道　☑ 显示音符 / 长按　☑ 显示打击特效　☑ 显示判定文字 |
| **判定线与轨道** | 判定线高度、轨道宽度、轨道间距 |
| **音符** | 下落速度、音符高度 |
| **对齐（判定与视觉）** | 音符提前量、提前量系数、开场余量、长按提前量、长按长度系数 |
| **透明度** | 判定线、音符、轨道底色、轨道边框、长按尾迹 |
| **行为开关** | 长按按住期间缩短、长按跳过松手方块、开场从顶端下落、音符倾斜、轨道右到左、显示诊断信息、自动播放 |
| **操作** | 恢复默认 / 长按归零 / 清除统计数据 |

每个开关行右侧都有 **[开]**（绿）或 **[关]**（红）的状态标记，不需要看勾选框判断。
面板底部实时显示对齐实测值（最近误差 / 平均误差 / 样本数 / 建议）与**当前键盘选中项**。

> 「显示判定线」「显示音符」两个勾选框就是**一键显隐**：把两个都取消，
> 画面就只剩原版游戏本身；勾回来即恢复叠加层。
> 面板本身**不受叠加层显隐影响**（否则隐藏后就无法调回来了）。

### 键盘操作（重要）

**本作通过 Rewired 接管输入，IMGUI 的鼠标点击会被吞掉**，所以面板**必须能用键盘操作**：

| 按键 | 作用 |
|---|---|
| **↑ / ↓** | 选择上一项 / 下一项（当前项显示在面板底部，如 `[3] 判定线高度`） |
| **← / →** | 调整当前项（按住连发；配合 Shift 加速 5 倍；开关类则切换） |
| **Enter** | 切换当前项（开关类）/ 触发当前按钮（恢复默认、长按归零、清除统计） |
| **Tab / 方向键** | 移动选择（同上 ↑↓） |

鼠标若可用也可以直接拖滑块、点输入框、点按钮；两种方式都能用。

### 谱面文件模式 —— 完全复刻 ReADOFAIMacro 的按键（`Chart/FilePath` + `F`）

这是现在的主用方式，读谱辅助**只依赖谱面文件**，不需要读取游戏当前的关卡：

1. 在配置里把 `Chart/FilePath` 填成 .adofai 的完整路径（本机已默认填好
   `tmp\OfficialCharts\CE-TX Dramatic Entrance!.adofai`）；
2. 进关卡，在**第一个音符该打的时候按 F**（`General/LoadKey`）—— 就像 ReADOFAIMacro
   在第一个轨道上按开始键那样。F 会载入/刷新谱面文件，并把"计划第一次按键"对齐到按下的瞬间；
3. 叠加层随后按文件显示整张谱面：下落时间轴和**排键**都来自文件，排键是
   **ReADOFAIMacro 原版算法的按键序列**（`PlayScript.cpp` 的完整移植：旋转角处理、
   Twirl / Pause / SetSpeed、180° 分组、轮指/硬抗分指、换手）——不是近似、不是自创规则。

细节：

- **按键 → 轨道**：主手手指 1..4 → 轨道 0..3，副手镜像（3..0）；>4 个一组的继续绕圈。
- **重开**：死了重来后再按一次 F 即可重新对齐；检测到歌曲回溯（重开）也会自动重新锚定，
  但按 F 最准。
- **离开关卡**自动结束文件模式，下次进关卡再按 F。
- **微调**：`Chart/FileOffsetSeconds`（秒，正数 = 整体更晚）用来校准手感。
- 日志里会打两行关键信息：
  `chart read (file): ... anchor ...s`（本次锚点）与
  `file chart sync check: live tile 1 at ...s, file anchor ...s (diff ...s)`——
  diff 接近 0 说明 F 按在该打第一个音符的时刻。
- `F` 在 `FilePath` 为空时 = 关闭文件模式，回到实时读谱。

### 实时模式（不填 `FilePath` 时）：`LaneMapping`

不依赖文件、直接从游戏读谱时，轨道映射可选：

- `Fingering`（默认）：内置人性化排键 —— 快速连打展开 4 轨轮指、较慢音符在中间两轨两指交替，
  永不连压同轨；`Chart/FingeringWindowMs`（默认 180ms）是轮指/交替的分界，可在 F5 面板里调；
- `Macro`：若配置了谱面文件，用文件里的宏按键计划给实时音符重排轨道（按 seqID 对齐）；
- `Position` / `Cycle` / `Sequential`：按进入方向 / 按序号循环 / 按顺序。

F5 面板 **排键 / 轨道映射 → 排键方式** 一行可用 ↑↓ 选中、←→ 或 Enter 切换；
面板里还显示谱面文件是否已载入/已开始。



| 按键 | 作用 |
|---|---|
| **F5** | 开 / 关中文控制面板（关闭时保存） |
| **F8** | 显示 / 隐藏整个叠加层 |
| **F7 / F9** | 普通音符更早 / 更晚过判定线（±0.01s，Shift = ±0.05s） |
| **`-` / `=`** | 长按长条更晚 / 更早（±0.01s，Shift = ±0.05s） |
| **`[` / `]`** | 长按长条长度缩放 |
| **Backspace** | 长按微调归零 |
| **F6** | 把当前对齐参数写进日志（排查用） |



按 **F5** 打开/关闭**对齐调参面板**，不用改配置文件、不用重启：

```
=== AdoFalling alignment (F5 to save & close) ===
judge err = 14 px   avg = 9 px   n = 23
trim  = 0.000 s     lead fraction = 0.80
display offset = -1.330 s   raw = 21.480 s
>> ALIGNED <<
F7 = earlier     F9 = later     (+Shift = x5)
END = reset trim     PgUp/PgDn = lead fraction
notes 907   holds 16   onscreen 4   judgeY 886
```

| 按键 | 作用 |
|---|---|
| **F5** | 打开 / 关闭对齐面板（关闭时**自动保存**到配置文件） |
| **F7** | 普通音符**更早**过判定线（+0.01s；按住 Shift = +0.05s） |
| **F9** | 普通音符**更晚**过判定线（−0.01s；按住 Shift = −0.05s） |
| **`-`** | **长按长条更晚**（−0.01s；Shift = −0.05s） |
| **`=`** | **长按长条更早**（+0.01s；Shift = +0.05s） |
| **Backspace** | 长按微调归零 |
| **`[` / `]`** | 长条长度缩放（±0.05，范围 0.2~3.0） |
| **END** | 普通音符 `trim` 归零 |
| **PgUp / PgDn** | 调整 `LeadFraction`（±0.05） |

> `-` / `=` / `Backspace` / `[` / `]` **不需要打开 F5 面板**也能用，改完立即写回配置文件，
> 方便一边打一边调。调整量会打到 BepInEx 日志（搜 `hold trim`）。

调参依据是面板上的 **`judge err` / `avg`**：这是插件在**每次命中瞬间实测**音符距判定线多少像素。

- `avg` **为正** = 判定时音符还没到线（音符偏晚）→ 按 **F7**；
- `avg` **为负** = 判定时音符已经过线（音符偏早）→ 按 **F9**；
- 面板会给出 `>> ALIGNED <<` 或文字提示（`avg` 在 ±6px 内视为对齐）。

调完后按 F5 关闭，值会写入 `BepInEx\config\com.dsh.adofalling.cfg` 的
`Chart/LeadOffsetSeconds`（精调）与 `Chart/LeadFraction`（粗调）。



把 `General/DebugHud` 设为 `true` 后，叠加层左上角会出现一块半透明诊断面板，**不需要翻日志文件**：

```
AdoFalling  60 FPS (dt 0.016s)
state=PlayerControl  shift=-1.780s  raw=1.234s
notes=168  holds=16  onscreen=4
first=0.000  judgeY=194  laneW=110
```

| 字段 | 含义 |
|---|---|
| `FPS (dt)` | 实际帧率与帧间隔。若 `FPS≈20`、`dt≈0.05`，说明时钟没每帧推进 |
| `time step` | **显示时钟每帧的推进量**。它应该接近 `dt`（例如 `0.0167`）。如果它明显小于 `dt` 或经常为 0，说明音符的推进被卡住 —— 这是判断"20FPS 手感"最直接的指标 |
| `state` | 游戏相位（`Start` / `Countdown` / `PlayerControl` …） |
| `shift` | 显示时钟相对游戏时钟的平移量，**负数=提前**。为 0 说明反应时间的修法没生效 |
| `raw` | 游戏自己的歌曲时间（秒） |
| `notes` / `holds` | 读到的音符数 / 其中长按数。`holds=0` 说明这一关没有长按，不是渲染问题 |
| `onscreen` | 当前在屏幕上的音符数 |
| `first` | 第一个音符的原始时间（有些谱是 0 或负数） |



游戏自带的音效**不在 Unity Resources 目录里**（它们在 `resources.assets` 的 110 个 `snd*` 资源中），
所以 `Resources.Load` 取不到。插件改为调用**游戏自己的播放接口**：

```csharp
AudioManager.Play("snd" + hitSound, AudioSettings.dspTime + 0.02, conductor.hitSoundGroup, volume);
```

这正是原版 `scrConductor` 播放命中音的方式，因此贴图/音量/混音组都和游戏一致。

| 配置 | 默认 | 说明 |
|---|---|---|
| `Feedback/LaneFlashStrength` | `0.7` | 轨道高亮强度（0 关闭，1 最亮） |
| `Feedback/JudgeFlashStrength` | `0.85` | 判定线闪光强度（0 关闭，1 最亮） |
| `Chart/SkipAutoTiles` | `true` | 不把原版**自动前进**的方块（`auto`、`midSpin`）生成为音符 |
| `Chart/MergeSameBeat` | `true` | 把**同一进入时刻**的方块合并成一个音符 |
| `Chart/SameBeatEpsilon` | `0.003` | 判定"同一拍"的时间容差（秒） |
> **本机已标定的默认值**（都写进了代码默认值，新配置自动生效）：
>
> | 参数 | 值 | 说明 |
> |---|---|---|
> | `Chart/LeadFraction` | `0` | 音符在 `entryTime` 时刻过判定线，与游戏判定同源 |
> | `Chart/LeadOffsetSeconds` | `0.07` | 普通音符精调（实测 `avg judge err ≈ -11px`） |
> | `Tuning/HoldOffsetSeconds` | `0` | 长按长条精调 |
> | `Tuning/HoldScale` | `1` | 长条长度按谱面原值 |
> | `Tuning/HoldShrinkWhileHeld` | `true` | 长条下落→按住期间收缩→松手消失 |
> | `Chart/SkipHoldReleaseTiles` | `true` | 不为长按后的松手方块重复生成音符 |
>
> 换机器 / 换显示器 / 改了 `NoteSpeed` 后，用 **F5** 面板重新标定即可。

| `Chart/CountdownLead` | `true` | 平移时间轴，让第一个音符从屏幕顶端开始下落 |
| `Chart/LeadFraction` | `0` | 提前量 = 下落时长 × 该系数。**0 = 音符正好在游戏判定的时刻过线（默认）**，1 = 提前一个整屏 |
| `Chart/LeadOffsetSeconds` | `0.07` | 手工精调（秒）。正数=音符更早过线，负数=更晚。**本机标定值**，用 F5 面板调 |
| `Chart/LeadInSeconds` | `0.35` | 在"一整屏下落"之外额外留的余量（秒），仅 `LeadFraction > 0` 时起作用 |
| `Tuning/TuneKey` | `F5` | 对齐面板开关 |
| `Tuning/TuneEarlierKey` / `TuneLaterKey` | `F7` / `F9` | 微调音符过线时刻 |
| `Style/HoldBodyOpacity` | `0.45` | 长按尾迹的透明度 |

> **对齐默认值**：`LeadFraction = 0` + `LeadOffsetSeconds = 0.07`。
> `LeadFraction = 0` 时 `显示时间 = 歌曲时间`，音符正好在 `scrFloor.entryTime` 这个时刻通过判定线 ——
> 而游戏也正是在这个时刻判定，二者同源。`0.07s` 是在**本机**用 F5 面板实测定标出来的精调量
> （`n=45` 次命中，`avg judge err ≈ -16px ≈ 26ms`，人工精度范围内）。
> 换机器、换显示器、或改了 `NoteSpeed` 后建议重新按 F5 标一次。

> **关于"原版按一下、MOD 要按两下"**：原版 `scrPlayer.Simulated_PlayerControl_Update` 里有
> `while (nextfloor.midSpin) nextfloor = nextfloor.nextfloor;` —— **midspin 方块是自动穿过的**，
> 玩家不需要为它多按一次；而 `CalculateFloorEntryTimes` 里 midspin 的角度增量 `num3 = 0`，
> 会让连续的方块**共享同一个 `entryTime`**。之前每个方块都生成一个音符，于是同一时刻出现了两个键，
> 看起来就成了"双押"。现在默认按上面两项过滤/合并。
> 每次读谱都会在日志里写出统计，例如
> `chart read: 168 notes from 184 tiles (auto -2, midspin -13, same-beat -1)`，
> 可以直接看到被过滤掉的数量。若某个谱真的需要多押，把 `MergeSameBeat` 或 `SkipAutoTiles` 关掉即可。

> **打击音效已移除**：游戏自带音效不在 Unity `Resources` 里，而单独播放会和原版命中音叠成双声，
> 所以现在完全交给游戏自己的音效，插件不再出声。

游戏目录（下称 `<GAME>`）：
`D:\steam\steamapps\common\A Dance of Fire and Ice`

---

## 2. 已安装的文件

```
<GAME>\winhttp.dll              BepInEx Doorstop 代理（原样保留）
<GAME>\version.dll              Doorstop 代理副本 ← 真正生效的那一个
<GAME>\doorstop_config.ini      Doorstop 配置
<GAME>\.doorstop_version
<GAME>\BepInEx\core\...         BepInEx 5.4.23.5 运行时
<GAME>\BepInEx\plugins\AdoFalling.dll   本 MOD
<GAME>\BepInEx\config\com.dsh.adofalling.cfg   配置文件
<GAME>\BepInEx\LogOutput.log    运行日志（排查问题先看这里）
```

> **为什么需要 `version.dll`**：Doorstop 4.5 只提供 `winhttp.dll`，而本作主程序只导入
> `UnityPlayer.dll` 与 `KERNEL32.dll`，`winhttp` 直到真正发起 HTTP 请求时才会被加载，
> 导致 Doorstop 初始化太晚/不发生（表现为 `BepInEx\` 目录完全不生成）。
> 把同一份代理复制为 `version.dll` 后，系统在加载 UnityPlayer 时即解析它，Doorstop 正常接管。
> 两个文件可以共存，`winhttp.dll` 保留即可。

---

## 3. 使用

1. 正常启动游戏（Steam 或直接运行 `<GAME>\A Dance of Fire and Ice.exe` 均可）。
2. 进入任意关卡，叠加层自动出现。
3. 热键：
   - `F8`：显示 / 隐藏叠加层
4. 配置文件：`<GAME>\BepInEx\config\com.dsh.adofalling.cfg`，改动后重启游戏生效
   （也可装 BepInEx ConfigurationManager 在游戏内改）。

### 主要配置项

| 段 | 键 | 默认 | 说明 |
|---|---|---|---|
| General | `ToggleKey` | `F8` | 显隐热键 |
| General | `LoadKey` | `F` | 载入谱面文件并开始文件模式（在关卡里第一个音符处按）；`FilePath` 为空时 = 关闭文件模式 |
| General | `PollInterval` | `0.05` | 游戏状态轮询间隔（秒） |
| General | `DebugHud` | `false` | 每 2 秒把状态/谱面信息写进日志，排查用 |
| General | `DemoMode` | `false` | 在任意界面画一条**合成**下落谱面，用来预览/调样式，不必进关卡 |
| General | `DemoBpm` | `150` | DemoMode 的节拍 |
| General | `ForceDemo` | `false` | 即使正在打歌也用合成谱面（纯调样式用） |
| Layout | `JudgeLineY` | `0.18` | 判定线高度（屏高比例，0 = 底部） |
| Layout | `NoteSpeed` | `620` | 下落速度（1080p 基准像素/秒） |
| Layout | `NoteHeight` | `26` | 按键高度（1080p 基准像素） |
| Layout | `LaneWidth` / `LaneGap` | `110` / `8` | 轨道宽度 / 间距（像素） |
| Layout | `RightToLeft` | `false` | 轨道从右往左排 |
| Layout | `LaneMapping` | `Fingering` | 轨道映射：`Fingering`（内置人性化）/ `Macro`（文件里的宏按键计划）/ `Position`（按方块进入方向）/ `Cycle`（方块序号 %4）/ `Sequential`（顺序） |
| Chart | `FilePath` | 空 | .adofai 谱面文件路径（文件模式）；本机默认填了 `tmp\OfficialCharts\CE-TX Dramatic Entrance!.adofai` |
| Chart | `FileOffsetSeconds` | `0` | 文件模式的时间微调（秒，正数 = 整体更晚） |
| Chart | `FingeringWindowMs` | `180` | `Fingering` 的轮指/交替分界（毫秒），越低越轮指 |
| Layout | `NoteRotation` | `false` | 按键朝判定线倾斜（VSRG 观感） |
| Style | `LaneOpacity` | `0.18` | 轨道底透明度（0 = 全透明） |
| Style | `LaneEdgeAlpha` | `0.7` | 轨道边框透明度（轨道"看得见"主要靠它） |
| Style | `JudgeLineOpacity` | `1` | 判定线透明度 |
| Style | `NoteOpacity` | `1` | 按键透明度 |
| Style | `LaneColor` / `LaneEdgeColor` / `JudgeLineColor` / `NoteColor` / `NoteAltColor` | — | 颜色（`RRGGBBAA`） |

> 关于「主要判定线与发射的按键和轨道不透明，其他透明」：
> 判定线、轨道边框、按键默认都是不透明（alpha = 1）；轨道底默认 `0.18` 半透明，
> 这样既能看清轨道又不会挡住原版谱面。想要轨道底也完全不透明就把 `LaneOpacity` 设为 `1`。

---

## 4. 读谱原理（为什么时间轴是准的）

全部取自游戏自身的运行时数据，不解析 `.adofai` JSON：

| 数据 | 来源（已反编译核对） |
|---|---|
| 音符时间 | `scrFloor.entryTime` —— 星球进入该方块的**绝对歌曲秒数**，已包含 BPM、`SetSpeed`、`Pause`、`Hold` 等全部事件 |
| 方块序号 | `scrFloor.seqID` |
| 进入方向 | `scrFloor.entryangle`（弧度）→ `LaneMapping=Position` 时量化到 4 轨 |
| 谱面列表 | `scrLevelMaker.instance.listFloors`（`List<scrFloor>`，`listFloors[i].seqID == i`） |
| 当前歌曲时间 | `scrConductor.instance.songposition_minusi`（双精度秒） |
| 是否在打歌 | `scrController.instance.currentState == States.PlayerControl` |
| 暂停 | `scrController.instance.paused` |

所有游戏接口都通过**缓存反射**访问，因此即使日后类型名变化，插件最多是「读不到谱面」而不会崩溃，
并且会在日志里打出实际原因。

> 注意：`currentFloorID` 不是方块序号，`currentSeqID` / `currFloor.seqID` 才是；
> `listFloors[0]` 是不会被演奏的填充方块。

---

## 5. 从源码构建

```
powershell -ExecutionPolicy Bypass -File build.ps1            # 只编译
powershell -ExecutionPolicy Bypass -File build.ps1 -Install   # 编译 + 部署到游戏
powershell -ExecutionPolicy Bypass -File build.ps1 -InstallLoader -Install   # 连 BepInEx 一起装
```

- 编译器：`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`（C# 5）
- 引用：游戏目录 `*_Data\Managed` 下的 `netstandard.dll` / `UnityEngine*.dll` / `Assembly-CSharp.dll`
  ＋ `BepInEx\core` 下的 `BepInEx.dll`、`0Harmony.dll`
- 产物：`build\AdoFalling.dll`，编译日志 `build\csc.log`

> 本机没有 .NET SDK，所以用 csc 直接编译；`build.ps1` 内部路径全部由脚本自身位置推导，
> 不会把中文路径经命令行传递（PS 5.1 会按 ANSI 误读）。

### 源码结构（`src\AdoFalling\`）

| 文件 | 职责 |
|---|---|
| `FallingPlugin.cs` | `BaseUnityPlugin` 入口：配置、每帧推进、`OnGUI` 绘制、热键 |
| `ControlPanel.cs` | **中文控制面板**（F5）：滑块 + 输入框 + 开关 + 中文界面 |
| `FallingConfig.cs` | 全部配置项 |
| `GameState.cs` | 反射读取游戏状态（歌曲时间/BPM/状态） |
| `GameClock.cs` | 显示时钟（每帧直读歌曲时间 + 平移）与判定事件钩子 |
| `ChartReader.cs` | 读谱：过滤 auto/midspin/松手方块、同拍合并、长按时长 |
| `FingeringPlanner.cs` | 人性化排键：移植 ReADOFAIMacro 的轮指/硬抗分指算法，把音符重新分配到 4 条轨道 |
| `FallingEngine.cs` | 每帧推进音符、判定特效、时间回溯（存档点）重建 |
| `FallingView.cs` | 屏幕几何：轨道位置、判定线高度 |
| `OverlayRenderer.cs` | 用 IMGUI 绘制判定线/轨道/下落按键/长条/判定特效 |
| `HitSoundPlayer.cs` | （已停用，仅保留文件） |
| `NoteInstance.cs` | 单个音符的数据 |

### 为什么用 IMGUI 而不是 UGUI Canvas

本作（Unity 6000.3.10f1）的渲染管线下，**插件自建的 UGUI Canvas 不会被合成到最终画面**：
逐帧日志证明画布状态正确（`mode=ScreenSpaceCamera`、`active=True`、四边形数量正确、
坐标与颜色正确），但截图上完全看不到；而把 `Canvas` 换成 ScreenSpaceOverlay、
或改绑到游戏相机、或自建一台 depth 最高的相机，结果都一样。

`OnGUI`（IMGUI）绘制的是**已完成帧之上**的内容，实测每次都稳定出现，因此最终采用它：
不需要 Canvas、相机、Sprite 或自定义 Shader，只用一个 1×1 白贴图 + `GUI.DrawTexture` 着色，
功能上与 UGUI 版本完全等价（`NoteRotation` 用 `GUIUtility.RotateAroundPivot` 实现倾斜）。

---

## 6. 已知限制 / 下一步

- **只做视觉**：不改判定、不加分、不接管输入。判定动画是对**游戏自身判定结果**的回显
  （`scrPlayer.onHit`），不是插件另算的一套。
- 判定文字会按 `scrFloor.grade`（`HitMargin`）显示 `PERFECT` / `EARLY` / `LATE` / `MULTI` / `AUTO`；
  读不到判定等级时显示 `HIT`。
- 长按（`holdLength`）、双押（`tapsNeeded`）、多星球（`MultiPlanet`）目前都按单键显示，还没有专属外观。
- 轨道是矩形条带；后续可加梯形/斜切（`NoteRotation` 已给按键做了倾斜）。
- 打歌输入的自动化被 Rewired 拦下，所以判定动画需要在真实关卡里人工确认一次。

---

## 7. 卸载

1. 删除 `<GAME>\BepInEx\plugins\AdoFalling.dll`（或整个 `<GAME>\BepInEx` 目录）
2. 删除 `<GAME>\version.dll`；如需完全移除加载器，再删 `winhttp.dll`、`doorstop_config.ini`、`.doorstop_version`
3. 若游戏仍能启动但 BepInEx 未生效，先确认 `version.dll` 是否还在

---

## 8. 排查

| 现象 | 处理 |
|---|---|
| 完全没有叠加层 | 看 `<GAME>\BepInEx\LogOutput.log`；没有该文件说明 Doorstop 未生效 → 确认 `version.dll` 存在且未被杀软删除 |
| 日志出现 `Assembly-CSharp is not loaded` | 插件被过早加载，反馈日志内容 |
| 日志出现 `scrLevelMaker.listFloors unreachable` | 读到 `Diagnostic` 字段，会给出具体原因 |
| 进关卡有轨道但没按键 | 把 `DebugHud` 设为 `true`，看 `notes=` 数量；若为 0，说明 `entryTime` 未读到 |
| 按键时间整体偏移 | 报出 `t=` 与 `seq=`，可按下落距离整体平移（`JudgeLineY`/`NoteSpeed` 调手感） |
