using System;

namespace IntraBox.Core
{
    /// <summary>
    /// Unix 时间戳转换纯逻辑（供界面与测试共用）。
    /// </summary>
    public static class TimestampHelper
    {
        public static DateTimeOffset FromUnixSeconds(long seconds)
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }

        public static long ToUnixSeconds(DateTimeOffset dto)
        {
            return dto.ToUnixTimeSeconds();
        }

        public static DateTimeOffset FromUnixMilliseconds(long milliseconds)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        }

        public static long ToUnixMilliseconds(DateTimeOffset dto)
        {
            return dto.ToUnixTimeMilliseconds();
        }
    }
}
