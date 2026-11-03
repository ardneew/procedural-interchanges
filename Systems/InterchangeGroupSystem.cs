using System.Collections.Generic;
using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using ProceduralInterchanges.Core.Models;
using Unity.Entities;
using Unity.Mathematics;

namespace ProceduralInterchanges.Systems
{
    public partial class InterchangeGroupSystem : GameSystemBase
    {
        private readonly List<InterchangeReceipt> m_Receipts = new List<InterchangeReceipt>();
        private int m_NextGroupId = 1;
        private InterchangeBuildService m_BuildService;

        public bool CanUndo => m_Receipts.Count > 0;

        public void RegisterReceipt(InterchangeReceipt receipt)
        {
            if (receipt == null) return;
            receipt.GroupId = m_NextGroupId++;
            m_Receipts.Add(receipt);
        }

        public InterchangeReceipt GetLastReceipt()
        {
            if (m_Receipts.Count == 0) return null;
            return m_Receipts[m_Receipts.Count - 1];
        }

        public bool UndoLastReceipt(out InterchangeReceipt undoneReceipt)
        {
            undoneReceipt = null;
            if (m_Receipts.Count == 0) return false;

            undoneReceipt = m_Receipts[m_Receipts.Count - 1];
            if (m_BuildService != null && m_BuildService.IsBuilding)
            {
                return false;
            }

            if (m_BuildService != null)
            {
                bool started = m_BuildService.StartUndoRestore(undoneReceipt);
                if (!started) return false;
            }

            m_Receipts.RemoveAt(m_Receipts.Count - 1);
            return true;
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_BuildService = World.GetOrCreateSystemManaged<InterchangeBuildService>();
        }

        protected override void OnUpdate()
        {
        }
    }
}
