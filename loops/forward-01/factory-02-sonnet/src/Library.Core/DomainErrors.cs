namespace Library.Core;

/// <summary>
/// Discriminated union of all domain error outcomes.
/// </summary>
public abstract record DomainError
{
    public record InvalidRequest(string Message) : DomainError;
    public record NotFound(string Message) : DomainError;
    public record NoCopiesAvailable() : DomainError;
    public record LoanLimitExceeded() : DomainError;
    public record MemberOverdueBlocked() : DomainError;
    public record AlreadyReturned() : DomainError;
}
