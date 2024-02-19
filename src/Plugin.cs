﻿using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using System;

namespace TerminalConflictFix;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
	public static Plugin Instance { get; internal set; }
	public static new ManualLogSource Logger { get; internal set; }

	private void Awake()
	{
		Instance = this;
		Logger = base.Logger;

		Harmony.CreateAndPatchAll(typeof(Plugin), PluginInfo.PLUGIN_GUID);

		// Plugin startup logic
		Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
	}

	private static TerminalNode longestMatch;
	private static int matchLength;

	[HarmonyILManipulator]
	[HarmonyPatch(typeof(Terminal), "ParseWord")]
	private static void Terminal_ParseWord(ILContext il)
	{
		var cursor = new ILCursor(il);

		var keywordLoc = 0;

		if (!cursor.TryGotoNext(MoveType.After,
				instr1 => instr1.MatchLdnull(),
				instr2 => instr2.MatchStloc(out keywordLoc)))
		{
			Logger.LogError("Failed IL hook for Terminal.ParseWord @ After keyword local");
			return;
		}

		cursor.EmitDelegate(() =>
		{
			matchLength = 0;
		});

		var continueLabel = default(ILLabel);

		if (!cursor.TryGotoNext(MoveType.Before, instr => instr.MatchLdloc(keywordLoc)) ||
			!cursor.TryGotoNext(MoveType.Before, instr => instr.MatchBrfalse(out continueLabel)))
		{
			Logger.LogError("Failed IL hook for Terminal.ParseWord @ Keyword null check");
			return;
		}

		cursor.Emit(OpCodes.Pop);
		cursor.Emit(OpCodes.Ldc_I4_1);

		var jLoc = 2;

		if (!cursor.TryGotoNext(MoveType.After,
				instr1 => instr1.MatchCallOrCallvirt<string>("get_Length"),
				instr2 => instr2.MatchStloc(out jLoc),
				instr3 => instr3.MatchBr(out _)))
		{
			Logger.LogError("Failed IL hook for Terminal.ParseWord @ Substring loop");
			return;
		}

		cursor.MoveAfterLabels();
		cursor.Emit(OpCodes.Ldloc, jLoc);
		cursor.EmitDelegate<Func<int, bool>>(j =>
		{
			return j <= matchLength;
		});
		cursor.Emit(OpCodes.Brtrue_S, continueLabel);

		if (!cursor.TryGotoNext(MoveType.After, instr => instr.MatchStloc(keywordLoc)))
		{
			Logger.LogError("Failed IL hook for Terminal.ParseWord @ Set matched keyword");
			return;
		}

		cursor.Emit(OpCodes.Ldloc, jLoc);
		cursor.Emit(OpCodes.Ldloc, keywordLoc);
		cursor.EmitDelegate<Action<int, TerminalKeyword>>((j, keyword) =>
		{
			matchLength = j;
			Logger.LogInfo($"Parsed \"{keyword.word}\" with {j} letters");
		});
	}

	[HarmonyILManipulator]
	[HarmonyPatch(typeof(Terminal), "ParseWordOverrideOptions")]
	private static void Terminal_ParseWordOverrideOptions(ILContext il)
	{
		var cursor = new ILCursor(il);

		cursor.EmitDelegate(() =>
		{
			longestMatch = null;
			matchLength = 0;
		});

		var iLoc = 0;
		var jLoc = -1;
		var loopStart = default(ILLabel);

		if (!cursor.TryGotoNext(MoveType.After,
				instr1 => instr1.MatchCallOrCallvirt<string>("get_Length"),
				instr2 => instr2.MatchStloc(out jLoc),
				instr3 => instr3.MatchBr(out loopStart)))
		{
			Logger.LogError("Failed IL hook for Terminal.ParseWordOverrideOptions @ Substring loop");
			return;
		}

		var loopContinue = cursor.DefineLabel();
		var loopEnd = cursor.DefineLabel();

		cursor.MoveAfterLabels();
		cursor.Emit(OpCodes.Ldloc, jLoc);
		cursor.EmitDelegate<Func<int, bool>>(j =>
		{
			return j <= matchLength;
		});
		cursor.Emit(OpCodes.Brtrue_S, loopEnd);

		if (!cursor.TryGotoNext(MoveType.After, instr => instr.MatchLdfld<CompatibleNoun>("result")))
		{
			Logger.LogError("Failed IL hook for Terminal.ParseWordOverrideOptions @ Return result");
			return;
		}

		cursor.Emit(OpCodes.Ldloc, jLoc);
		cursor.Emit(OpCodes.Ldloc, iLoc);
		cursor.Emit(OpCodes.Ldarg_2);
		cursor.EmitDelegate<Action<TerminalNode, int, int, CompatibleNoun[]>>((result, j, i, options) =>
		{
			longestMatch = result;
			matchLength = j;
			Logger.LogInfo($"Parsed \"{options[i].noun.word}\" with {j} letters");
		});
		cursor.Emit(OpCodes.Br_S, loopContinue);
		cursor.Emit(OpCodes.Ldnull);

		cursor.Index++;
		cursor.MarkLabel(loopContinue);

		cursor.GotoLabel(loopStart);

		if (!cursor.TryGotoNext(MoveType.After, instr => instr.MatchBgt(out _)))
		{
			Logger.LogError("Failed IL hook for Terminal.ParseWordOverrideOptions @ Loop end");
			return;
		}

		cursor.MarkLabel(loopEnd);

		if (!cursor.TryGotoNext(MoveType.After, instr => instr.MatchLdnull()))
		{
			Logger.LogError("Failed IL hook for Terminal.ParseWordOverrideOptions @ Final return");
			return;
		}

		cursor.Emit(OpCodes.Pop);
		cursor.EmitDelegate(() =>
		{
			return longestMatch;
		});
	}
}