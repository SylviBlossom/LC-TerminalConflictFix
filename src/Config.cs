using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace TerminalConflictFix;

public class Config
{
	public static ConfigEntry<bool> RemoveCommandPunctuation { get; private set; }

	public Config(ConfigFile cfg)
	{
		RemoveCommandPunctuation = cfg.Bind("General", "RemoveCommandPunctuation", true, "Whether dashes and other punctuation in commands should be ignored, to fix issues with modded names. It's recommended to leave this ON.");
	}
}
