using System.Collections.Generic;
using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Input;
using Game.Modding;
using Game.Settings;
using Game.UI;
using Game.UI.Widgets;

namespace ProceduralInterchanges
{
    [FileLocation(nameof(ProceduralInterchanges))]
    [SettingsUIGroupOrder(kGeneralGroup, kDefaultsGroup, kKeybindingGroup)]
    [SettingsUIShowGroupName(kGeneralGroup, kDefaultsGroup, kKeybindingGroup)]
    [SettingsUIKeyboardAction(Mod.kToggleToolActionName, ActionType.Button, usages: new string[] { Usages.kDefaultUsage }, interactions: new string[] { "UIButton" })]
    [SettingsUIKeyboardAction(Mod.kUndoActionName, ActionType.Button, usages: new string[] { Usages.kDefaultUsage }, interactions: new string[] { "UIButton" })]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";
        public const string kGeneralGroup = "General";
        public const string kDefaultsGroup = "Defaults";
        public const string kKeybindingGroup = "KeyBinding";

        public Setting(IMod mod) : base(mod)
        {
        }

        [SettingsUISection(kSection, kGeneralGroup)]
        public bool EnableLiveGhostPreview { get; set; } = true;

        [SettingsUISection(kSection, kGeneralGroup)]
        public bool EnableEngineeringOverlay { get; set; } = true;

        [SettingsUISlider(min = 80f, max = 350f, step = 5f, scalarMultiplier = 1f, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(kSection, kDefaultsGroup)]
        public float DefaultFootprintRadius { get; set; } = 180f;

        [SettingsUISlider(min = 2.0f, max = 8.0f, step = 0.5f, scalarMultiplier = 1f, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(kSection, kDefaultsGroup)]
        public float DefaultMaxMainlineGrade { get; set; } = 4.0f;

        [SettingsUISlider(min = 3.0f, max = 10.0f, step = 0.5f, scalarMultiplier = 1f, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(kSection, kDefaultsGroup)]
        public float DefaultMaxRampGrade { get; set; } = 6.0f;

        [SettingsUISlider(min = 8.0f, max = 15.0f, step = 0.5f, scalarMultiplier = 1f, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(kSection, kDefaultsGroup)]
        public float DefaultBridgeClearance { get; set; } = 9.5f;

        [SettingsUIKeyboardBinding(BindingKeyboard.I, Mod.kToggleToolActionName, ctrl: true, shift: true)]
        [SettingsUISection(kSection, kKeybindingGroup)]
        public ProxyBinding ToggleToolBinding { get; set; }

        [SettingsUIKeyboardBinding(BindingKeyboard.Z, Mod.kUndoActionName, ctrl: true)]
        [SettingsUISection(kSection, kKeybindingGroup)]
        public ProxyBinding UndoLastInterchangeBinding { get; set; }

        public override void SetDefaults()
        {
            EnableLiveGhostPreview = true;
            EnableEngineeringOverlay = true;
            DefaultFootprintRadius = 180f;
            DefaultMaxMainlineGrade = 4.0f;
            DefaultMaxRampGrade = 6.0f;
            DefaultBridgeClearance = 9.5f;
        }
    }

    public class LocaleEN : IDictionarySource
    {
        private readonly Setting m_Setting;

        public LocaleEN(Setting setting)
        {
            m_Setting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Procedural Interchanges" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnableLiveGhostPreview)), "Live 3D Ghost Previews" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnableLiveGhostPreview)), "Renders live 3D highway bridge and road preview models in the world while adjusting parameters." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnableEngineeringOverlay)), "3D Engineering Overlay" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnableEngineeringOverlay)), "Shows vector CAD curves, grade-separated clearance indicators, and AASHTO compliance color grading." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.DefaultFootprintRadius)), "Default Footprint Radius (m)" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.DefaultFootprintRadius)), "Standard circular interchange footprint radius in meters (80m to 350m)." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.DefaultMaxMainlineGrade)), "Max Mainline Slope (%)" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.DefaultMaxMainlineGrade)), "Maximum vertical slope gradient for continuous through-highways (AASHTO standard: 4%)." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.DefaultMaxRampGrade)), "Max Ramp Slope (%)" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.DefaultMaxRampGrade)), "Maximum vertical slope gradient for connecting and loop ramps (AASHTO standard: 6%)." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.DefaultBridgeClearance)), "Min Bridge Clearance (m)" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.DefaultBridgeClearance)), "Minimum vertical clearance between crossing bridge decks and lower carriageways (min 8.0m in-game elevated limit, default 9.5m)." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ToggleToolBinding)), "Toggle Interchange Tool" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ToggleToolBinding)), "Keyboard shortcut to activate or dismiss the Procedural Interchanges studio tool." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.UndoLastInterchangeBinding)), "Undo Last Built Interchange" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.UndoLastInterchangeBinding)), "Demolishes the last generated interchange and restores the original pre-existing highways." },
                { m_Setting.GetOptionGroupLocaleID(Setting.kGeneralGroup), "General Settings" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kDefaultsGroup), "Engineering Defaults" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kKeybindingGroup), "Key Bindings" }
            };
        }

        public void Unload()
        {
        }
    }
}
