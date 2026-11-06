using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Input;
using Game.Modding;
using Game.SceneFlow;
using ProceduralInterchanges.Systems;
using Unity.Entities;

namespace ProceduralInterchanges
{
    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger(nameof(ProceduralInterchanges)).SetShowsErrorsInUI(false);

        private Setting m_Setting;
        public static Setting InstanceSetting { get; private set; }
        public static ProxyAction m_ToggleToolAction;
        public static ProxyAction m_UndoAction;

        public const string kToggleToolActionName = "ToggleToolBinding";
        public const string kUndoActionName = "UndoLastInterchangeBinding";

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                log.Info($"Current mod asset at {asset.path}");

            m_Setting = new Setting(this);
            InstanceSetting = m_Setting;
            m_Setting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(m_Setting));

            m_Setting.RegisterKeyBindings();

            m_ToggleToolAction = m_Setting.GetAction(kToggleToolActionName);
            m_UndoAction = m_Setting.GetAction(kUndoActionName);

            if (m_ToggleToolAction != null)
            {
                m_ToggleToolAction.shouldBeEnabled = true;
            }

            if (m_UndoAction != null)
            {
                m_UndoAction.shouldBeEnabled = true;
            }

            AssetDatabase.global.LoadSettings(nameof(ProceduralInterchanges), m_Setting, new Setting(this));

            // Imperative System Registration Order (Section 1.2 of Blueprint)
            updateSystem.UpdateAt<HighwayPrefabCatalogSystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<InterchangeGroupSystem>(SystemUpdatePhase.ModificationEnd);
            updateSystem.UpdateBefore<InterchangeBuildService, InterchangeBuildToolSystem>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAt<InterchangeBuildToolSystem>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAt<InterchangeToolSystem>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAt<InterchangeTooltipSystem>(SystemUpdatePhase.UITooltip);
            updateSystem.UpdateAt<InterchangeUISystem>(SystemUpdatePhase.UIUpdate);
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));
            InstanceSetting = null;
            if (m_Setting != null)
            {
                m_Setting.UnregisterInOptionsUI();
                m_Setting = null;
            }
        }
    }
}
