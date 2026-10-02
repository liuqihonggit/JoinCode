namespace PithosDB.Core;

/// <summary>
/// Thrown by <see cref="Transaction.Commit"/> when one or more keys read during the
/// transaction were modified by a concurrent writer before the transaction could commit.
/// The transaction has been finalized; begin a new one to retry the operation.
/// </summary>
public sealed class TransactionConflictException : Exception
{
    public TransactionConflictException()
        : base("Transaction aborted: one or more keys in the read set were modified by a concurrent writer.") { }
}
