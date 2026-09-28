
namespace Structura.Collections
{
    public partial class SwissTable<TKey, TValue>
    {
        /// <summary>
        /// 指定瑞士表插入键时遇到已存在键的处理行为。
        /// </summary>
        enum InsertionBehavior
        {
            /// <summary>
            /// 不执行任何特殊处理，遇到已存在键时由调用方自行决定后续动作。
            /// </summary>
            None,
            /// <summary>
            /// 遇到已存在键时直接覆盖其对应值。
            /// </summary>
            OverwriteExisting,
            /// <summary>
            /// 遇到已存在键时抛出异常以阻止覆盖。
            /// </summary>
            ThrowOnExisting
        }
    }
}
