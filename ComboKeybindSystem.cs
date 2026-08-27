using System;
using System.Collections.Generic;
using System.Reflection;
using ImproveGame.UI.ModernConfig;
using ImproveGame.UI.ModernConfig.OptionElements;
using ImproveGame.UIFramework.Graphics2D;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoMod.RuntimeDetour;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;

namespace ComboKeybinds;

/// <summary>
/// 让泰拉瑞亚的键位支持组合键（如 Ctrl+R、Alt+K）。
///
/// 原理（针对 tML 1.4.4.9 / 2026.06，2026-08-27 验证）：
/// - 原版重绑流程：点击键位槽 -> PlayerInput.ListenFor(target, mode) 进入监听，
///   PlayerInput 每帧检测到按键 -> CheckRebindingProcessKeyboard(newKey) ，
///   收到第一个键就写入 KeyStatus 并 ListenFor(null) 退出监听——所以「按下即绑、只能绑一个键」。
/// - 本模组 hook 该方法：录制期间吞掉回调（不写 KeyStatus、不退出监听），把按下的键逐个采集，
///   在 ModSystem.Update 里检测「所有按键全部松开」时，把整组键提交成一个组合串（如 LeftControl+R）。
///   单键场景提交逻辑与原版完全一致（按已绑定的键=取消绑定）。
/// - 运行时判定：原版 KeyConfiguration.Processkey 是精确字符串匹配，组合串永远不会命中；
///   本模组 hook Processkey，当组合串的「主键(最后一段)」被按下且「修饰键(前面各段)」当前仍按住时，
///   把对应 action 的 KeyStatus 置为按下。对所有注册进 KeyStatus 的键位（原版 + ModKeybind）生效。
/// </summary>
public class ComboKeybindSystem : ModSystem
{
	private static readonly List<string> RecordingKeys = new();
	private static string _recordingTarget; // 本次录制对应的 action（PlayerInput.ListeningTrigger 快照）
	private static InputMode _recordingMode = InputMode.Keyboard;
	private static bool _prevFrameHadKeys; // 上一帧是否有键按住（用于「全部松开」边沿检测）

	private static readonly FieldInfo _listeningInputModeField =
		typeof(PlayerInput).GetField("_listeningInputMode", BindingFlags.NonPublic | BindingFlags.Static);

	private static FieldInfo _onBindingChangeField;
	private static string _lastFingerprint; // 上一帧按键配置指纹（变化检测用）

	// ---- 按键冲突检测 ----
	private static readonly HashSet<string> ConflictsKeyboard = new();
	private static readonly HashSet<string> ConflictsKeyboardUI = new();
	private static readonly HashSet<string> NoConflicts = new(); // 手柄项不标红
	private static readonly Dictionary<UIKeybindingListItem, Color> OriginalColors = new();
	private static readonly Color ConflictColor = new(200, 60, 60); // 淡红 #C83C3C

	private static readonly FieldInfo _keybindField =
		typeof(UIKeybindingListItem).GetField("_keybind", BindingFlags.NonPublic | BindingFlags.Instance);
	private static readonly FieldInfo _inputmodeField =
		typeof(UIKeybindingListItem).GetField("_inputmode", BindingFlags.NonPublic | BindingFlags.Instance);
	private static readonly FieldInfo _colorField =
		typeof(UIKeybindingListItem).GetField("_color", BindingFlags.NonPublic | BindingFlags.Instance);

	public override void OnModLoad()
	{
		if (Main.dedServ)
		{
			return; // 纯客户端功能
		}
		On_PlayerInput.KeyboardInput += KeyboardInput;
		On_PlayerInput.CheckRebindingProcessKeyboard += CheckRebindingProcessKeyboard;
		On_KeyConfiguration.Processkey += Processkey;
		On_UIKeybindingListItem.DrawSelf += DrawSelf;
		PlayerInput.OnBindingChange += RecomputeConflicts;
		RecomputeConflicts();
	}

	// 所有模组加载完成后（ImproveGame 是否启用此时才准确）再接入其界面
	public override void PostSetupContent()
	{
		if (ModLoader.GetMod("ImproveGame") != null)
		{
			HookImproveGame();
		}
	}

