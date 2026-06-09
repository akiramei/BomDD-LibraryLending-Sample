using Library.Core;

namespace Library.Api;

public enum LoanOutcome { Created, NotFound, Blocked }

public sealed record LoanResult(LoanOutcome Outcome, Loan? Loan, LendingError Error)
{
    public static LoanResult Ok(Loan loan) => new(LoanOutcome.Created, loan, LendingError.None);
    public static LoanResult NotFound() => new(LoanOutcome.NotFound, null, LendingError.None);
    public static LoanResult Blocked(LendingError error) => new(LoanOutcome.Blocked, null, error);
}

public enum ReturnOutcome { Ok, NotFound, AlreadyReturned, InvalidInstant }

public sealed record ReturnResult(ReturnOutcome Outcome, Loan? Loan)
{
    public static ReturnResult Ok(Loan loan) => new(ReturnOutcome.Ok, loan);
    public static ReturnResult NotFound() => new(ReturnOutcome.NotFound, null);
    public static ReturnResult AlreadyReturned() => new(ReturnOutcome.AlreadyReturned, null);
    public static ReturnResult InvalidInstant() => new(ReturnOutcome.InvalidInstant, null);
}
