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
    }
}
