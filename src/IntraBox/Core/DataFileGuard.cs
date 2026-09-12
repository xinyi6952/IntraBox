using System;
using System.IO;

namespace IntraBox.Core
{
    /// <summary>
    /// 损坏数据文件隔离：挪成 .bak，避免随后按空列表写盘把用户文件盖掉。
    /// </summary>
    public static class DataFileGuard
    {
        /// <summary>
        /// 把 path 挪到 path.bak；已有 bak 则带时间戳。
        /// 文件不存在视为成功。Move 失败返回 false（调用方禁止写回原路径）。
        /// </summary>
        public static bool TryQuarantine(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return true;
            try
            {
                string bak = path + ".bak";
                if (File.Exists(bak))
                    bak = path + "." + DateTime.Now.ToString("yyyyMMddHHmmss") + ".bak";
                if (File.Exists(bak))
                    bak = path + "." + DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".bak";
                File.Move(path, bak);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
