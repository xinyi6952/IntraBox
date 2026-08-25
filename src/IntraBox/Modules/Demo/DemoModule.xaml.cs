using System;
using System.Windows.Controls;
using IntraBox.Core;

namespace IntraBox.Modules.Demo
{
    /// <summary>
    /// 示例模块：每次创建时记录实例创建时间，用于直观验证「切换即销毁、再进入即重建」。
    /// </summary>
    public partial class DemoModule : UserControl, IModuleView
    {
        public DemoModule()
        {
            InitializeComponent();
            CreateTimeText.Text = "本实例创建时间：" + DateTime.Now.ToString("HH:mm:ss.fff");
        }

        public void OnActivated()
        {
            // 本模块故意不记忆状态，用来验证「切换即销毁、再进入即重建」
        }

        public void OnDeactivated()
        {
            // 不写入 HistoryManager
        }
    }
}
