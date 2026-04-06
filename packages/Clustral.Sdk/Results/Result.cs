namespace Clustral.Sdk.Results;

/// <summary>
/// Immutable discriminated union representing either a successful value
/// or a failure. Struct-based for zero allocation on the success path.
/// NativeAOT-safe — no reflection or dynamic.
/// </summary>
public readonly struct Result<T>
{
    private readonly T? _value;
    private readonly ResultError? _error;

    private Result(T value)
    {
        _value = value;
        _error = null;
    }

    private Result(ResultError error)
    {
        _value = default;
        _error = error;
    }

    /// <summary>The success value. Only valid when <see cref="IsSuccess"/> is <c>true</c>.</summary>
    public T Value => IsSuccess ? _value! : throw new InvalidOperationException("Cannot access Value on a failed Result.");

    /// <summary>The error. Only valid when <see cref="IsFailure"/> is <c>true</c>.</summary>
    public ResultError? Error => _error;

    public bool IsSuccess => _error is null;
    public bool IsFailure => _error is not null;

    // ── Factory methods ──────────────────────────────────────────────────

    public static Result<T> Success(T value) => new(value);
    public static Result<T> Fail(ResultError error) => new(error);

    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(ResultError error) => Fail(error);

    // ── Functional combinators ───────────────────────────────────────────

    /// <summary>Transforms the success value; passes through errors unchanged.</summary>
    public Result<TOut> Map<TOut>(Func<T, TOut> map) =>
        IsSuccess ? Result<TOut>.Success(map(_value!)) : Result<TOut>.Fail(_error!);

    /// <summary>Transforms the error; passes through success values unchanged.</summary>
    public Result<T> MapError(Func<ResultError, ResultError> map) =>
        IsFailure ? Result<T>.Fail(map(_error!)) : this;

    /// <summary>Async version of <see cref="Map{TOut}"/>.</summary>
    public async Task<Result<TOut>> MapAsync<TOut>(Func<T, Task<TOut>> map) =>
        IsSuccess ? Result<TOut>.Success(await map(_value!)) : Result<TOut>.Fail(_error!);

    /// <summary>Pattern match: dispatch to one of two functions based on success/failure.</summary>
    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<ResultError, TOut> onFailure) =>
        IsSuccess ? onSuccess(_value!) : onFailure(_error!);

    /// <summary>Adds a validation check: if predicate fails, returns the error.</summary>
    public Result<T> Ensure(Func<T, bool> predicate, ResultError error) =>
        IsSuccess && !predicate(_value!) ? Fail(error) : this;

    /// <summary>Executes an action on failure (e.g., logging). Returns self for chaining.</summary>
    public Result<T> OnFailure(Action<ResultError> action)
    {
        if (IsFailure) action(_error!);
        return this;
    }

    /// <summary>Throws <see cref="ResultFailureException"/> if failed. Use sparingly.</summary>
    public T ThrowIfFailed()
    {
        if (IsFailure) throw new ResultFailureException(_error!);
        return _value!;
    }

    public override string ToString() =>
        IsSuccess ? $"Success({_value})" : $"Fail({_error!.Code}: {_error.Message})";
}

/// <summary>
/// Non-generic Result for void-returning operations.
/// </summary>
public readonly struct Result
{
    private readonly ResultError? _error;

    private Result(ResultError error) => _error = error;

    public bool IsSuccess => _error is null;
    public bool IsFailure => _error is not null;
    public ResultError? Error => _error;

    public static Result Success() => default;
    public static Result Fail(ResultError error) => new(error);
    public static implicit operator Result(ResultError error) => Fail(error);

    public TOut Match<TOut>(Func<TOut> onSuccess, Func<ResultError, TOut> onFailure) =>
        IsSuccess ? onSuccess() : onFailure(_error!);

    public void ThrowIfFailed()
    {
        if (IsFailure) throw new ResultFailureException(_error!);
    }

    public override string ToString() =>
        IsSuccess ? "Success" : $"Fail({_error!.Code}: {_error.Message})";
}
