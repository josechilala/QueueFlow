namespace QueueFlow.Application.Common;

public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess == (error != Error.None))
        {
            throw new ArgumentException("A successful result cannot contain an error and a failed result must contain one.", nameof(error));
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public static Result Success() => new(true, Error.None);

    public static Result Failure(Error error) => new(false, error);

    public static Result<TValue> Success<TValue>(TValue value) => Result<TValue>.CreateSuccess(value);

    public static Result<TValue> Failure<TValue>(Error error) => Result<TValue>.CreateFailure(error);
}

public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    private Result(TValue value)
        : base(true, Error.None)
    {
        _value = value;
    }

    private Result(Error error)
        : base(false, error)
    {
    }

    public TValue Value => IsSuccess
        ? _value ?? throw new InvalidOperationException("A successful result must contain a value.")
        : throw new InvalidOperationException("The value of a failed result cannot be accessed.");

    internal static Result<TValue> CreateSuccess(TValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(value);
    }

    internal static Result<TValue> CreateFailure(Error error) => new(error);
}
