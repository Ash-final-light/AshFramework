using System;
using System.Collections.Generic;

namespace AshACT.Effects
{
    public enum MarkerRequestError
    {
        HandlerUnavailable,
        None,
        InvalidRequest,
        UnsupportedOperation,
        PreparationFailed
    }

    /// <summary>表示批次能否准备提交；成功不表示已改变标记，也不表示必然增加了层数。</summary>
    public readonly struct MarkerRequestResult
    {
        public MarkerRequestError Error
        {
            get;
        }
        public string Message
        {
            get;
        }
        public bool Succeeded => Error == MarkerRequestError.None;

        public MarkerRequestResult(MarkerRequestError error, string message = null)
        {
            if (!Enum.IsDefined(typeof(MarkerRequestError), error))
            {
                throw new ArgumentOutOfRangeException(nameof(error));
            }

            Error = error;
            Message = message;
        }
    }

    /// <summary>
    /// 外部标记管理器实现的批次入口。所有调用在同一模拟线程同步执行，不依赖墙上时间。
    /// 管理器绑定所属模拟域及其锁定的标记定义；请求身份仅在该域中解释。
    /// </summary>
    public interface IMarkerRequestHandler
    {
        /// <summary>
        /// 按输入顺序在候选状态中准备同帧请求，期间不得改变可见状态或发布通知。
        /// 应拒绝空列表、无效请求和跨帧批次；如需保留输入，必须复制列表。
        /// 成功必须返回非空事务；失败必须返回空事务，并丢弃整个候选批次。
        /// 满层忽略等正常规则结果可以准备成功，具体处理由标记管理器决定。
        /// </summary>
        MarkerRequestResult TryPrepare(IReadOnlyList<MarkerRequest> requests, out IMarkerRequestTransaction transaction);
    }

    /// <summary>仅表示已准备的标记候选批次，不拥有属性事务或效果实例。</summary>
    public interface IMarkerRequestTransaction : IDisposable
    {
        /// <summary>
        /// 由协调方在整笔操作均可提交后调用一次；必须保证不失败、不抛异常，不调用外部回调。
        /// 未提交时释放必须丢弃候选状态；提交后释放只清理资源，不撤销状态。
        /// 未来接入方必须在所有系统提交完毕后单独安排通知发布。
        /// </summary>
        void Commit();
    }
}
