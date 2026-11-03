using Game.Tools;
using Game.UI.Localization;
using Game.UI.Tooltip;
using ProceduralInterchanges.Core.Models;
using Unity.Entities;

namespace ProceduralInterchanges.Systems
{
    public partial class InterchangeTooltipSystem : TooltipSystemBase
    {
        private ToolSystem m_ToolSystem;
        private InterchangeToolSystem m_InterchangeTool;
        private StringTooltip m_Tooltip;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_InterchangeTool = World.GetOrCreateSystemManaged<InterchangeToolSystem>();
            m_Tooltip = new StringTooltip();
        }

        protected override void OnUpdate()
        {
            if (m_ToolSystem.activeTool != m_InterchangeTool)
            {
                return;
            }

            string text = m_InterchangeTool.Stage switch
            {
                ToolStage.PickCenter => "Click on terrain or highway to set interchange center point.",
                ToolStage.SelectHighways => $"Click highway edge to pair legs ({m_InterchangeTool.CurrentLegs.Count} selected). Shift+Click for single. Press Enter when done.",
                ToolStage.ConfigureInterchange => "Adjust parameters in Studio Panel, or press Enter / click Build to construct.",
                ToolStage.Building => "Constructing interchange networks...",
                _ => string.Empty
            };

            if (!string.IsNullOrEmpty(text))
            {
                m_Tooltip.value = LocalizedString.Value(text);
                AddMouseTooltip(m_Tooltip);
            }
        }
    }
}
