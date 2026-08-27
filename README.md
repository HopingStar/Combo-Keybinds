# Combo Keybinds

让泰拉瑞亚的键位支持**组合键**（如 `Ctrl+R`、`Alt+K`），模组一多、按键不够用时的救星。

对**原版键位**和**所有模组的键位**（ModKeybind）都生效，支持 tModLoader 2026.06 / Terraria 1.4.4.9。

## 下载

从 [GitHub Releases](https://github.com/HopingStar/Combo-Keybinds/releases) 下载最新版 `.tmod`。

安装：把 `.tmod` 放入 模组文件夹（`文档\My Games\Terraria\tModLoader\Mods`），在游戏内 模组管理 中启用即可。

## 功能特性

- 🎹 **录制组合键**：在 设置→控制 里点某个键位槽，先按住 `Ctrl`（或 `Alt`），再按一个普通键，**全部松开**即可绑成 `Ctrl+R` 这样的组合键
- ⚡ **触发语义清晰**：主键按下的瞬间修饰键仍按住才触发；只按主键不误触
- 🔧 **单键不改变**：只按一个键重绑则与原版完全一致
- 🚦 **冲突检测**（可选）：
  - 原版键位界面：绑定相同的键位标红，悬浮显示冲突详情
  - ImproveGame（更好的体验）键位界面：显示红点提示
- 🧩 **零配置可用**：默认开启，所有功能开箱即用

## 快速上手

### 录制组合键

1. 打开 设置 → 控制
2. 点击要修改的键位槽（原版键位或任意模组键位均可）
3. **先按住修饰键**（`Ctrl` 或 `Alt`），**再按下主键**（如 `R`）
4. 全部松开 → 槽位显示为 `Ctrl+R`

### 触发

按住 `Ctrl` 再按 `R` → 触发对应功能；只按 `R` → 不触发。

### 解除 / 重绑

再次点击该键位，**只按一个键**重绑即可覆盖原组合键（与原版行为一致）。

## 冲突检测

- 相同绑定字符串（如两个键位都绑成 `LeftControl+R`）会被视为冲突，标红显示
- 鼠标悬浮冲突键位时，在描述下方换行显示冲突详情（每项一行）
- 在 配置 → Combo Keybinds 中可分别开关「原版界面」与「ImproveGame 界面」的冲突显示
- 未安装 ImproveGame 时，其相关配置项自动隐藏，模组不受影响

## 配置

设置 → 模组配置 → Combo Keybinds：

| 配置项 | 说明 |
|---|---|
| 在控件中显示冲突按键 | 在游戏自带按键设置界面中把冲突键位标红 |
| 在"更好的体验"控件中显示冲突按键 | 在 ImproveGame 按键界面中显示冲突红点 |

## 兼容性

- tModLoader 2026.06 / Terraria 1.4.4.9
- 核心功能（录制、触发、冲突检测）已在游戏内实测通过

## 构建（开发者）

```bash
dotnet build "H:\WorkSpace\mod_build\ComboKeybinds\ComboKeybinds.csproj"
```

产物 `.tmod` 自动输出到 `D:\Documents\My Games\Terraria\tModLoader\Mods\`。构建前请先关闭 tModLoader。

> 工程依赖 `tModLoader.targets` 与 `ImproveGame.dll`（ModAssemblies），路径在 `ComboKeybinds.csproj` 中已配置为绝对路径，可按需修改。

## 目录结构

```
ComboKeybinds/
├── ComboKeybinds.cs           # Mod 主类
├── ComboKeybindSystem.cs      # 核心逻辑：录制状态机 + 运行时判定 + 冲突检测
├── ComboKeybindConfig.cs      # ModConfig（2 个开关项）
├── Localization/
│   ├── zh-Hans.hjson          # 中文文本
│   └── en-US.hjson            # 英文文本
├── build.txt                  # 模组元信息
├── description.txt            # 模组简介
└── icon.png                   # 模组图标（80×80）
```

## 技术实现

- **录制状态机**：检测到按键进入录制态，采集 `修饰键 + 主键`，全部松开后一次性提交绑定
- **运行时判定**：重写 `KeyConfiguration.Processkey`，识别 `+` 分隔的组合串，主键触发时校验修饰键状态
- **冲突检测**：逐帧比对键位配置指纹，实时更新标红与悬浮提示（无需重启）

## 作者

**HopingStar** — [GitHub](https://github.com/HopingStar)

## 许可证

[MIT](LICENSE) © 2026 HopingStar
