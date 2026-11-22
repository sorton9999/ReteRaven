
using LogisticsEngine;
using LogisticsEngine.AirportLogistics.Engine;
using ReteEngine;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace LogisticsSim
{
    class Program
    {

        static void Main(string[] args)
        {
            var sim = new SimClass();
            sim.Run().GetAwaiter().GetResult();
        }

    }
}
