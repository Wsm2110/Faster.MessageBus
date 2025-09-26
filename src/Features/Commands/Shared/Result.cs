namespace Faster.MessageBus.Features.Commands.Shared
{
    /// <summary>
    /// Represents the outcome of an operation, which can either be a successful value or an error.
    /// This struct is immutable and designed for high performance by avoiding heap allocations
    /// when passed by value.
    /// </summary>
    /// <typeparam name="T">The type of the successful value.</typeparam>
    public readonly struct Result<T>
    {
        #region Fields

        private readonly T _value;
        private readonly Exception? _error;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the successful value of the operation.
        /// Accessing this property when <see cref="IsSuccess"/> is <c>false</c>
        /// will return <c>default(T)</c>. Consumers should always check <see cref="IsSuccess"/>
        /// before relying on this value.
        /// </summary>
        public T Value => _value;

        /// <summary>
        /// Gets the exception representing the failure of the operation.
        /// This will be <c>null</c> if <see cref="IsSuccess"/> is <c>true</c>.
        /// </summary>
        public Exception? Error => _error;

        /// <summary>
        /// Gets a value indicating whether the operation was successful.
        /// </summary>
        public bool IsSuccess => _error is null;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new instance of <see cref="Result{T}"/> representing a successful outcome.
        /// </summary>
        /// <param name="value">The successful value of the operation.</param>
        public Result(T value)
        {
            _value = value;
            _error = null;
        }

        /// <summary>
        /// Creates a new instance of <see cref="Result{T}"/> representing a failed outcome.
        /// </summary>
        /// <param name="error">The <see cref="Exception"/> representing the failure. Can be <c>null</c>
        /// to indicate a generic failure without specific error details, but <see cref="IsSuccess"/>
        /// will still be <c>false</c>.</param>
        public Result(Exception? error)
        {
            _error = error;
            _value = default!; // Suppress nullable warning if T is non-nullable, consumer must check IsSuccess
        }

        #endregion

        #region Factory Methods

        /// <summary>
        /// Creates a successful result with the specified value.
        /// </summary>
        /// <param name="value">The successful value.</param>
        /// <returns>A new <see cref="Result{T}"/> instance representing success.</returns>
        public static Result<T> Success(T value) => new Result<T>(value);

        /// <summary>
        /// Creates a failed result with the specified exception.
        /// </summary>
        /// <param name="error">The <see cref="Exception"/> representing the failure.</param>
        /// <returns>A new <see cref="Result{T}"/> instance representing failure.</returns>
        public static Result<T> Failure(Exception? error) => new Result<T>(error);

        #endregion

        #region Match Methods

        /// <summary>
        /// Executes one of two provided functions based on whether the <see cref="Result{T}"/>
        /// represents a success or a failure, and returns the result of the executed function.
        /// </summary>
        /// <typeparam name="TResult">The type of the return value for both functions.</typeparam>
        /// <param name="onSuccess">The function to execute if the result is successful.
        /// It receives the successful <typeparamref name="T"/> value as input.</param>
        /// <param name="onFailure">The function to execute if the result is a failure.
        /// It receives the <see cref="Exception"/> as input.</param>
        /// <returns>The result of either <paramref name="onSuccess"/> or <paramref name="onFailure"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown if either <paramref name="onSuccess"/>
        /// or <paramref name="onFailure"/> is <c>null</c>.</exception>
        public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<Exception?, TResult> onFailure)
        {          
            return IsSuccess ? onSuccess(_value) : onFailure(_error);
        }

        /// <summary>
        /// Executes one of two provided actions based on whether the <see cref="Result{T}"/>
        /// represents a success or a failure. This method does not return a value.
        /// </summary>
        /// <param name="onSuccess">The action to execute if the result is successful.
        /// It receives the successful <typeparamref name="T"/> value as input.</param>
        /// <param name="onFailure">The action to execute if the result is a failure.
        /// It receives the <see cref="Exception"/> as input.</param>
        /// <exception cref="ArgumentNullException">Thrown if either <paramref name="onSuccess"/>
        /// or <paramref name="onFailure"/> is <c>null</c>.</exception>
        public void Match(Action<T> onSuccess, Action<Exception?> onFailure)
        {        
            if (IsSuccess)
            {
                onSuccess(_value);
            }
            else
            {
                onFailure(_error);
            }
        }

        #endregion

        #region Conversion Operators

        /// <summary>
        /// Explicitly converts a successful <see cref="Result{T}"/> to its underlying value <typeparamref name="T"/>.
        /// </summary>
        /// <param name="result">The <see cref="Result{T}"/> instance to convert.</param>
        /// <returns>The successful value of the result.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the result is a failure,
        /// or the actual <see cref="Exception"/> if one is present in the failed result.</exception>
        public static explicit operator T(Result<T> result)
        {
            if (!result.IsSuccess)
            {
                // Rethrow the actual error if available for consistency
                if (result.Error != null)
                {
                    throw result.Error;
                }
                // Fallback for a generic failure without a specific exception
                throw new InvalidOperationException("Cannot convert a failed result to its value.");
            }
            return result.Value;
        }

        /// <summary>
        /// Explicitly converts an <see cref="Exception"/> to a failed <see cref="Result{T}"/>.
        /// </summary>
        /// <param name="error">The <see cref="Exception"/> to convert.</param>
        /// <returns>A new <see cref="Result{T}"/> instance representing the failure.</returns>
        public static explicit operator Result<T>(Exception? error)
        {
            return Result<T>.Failure(error);
        }

        /// <summary>
        /// Implicitly converts a <typeparamref name="T"/> value to a successful <see cref="Result{T}"/>.
        /// This provides convenience, allowing a direct return of a value from methods returning <see cref="Result{T}"/>.
        /// </summary>
        /// <param name="value">The successful value to convert.</param>
        /// <returns>A new <see cref="Result{T}"/> instance representing success.</returns>
        public static implicit operator Result<T>(T value)
        {
            return Result<T>.Success(value);
        }

        #endregion
    }
}