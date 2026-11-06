using System.Collections.Generic;
using ProceduralInterchanges.Core.Models;

namespace ProceduralInterchanges.Core.Generators
{
    public static class InterchangeGeneratorRegistry
    {
        private static readonly Dictionary<InterchangeArchetype, IInterchangeGenerator> s_Generators = new Dictionary<InterchangeArchetype, IInterchangeGenerator>();
        private static readonly List<IInterchangeGenerator> s_AllGenerators = new List<IInterchangeGenerator>();

        static InterchangeGeneratorRegistry()
        {
            Register(new TrumpetGenerator());
            Register(new DirectionalTGenerator());
            Register(new YStackGenerator());
            Register(new DiamondGenerator());
            Register(new ParcloA4Generator());
            Register(new CloverleafGenerator());
            Register(new FourLevelStackGenerator());
            Register(new TurbineGenerator());
        }

        private static void Register(IInterchangeGenerator gen)
        {
            s_Generators[gen.Archetype] = gen;
            s_AllGenerators.Add(gen);
        }

        public static IReadOnlyList<IInterchangeGenerator> All => s_AllGenerators;

        public static IReadOnlyList<IInterchangeGenerator> GetAllGenerators()
        {
            return s_AllGenerators;
        }

        public static IInterchangeGenerator GetGenerator(InterchangeArchetype archetype)
        {
            if (s_Generators.TryGetValue(archetype, out var gen))
            {
                return gen;
            }
            return s_Generators[InterchangeArchetype.Diamond];
        }

        public static List<IInterchangeGenerator> GetSupportedGenerators(IReadOnlyList<HighwayLeg> legs)
        {
            var list = new List<IInterchangeGenerator>();
            if (legs == null) return list;

            int count = legs.Count;
            for (int i = 0; i < s_AllGenerators.Count; i++)
            {
                var gen = s_AllGenerators[i];
                if (count >= gen.MinLegs && count <= gen.MaxLegs)
                {
                    if (gen.SupportsConfiguration(legs, out _))
                    {
                        list.Add(gen);
                    }
                }
            }

            return list;
        }
    }
}
