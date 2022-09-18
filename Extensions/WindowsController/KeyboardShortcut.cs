using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.Controllers;
using System;

namespace ColorChord.NET.Extensions.WindowsController
{
    [ThreadedInstance]
    public class KeyboardShortcut : Controller
    {
        private Dictionary<string, ISetting> Targets = new();


        public KeyboardShortcut(string name, Dictionary<string, object> config, IControllerInterface controllerInterface) : base(name, config, controllerInterface)
        {
            const string SHORTCUTS = "Shortcuts";
            const string KEY_COMBO = "KeyCombo";
            const string NO_REPEAT = "NoRepeat";
            // TODO: configurer?
            if (config.ContainsKey(SHORTCUTS))
            {
                Dictionary<string, object>[] ShortcutList = (Dictionary<string, object>[])config[SHORTCUTS];
                foreach (Dictionary<string, object> ShortcutConfig in ShortcutList)
                {
                    if (!ShortcutConfig.ContainsKey(KEY_COMBO)) { Log.Warn($"Shortcut list entry did not have a \"{KEY_COMBO}\" set. Please check your config."); continue; }
                    if (!ShortcutConfig.ContainsKey(ConfigNames.NAME)) { Log.Warn($"Shortcut list entry did not have a \"{ConfigNames.NAME}\" set. Please check your config."); continue; }
                    if (!ShortcutConfig.ContainsKey(ConfigNames.TARGET)) { Log.Warn($"Shortcut list entry did not have a \"{ConfigNames.TARGET}\" set. Please check you config."); continue; }

                    string ShortcutName = (string)ShortcutConfig[ConfigNames.NAME];
                    string KeyCombo = (string)ShortcutConfig[KEY_COMBO];
                    (Win32.KeyModifiers Modifiers, Win32.Keycode Keycode, bool ShortcutValid) = ParseCombo(KeyCombo);
                    if (!ShortcutValid) { continue; }
                    if (ShortcutConfig.ContainsKey(NO_REPEAT) && (bool)ShortcutConfig[NO_REPEAT]) { Modifiers |= Win32.KeyModifiers.NoRepeat; }

                    string TargetName = (string)ShortcutConfig[ConfigNames.TARGET];
                    ISetting? TargetSetting = this.Interface.FindSetting(TargetName);
                    if (TargetSetting == null) { Log.Warn($"Could not find target setting \"{TargetName}\". Please check your config."); continue; }
                    this.Targets.Add(ShortcutName, TargetSetting);

                    Extension.WindowsInterface!.AddShortcut(ShortcutName, Modifiers, Keycode);
                }
            }
        }

        private (Win32.KeyModifiers, Win32.Keycode, bool) ParseCombo(string keyCombo)
        {
            bool Valid = true;
            string[] Parts = keyCombo.Split('+');
            Win32.KeyModifiers Modifiers = Win32.KeyModifiers.None;
            Win32.Keycode Key = Win32.Keycode.A;

            for (int i = 0; i < Parts.Length; i++)
            {
                string PartClean = Parts[i].Trim();
                if (i != Parts.Length - 1) // Not the last part -> modifier keys
                {
                    if (Enum.TryParse(PartClean, true, out Win32.KeyModifiers ThisModifier)) { Modifiers |= ThisModifier; }
                    else { Log.Warn($"Unknown modifier was found in a key combo definition: \"{PartClean}\" (Found while parsing \"{keyCombo}\")"); Valid = false; }
                }
                else // Last part -> actual key
                {
                    if (!Enum.TryParse(PartClean, true, out Key)) { Log.Warn($"Unknown key was found in a key combo definition: \"{PartClean}\" (Found while parsing \"{keyCombo}\")"); Valid = false; }
                }
            }

            return (Modifiers, Key, Valid);
        }

        public override void Start()
        {
            Extension.WindowsInterface!.SetCallback(HandleShortcut);
            Extension.WindowsInterface!.Start();
        }

        internal void HandleShortcut(string name, Win32.KeyModifiers modifiers, Win32.Keycode key)
        {
            if (this.Targets.TryGetValue(name, out ISetting? Setting))
            {
                this.Interface.ToggleSettingValue(Setting);
            }
        }

        public override void Stop() => Extension.WindowsInterface?.Stop();
    }
}
