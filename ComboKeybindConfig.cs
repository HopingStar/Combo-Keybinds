using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace ComboKeybinds;

/// <summary>
/// 组合键位配置（单个界面，两个开关）。
/// 标签/提示使用自动本地化键：Mods.ComboKeybinds.Configs.ComboKeybindConfig.*
/// </summary>
public class ComboKeybindConfig : ModConfig
{
	public override ConfigScope Mode => ConfigScope.ClientSide;

	/// <summary>在游戏自带的按键设置界面中标红冲突按键。</summary>
	public bool ShowConflictsVanilla { get; set; } = true;

	/// <summary>在 ImproveGame（更好的体验）的按键设置界面中显示冲突红点。</summary>
	public bool ShowConflictsImproveGame { get; set; } = true;
}