	public override void OnModUnload()
	{
		if (Main.dedServ)
		{
			return;
		}
		_improveGameHook?.Undo();
		_improveGameHook = null;
		On_PlayerInput.KeyboardInput -= KeyboardInput;
		On_PlayerInput.CheckRebindingProcessKeyboard -= CheckRebindingProcessKeyboard;
		On_KeyConfiguration.Processkey -= Processkey;
		On_UIKeybindingListItem.DrawSelf -= DrawSelf;
		PlayerInput.OnBindingChange -= RecomputeConflicts;
		OriginalColors.Clear();
		ConflictsKeyboard.Clear();
		ConflictsKeyboardUI.Clear();
	}

	// 录制状态机（挂在实际的键盘输入处理上，设置/控制界面打开时也每帧执行）：
	// 采集到键后，若「上一帧有键、本帧全部松开」→ 提交组合键
	private static bool KeyboardInput(On_PlayerInput.orig_KeyboardInput orig)
	{
		bool result = orig(); // 原版输入处理（监听循环在内部，录制键在此采集）

		if (RecordingKeys.Count > 0)
		{
			bool curDown = PlayerInput.GetPressedKeys().Count > 0;
			if (!curDown && _prevFrameHadKeys)
			{
				CommitComboBinding();
			}
			_prevFrameHadKeys = curDown;
		}
		else
		{
			_prevFrameHadKeys = false;
		}

		// 兜底：每帧检测按键配置变化，实时重算冲突（覆盖所有绑定变化来源）
		UpdateConflictDetection();

		return result;
	}

	// ---- 录制采集：吞掉原版「按下即绑定」，把按下的键逐个记下来 ----
	private static bool CheckRebindingProcessKeyboard(On_PlayerInput.orig_CheckRebindingProcessKeyboard orig, string newKey)
	{
		if (!PlayerInput.CurrentlyRebinding)
		{
			return orig(newKey);
		}

		InputMode mode = GetListeningInputMode();
		if (mode != InputMode.Keyboard && mode != InputMode.KeyboardUI)
		{
			return orig(newKey); // 手柄不接管
		}

		// 目标 action 变了 = 新一轮监听，清空上一轮残留
		string trigger = PlayerInput.ListeningTrigger;
		if (trigger != _recordingTarget)
		{
			_recordingTarget = trigger;
			RecordingKeys.Clear();
			_prevFrameHadKeys = false;
		}

		// Esc 走原版（原版会把它绑上去或取消），本次录制会话结束，清空残留
		if (newKey == "Escape")
		{
			ResetRecording();
			return orig(newKey);
		}

		// 鼠标键：组合录制中忽略（防止打断），否则走原版单键绑定
		if (newKey.StartsWith("Mouse"))
		{
			if (RecordingKeys.Count > 0)
			{
				return true;
			}
			return orig(newKey);
		}

		if (newKey == "None")
		{
			return orig(newKey);
		}

		_recordingMode = mode;
		// 仅支持 修饰键 + 主键 的组合，防止误判：
		// - 第 1 个键：可能是修饰键（等待主键）或直接是主键（单键绑定）
		// - 第 2 个键：仅当第 1 个是修饰键、且第 2 个不是修饰键时才记录（组合完成）
		// - 其余情况（已满 2 键、连续修饰键、主键后再按键）一律忽略
		if (RecordingKeys.Count == 0)
		{
			RecordingKeys.Add(newKey);
		}
		else if (RecordingKeys.Count == 1 && IsModifierKey(RecordingKeys[0]) && !IsModifierKey(newKey))
		{
			RecordingKeys.Add(newKey);
		}

		return true; // 吞掉：原版不绑定、不退出监听，等待更多键或松开
	}

