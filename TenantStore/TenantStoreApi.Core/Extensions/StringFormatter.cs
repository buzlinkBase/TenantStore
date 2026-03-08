using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TenantStoreApi.Core;

public static class StringFormatter
{
    public static string FormatCode(this int count)
    {
        return count.ToString().PadLeft(6, '0');
    }
}