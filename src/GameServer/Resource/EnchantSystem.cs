using System;
using System.Collections.Generic;
using System.Linq;
namespace NeoNetsphere.Resource
{
    public class EnchantSys
    {
        public byte Level { get; set; }
        public List<EnchantGroup> EnchantGroup { get; set; }

        public EnchantSys()
        {
        }



    }

    public class EnchantSystem
    {
        public uint Effect { get; set; }

        public byte Chance { get; set; }

        public EnchantSystem()
        {
        }
    }

    public class EnchantGroup
    {
        public Random Random;

        public ItemCategory Category { get; set; }
        public byte SubCategory { get; set; }

        public List<EnchantSystem> EnchantSystem { get; set; }

        public EnchantSystem Eff()
        {
            List<EnchantSystem> source;
            do
            {
                source = (from i in EnchantSystem
                          where i.Chance >= Random.Next(101)
                          select i).ToList();
            }
            while (!source.Any());
            return source.FirstOrDefault();
        }

        public EnchantGroup()
        {
            Random = new Random();
        }
    }



}
