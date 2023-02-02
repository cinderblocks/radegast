using System.Linq;
using System.Text;

namespace Radegast.RadegastUtils
{
    public static class Utils
    {
        public static string MD5(string input)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                return "$1$" + string.Join(string.Empty, md5.ComputeHash(Encoding.UTF8.GetBytes(input)).Select(b => b.ToString("x2")));
            }
        }
    }
}
