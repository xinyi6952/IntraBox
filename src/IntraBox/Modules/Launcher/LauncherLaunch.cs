using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using IntraBox.Core;

namespace IntraBox.Modules.Launcher
{
    /// <summary>打开收藏：程序可按条勾选二次确认；文件可用指定打开方式。</summary>
    public static class LauncherLaunch
    {
        public static bool ConfirmIfNeeded(LauncherNode node, Window owner)
        {
            if (node == null || node.IsCategory) return false;
            string detail = (node.Name ?? "") + "\n" + (node.Target ?? "");
            if (!LauncherSystemCatalog.IsNotepadItem(node.Uid)
                && LauncherProcess.IsAlreadyRunning(node.Kind, node.Target, node.OpenWith))
            {
                string again = "该程序已在运行，确定再打开一份？\n\n" + detail;
                return AskOpen(owner, again);
            }
            if (!LauncherTarget.NeedsOpenConfirm(node.Kind, node.ConfirmOpen)) return true;
            string msg = "确定打开该程序？\n\n" + detail;
            return AskOpen(owner, msg);
        }

        private static bool AskOpen(Window owner, string message)
        {
            if (owner != null && owner.Topmost)
                return ConfirmHelper.WarnOverTopmost(owner, message, "打开确认");
            return ConfirmHelper.Action(message, "打开确认");
        }

        public static bool TryOpen(LauncherNode node)
        {
            if (node == null || node.IsCategory)
            {
                MessageBox.Show("无法打开目录。", "启动器", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }
            string err;
            if (!LauncherTarget.TryValidate(node.Kind, node.Target, out err))
            {
                MessageBox.Show(err, "启动器", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (!LauncherTarget.TryValidateOpenWith(node.Kind, node.OpenWith, out err))
            {
                MessageBox.Show(err, "启动器", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            string target = node.Target.Trim();
            if (node.Kind == LauncherTarget.KindUrl)
                return StartShell(target, null);
            if (node.Kind == LauncherTarget.KindFolder)
            {
                if (!Directory.Exists(target))
                {
                    MessageBox.Show("找不到文件夹：\n" + target, "启动器", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                return StartShell(target, null);
            }
            if (!File.Exists(target))
            {
                MessageBox.Show("找不到目标：\n" + target, "启动器", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (node.Kind == LauncherTarget.KindFile && !string.IsNullOrWhiteSpace(node.OpenWith))
            {
                string openWith = node.OpenWith.Trim();
                if (!File.Exists(openWith))
                {
                    MessageBox.Show("找不到打开方式：\n" + openWith, "启动器", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                return StartShell(openWith, LauncherTarget.QuoteArg(target));
            }
            if (LauncherSystemCatalog.IsNotepadItem(node.Uid))
                return StartNotepadEmpty(target);
            return StartShell(target, null);
        }

        private static bool StartNotepadEmpty(string notepadExe)
        {
            string empty = LauncherNotepad.TryCreateEmptyDocument();
            if (string.IsNullOrEmpty(empty))
                return StartShell(notepadExe, null);
            return StartShell(notepadExe, LauncherTarget.QuoteArg(empty));
        }

        private static bool StartShell(string fileName, string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo();
                psi.FileName = fileName;
                if (!string.IsNullOrEmpty(arguments))
                    psi.Arguments = arguments;
                psi.UseShellExecute = true;
                Process.Start(psi);
                LauncherProcess.InvalidateScanCache();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法打开：\n" + ex.Message, "启动器", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }
    }
}
