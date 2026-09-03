using System.Threading;

namespace IntraBox.Core
{
    /// <summary>
    /// 后台化「取消 + 版本号」任务管理器（组合模式，替代原先各模块重复的样板代码）。
    /// WPF 模块视图为 partial 类、生成的 .g.cs 固定继承 UserControl，无法再继承自定义基类，
    /// 因此以组合方式复用：模块内 <c>private readonly AsyncTaskGate _gate = new AsyncTaskGate();</c>
    /// 然后 <c>CancelPending()</c> 一行委托给 <c>_gate.Cancel();</c>。
    /// </summary>
    public sealed class AsyncTaskGate
    {
        private CancellationTokenSource _cts;
        private int _version;

        /// <summary>供旧的模块内代码继续使用的字段访问器。</summary>
        public CancellationTokenSource Current
        {
            get { return _cts; }
            set { _cts = value; }
        }

        /// <summary>使上一次在途任务失效并取消，返回新的版本号与取消令牌。</summary>
        public int Next(out CancellationToken token)
        {
            Cancel();
            int v = ++_version;
            var cts = new CancellationTokenSource();
            _cts = cts;
            token = cts.Token;
            return v;
        }

        /// <summary>当前版本号（模块内用作对比基准）。</summary>
        public int Version
        {
            get { return _version; }
        }

        /// <summary>递增版本号并返回新值（用于开始一次新的在途任务）。</summary>
        public int Bump()
        {
            return ++_version;
        }

        /// <summary>判断给定版本号是否仍是当前最新（期间若有新任务/离开模块则为 false）。</summary>
        public bool IsCurrent(int version)
        {
            return version == _version;
        }

        /// <summary>取消在途后台任务：版本号递增使过期结果失效，并取消释放 CancellationTokenSource。</summary>
        public void Cancel()
        {
            _version++;
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }
        }
    }
}
