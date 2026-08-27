# Combo Keybinds — 推广文案（可直接复制）

> 使用说明：下面两份文案按渠道选用。发帖前建议先准备好一段 20~30 秒的演示 GIF/视频（录制组合键 + 冲突标红），带图的帖子点击率高得多。

---

## 一、英文版（Reddit r/Terraria / r/tModLoader、tML Discord #mod-releases、Terraria 官方论坛、Steam 社区）

**标题（三选一）**
- Terraria keybinds finally support combos (Ctrl+R, Alt+K) — free mod
- Running out of keybind slots? Bind combos now (Ctrl+R) with this mod
- tML still has no combo keys — this free mod adds them (Ctrl+R, Alt+K)

**正文**

> Running out of keybind slots with too many mods?
>
> I made a tModLoader mod that lets **any keybind** (vanilla or from other mods) be bound as a **combo**: hold a modifier (Ctrl/Alt), press a main key, release — done. Ctrl+R just works, plain R won't trigger.
>
> What it does:
> - Record combos right in the vanilla controls menu — no extra UI
> - Works for vanilla keybinds AND every mod's keybinds
> - Press only one key = identical to vanilla behavior
> - Optional conflict detection: conflicting binds turn red + hover tooltip shows details
> - Also highlights conflicts in ImproveGame's keybind screen
>
> Why this exists: tML still has no built-in combo keys (issue #5013 is still open). The only other attempt (ControllerConfigurator) records wrong — it commits on the first key press, so you can never record a two-key combo. This mod waits until you release everything, so combos actually work.
>
> **Download:** https://github.com/HopingStar/Combo-Keybinds/releases
> **Source:** https://github.com/HopingStar/Combo-Keybinds
>
> Install: drop the `.tmod` into `Documents\My Games\Terraria\tModLoader\Mods`, enable in-game.
>
> Free, MIT-licensed, for tML 2026.06 / Terraria 1.4.4.9.

---

## 二、中文版（泰拉瑞亚贴吧、B站专栏、QQ 群）

**标题（二选一）**
- 自制模组：泰拉瑞亚键位终于支持组合键了（Ctrl+R / Alt+K）！
- 模组太多键位不够？这个模组让你绑定 Ctrl+R 这样的组合键

**正文**

> 模组一多，键位就不够用了？我做了个 tModLoader 模组，让**所有键位**（原版 + 其它模组的键位）都能绑成组合键：
>
> - 在游戏自带的 设置→控制 里直接录制：先按住 Ctrl（或 Alt），再按一个键，全部松开即绑定成功
> - 触发规则：修饰键 + 主键同时按才生效，只按主键不会误触
> - 只按一个键 = 和原版行为完全一样，不改变原有习惯
> - 可选冲突检测：绑定相同的键位标红 + 悬浮显示冲突详情
> - 兼容 ImproveGame（更好的体验）的按键界面，冲突显示红点
>
> 背景：tML 官方至今没有组合键（issue #5013 还开着），之前唯一的 ControllerConfigurator 录制逻辑有 bug——按下第一个键就提交绑定，永远绑不了组合键。这个模组修好了这个逻辑，真正能录出 Ctrl+R。
>
> **下载：** https://github.com/HopingStar/Combo-Keybinds/releases
> （把 .tmod 放进 `文档\My Games\Terraria\tModLoader\Mods`，游戏内启用即可）
>
> 开源于：https://github.com/HopingStar/Combo-Keybinds
> MIT 协议 · 免费 · 支持 tML 2026.06 / Terraria 1.4.4.9

---

## 各渠道注意事项

| 渠道 | 注意事项 |
|---|---|
| **r/Terraria** | 成员多、流量大，但**严格禁自推**（self-promotion）。发帖前先回复别人的帖子混脸熟，或把帖子发成「分享工具」性质而非纯广告 |
| **r/tModLoader** | 模组开发/发布氛围更宽松，直发即可 |
| **tML Discord** | 加服务器后进 `#mod-releases` 频道，看置顶公告的格式要求 |
| **Terraria 官方论坛** | 注册后发到 Mod 发布版块 |
| **Steam 社区** | tML 讨论区直发 |
| **贴吧** | 中文玩家聚集地，直接发，带演示图更好 |
| **B站** | 录一段 30 秒演示视频（Win+G 可录屏），标题带「泰拉瑞亚 组合键 模组」关键词 |

## 演示素材建议（最重要的转化点）

录一段 **20~30 秒** 演示：
1. 打开 设置→控制 → 点某个键位 → 按住 Ctrl+R → 松开显示绑定
2. 游戏里按 Ctrl+R 触发功能；只按 R 不触发
3. 展示冲突标红（两个键位绑一样的键）

Win+G（Xbox Game Bar）或 OBS 录屏 → 转 GIF 用在线工具（如 ezgif.com）。