	// ---- 全部松开 -> 提交组合键绑定 ----
	private static void CommitComboBinding()
	{
		bool bound = false;
		string target = _recordingTarget;
		if (target != null && RecordingKeys.Count > 0)
		{
			KeyConfiguration config = PlayerInput.CurrentProfile.InputModes[_recordingMode];
			if (config.KeyStatus.TryGetValue(target, out List<string> list))
			{
				if (RecordingKeys.Count == 1)
				{
					// 单键：与原版一致——按已绑定的键=取消，否则绑定
					string key = RecordingKeys[0];
					if (list.Contains(key))
					{
						list.Remove(key);
					}
					else
					{
						list.Clear();
						list.Add(key);
					}
				}
				else
				{
					// 组合键：把整组键拼成一个串（按键记录顺序，最后一个按下的作为主键）
					list.Clear();
					list.Add(string.Join("+", RecordingKeys));
				}
				bound = true;
			}
		}

		if (bound)
		{
			// 原版 CheckRebindingProcessKeyboard 的收尾
			PlayerInput.ListenFor(null, _recordingMode);
			Main.blockKey = RecordingKeys[^1];
			Main.blockInput = false;
			Main.ChromaPainter.CollectBoundKeys();
			SoundEngine.PlaySound(SoundID.MenuTick);
			InvokeOnBindingChange();
			RecomputeConflicts();
		}

		ResetRecording();
	}

	private static void ResetRecording()
	{
		RecordingKeys.Clear();
		_recordingTarget = null;
		_prevFrameHadKeys = false;
	}

	// ---- 运行时判定：组合键命中（主键按下且修饰键按住）----
	private static void Processkey(On_KeyConfiguration.orig_Processkey orig, KeyConfiguration self, TriggersSet set, string newKey, InputMode mode)
	{
		bool didAnyCombos = false;

		foreach (KeyValuePair<string, List<string>> kvp in self.KeyStatus)
		{
			foreach (string binding in kvp.Value)
			{
				if (string.IsNullOrEmpty(binding) || binding.IndexOf('+') < 0)
				{
					continue;
				}

				// 只认「修饰键 + 主键」两段式组合串，其余格式忽略
				string[] parts = binding.Split('+');
				if (parts.Length != 2 || !IsModifierKey(parts[0]) || parts[^1] != newKey)
				{
					continue;
				}

				if (ModifierKeysHeld(parts))
				{
					set.KeyStatus[kvp.Key] = true;
					set.LatestInputMode[kvp.Key] = mode;
					didAnyCombos = true;
				}
			}
		}

		if (didAnyCombos)
		{
			// 照抄 vanilla Processkey 的移动键判定（保持行为一致）
			if (set.Up || set.Down || set.Left || set.Right || set.HotbarPlus || set.HotbarMinus ||
				((Main.gameMenu || Main.ingameOptionsWindow) && (set.MenuUp || set.MenuDown || set.MenuLeft || set.MenuRight)))
			{
				set.UsedMovementKey = true;
			}
			// 不调用 orig：修饰键按住时，主键的普通单键绑定不触发
		}
		else
		{
			orig(self, set, newKey, mode);
		}
	}

	private static bool IsModifierKey(string key)
	{
		return key == "LeftControl" || key == "RightControl" ||
			key == "LeftAlt" || key == "RightAlt" ||
			key == "LeftShift" || key == "RightShift";
	}

	private static bool ModifierKeysHeld(string[] parts)
	{
		HashSet<string> pressed = new();
		foreach (Microsoft.Xna.Framework.Input.Keys key in PlayerInput.GetPressedKeys())
		{
			pressed.Add(key.ToString());
		}

		for (int i = 0; i < parts.Length - 1; i++)
		{
			if (!pressed.Contains(parts[i]))
			{
				return false;
			}
		}
		return true;
	}

