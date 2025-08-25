using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace PRC_API_Worker
{
    public static class Config
    {
# if DEBUG
        public static readonly bool isDev = true;
# else
        public static readonly bool isDev = false;
# endif

        public static readonly string replicaId = Environment.GetEnvironmentVariable("RAILWAY_REPLICA_ID") ?? "dev";
        public static readonly string deploymentId = Environment.GetEnvironmentVariable("RAILWAY_DEPLOYMENT_ID") ?? "dev";
        public static readonly string serviceId = Environment.GetEnvironmentVariable("RAILWAY_SERVICE_ID") ?? "dev";
        public static readonly string environmentId = Environment.GetEnvironmentVariable("RAILWAY_ENVIRONMENT_ID") ?? "dev";
    }
}
