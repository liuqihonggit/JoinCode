
namespace Structura.Collections
{
    public partial class SwissTable<TKey, TValue>
    {
        enum InsertionBehavior
        {
            None,
            OverwriteExisting,
            ThrowOnExisting
        }
    }
}