	// 事件「OnBindingChange」只能在声明类内部调用，模组里用反射触发（字段式事件的 backing field 是 private）
	private static void InvokeOnBindingChange()
	{
		_onBindingChangeField ??= typeof(PlayerInput).GetField("OnBindingChange", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
		(_onBindingChangeField?.GetValue(null) as Action)?.Invoke();
	}

	private static InputMode GetListeningInputMode()
	{
		if (_listeningInputModeField?.GetValue(null) is InputMode mode)
		{
			return mode;
		}
		return InputMode.Keyboard;
	}

	// ==================== 按键冲突检测与标红 ====================

	// 绑定配置变化（原版或本模组改键）时重算冲突集合
	private static void RecomputeConflicts()
	{
		ConflictsKeyboard.Clear();
		ConflictsKeyboardUI.Clear();
		RecomputeConflictsFor(ConflictsKeyboard, InputMode.Keyboard);
		RecomputeConflictsFor(ConflictsKeyboardUI, InputMode.KeyboardUI);
	}

	// 每帧检测按键绑定配置是否变化（指纹比对），变化则实时重算冲突集合
	private static void UpdateConflictDetection()
	{
		string fingerprint = ComputeKeyStatusFingerprint();
		if (fingerprint != _lastFingerprint)
		{
			_lastFingerprint = fingerprint;
			RecomputeConflicts();
		}
	}

	private static string ComputeKeyStatusFingerprint()
	{
		System.Text.StringBuilder sb = new();
		foreach (InputMode mode in new[] { InputMode.Keyboard, InputMode.KeyboardUI })
		{
			foreach (KeyValuePair<string, List<string>> kvp in PlayerInput.CurrentProfile.InputModes[mode].KeyStatus)
			{
				sb.Append(kvp.Key).Append('=');
				foreach (string binding in kvp.Value)
				{
					sb.Append(binding).Append(';');
				}
				sb.Append('|');
			}
		}
		return sb.ToString();
	}

	private static void RecomputeConflictsFor(HashSet<string> conflicts, InputMode mode)
	{
		Dictionary<string, List<string>> status = PlayerInput.CurrentProfile.InputModes[mode].KeyStatus;
		Dictionary<string, List<string>> bindingToActions = new();
		foreach (KeyValuePair<string, List<string>> kvp in status)
		{
			foreach (string binding in kvp.Value)
			{
				if (string.IsNullOrEmpty(binding))
				{
					continue;
				}
				if (!bindingToActions.TryGetValue(binding, out List<string> list))
				{
					list = new List<string>();
					bindingToActions[binding] = list;
				}
				if (!list.Contains(kvp.Key))
				{
					list.Add(kvp.Key);
				}
			}
		}

		// 完全相同的绑定串被 2 个及以上功能使用 → 这些功能全部冲突
		foreach (KeyValuePair<string, List<string>> kvp in bindingToActions)
		{
			if (kvp.Value.Count >= 2)
			{
				foreach (string action in kvp.Value)
				{
					conflicts.Add(action);
				}
			}
		}
	}

	// hook 键位列表项绘制：冲突项面板变淡红 + hover 显示冲突 tooltip
	private static void DrawSelf(On_UIKeybindingListItem.orig_DrawSelf orig, UIKeybindingListItem self, SpriteBatch spriteBatch)
	{
		string keybind = (string)_keybindField.GetValue(self);
		bool isConflict = GetConflictsFor(self).Contains(keybind);
		bool showConflicts = ModContent.GetInstance<ComboKeybindConfig>()?.ShowConflictsVanilla ?? true;

		if (isConflict && showConflicts)
		{
			// 缓存原面板色；冲突项替换成淡红（解决冲突后自动恢复）
			if (!OriginalColors.ContainsKey(self))
			{
				OriginalColors[self] = (Color)_colorField.GetValue(self);
			}
			_colorField.SetValue(self, ConflictColor);
		}
		else if (OriginalColors.ContainsKey(self))
		{
			_colorField.SetValue(self, OriginalColors[self]);
		}
		orig(self, spriteBatch);

		if (isConflict && showConflicts && self.IsMouseHovering)
		{
			string conflict = BuildConflictText(keybind);
			if (conflict != null)
			{
				// 读取当前鼠标提示（其他模组的描述），追加在冲突信息上方
				string existing = GetCurrentMouseText();
				Main.instance.MouseText(string.IsNullOrEmpty(existing) ? conflict : existing + "\n\n" + conflict);
			}
		}
	}

	// 生成冲突提示文本（vanilla 与 ImproveGame 界面共用）
	private static string BuildConflictText(string keybindName)
	{
		List<string> names = new();
		foreach (string action in ConflictsKeyboard)
		{
			if (action != keybindName)
			{
				names.Add(GetActionDisplayName(action));
			}
		}
		if (names.Count == 0)
		{
			return null;
		}
		string text = "冲突：\n";
		foreach (string name in names)
		{
			text += "  " + name + "\n";
		}
		return text.TrimEnd('\n');
	}

	// ==================== ImproveGame（更好的体验）界面兼容 ====================

	private static Hook _improveGameHook;

	[JITWhenModsEnabled("ImproveGame")]
	private static void HookImproveGame()
	{
		try
		{
			Type type = typeof(ImproveGame.UI.ModernConfig.OptionElements.OptionKeybind);
			MethodInfo drawSelf = type.GetMethod("DrawSelf", BindingFlags.Public | BindingFlags.Instance);
			if (drawSelf == null)
			{
				return;
			}
			_improveGameHook = new Hook(drawSelf, new ImproveGameDrawSelf(ImproveGameDrawSelfHandler));
			_improveGameHook.Apply();
		}
		catch
		{
			// ImproveGame 结构不兼容时静默失败，不影响主功能
		}
	}

	private delegate void ImproveGameDrawSelf(Action<OptionKeybind, SpriteBatch> orig, OptionKeybind self, SpriteBatch spriteBatch);

	[JITWhenModsEnabled("ImproveGame")]
	private static void ImproveGameDrawSelfHandler(Action<OptionKeybind, SpriteBatch> orig, OptionKeybind self, SpriteBatch spriteBatch)
	{
		try
		{
			orig(self, spriteBatch);
		}
		catch
		{
			// 不影响原版绘制
		}

		if (ModContent.GetInstance<ComboKeybindConfig>() is not { ShowConflictsImproveGame: true })
		{
			return;
		}
		if (!ConflictsKeyboard.Contains(self.KeybindName))
		{
			return;
		}

		// 红点（键位项右上角）
		CalculatedStyle dims = self.GetDimensions();
		Vector2 dotPos = new(dims.X + dims.Width - 14f, dims.Y + 5f);
		SDFRectangle.NoBorder(dotPos, new Vector2(8f, 8f), new Vector4(4f), Color.Red, Main.UIScaleMatrix);

		// tooltip 追加冲突信息（hover 时，追加到 ImproveGame 自己的提示下方）
		if (self.IsMouseHovering)
		{
			string conflict = BuildConflictText(self.KeybindName);
			if (conflict != null)
			{
				string existing = GetTooltipPanelText();
				TooltipPanel.SetText(string.IsNullOrEmpty(existing) ? conflict : existing + "\n\n" + conflict);
			}
		}
	}

	// 反射读取 ImproveGame TooltipPanel 当前文本（Instance 字段是 internal）
	[JITWhenModsEnabled("ImproveGame")]
	private static string GetTooltipPanelText()
	{
		try
		{
			FieldInfo instanceField = typeof(TooltipPanel).GetField("Instance", BindingFlags.NonPublic | BindingFlags.Static);
			object instance = instanceField?.GetValue(null);
			if (instance == null)
			{
				return "";
			}
			FieldInfo textField = instance.GetType().GetField("Text", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			object textElement = textField?.GetValue(instance);
			if (textElement == null)
			{
				return "";
			}
			PropertyInfo textOrKey = textElement.GetType().GetProperty("TextOrKey", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			return textOrKey?.GetValue(textElement) as string ?? "";
		}
		catch
		{
			return "";
		}
	}

	private static HashSet<string> GetConflictsFor(UIKeybindingListItem self)
	{
		InputMode mode = (InputMode)_inputmodeField.GetValue(self);
		return mode switch
		{
			InputMode.Keyboard => ConflictsKeyboard,
			InputMode.KeyboardUI => ConflictsKeyboardUI,
			_ => NoConflicts, // 手柄项不标红
		};
	}

	// 按键功能的显示名（vanilla 用原版本地化，ModKeybind 用 DisplayName）
	// internal 字段：KeybindLoader.modKeybinds (IDictionary<string, ModKeybind>)
	private static readonly FieldInfo _modKeybindsField =
		typeof(KeybindLoader).GetField("modKeybinds", BindingFlags.NonPublic | BindingFlags.Static);

	// 读取当前鼠标提示文本：优先 tML 2026 的 _mouseTextCache.cursorText（Main.MouseText 的实际存储），
	// 其次 public 的 hoverItemName（部分旧组件使用）。取不到就返回空（无描述）。
	private static string GetCurrentMouseText()
	{
		try
		{
			FieldInfo cacheField = typeof(Main).GetField("_mouseTextCache", BindingFlags.NonPublic | BindingFlags.Static);
			object cache = cacheField?.GetValue(null);
			if (cache != null)
			{
				string cursor = cache.GetType().GetField("cursorText")?.GetValue(cache) as string;
				if (!string.IsNullOrEmpty(cursor))
				{
					return cursor;
				}
			}
		}
		catch
		{
		}
		return Main.hoverItemName ?? "";
	}

	private static string GetActionDisplayName(string action)
	{
		if (_modKeybindsField?.GetValue(null) is IDictionary<string, ModKeybind> map && map.TryGetValue(action, out ModKeybind keybind))
		{
			return keybind.DisplayName.Value;
		}

		switch (action)
		{
			default: return action;
			case "MouseLeft": return Lang.menu[162].Value;
			case "MouseRight": return Lang.menu[163].Value;
			case "MouseMiddle": return Language.GetTextValue("tModLoader.MouseMiddle");
			case "MouseXButton1": return Language.GetTextValue("tModLoader.MouseXButton1");
			case "MouseXButton2": return Language.GetTextValue("tModLoader.MouseXButton2");
			case "Up": return Lang.menu[148].Value;
			case "Down": return Lang.menu[149].Value;
			case "Left": return Lang.menu[150].Value;
			case "Right": return Lang.menu[151].Value;
			case "Jump": return Lang.menu[152].Value;
			case "Throw": return Lang.menu[153].Value;
			case "Inventory": return Lang.menu[154].Value;
			case "Grapple": return Lang.menu[155].Value;
			case "SmartSelect": return Lang.menu[160].Value;
			case "SmartCursor": return Lang.menu[161].Value;
			case "QuickMount": return Lang.menu[158].Value;
			case "QuickHeal": return Lang.menu[159].Value;
			case "QuickMana": return Lang.menu[156].Value;
			case "QuickBuff": return Lang.menu[157].Value;
			case "MapZoomIn": return Lang.menu[168].Value;
			case "MapZoomOut": return Lang.menu[169].Value;
			case "MapAlphaUp": return Lang.menu[171].Value;
			case "MapAlphaDown": return Lang.menu[170].Value;
			case "MapFull": return Lang.menu[173].Value;
			case "MapStyle": return Lang.menu[172].Value;
			case "Hotbar1": return Lang.menu[176].Value;
			case "Hotbar2": return Lang.menu[177].Value;
			case "Hotbar3": return Lang.menu[178].Value;
			case "Hotbar4": return Lang.menu[179].Value;
			case "Hotbar5": return Lang.menu[180].Value;
			case "Hotbar6": return Lang.menu[181].Value;
			case "Hotbar7": return Lang.menu[182].Value;
			case "Hotbar8": return Lang.menu[183].Value;
			case "Hotbar9": return Lang.menu[184].Value;
			case "Hotbar10": return Lang.menu[185].Value;
			case "HotbarMinus": return Lang.menu[174].Value;
			case "HotbarPlus": return Lang.menu[175].Value;
			case "DpadRadial1": return Lang.menu[186].Value;
			case "DpadRadial2": return Lang.menu[187].Value;
			case "DpadRadial3": return Lang.menu[188].Value;
			case "DpadRadial4": return Lang.menu[189].Value;
			case "RadialHotbar": return Lang.menu[190].Value;
			case "RadialQuickbar": return Lang.menu[244].Value;
			case "DpadSnap1": return Lang.menu[191].Value;
			case "DpadSnap2": return Lang.menu[192].Value;
			case "DpadSnap3": return Lang.menu[193].Value;
			case "DpadSnap4": return Lang.menu[194].Value;
			case "LockOn": return Lang.menu[231].Value;
			case "ViewZoomIn": return Language.GetTextValue("UI.ZoomIn");
			case "ViewZoomOut": return Language.GetTextValue("UI.ZoomOut");
			case "ToggleCreativeMenu": return Language.GetTextValue("UI.ToggleCreativeMenu");
			case "Loadout1": return Language.GetTextValue("UI.Loadout1");
			case "Loadout2": return Language.GetTextValue("UI.Loadout2");
			case "Loadout3": return Language.GetTextValue("UI.Loadout3");
			case "ToggleCameraMode": return Language.GetTextValue("UI.ToggleCameraMode");
		}
	}
}
